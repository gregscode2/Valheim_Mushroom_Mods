using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RandomYggdrasil
{
    /// <summary>
    /// Server-authoritative rotation sync over peer ZRpc, matching Combat Adjustments
    /// and Separate Spawns. Does not wrap login sockets (unlike ServerSync).
    /// </summary>
    internal static class RotationSync
    {
        internal const int ProtocolVersion = 1;
        internal const string RpcSync = "RandomYggdrasil.ConfigSync";
        internal const string RpcSyncRequest = "RandomYggdrasil.ConfigSyncRequest";

        private static readonly Dictionary<string, int> ClientSyncedRotations =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static bool clientSyncReceived;

        internal static bool HasReceivedSync
        {
            get { return clientSyncReceived; }
        }

        internal static bool IsServerAuthority()
        {
            ZNet net = ZNet.instance;
            return net == null || net.IsServer();
        }

        internal static bool TryGetSyncedRotation(string worldIdentifier, out int degrees)
        {
            if (clientSyncReceived && ClientSyncedRotations.TryGetValue(worldIdentifier, out degrees))
            {
                return degrees >= 0 && degrees < 360;
            }

            degrees = -1;
            return false;
        }

        internal static void Initialize(Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(ZNet), "OnNewConnection", new[] { typeof(ZNetPeer) }),
                postfix: new HarmonyMethod(typeof(RotationSync), nameof(ZNetOnNewConnectionPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(ZNet), "RPC_PeerInfo", new[] { typeof(ZRpc), typeof(ZPackage) }),
                postfix: new HarmonyMethod(typeof(RotationSync), nameof(ZNetRpcPeerInfoPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) }),
                postfix: new HarmonyMethod(typeof(RotationSync), nameof(ZNetDisconnectPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(ZNet), "OnDestroy", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(RotationSync), nameof(ZNetOnDestroyPrefix)));
        }

        internal static void Broadcast()
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer())
            {
                return;
            }

            foreach (ZNetPeer peer in net.GetConnectedPeers())
            {
                if (peer != null && peer.IsReady() && peer.m_rpc != null)
                {
                    SendToRpc(peer.m_rpc);
                }
            }
        }

        private static IEnumerator DeferNetworkFrames(int frameCount, Action action)
        {
            for (int i = 0; i < frameCount; i++)
            {
                yield return null;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RandomYggdrasil: deferred config sync failed: " + ex.Message);
            }
        }

        private static void RequestFromServer()
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer())
            {
                return;
            }

            ZRpc rpc = net.GetServerRPC();
            if (rpc == null || !rpc.IsConnected())
            {
                return;
            }

            ZPackage pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(RandomYggdrasilMod.PluginVersion);
            rpc.Invoke(RpcSyncRequest, pkg);
        }

        private static void SendToRpc(ZRpc rpc)
        {
            if (rpc == null || !rpc.IsConnected())
            {
                return;
            }

            RandomYggdrasilMod.EnsureCurrentWorldRotation();

            Dictionary<string, int> snapshot = RandomYggdrasilMod.GetRotationSnapshot();
            ZPackage pkg = new ZPackage();
            pkg.Write(ProtocolVersion);
            pkg.Write(RandomYggdrasilMod.PluginVersion);
            pkg.Write(snapshot.Count);
            foreach (KeyValuePair<string, int> pair in snapshot)
            {
                pkg.Write(pair.Key);
                pkg.Write(pair.Value);
            }

            rpc.Invoke(RpcSync, pkg);
        }

        private static void ReceiveSyncRequestRpc(ZRpc rpc, ZPackage request)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || rpc == null || request == null)
            {
                return;
            }

            try
            {
                int guestProtocol = request.ReadInt();
                string guestVersion = request.ReadString();
                if (guestProtocol != ProtocolVersion
                    || !string.Equals(guestVersion, RandomYggdrasilMod.PluginVersion, StringComparison.Ordinal))
                {
                    Debug.LogWarning(
                        "RandomYggdrasil: config sync version mismatch for a guest (guest "
                        + guestVersion + " / protocol " + guestProtocol + "; host "
                        + RandomYggdrasilMod.PluginVersion + " / protocol " + ProtocolVersion
                        + "). Sending host values anyway.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RandomYggdrasil: config sync request read failed: " + ex.Message);
            }

            SendToRpc(rpc);
        }

        private static void ReceiveSyncRpc(ZRpc rpc, ZPackage pkg)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer() || rpc == null || pkg == null || net.GetServerRPC() != rpc)
            {
                return;
            }

            try
            {
                int protocol = pkg.ReadInt();
                string hostVersion = pkg.ReadString();
                if (protocol != ProtocolVersion
                    || !string.Equals(hostVersion, RandomYggdrasilMod.PluginVersion, StringComparison.Ordinal))
                {
                    Debug.LogWarning(
                        "RandomYggdrasil: host mod version differs (" + hostVersion + " vs "
                        + RandomYggdrasilMod.PluginVersion + "). Using local rotation until versions match.");
                    return;
                }

                int count = pkg.ReadInt();
                if (count < 0 || count > 10000)
                {
                    throw new InvalidOperationException("Invalid rotation sync entry count: " + count);
                }

                ClientSyncedRotations.Clear();
                for (int i = 0; i < count; i++)
                {
                    string worldIdentifier = pkg.ReadString();
                    int degrees = pkg.ReadInt();
                    if (degrees >= 0 && degrees < 360 && !string.IsNullOrEmpty(worldIdentifier))
                    {
                        ClientSyncedRotations[worldIdentifier] = degrees;
                    }
                }

                clientSyncReceived = true;
                Debug.Log("RandomYggdrasil: received " + ClientSyncedRotations.Count + " world rotation(s) from server");
                RandomYggdrasilMod.TryApplyStoredRotation();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RandomYggdrasil: config sync failed: " + ex.Message);
            }
        }

        private static void ClearClientSync(string reason)
        {
            if (!clientSyncReceived && ClientSyncedRotations.Count == 0)
            {
                return;
            }

            clientSyncReceived = false;
            ClientSyncedRotations.Clear();
            Debug.Log("RandomYggdrasil: client config sync ended (" + reason + ")");
        }

        public static void ZNetOnNewConnectionPostfix(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null)
            {
                return;
            }

            try
            {
                peer.m_rpc.Register<ZPackage>(RpcSync, ReceiveSyncRpc);
                peer.m_rpc.Register<ZPackage>(RpcSyncRequest, ReceiveSyncRequestRpc);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RandomYggdrasil: config sync RPC registration failed: " + ex.Message);
            }
        }

        public static void ZNetRpcPeerInfoPostfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || rpc == null)
            {
                return;
            }

            const int deferFrames = 2;
            if (__instance.IsServer())
            {
                __instance.StartCoroutine(DeferNetworkFrames(deferFrames, () => SendToRpc(rpc)));
            }
            else if (__instance.GetServerRPC() == rpc)
            {
                __instance.StartCoroutine(DeferNetworkFrames(deferFrames, RequestFromServer));
            }
        }

        public static void ZNetDisconnectPostfix(ZNet __instance, ZNetPeer peer)
        {
            if (__instance != null && !__instance.IsServer() && peer != null && peer.m_server)
            {
                ClearClientSync("disconnect");
            }
        }

        public static void ZNetOnDestroyPrefix(ZNet __instance)
        {
            if (__instance != null && !__instance.IsServer())
            {
                ClearClientSync("network shutdown");
            }
        }
    }
}
