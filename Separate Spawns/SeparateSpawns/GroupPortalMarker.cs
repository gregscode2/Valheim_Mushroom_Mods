using System;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class GroupPortalMarker : MonoBehaviour
    {
        internal const string ActivateRpcName = "SeparateSpawns_ActivatePortal";
        public string GroupName;
        public bool IsSpawnEnd;
        public bool Activated;

        public const string ZdoGroupKey = "separate_spawns_group";
        public const string ZdoSpawnEndKey = "separate_spawns_spawn_end";
        public const string ZdoActivatedKey = "separate_spawns_activated";

        private ZNetView _nview;
        private bool _activateRpcRegistered;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            EnsureActivateRpcRegistered();
            LoadFromZdo();
        }

        internal void EnsureActivateRpcRegistered()
        {
            if (_activateRpcRegistered || _nview == null)
            {
                return;
            }

            _nview.Register(ActivateRpcName, new Action<long>(RPC_ActivatePortal));
            _activateRpcRegistered = true;
        }

        public void RPC_ActivatePortal(long sender)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            HandleLegacyActivateRpc(sender);
        }

        internal void HandleLegacyActivateRpc(long sender)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            PortalActivationSync.HandleActivation(sender, _nview.GetZDO().m_uid);
        }

        public void SyncFromZdo()
        {
            LoadFromZdo();
        }

        public static bool TryReadFromZdo(ZDO zdo, out string groupName, out bool isSpawnEnd, out bool activated)
        {
            groupName = null;
            isSpawnEnd = false;
            activated = false;

            if (zdo == null)
            {
                return false;
            }

            groupName = zdo.GetString(ZdoGroupKey);
            if (string.IsNullOrEmpty(groupName))
            {
                groupName = null;
                return false;
            }

            isSpawnEnd = zdo.GetBool(ZdoSpawnEndKey);
            activated = zdo.GetBool(ZdoActivatedKey);
            return true;
        }

        public static GroupPortalMarker AttachFromZdoIfNeeded(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var existing = go.GetComponent<GroupPortalMarker>();
            if (existing != null)
            {
                existing.SyncFromZdo();
                if (string.IsNullOrEmpty(existing.GroupName))
                {
                    existing.LoadFromZdo();
                }

                return string.IsNullOrEmpty(existing.GroupName) ? null : existing;
            }

            var nview = go.GetComponent<ZNetView>();
            var zdo = nview?.GetZDO();
            if (zdo == null)
            {
                return null;
            }

            var groupName = zdo.GetString(ZdoGroupKey);
            if (string.IsNullOrEmpty(groupName))
            {
                return null;
            }

            var marker = go.AddComponent<GroupPortalMarker>();
            marker.EnsureActivateRpcRegistered();
            marker.LoadFromZdo();
            return marker;
        }

        public void LoadFromZdo()
        {
            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            if (_nview?.GetZDO() == null)
            {
                return;
            }

            var zdo = _nview.GetZDO();
            var storedGroup = zdo.GetString(ZdoGroupKey);
            if (storedGroup.Length == 0)
            {
                return;
            }

            GroupName = storedGroup;
            IsSpawnEnd = zdo.GetBool(ZdoSpawnEndKey);
            Activated = zdo.GetBool(ZdoActivatedKey);
        }

        public void Initialize(string groupName, bool isSpawnEnd, bool activated)
        {
            GroupName = groupName;
            IsSpawnEnd = isSpawnEnd;
            Activated = activated;

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            if (_nview?.GetZDO() == null || !_nview.IsOwner())
            {
                return;
            }

            var zdo = _nview.GetZDO();
            zdo.Set(ZdoGroupKey, GroupName);
            zdo.Set(ZdoSpawnEndKey, IsSpawnEnd);
            zdo.Set(ZdoActivatedKey, Activated);
            PortalManager.ApplyGroupTag(zdo, GroupName);
        }

        public void SetActivated(bool activated)
        {
            Activated = activated;
            if (_nview?.GetZDO() != null && _nview.IsOwner())
            {
                _nview.GetZDO().Set(ZdoActivatedKey, activated);
            }
        }
    }
}
