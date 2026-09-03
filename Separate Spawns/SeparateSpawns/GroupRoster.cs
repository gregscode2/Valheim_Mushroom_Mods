using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class GroupRoster
    {
        public Dictionary<string, GroupEntry> Groups { get; set; } = new Dictionary<string, GroupEntry>();

        private static readonly JsonSerializerSettings RosterJsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new GroupRosterJsonConverter() }
        };

        public static string RosterPath => ModPaths.ResolveConfigPath(ModPaths.RosterFile);

        public static string RosterWritePath => ModPaths.GetWriteConfigPath(ModPaths.RosterFile);

        public static GroupRoster CreateEmpty()
        {
            return new GroupRoster
            {
                Groups = new Dictionary<string, GroupEntry>()
            };
        }

        public static GroupRoster LoadFromDisk()
        {
            var rosterPath = ResolveReadPath();
            if (!File.Exists(rosterPath))
            {
                var sample = CreateSample();
                sample.Save();
                return sample;
            }

            try
            {
                ModLog.Info($"Loading group roster from {rosterPath}.");
                return FromJson(File.ReadAllText(rosterPath));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to load group roster: {ex}");
                return CreateEmpty();
            }
        }

        private static string ResolveReadPath()
        {
            string bestPath = null;
            var bestMemberCount = -1;
            var bestGroupCount = -1;

            foreach (var root in ModPaths.GetConfigRoots())
            {
                var path = Path.Combine(root, ModPaths.RosterFile);
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var roster = FromJson(File.ReadAllText(path));
                    var memberCount = roster.Groups.Values.Sum(entry => entry?.Players?.Count ?? 0);
                    var groupCount = roster.Groups.Count;
                    if (memberCount > bestMemberCount ||
                        (memberCount == bestMemberCount && groupCount > bestGroupCount))
                    {
                        bestPath = path;
                        bestMemberCount = memberCount;
                        bestGroupCount = groupCount;
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"Ignoring unreadable roster at {path}: {ex.Message}");
                }
            }

            return bestPath ?? ModPaths.GetWriteConfigPath(ModPaths.RosterFile);
        }

        public static GroupRoster FromJson(string json)
        {
            var roster = JsonConvert.DeserializeObject<GroupRoster>(json, RosterJsonSettings) ?? CreateEmpty();
            roster.Groups ??= new Dictionary<string, GroupEntry>();
            foreach (var key in roster.Groups.Keys.ToList())
            {
                roster.Groups[key] = roster.Groups[key] ?? new GroupEntry();
                roster.Groups[key].Players ??= new List<string>();
            }

            return roster;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, RosterJsonSettings);
        }

        public void Save()
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                return;
            }

            var rosterPath = RosterWritePath;
            Directory.CreateDirectory(Path.GetDirectoryName(rosterPath));
            File.WriteAllText(rosterPath, ToJson());
        }

        public IReadOnlyList<string> GetGroupNames()
        {
            return Groups.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        public string GetGroupForPlayer(string platformUserId)
        {
            if (string.IsNullOrWhiteSpace(platformUserId))
            {
                return null;
            }

            foreach (var pair in Groups)
            {
                if (pair.Value?.Players != null &&
                    pair.Value.Players.Any(id => PlatformIdHelper.IdsMatch(id, platformUserId)))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        public string AssignRandomGroup(string platformUserId)
        {
            var names = GetGroupNames();
            if (names.Count == 0)
            {
                return null;
            }

            var normalized = PlatformIdHelper.Normalize(platformUserId);
            var group = names[UnityEngine.Random.Range(0, names.Count)];
            if (!Groups.TryGetValue(group, out var entry))
            {
                entry = new GroupEntry();
                Groups[group] = entry;
            }

            entry.Players ??= new List<string>();
            if (!entry.Players.Any(id => PlatformIdHelper.IdsMatch(id, normalized)))
            {
                entry.Players.Add(normalized);
                Save();
                RosterSync.Broadcast();
                ModLog.Info($"Assigned unlisted player {normalized} to {group}.");
            }

            return group;
        }

        public bool NeedsDifficultyAssignment(IEnumerable<string> layoutGroupNames)
        {
            if (layoutGroupNames == null)
            {
                return false;
            }

            foreach (var groupName in layoutGroupNames)
            {
                if (string.IsNullOrEmpty(groupName))
                {
                    continue;
                }

                if (!Groups.TryGetValue(groupName, out var entry) || entry == null || !entry.HasDifficulty)
                {
                    return true;
                }
            }

            return false;
        }

        public void ApplySpawnDifficulties(IReadOnlyDictionary<string, int> difficulties)
        {
            if (difficulties == null || difficulties.Count == 0)
            {
                return;
            }

            foreach (var pair in difficulties)
            {
                if (!Groups.TryGetValue(pair.Key, out var entry))
                {
                    entry = new GroupEntry();
                    Groups[pair.Key] = entry;
                }

                entry.Players ??= new List<string>();
                entry.Difficulty = pair.Value;
                ModLog.Info($"Group {pair.Key} spawn difficulty: {pair.Value}.");
            }

            Save();
            RosterSync.Broadcast();
        }

        private static GroupRoster CreateSample()
        {
            return new GroupRoster
            {
                Groups = new Dictionary<string, GroupEntry>
                {
                    ["groupA"] = new GroupEntry(),
                    ["groupB"] = new GroupEntry()
                }
            };
        }
    }
}
