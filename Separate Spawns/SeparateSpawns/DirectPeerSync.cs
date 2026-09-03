using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    /// <summary>
    /// Server/client sync over peer ZRpc (same channel as RemotePrint, PlayerList, etc.).
    /// ZRoutedRpc alone was not reliably delivering payloads to joining clients.
    /// </summary>
    internal static class DirectPeerSync
    {
        private const string RequestRpc = "SeparateSpawns.RequestDirectSync";
        private const string SyncRosterRpc = "SeparateSpawns.SyncRosterDirect";
        private const string SyncLayoutRpc = "SeparateSpawns.SyncLayoutDirect";

        public static void RegisterClientHandlers(ZRpc serverRpc)
        {
            if (serverRpc == null)
            {
                return;
            }

            serverRpc.Register<string>(SyncRosterRpc, (_, json) => RosterSync.ApplyPayload(json, "direct ZRpc"));
            serverRpc.Register<string>(SyncLayoutRpc, (_, json) => LayoutSync.ApplyPayload(json, "direct ZRpc"));
            ModLog.Info("Registered direct Separate Spawns sync handlers on server connection.");
        }

        public static void RegisterServerPeer(ZNetPeer peer)
        {
            if (peer?.m_rpc == null)
            {
                return;
            }

            peer.m_rpc.Register(RequestRpc, _ => SendToPeer(peer));
        }

        public static void RequestFromServer()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || !ClientSyncHelper.CanReachServer())
            {
                return;
            }

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer.m_server && peer.m_rpc != null && peer.m_rpc.IsConnected())
                {
                    ModLog.Info("Requesting Separate Spawns sync via direct ZRpc...");
                    peer.m_rpc.Invoke(RequestRpc);
                    return;
                }
            }

            ModLog.Warning("Could not find connected server peer for direct Separate Spawns sync.");
        }

        public static void SendToPeer(ZNetPeer peer)
        {
            if (peer?.m_rpc == null || !peer.m_rpc.IsConnected() || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            if (Plugin.Roster == null || Plugin.Roster.GetGroupNames().Count == 0)
            {
                RosterSync.LoadServerRosterFromDisk();
            }

            if (Plugin.LayoutCache.Current == null)
            {
                var worldUid = ZNet.instance.GetWorldUID();
                if (worldUid != 0)
                {
                    var existing = WorldLayoutStore.Load(worldUid);
                    if (existing != null)
                    {
                        Plugin.LayoutCache.Set(existing);
                    }
                }
            }

            if (Plugin.Roster != null)
            {
                peer.m_rpc.Invoke(SyncRosterRpc, Plugin.Roster.ToJson());
                ModLog.Info($"Sent roster to peer {peer.m_uid} via direct ZRpc.");
            }
            else
            {
                ModLog.Warning($"Direct roster sync skipped for peer {peer.m_uid}; server roster unavailable.");
            }

            if (Plugin.LayoutCache.Current != null)
            {
                var payload = JsonConvert.SerializeObject(Plugin.LayoutCache.Current, JsonSettings.Compact);
                peer.m_rpc.Invoke(SyncLayoutRpc, payload);
                ModLog.Info(
                    $"Sent layout to peer {peer.m_uid} via direct ZRpc ({Plugin.LayoutCache.Current.GroupSpawnPositions.Count} spawns).");
            }
            else
            {
                ModLog.Warning($"Direct layout sync skipped for peer {peer.m_uid}; server layout unavailable.");
            }
        }
    }
}
