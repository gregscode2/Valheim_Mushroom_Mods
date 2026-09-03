using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace HaldorExpansion
{
    /// <summary>
    /// Server-authoritative config sync over peer ZRpc. Same pattern as Craftable
    /// Spawners and Combat Adjustments: do not wrap login sockets the way ServerSync
    /// did, so this survives game updates that change the socket layer. Clients apply
    /// server values at runtime without overwriting their local .cfg.
    /// </summary>
    internal static class TradeConfigSync
    {
        internal const int ProtocolVersion = 1;
        internal const string RpcSync = "HaldorExpansion.ConfigSync";
        internal const string RpcSyncRequest = "HaldorExpansion.ConfigSyncRequest";

        private static readonly Dictionary<string, ConfigEntryBase> Entries =
            new Dictionary<string, ConfigEntryBase>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<ConfigEntryBase, object> ClientSyncedValues =
            new Dictionary<ConfigEntryBase, object>();

        private static bool _clientSyncActive;

        internal static bool ClientSyncActive => _clientSyncActive;

        internal static bool IsServerAuthority()
        {
            var net = ZNet.instance;
            return net == null || net.IsServer();
        }

        internal static void Register(ConfigEntryBase entry)
        {
            if (entry == null) return;

            var path = BuildPath(entry.Definition.Section, entry.Definition.Key);
            if (!Entries.ContainsKey(path))
                Entries[path] = entry;
        }

        internal static bool TryGetSyncedValue<T>(ConfigEntry<T> entry, out T value)
        {
            object stored;
            if (_clientSyncActive
                && ClientSyncedValues.TryGetValue(entry, out stored)
                && stored is T typed)
            {
                value = typed;
                return true;
            }

            value = default(T);
            return false;
        }

        internal static void OnServerConfigChanged()
        {
            if (!IsServerAuthority()) return;
            if (Plugin.Settings != null && !Plugin.Settings.LockConfiguration) return;

            Broadcast();
        }

        internal static void Initialize(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }),
                postfix: nameof(ZNetOnNewConnectionPostfix));
            Patch(harmony, AccessTools.Method(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }),
                postfix: nameof(ZNetRpcPeerInfoPostfix));
            Patch(harmony, AccessTools.Method(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) }),
                postfix: nameof(ZNetDisconnectPostfix));
            Patch(harmony, AccessTools.Method(typeof(ZNet), "OnDestroy", Type.EmptyTypes),
                prefix: nameof(ZNetOnDestroyPrefix));
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodInfo method,
                                  string postfix = null, string prefix = null)
        {
            if (method == null)
            {
                Plugin.Log.LogWarning("Config sync: could not find a ZNet method to patch.");
                return;
            }

            var postfixMethod = postfix == null ? null : new HarmonyMethod(typeof(TradeConfigSync), postfix);
            var prefixMethod = prefix == null ? null : new HarmonyMethod(typeof(TradeConfigSync), prefix);
            harmony.Patch(method, prefix: prefixMethod, postfix: postfixMethod);
        }

        private static string BuildPath(string section, string key)
        {
            return (section ?? string.Empty) + "\u001f" + (key ?? string.Empty);
        }

        private static void Broadcast()
        {
            var net = ZNet.instance;
            if (net == null || !net.IsServer()) return;

            foreach (var peer in net.GetConnectedPeers())
            {
                if (peer != null && peer.IsReady() && peer.m_rpc != null)
                    SendToRpc(peer.m_rpc);
            }
        }

        private static IEnumerator DeferNetworkFrames(int frameCount, Action action)
        {
            for (var i = 0; i < frameCount; i++)
                yield return null;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Config sync deferred action failed: " + ex.Message);
            }
        }

        private static void RequestFromServer()
        {
            var net = ZNet.instance;
            if (net == null || net.IsServer()) return;

            var rpc = net.GetServerRPC();
            if (rpc == null || !rpc.IsConnected()) return;

            var pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(Plugin.PluginVersion);
            rpc.Invoke(RpcSyncRequest, pkg);
        }

        private static bool ShouldSend()
        {
            return Plugin.Settings == null || Plugin.Settings.LockConfiguration;
        }

        private static void SendToRpc(ZRpc rpc)
        {
            if (rpc == null || !rpc.IsConnected()) return;
            if (!ShouldSend()) return;

            var pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(Plugin.PluginVersion);
            pkg.Write(Entries.Count);
            foreach (var entry in Entries.Values)
            {
                pkg.Write(entry.Definition.Section ?? string.Empty);
                pkg.Write(entry.Definition.Key ?? string.Empty);
                pkg.Write(entry.GetSerializedValue() ?? string.Empty);
            }

            rpc.Invoke(RpcSync, pkg);
        }

        private static void ReceiveSyncRequestRpc(ZRpc rpc, ZPackage request)
        {
            var net = ZNet.instance;
            if (net == null || !net.IsServer() || rpc == null || request == null) return;

            try
            {
                var guestProtocol = request.ReadInt();
                var guestVersion = request.ReadString();
                if (guestProtocol != ProtocolVersion
                    || !string.Equals(guestVersion, Plugin.PluginVersion, StringComparison.Ordinal))
                {
                    Plugin.Log.LogWarning(
                        "Config sync version mismatch for a guest (guest " + guestVersion
                        + " / protocol " + guestProtocol + "; host " + Plugin.PluginVersion
                        + " / protocol " + ProtocolVersion + "). Sending host values anyway.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Config sync request read failed: " + ex.Message);
            }

            SendToRpc(rpc);
        }

        private static void ReceiveSyncRpc(ZRpc rpc, ZPackage pkg)
        {
            var net = ZNet.instance;
            if (net == null || net.IsServer() || rpc == null || pkg == null || net.GetServerRPC() != rpc)
                return;

            try
            {
                var protocol = pkg.ReadInt();
                var hostVersion = pkg.ReadString();
                if (protocol != ProtocolVersion
                    || !string.Equals(hostVersion, Plugin.PluginVersion, StringComparison.Ordinal))
                {
                    ClearClientSync("version mismatch");
                    Plugin.Log.LogWarning(
                        "Host mod version differs (" + hostVersion + " vs " + Plugin.PluginVersion
                        + "). Using local config.");
                    return;
                }

                var count = pkg.ReadInt();
                if (count < 0 || count > 10000)
                    throw new InvalidOperationException("Invalid config sync entry count: " + count);

                var typed = new Dictionary<ConfigEntryBase, object>();
                for (var i = 0; i < count; i++)
                {
                    var section = pkg.ReadString();
                    var key = pkg.ReadString();
                    var serialized = pkg.ReadString();

                    ConfigEntryBase entry;
                    if (!Entries.TryGetValue(BuildPath(section, key), out entry))
                        continue;

                    var value = ConvertSyncedValue(entry, serialized);
                    if (value != null)
                        typed[entry] = value;
                }

                var firstActivation = !_clientSyncActive;
                ClientSyncedValues.Clear();
                foreach (var pair in typed)
                    ClientSyncedValues[pair.Key] = pair.Value;

                _clientSyncActive = true;

                Plugin.Log.LogInfo(
                    firstActivation
                        ? "Client config sync active (" + typed.Count + " settings from host "
                          + hostVersion + "). Trade table hash: " + TradeTable.Hash
                        : "Client config sync updated (" + typed.Count
                          + " settings). Trade table hash: " + TradeTable.Hash);
            }
            catch (Exception ex)
            {
                ClearClientSync("invalid payload");
                Plugin.Log.LogWarning("Config sync failed: " + ex.Message);
            }
        }

        private static object ConvertSyncedValue(ConfigEntryBase entry, string serialized)
        {
            if (entry.SettingType.IsEnum)
            {
                try
                {
                    return Enum.Parse(entry.SettingType, serialized ?? string.Empty, true);
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning(
                        "Config sync could not parse '" + serialized + "' as "
                        + entry.SettingType.Name + " for " + entry.Definition.Section
                        + "." + entry.Definition.Key + ".");
                    return null;
                }
            }

            return TomlTypeConverter.ConvertToValue(serialized, entry.SettingType);
        }

        private static void ClearClientSync(string reason)
        {
            if (!_clientSyncActive && ClientSyncedValues.Count == 0) return;

            _clientSyncActive = false;
            ClientSyncedValues.Clear();
            Plugin.Log.LogInfo("Client config sync ended (" + reason + ").");
        }

        public static void ZNetOnNewConnectionPostfix(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;

            try
            {
                peer.m_rpc.Register<ZPackage>(RpcSync, ReceiveSyncRpc);
                peer.m_rpc.Register<ZPackage>(RpcSyncRequest, ReceiveSyncRequestRpc);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Config sync RPC registration failed: " + ex.Message);
            }
        }

        public static void ZNetRpcPeerInfoPostfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || rpc == null) return;

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
}
