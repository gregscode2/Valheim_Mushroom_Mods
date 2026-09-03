using UnityEngine;

namespace SeparateSpawns
{
    /// <summary>
    /// Portal activation via ZRoutedRpc so dedicated-server clients always reach the server.
    /// When the server has no loaded Player body (common on headless servers at distant spawns),
    /// core payment is delegated back to the owning client before the server commits activation.
    /// </summary>
    internal static class PortalActivationSync
    {
        internal const string RpcName = "SeparateSpawns.ActivatePortal";
        internal const string PrepareConsumeRpcName = "SeparateSpawns.PreparePortalConsume";
        internal const string FinishRpcName = "SeparateSpawns.FinishPortalActivation";
        internal const string CommittedRpcName = "SeparateSpawns.PortalActivationCommitted";

        private static bool _registered;
        private static ZRoutedRpc _registeredInstance;

        public static void Register()
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            if (_registered && ReferenceEquals(_registeredInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register<ZDOID>(RpcName, OnActivatePortal);
            ZRoutedRpc.instance.Register<ZDOID>(PrepareConsumeRpcName, OnPrepareConsumePortal);
            ZRoutedRpc.instance.Register<ZDOID>(FinishRpcName, OnFinishPortalActivation);
            ZRoutedRpc.instance.Register<string>(CommittedRpcName, OnPortalActivationCommitted);
            _registered = true;
            _registeredInstance = ZRoutedRpc.instance;
        }

        public static bool RequestActivation(GroupPortalMarker marker)
        {
            if (marker == null || ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
            {
                return false;
            }

            var nview = marker.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                ModLog.Warning("Portal activation request failed: portal ZNetView is invalid.");
                return false;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(RpcName, nview.GetZDO().m_uid);
            return true;
        }

        private static void OnActivatePortal(long sender, ZDOID portalId)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            HandleActivation(sender, portalId);
        }

        internal static void HandleActivation(long senderPeerId, ZDOID portalId)
        {
            if (portalId.IsNone())
            {
                ModLog.Warning($"Portal activation from peer {senderPeerId} ignored: portal id is none.");
                return;
            }

            if (ZDOMan.instance == null)
            {
                ModLog.Warning($"Portal activation from peer {senderPeerId} ignored: world not ready.");
                return;
            }

            var zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo == null)
            {
                ModLog.Warning($"Portal activation from peer {senderPeerId} ignored: portal ZDO {portalId} not found.");
                return;
            }

            if (!GroupPortalMarker.TryReadFromZdo(zdo, out var groupName, out var isSpawnEnd, out var activated))
            {
                ModLog.Warning($"Portal activation from peer {senderPeerId} ignored: {portalId} is not a group portal.");
                return;
            }

            if (!isSpawnEnd || activated)
            {
                return;
            }

            if (!PortalManager.IsPeerInGroup(senderPeerId, groupName))
            {
                var platformId = PlatformIdHelper.GetPlatformUserIdFromPeer(senderPeerId);
                ModLog.Info(
                    $"Portal activation rejected for peer {senderPeerId} ({platformId}): not in group {groupName}.");
                PortalManager.MessagePeer(senderPeerId, MessageHud.MessageType.Center, "This portal belongs to another group.");
                return;
            }

            var player = PlatformIdHelper.GetPlayerFromPeerId(senderPeerId);
            if (player != null)
            {
                PortalManager.TryActivatePortalServer(zdo, player);
                return;
            }

            ModLog.Info(
                $"Portal activation for peer {senderPeerId}: player body not loaded on server; requesting client-side core payment.");
            ZRoutedRpc.instance.InvokeRoutedRPC(senderPeerId, PrepareConsumeRpcName, portalId);
        }

        private static void OnPrepareConsumePortal(long sender, ZDOID portalId)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            if (portalId.IsNone() || ZRoutedRpc.instance == null)
            {
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null)
            {
                ModLog.Warning("Portal core payment skipped: local player is unavailable.");
                return;
            }

            if (!PortalManager.TryConsumePortalCost(player, out var costMessage))
            {
                if (!string.IsNullOrEmpty(costMessage))
                {
                    player.Message(MessageHud.MessageType.Center, costMessage);
                }

                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(FinishRpcName, portalId);
        }

        private static void OnFinishPortalActivation(long sender, ZDOID portalId)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            if (portalId.IsNone() || ZDOMan.instance == null)
            {
                return;
            }

            var zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo == null)
            {
                ModLog.Warning($"Portal finish from peer {sender} ignored: portal ZDO {portalId} not found.");
                return;
            }

            if (!GroupPortalMarker.TryReadFromZdo(zdo, out var groupName, out var isSpawnEnd, out var activated))
            {
                ModLog.Warning($"Portal finish from peer {sender} ignored: {portalId} is not a group portal.");
                return;
            }

            if (!isSpawnEnd || activated)
            {
                return;
            }

            if (!PortalManager.IsPeerInGroup(sender, groupName))
            {
                ModLog.Warning($"Portal finish from peer {sender} ignored: not in group {groupName}.");
                return;
            }

            PortalManager.FinalizePortalActivation(groupName, sender);
        }

        private static void OnPortalActivationCommitted(long sender, string groupName)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || string.IsNullOrEmpty(groupName))
            {
                return;
            }

            PortalManager.RefreshClientPortalState(groupName);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Group portal activated.");
        }
    }
}
