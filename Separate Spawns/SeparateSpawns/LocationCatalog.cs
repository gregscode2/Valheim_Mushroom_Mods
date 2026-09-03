using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class LocationCatalog
    {
        public Vector3 SacrificialStones { get; private set; }
        public List<Vector3> BurialChambers { get; } = new List<Vector3>();
        public List<Vector3> EikthyrAltars { get; } = new List<Vector3>();

        public static LocationCatalog Build(ModConfig config)
        {
            var catalog = new LocationCatalog();
            var burialNames = new HashSet<string>(config.GetBurialChamberNames().Select(name => name.Trim()));
            var eikthyrName = config.EikthyrLocationName.Value.Trim();
            var startTempleName = config.StartTempleLocationName.Value.Trim();

            if (ZoneSystem.instance.GetLocationIcon(startTempleName, out var stones))
            {
                catalog.SacrificialStones = stones;
            }

            foreach (var instance in EnumerateLocations())
            {
                var prefabName = instance.m_location.m_prefabName;
                if (string.IsNullOrEmpty(prefabName))
                {
                    prefabName = instance.m_location.m_name;
                }

                if (burialNames.Contains(prefabName))
                {
                    catalog.BurialChambers.Add(instance.m_position);
                }

                if (prefabName == eikthyrName)
                {
                    catalog.EikthyrAltars.Add(instance.m_position);
                }
            }

            ModLog.Info(
                $"Location catalog: burial_chambers={catalog.BurialChambers.Count} (names={string.Join(",", burialNames)}), eikthyr={catalog.EikthyrAltars.Count}, stones={catalog.SacrificialStones}");
            return catalog;
        }

        private static IEnumerable<ZoneSystem.LocationInstance> EnumerateLocations()
        {
            var method = AccessTools.Method(typeof(ZoneSystem), "GetLocationList");
            if (method == null || ZoneSystem.instance == null)
            {
                yield break;
            }

            if (!(method.Invoke(ZoneSystem.instance, null) is IEnumerable locations))
            {
                yield break;
            }

            foreach (ZoneSystem.LocationInstance instance in locations)
            {
                yield return instance;
            }
        }

        public int CountBurialChambersNear(Vector3 spawn, float radius, BiomeMapBuilder map)
        {
            var count = 0;
            foreach (var chamber in BurialChambers)
            {
                if (Vector3.Distance(spawn, chamber) > radius)
                {
                    continue;
                }

                if (map.TryGetCell(chamber, out _, out var biome, out _, out _, out var isLand) &&
                    isLand &&
                    biome == Heightmap.Biome.BlackForest)
                {
                    count++;
                }
            }

            return count;
        }

        public Vector3? FindEikthyrNear(Vector3 spawn, float radius)
        {
            Vector3? closest = null;
            var bestDistance = float.MaxValue;
            foreach (var altar in EikthyrAltars)
            {
                var distance = Vector3.Distance(spawn, altar);
                if (distance <= radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = altar;
                }
            }

            return closest;
        }
    }
}
