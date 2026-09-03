namespace SeparateSpawns
{
    /// <summary>
    /// Mirrors Game.SetConnection so portal pairs connect even when one end is client-owned.
    /// </summary>
    internal static class PortalConnectionHelper
    {
        private const string SetConnectionRpcName = "RPC_SetConnection";

        public static void ConnectPair(ZDO spawnZdo, ZDO stonesZdo)
        {
            if (spawnZdo == null || stonesZdo == null)
            {
                return;
            }

            SetConnection(spawnZdo, stonesZdo.m_uid);
            SetConnection(stonesZdo, spawnZdo.m_uid);
        }

        private static void SetConnection(ZDO portal, ZDOID connection)
        {
            if (portal == null || ZNet.instance == null || ZDOMan.instance == null || ZRoutedRpc.instance == null)
            {
                return;
            }

            var owner = portal.GetOwner();
            var ownerIsConnectedPeer = ZNet.instance.GetPeer(owner) != null;
            if (owner == 0L || !ownerIsConnectedPeer)
            {
                portal.SetOwner(ZDOMan.GetSessionID());
                portal.SetConnection(ZDOExtraData.ConnectionType.Portal, connection);
                ZDOMan.instance.ForceSendZDO(portal.m_uid);
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(owner, SetConnectionRpcName, portal.m_uid, connection);
        }
    }
}
