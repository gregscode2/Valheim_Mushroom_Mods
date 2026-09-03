using System.Collections;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class ClientSyncHelper
    {
        public static bool CanReachServer()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
            {
                return false;
            }

            return ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        public static void ResetClientState()
        {
            RosterSync.ResetClientState();
        }

        public static IEnumerator RunSyncRetry(MonoBehaviour host)
        {
            var wasConnected = false;

            while (host != null)
            {
                if (!CanReachServer())
                {
                    if (wasConnected)
                    {
                        ResetClientState();
                        ModLog.Info("Server disconnected; waiting to resync Separate Spawns data.");
                    }

                    wasConnected = false;
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (!wasConnected)
                {
                    ModLog.Info("Connected to server; syncing Separate Spawns roster and layout.");
                    wasConnected = true;
                }

                RosterSync.Register();
                LayoutSync.Register();
                PortalActivationSync.Register();

                if (!RosterSync.ClientHasRoster || Plugin.LayoutCache.Current == null)
                {
                    DirectPeerSync.RequestFromServer();
                }

                if (!RosterSync.ClientHasRoster)
                {
                    RosterSync.RequestFromServer();
                }

                if (Plugin.LayoutCache.Current == null)
                {
                    LayoutSync.RequestLayoutFromServer();
                }

                if (RosterSync.ClientHasRoster && Plugin.LayoutCache.Current != null)
                {
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                yield return new WaitForSeconds(2f);
            }
        }
    }
}
