using UnityEngine;

namespace SeparateSpawns
{
    internal static class GroupSpawnResolver
    {
        private static bool _loggedMissingPlatformId;

        public static Vector3? GetSpawnForLocalPlayer()
        {
            var platformId = PlatformIdHelper.GetLocalPlatformUserId();
            if (string.IsNullOrEmpty(platformId))
            {
                if (!_loggedMissingPlatformId)
                {
                    ModLog.Warning(
                        "Cannot resolve group spawn: local platform user id was empty (Steam/platform APIs may not be ready yet).");
                    _loggedMissingPlatformId = true;
                }

                return null;
            }

            _loggedMissingPlatformId = false;

            return GetSpawnForPlatformUser(platformId);
        }

        public static Vector3? GetSpawnForPlatformUser(string platformUserId)
        {
            if (!RosterIsAvailable())
            {
                return null;
            }

            var normalized = PlatformIdHelper.Normalize(platformUserId);
            var group = ResolveGroupForPlayer(normalized);
            if (string.IsNullOrEmpty(group))
            {
                return null;
            }

            if (Plugin.LayoutCache.Current == null)
            {
                return null;
            }

            var spawn = Plugin.LayoutCache.GetSpawnForGroup(group);
            if (!spawn.HasValue)
            {
                LogGroupLayoutMismatchOnce(group, normalized);
                return null;
            }

            ModLog.Info($"Resolved {normalized} -> {group} spawn ({spawn.Value.x:F0}, {spawn.Value.z:F0}).");
            return spawn.Value;
        }

        public static bool IsSeparateSpawnPending()
        {
            if (Plugin.LayoutCache.Current?.Failed == true)
            {
                return false;
            }

            if (!SeparateSpawnsEnabled())
            {
                return false;
            }

            var platformId = PlatformIdHelper.GetLocalPlatformUserId();
            if (string.IsNullOrEmpty(platformId))
            {
                return true;
            }

            if (!RosterIsAvailable())
            {
                return true;
            }

            var group = ResolveGroupForPlayer(platformId);
            if (string.IsNullOrEmpty(group))
            {
                return true;
            }

            if (Plugin.LayoutCache.Current == null)
            {
                return true;
            }

            if (Plugin.LayoutCache.GetSpawnForGroup(group).HasValue)
            {
                return false;
            }

            // Roster group name does not exist in the frozen world layout.
            return false;
        }

        public static bool HasGroupLayoutMismatch()
        {
            if (Plugin.LayoutCache.Current == null || Plugin.LayoutCache.Current.Failed || !RosterIsAvailable())
            {
                return false;
            }

            var platformId = PlatformIdHelper.GetLocalPlatformUserId();
            if (string.IsNullOrEmpty(platformId))
            {
                return false;
            }

            var group = ResolveGroupForPlayer(platformId);
            return !string.IsNullOrEmpty(group) && !Plugin.LayoutCache.GetSpawnForGroup(group).HasValue;
        }

        public static string GetGroupForLocalPlayer()
        {
            var platformId = PlatformIdHelper.GetLocalPlatformUserId();
            if (string.IsNullOrEmpty(platformId))
            {
                return null;
            }

            return GetGroupNameForPlatformUser(platformId);
        }

        public static string GetGroupNameForPlatformUser(string platformUserId)
        {
            if (string.IsNullOrEmpty(platformUserId) || !RosterIsAvailable())
            {
                return null;
            }

            return ResolveGroupForPlayer(platformUserId);
        }

        public static bool IsLocalPlayerInGroup(string groupName)
        {
            var group = GetGroupForLocalPlayer();
            return !string.IsNullOrEmpty(group) && group == groupName;
        }

        private static bool RosterIsAvailable()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                return Plugin.Roster != null && Plugin.Roster.GetGroupNames().Count > 0;
            }

            return RosterSync.ClientHasRoster && Plugin.Roster != null;
        }

        private static string ResolveGroupForPlayer(string platformUserId)
        {
            var normalized = PlatformIdHelper.Normalize(platformUserId);
            var group = Plugin.Roster.GetGroupForPlayer(normalized);
            if (!string.IsNullOrEmpty(group))
            {
                return group;
            }

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                group = Plugin.Roster.AssignRandomGroup(normalized);
                if (string.IsNullOrEmpty(group))
                {
                    ModLog.Warning($"No groups available for player {normalized}.");
                }

                return group;
            }

            RosterSync.RequestAssignment(normalized);
            return null;
        }

        private static bool SeparateSpawnsEnabled()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                return Plugin.Roster != null && Plugin.Roster.GetGroupNames().Count > 0;
            }

            if (!RosterSync.ClientHasRoster)
            {
                return ZNet.instance != null;
            }

            return Plugin.Roster != null && Plugin.Roster.GetGroupNames().Count > 0;
        }

        private static string _loggedLayoutMismatchGroup;

        private static void LogGroupLayoutMismatchOnce(string group, string platformUserId)
        {
            if (group == _loggedLayoutMismatchGroup)
            {
                return;
            }

            _loggedLayoutMismatchGroup = group;
            var layoutGroups = Plugin.LayoutCache.Current?.GroupSpawnPositions.Keys;
            var available = layoutGroups == null
                ? "(none)"
                : string.Join(", ", layoutGroups);
            ModLog.Error(
                $"Roster group '{group}' for player {platformUserId} has no spawn in this world's layout " +
                $"(layout groups: {available}). Group names must match the roster used when the world was created.");
        }
    }
}
