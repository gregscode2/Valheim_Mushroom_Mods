using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace CraftableSpawners.Configuration;

/// <summary>
/// Server-authoritative config sync over peer ZRpc (same pattern as Combat Adjustments
/// and Separate Spawns). Does not wrap login sockets the way ServerSync did, so it
/// survives game updates that change the socket layer. Clients apply server values at
/// runtime without overwriting their local .cfg.
/// </summary>
internal static class SpawnerConfigSync
{
    internal const int ProtocolVersion = 1;
    internal const string RpcSync = "CraftableSpawners.ConfigSync";
    internal const string RpcSyncRequest = "CraftableSpawners.ConfigSyncRequest";

    private static readonly Dictionary<string, ConfigEntryBase> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ConfigEntryBase, object> ClientSyncedValues = new();

    private static bool clientSyncActive;

    internal static bool ClientSyncActive => clientSyncActive;

    internal static bool IsServerAuthority()
    {
        ZNet net = ZNet.instance;
        return net == null || net.IsServer();
    }

    internal static void Register(ConfigEntryBase entry)
    {
        if (entry == null)
            return;

        string path = BuildPath(entry.Definition.Section, entry.Definition.Key);
        if (!Entries.ContainsKey(path))
            Entries[path] = entry;
    }

    internal static bool TryGetSyncedValue<T>(ConfigEntry<T> entry, out T value)
    {
        if (clientSyncActive
            && ClientSyncedValues.TryGetValue(entry, out object stored)
            && stored is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    internal static void OnServerConfigChanged()
    {
        if (!IsServerAuthority())
            return;

        Broadcast();
    }

    internal static void Initialize(Harmony harmony)
    {
        Patch(harmony, AccessTools.Method(typeof(ZNet), "OnNewConnection", [typeof(ZNetPeer)]),
            postfix: nameof(ZNetOnNewConnectionPostfix));
        Patch(harmony, AccessTools.Method(typeof(ZNet), "RPC_PeerInfo", [typeof(ZRpc), typeof(ZPackage)]),
            postfix: nameof(ZNetRpcPeerInfoPostfix));
        Patch(harmony, AccessTools.Method(typeof(ZNet), "Disconnect", [typeof(ZNetPeer)]),
            postfix: nameof(ZNetDisconnectPostfix));
        Patch(harmony, AccessTools.Method(typeof(ZNet), "OnDestroy", Type.EmptyTypes),
            prefix: nameof(ZNetOnDestroyPrefix));
    }

    private static void Patch(Harmony harmony, System.Reflection.MethodInfo method, string postfix = null, string prefix = null)
    {
        if (method == null)
        {
            CraftableSpawnersPlugin.Log.LogWarning("Config sync: could not find a ZNet method to patch.");
            return;
        }

        HarmonyMethod postfixMethod = postfix == null ? null : new HarmonyMethod(typeof(SpawnerConfigSync), postfix);
        HarmonyMethod prefixMethod = prefix == null ? null : new HarmonyMethod(typeof(SpawnerConfigSync), prefix);
        harmony.Patch(method, prefix: prefixMethod, postfix: postfixMethod);
    }

    private static string BuildPath(string section, string key) =>
        (section ?? string.Empty) + "\u001f" + (key ?? string.Empty);

    private static void Broadcast()
    {
        ZNet net = ZNet.instance;
        if (net == null || !net.IsServer())
            return;

        foreach (ZNetPeer peer in net.GetConnectedPeers())
        {
            if (peer != null && peer.IsReady() && peer.m_rpc != null)
                SendToRpc(peer.m_rpc);
        }
    }

    private static IEnumerator DeferNetworkFrames(int frameCount, Action action)
    {
        for (int i = 0; i < frameCount; i++)
            yield return null;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            CraftableSpawnersPlugin.Log.LogWarning($"Config sync deferred action failed: {ex.Message}");
        }
    }

    private static void RequestFromServer()
    {
        ZNet net = ZNet.instance;
        if (net == null || net.IsServer())
            return;

        ZRpc rpc = net.GetServerRPC();
        if (rpc == null || !rpc.IsConnected())
            return;

        ZPackage pkg = new();
        pkg.Write(ProtocolVersion);
        pkg.Write(CraftableSpawnersPlugin.Version);
        rpc.Invoke(RpcSyncRequest, pkg);
    }

    private static void SendToRpc(ZRpc rpc)
    {
        if (rpc == null || !rpc.IsConnected())
            return;

        ZPackage pkg = new();
        pkg.Write(ProtocolVersion);
        pkg.Write(CraftableSpawnersPlugin.Version);
        pkg.Write(Entries.Count);
        foreach (ConfigEntryBase entry in Entries.Values)
        {
            pkg.Write(entry.Definition.Section ?? string.Empty);
            pkg.Write(entry.Definition.Key ?? string.Empty);
            pkg.Write(entry.GetSerializedValue() ?? string.Empty);
        }

        rpc.Invoke(RpcSync, pkg);
    }

    private static void ReceiveSyncRequestRpc(ZRpc rpc, ZPackage request)
    {
        ZNet net = ZNet.instance;
        if (net == null || !net.IsServer() || rpc == null || request == null)
            return;

        try
        {
            int guestProtocol = request.ReadInt();
            string guestVersion = request.ReadString();
            if (guestProtocol != ProtocolVersion
                || !string.Equals(guestVersion, CraftableSpawnersPlugin.Version, StringComparison.Ordinal))
            {
                CraftableSpawnersPlugin.Log.LogWarning(
                    $"Config sync version mismatch for a guest (guest {guestVersion} / protocol {guestProtocol}; "
                    + $"host {CraftableSpawnersPlugin.Version} / protocol {ProtocolVersion}). Sending host values anyway.");
            }
        }
        catch (Exception ex)
        {
            CraftableSpawnersPlugin.Log.LogWarning($"Config sync request read failed: {ex.Message}");
        }

        SendToRpc(rpc);
    }

    private static void ReceiveSyncRpc(ZRpc rpc, ZPackage pkg)
    {
        ZNet net = ZNet.instance;
        if (net == null || net.IsServer() || rpc == null || pkg == null || net.GetServerRPC() != rpc)
            return;

        try
        {
            int protocol = pkg.ReadInt();
            string hostVersion = pkg.ReadString();
            if (protocol != ProtocolVersion
                || !string.Equals(hostVersion, CraftableSpawnersPlugin.Version, StringComparison.Ordinal))
            {
                ClearClientSync("version mismatch");
                CraftableSpawnersPlugin.Log.LogWarning(
                    $"Host mod version differs ({hostVersion} vs {CraftableSpawnersPlugin.Version}). Using local config.");
                return;
            }

            int count = pkg.ReadInt();
            if (count < 0 || count > 10000)
                throw new InvalidOperationException($"Invalid config sync entry count: {count}");

            Dictionary<ConfigEntryBase, object> typed = new();
            for (int i = 0; i < count; i++)
            {
                string section = pkg.ReadString();
                string key = pkg.ReadString();
                string serialized = pkg.ReadString();

                if (!Entries.TryGetValue(BuildPath(section, key), out ConfigEntryBase entry))
                    continue;

                object value = TomlTypeConverter.ConvertToValue(serialized, entry.SettingType);
                if (value != null)
                    typed[entry] = value;
            }

            bool firstActivation = !clientSyncActive;
            ClientSyncedValues.Clear();
            foreach (KeyValuePair<ConfigEntryBase, object> pair in typed)
                ClientSyncedValues[pair.Key] = pair.Value;

            clientSyncActive = true;
            SpawnerSetup.RefreshFromConfig();

            CraftableSpawnersPlugin.Dbgl(
                firstActivation
                    ? $"Client config sync active ({typed.Count} settings from host {hostVersion})."
                    : $"Client config sync updated ({typed.Count} settings).",
                forceLog: firstActivation);
        }
        catch (Exception ex)
        {
            ClearClientSync("invalid payload");
            CraftableSpawnersPlugin.Log.LogWarning($"Config sync failed: {ex.Message}");
        }
    }

    private static void ClearClientSync(string reason)
    {
        if (!clientSyncActive && ClientSyncedValues.Count == 0)
            return;

        clientSyncActive = false;
        ClientSyncedValues.Clear();
        SpawnerSetup.RefreshFromConfig();
        CraftableSpawnersPlugin.Dbgl($"Client config sync ended ({reason}).", forceLog: true);
    }

    public static void ZNetOnNewConnectionPostfix(ZNetPeer peer)
    {
        if (peer?.m_rpc == null)
            return;

        try
        {
            peer.m_rpc.Register<ZPackage>(RpcSync, ReceiveSyncRpc);
            peer.m_rpc.Register<ZPackage>(RpcSyncRequest, ReceiveSyncRequestRpc);
        }
        catch (Exception ex)
        {
            CraftableSpawnersPlugin.Log.LogWarning($"Config sync RPC registration failed: {ex.Message}");
        }
    }

    public static void ZNetRpcPeerInfoPostfix(ZNet __instance, ZRpc rpc)
    {
        if (__instance == null || rpc == null)
            return;

        const int deferFrames = 2;
        if (__instance.IsServer())
            __instance.StartCoroutine(DeferNetworkFrames(deferFrames, () => SendToRpc(rpc)));
        else if (__instance.GetServerRPC() == rpc)
            __instance.StartCoroutine(DeferNetworkFrames(deferFrames, RequestFromServer));
    }

    public static void ZNetDisconnectPostfix(ZNet __instance, ZNetPeer peer)
    {
        if (__instance != null && !__instance.IsServer() && peer != null && peer.m_server)
            ClearClientSync("disconnect");
    }

    public static void ZNetOnDestroyPrefix(ZNet __instance)
    {
        if (__instance != null && !__instance.IsServer())
            ClearClientSync("network shutdown");
    }
}
