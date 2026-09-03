using HarmonyLib;

namespace SeparateSpawns.Patches
{
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class ZNetPeerInfoPatch
    {
        private static ZNetPeer FindPeer(ZNet net, ZRpc rpc)
        {
            foreach (var peer in net.GetPeers())
            {
                if (peer.m_rpc == rpc)
                {
                    return peer;
                }
            }

            return null;
        }

        private static void Postfix(ZNet __instance, ZRpc rpc)
        {
            var peer = FindPeer(__instance, rpc);
            if (peer == null)
            {
                return;
            }

            if (__instance.IsServer())
            {
                DirectPeerSync.RegisterServerPeer(peer);
                DirectPeerSync.SendToPeer(peer);
                return;
            }

            DirectPeerSync.RegisterClientHandlers(rpc);
            DirectPeerSync.RequestFromServer();
        }
    }
}
