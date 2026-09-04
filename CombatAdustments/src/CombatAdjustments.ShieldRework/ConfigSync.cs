using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CombatAdjustments.ShieldRework;

/// <summary>
/// Server-authoritative multiplayer config: the host / dedicated server sends its
/// effective settings to joining clients. Clients read synced values at runtime
/// without overwriting their local .cfg file.
/// </summary>
internal static class ConfigSync
{
    public const int ProtocolVersion = 1;
    public const string RpcSync = "CASR_ConfigSync";
    public const string RpcSyncRequest = "CASR_ConfigSyncRequest";

    private static readonly Dictionary<string, ConfigEntryBase> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<ConfigEntryBase> RegisteredEntries = new();

    private static readonly Dictionary<ConfigEntryBase, object> ClientSyncedValues = new();
    private static readonly HashSet<MethodInfo> PatchedValueGetters = new();

    private static bool _clientSyncActive;
    private static int _readLocalDepth;
    private static Harmony? _harmony;

    internal static bool ClientSyncActive => _clientSyncActive;

    internal static bool IsServerAuthority()
    {
        ZNet? net = ZNet.instance;
        return net == null || net.IsServer();
    }

    internal static bool IsConnectedClient()
    {
        ZNet? net = ZNet.instance;
        return net != null && !net.IsServer() && net.GetServerRPC() != null;
    }

    internal static void OnServerConfigChanged()
    {
        if (!IsServerAuthority() || !ShieldReworkPlugin.SyncConfigInMultiplayer.Value)
            return;

        BroadcastToAllPeers();
    }

    internal static void Initialize(Harmony harmony)
    {
        _harmony = harmony;

        MethodInfo? onNew = AccessTools.Method(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) });
        MethodInfo? peerInfo = AccessTools.Method(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) });
        MethodInfo? disconnect = AccessTools.Method(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) });
        MethodInfo? onDestroy = AccessTools.Method(typeof(ZNet), "OnDestroy", Type.EmptyTypes);
        MethodInfo? configSave = AccessTools.Method(typeof(ConfigFile), "Save", Type.EmptyTypes);

        if (onNew != null)
            harmony.Patch(onNew, postfix: new HarmonyMethod(typeof(ConfigSync), nameof(ZNetOnNewConnectionPostfix)));
        else
            ShieldReworkPlugin.Log.LogWarning("Config sync: ZNet.OnNewConnection not found.");

        if (peerInfo != null)
            harmony.Patch(peerInfo, postfix: new HarmonyMethod(typeof(ConfigSync), nameof(ZNetRpcPeerInfoPostfix)));
        else
            ShieldReworkPlugin.Log.LogWarning("Config sync: ZNet.RPC_PeerInfo not found.");

        if (disconnect != null)
            harmony.Patch(disconnect, postfix: new HarmonyMethod(typeof(ConfigSync), nameof(ZNetDisconnectPostfix)));

        if (onDestroy != null)
            harmony.Patch(onDestroy, prefix: new HarmonyMethod(typeof(ConfigSync), nameof(ZNetOnDestroyPrefix)));

        if (configSave != null)
        {
            harmony.Patch(
                configSave,
                prefix: new HarmonyMethod(typeof(ConfigSync), nameof(ConfigFileSavePrefix)),
                finalizer: new HarmonyMethod(typeof(ConfigSync), nameof(ConfigFileSaveFinalizer)));
        }

        PatchConfigValueGetters(harmony);
    }

    internal static void Register(ConfigEntryBase entry)
    {
        if (entry == null)
            return;

        string path = BuildPath(entry);
        if (Entries.ContainsKey(path))
            return;

        Entries[path] = entry;
        RegisteredEntries.Add(entry);
    }

    /// <summary>
    /// Defer network work out of <see cref="ZNet.RPC_PeerInfo"/> so we do not invoke RPCs
    /// while other mods (e.g. ServerSync) wrap the socket during login.
    /// </summary>
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
            ShieldReworkPlugin.Log.LogWarning($"Config sync deferred action failed: {ex.Message}");
        }
    }

    internal static void RequestFromServer()
    {
        ZNet? net = ZNet.instance;
        if (net == null || net.IsServer())
            return;

        ZRpc? rpc = net.GetServerRPC();
        if (rpc == null || !rpc.IsConnected())
            return;

        try
        {
            var pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(ShieldReworkPlugin.PluginVersion);
            rpc.Invoke(RpcSyncRequest, pkg);
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogWarning($"Config sync request failed: {ex.Message}");
        }
    }

    private static bool ShouldSync(ConfigEntryBase entry)
    {
        if (entry.SettingType == typeof(KeyCode))
            return false;

        string section = entry.Definition.Section ?? string.Empty;
        string key = entry.Definition.Key ?? string.Empty;

        // Internal reseed marker — server-only bookkeeping.
        if (section.Equals("General", StringComparison.OrdinalIgnoreCase)
            && key.Equals("GrantTableVersion", StringComparison.OrdinalIgnoreCase))
            return false;

        if (section.Equals("General", StringComparison.OrdinalIgnoreCase)
            && key.Equals("SyncConfigInMultiplayer", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string BuildPath(ConfigEntryBase entry) =>
        (entry.Definition.Section ?? string.Empty) + "\u001f" + (entry.Definition.Key ?? string.Empty);

    private static string BuildPath(string section, string key) =>
        (section ?? string.Empty) + "\u001f" + (key ?? string.Empty);

    private static string GetRawLocalValue(ConfigEntryBase entry)
    {
        _readLocalDepth++;
        try
        {
            return entry.GetSerializedValue() ?? string.Empty;
        }
        finally
        {
            _readLocalDepth--;
        }
    }

    private static ZPackage BuildEnvelope()
    {
        var envelope = new ZPackage();
        envelope.Write(ProtocolVersion);
        envelope.Write(ShieldReworkPlugin.PluginVersion);

        var payload = new ZPackage();
        var list = new List<ConfigEntryBase>();
        foreach (var entry in Entries.Values)
        {
            if (ShouldSync(entry))
                list.Add(entry);
        }

        list.Sort((a, b) =>
        {
            int c = StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Section, b.Definition.Section);
            return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Key, b.Definition.Key);
        });

        payload.Write(list.Count);
        foreach (var entry in list)
        {
            payload.Write(entry.Definition.Section ?? string.Empty);
            payload.Write(entry.Definition.Key ?? string.Empty);
            payload.Write(GetRawLocalValue(entry));
        }

        envelope.WriteCompressed(payload);
        return envelope;
    }

    private static void SendToRpc(ZRpc rpc)
    {
        if (rpc == null || !rpc.IsConnected())
            return;

        if (!ShieldReworkPlugin.SyncConfigInMultiplayer.Value)
            return;

        try
        {
            rpc.Invoke(RpcSync, BuildEnvelope());
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogWarning($"Config sync send failed: {ex.Message}");
        }
    }

    private static void BroadcastToAllPeers()
    {
        ZNet? net = ZNet.instance;
        if (net == null || !net.IsServer())
            return;

        foreach (ZNetPeer peer in net.GetConnectedPeers())
        {
            if (peer != null && peer.IsReady() && peer.m_rpc != null)
                SendToRpc(peer.m_rpc);
        }
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
            ShieldReworkPlugin.Log.LogWarning($"Config sync RPC registration failed: {ex.Message}");
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
            RestoreClientLocal("disconnect");
    }

    public static void ZNetOnDestroyPrefix(ZNet __instance)
    {
        if (__instance != null && !__instance.IsServer())
            RestoreClientLocal("network shutdown");
    }

    private static void ReceiveSyncRequestRpc(ZRpc rpc, ZPackage request)
    {
        ZNet? net = ZNet.instance;
        if (net == null || !net.IsServer() || rpc == null || request == null)
            return;

        try
        {
            int guestProtocol = request.ReadInt();
            string guestVersion = request.ReadString();
            if (guestProtocol != ProtocolVersion
                || !string.Equals(guestVersion, ShieldReworkPlugin.PluginVersion, StringComparison.Ordinal))
            {
                ShieldReworkPlugin.Log.LogWarning(
                    $"Config sync version mismatch for a guest (guest {guestVersion} / protocol {guestProtocol}; " +
                    $"host {ShieldReworkPlugin.PluginVersion} / protocol {ProtocolVersion}). Sending host values anyway.");
            }
        }
        catch (Exception ex)
        {
            ShieldReworkPlugin.Log.LogWarning($"Config sync request read failed: {ex.Message}");
        }

        SendToRpc(rpc);
    }

    private static void ReceiveSyncRpc(ZRpc rpc, ZPackage envelope)
    {
        ZNet? net = ZNet.instance;
        if (net == null || net.IsServer() || rpc == null || envelope == null || net.GetServerRPC() != rpc)
            return;

        if (!ShieldReworkPlugin.SyncConfigInMultiplayer.Value)
        {
            RestoreClientLocal("SyncConfigInMultiplayer disabled locally");
            return;
        }

        try
        {
            int protocol = envelope.ReadInt();
            string hostVersion = envelope.ReadString();
            if (protocol != ProtocolVersion
                || !string.Equals(hostVersion, ShieldReworkPlugin.PluginVersion, StringComparison.Ordinal))
            {
                RestoreClientLocal("version mismatch");
                NotifyPlayer(
                    $"Combat Adjustments: host mod version differs ({hostVersion} vs {ShieldReworkPlugin.PluginVersion}). Using your local config.");
                return;
            }

            ZPackage payload = envelope.ReadCompressedPackage();
            int count = payload.ReadInt();
            if (count < 0 || count > 10000)
                throw new InvalidOperationException($"Invalid config sync entry count: {count}");

            var typed = new Dictionary<ConfigEntryBase, object>();
            int applied = 0;
            for (int i = 0; i < count; i++)
            {
                string section = payload.ReadString();
                string key = payload.ReadString();
                string serialized = payload.ReadString();

                if (!Entries.TryGetValue(BuildPath(section, key), out ConfigEntryBase? entry)
                    || !ShouldSync(entry))
                    continue;

                if (!TryParseValue(entry.SettingType, serialized, out object? value) || value == null)
                    throw new InvalidOperationException($"Could not parse [{section}] {key} = {serialized}");

                typed[entry] = value;
                applied++;
            }

            if (applied == 0)
                throw new InvalidOperationException("Config sync payload contained no applicable entries.");

            bool firstActivation = !_clientSyncActive;
            ClientSyncedValues.Clear();
            foreach (var pair in typed)
                ClientSyncedValues[pair.Key] = pair.Value;

            _clientSyncActive = true;
            ApplyRuntimeFromConfig();

            if (firstActivation)
            {
                NotifyPlayer($"Combat Adjustments: using host configuration ({applied} settings).");
                ShieldReworkPlugin.Log.LogInfo(
                    $"Client config sync active ({applied} settings from host {hostVersion}).");
            }
            else
            {
                ShieldReworkPlugin.Log.LogInfo($"Client config sync updated ({applied} settings).");
            }
        }
        catch (Exception ex)
        {
            RestoreClientLocal("invalid payload");
            ShieldReworkPlugin.Log.LogWarning($"Config sync failed: {ex.Message}");
            NotifyPlayer("Combat Adjustments: host config sync failed; using local settings.");
        }
    }

    private static void RestoreClientLocal(string reason)
    {
        if (!_clientSyncActive)
            return;

        _clientSyncActive = false;
        ClientSyncedValues.Clear();
        ApplyRuntimeFromConfig();
        ShieldReworkPlugin.Log.LogInfo($"Client config sync ended ({reason}).");
    }

    private static void ApplyRuntimeFromConfig()
    {
        if (ObjectDB.instance != null)
        {
            ShieldStats.ApplyToObjectDB(ObjectDB.instance);
            WeaponBlockStats.ApplyToObjectDB(ObjectDB.instance);
            FeastStats.ApplyToObjectDB(ObjectDB.instance);
        }
    }

    private static void NotifyPlayer(string message)
    {
        Player? player = Player.m_localPlayer;
        if (player != null)
            player.Message(MessageHud.MessageType.TopLeft, message, 0, null);
    }

    private static bool TryParseValue(Type type, string serialized, out object? value)
    {
        try
        {
            if (type == typeof(string))
            {
                value = serialized;
                return true;
            }

            if (type == typeof(bool))
            {
                value = bool.Parse(serialized);
                return true;
            }

            if (type == typeof(int))
            {
                value = int.Parse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture);
                return true;
            }

            if (type == typeof(float))
            {
                value = float.Parse(serialized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                return true;
            }

            if (type.IsEnum)
            {
                value = Enum.Parse(type, serialized, ignoreCase: true);
                return true;
            }
        }
        catch
        {
            value = null;
            return false;
        }

        value = null;
        return false;
    }

    private static void PatchConfigValueGetters(Harmony harmony)
    {
        MethodInfo? postfix = AccessTools.Method(typeof(ConfigSync), nameof(ConfigEntryValueGetterPostfix));
        if (postfix == null)
            return;

        PatchValueGetter<bool>(harmony, postfix);
        PatchValueGetter<float>(harmony, postfix);
        PatchValueGetter<int>(harmony, postfix);
        PatchValueGetter<string>(harmony, postfix);
    }

    private static void PatchValueGetter<T>(Harmony harmony, MethodInfo postfix)
    {
        MethodInfo? getter = typeof(ConfigEntry<T>).GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
        if (getter == null || PatchedValueGetters.Contains(getter))
            return;

        harmony.Patch(getter, postfix: new HarmonyMethod(postfix.MakeGenericMethod(typeof(T))));
        PatchedValueGetters.Add(getter);
    }

    public static void ConfigEntryValueGetterPostfix<T>(ConfigEntry<T> __instance, ref T __result)
    {
        if (!_clientSyncActive || _readLocalDepth > 0 || __instance == null)
            return;

        if (!RegisteredEntries.Contains(__instance))
            return;

        if (ClientSyncedValues.TryGetValue(__instance, out object? value) && value is T typed)
            __result = typed;
    }

    public static void ConfigFileSavePrefix(ConfigFile __instance, ref bool __state)
    {
        __state = false;
        if (_clientSyncActive
            && __instance == ShieldReworkPlugin.ModConfig)
        {
            _readLocalDepth++;
            __state = true;
        }
    }

    public static Exception? ConfigFileSaveFinalizer(Exception? __exception, bool __state)
    {
        if (__state && _readLocalDepth > 0)
            _readLocalDepth--;
        return __exception;
    }
}
