using HarmonyLib;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(ZoneSystem), "OnNewPeer")]
    internal static class ZoneSystemOnNewPeerPatch
    {
        private static void Postfix(long peerID)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            var peer = ZNet.instance.GetPeer(peerID);
            if (peer != null)
            {
                DirectPeerSync.SendToPeer(peer);
            }

            RosterSync.SendToPeer(peerID);
            if (Plugin.LayoutCache.Current != null)
            {
                LayoutSync.SendToPeer(peerID, Plugin.LayoutCache.Current);
            }

            ModLog.Info($"Pushed Separate Spawns roster/layout to peer {peerID}.");
        }
    }
}
