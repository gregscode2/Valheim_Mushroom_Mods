using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class EikthyrPlacer
    {
        private const int MaxPlacementAttempts = 3;

        public static Vector3? EnsureAltarNearSpawn(string groupName, Vector3 spawn, ModConfig config, WorldLayoutData layoutData)
        {
            if (layoutData.SpawnedEikthyrPositions.TryGetValue(groupName, out var existing))
            {
                return existing;
            }

            var catalog = LocationCatalog.Build(config);
            var natural = catalog.FindEikthyrNear(spawn, config.EikthyrReach.Value);
            if (natural.HasValue)
            {
                layoutData.SpawnedEikthyrPositions[groupName] = natural.Value;
                return natural.Value;
            }

            var candidates = FindPlacementCandidates(spawn, config.EikthyrReach.Value, MaxPlacementAttempts);
            if (candidates.Count == 0)
            {
                ModLog.Warning($"Failed to find any Meadows placement for Eikthyr altar for {groupName}.");
                return null;
            }

            for (var attempt = 0; attempt < candidates.Count; attempt++)
            {
                var placement = candidates[attempt];
                if (ZoneSystem.instance.TestSpawnLocation(config.EikthyrLocationName.Value, placement, disableSave: false))
                {
                    layoutData.SpawnedEikthyrPositions[groupName] = placement;
                    catalog.EikthyrAltars.Add(placement);
                    if (attempt > 0)
                    {
                        ModLog.Info($"Placed Eikthyr altar for {groupName} on attempt {attempt + 1}/{candidates.Count}.");
                    }

                    return placement;
                }

                ModLog.Warning($"Eikthyr altar placement attempt {attempt + 1}/{candidates.Count} failed for {groupName} at ({placement.x:F0}, {placement.z:F0}).");
            }

            ModLog.Warning($"Failed to place Eikthyr altar for {groupName} after {candidates.Count} attempts.");
            return null;
        }

        private static List<Vector3> FindPlacementCandidates(Vector3 spawn, float radius, int maxCandidates)
        {
            var generator = WorldGenerator.instance;
            var scored = new List<(Vector3 position, float distance)>();

            for (var ring = 1; ring <= 4; ring++)
            {
                var distance = ring * (radius / 4f);
                for (var i = 0; i < 16; i++)
                {
                    var angle = i * Mathf.PI * 2f / 16f;
                    var candidate = spawn + new Vector3(Mathf.Sin(angle) * distance, 0f, Mathf.Cos(angle) * distance);
                    if (generator.GetBiome(candidate) != Heightmap.Biome.Meadows)
                    {
                        continue;
                    }

                    var height = generator.GetHeight(candidate.x, candidate.z);
                    if (height <= ValheimHeights.WaterSurface)
                    {
                        continue;
                    }

                    candidate.y = height;
                    scored.Add((candidate, Vector3.Distance(spawn, candidate)));
                }
            }

            scored.Sort((a, b) => a.distance.CompareTo(b.distance));

            var results = new List<Vector3>();
            foreach (var entry in scored)
            {
                if (IsTooCloseToExisting(entry.position, results, 10f))
                {
                    continue;
                }

                results.Add(entry.position);
                if (results.Count >= maxCandidates)
                {
                    break;
                }
            }

            return results;
        }

        private static bool IsTooCloseToExisting(Vector3 candidate, List<Vector3> existing, float minSeparation)
        {
            foreach (var other in existing)
            {
                if (Vector3.Distance(candidate, other) < minSeparation)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
