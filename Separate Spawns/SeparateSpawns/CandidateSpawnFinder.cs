using System.Collections.Generic;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class CandidateSpawnFinder
    {
        public sealed class RejectionStats
        {
            public int NotMeadows;
            public int NoNearbyForest;
            public int NoAdjacentCoast;
            public int TooCloseToStones;
            public int NotEnoughChambers;
            public int Underwater;
            public int TotalChecked;
            public int Accepted;
        }

        public static List<CandidateSpawnPoint> Find(BiomeMapBuilder map, LocationCatalog locations, ModConfig config, out RejectionStats stats)
        {
            stats = new RejectionStats();
            var candidates = new List<CandidateSpawnPoint>();
            var generator = WorldGenerator.instance;
            var forestProximity = config.BlackForestProximity.Value;

            foreach (var gridPoint in map.EnumerateGridPoints())
            {
                stats.TotalChecked++;
                if (!map.TryGetCell(gridPoint, out _, out var biome, out var patchId, out var islandId, out var isLand))
                {
                    continue;
                }

                if (biome != Heightmap.Biome.Meadows)
                {
                    stats.NotMeadows++;
                    continue;
                }

                if (!map.HasBlackForestWithin(gridPoint, forestProximity))
                {
                    stats.NoNearbyForest++;
                    continue;
                }

                if (!map.HasAdjacentCoast(patchId))
                {
                    stats.NoAdjacentCoast++;
                    continue;
                }

                if (locations.SacrificialStones != Vector3.zero &&
                    Vector3.Distance(gridPoint, locations.SacrificialStones) < config.MinStonesDistance.Value)
                {
                    stats.TooCloseToStones++;
                    continue;
                }

                var height = generator.GetHeight(gridPoint.x, gridPoint.z);
                if (height <= ValheimHeights.WaterSurface)
                {
                    stats.Underwater++;
                    continue;
                }

                var chamberCount = locations.CountBurialChambersNear(
                    gridPoint,
                    forestProximity,
                    map);
                if (chamberCount < config.MinBurialChambers.Value)
                {
                    stats.NotEnoughChambers++;
                    continue;
                }

                stats.Accepted++;
                candidates.Add(new CandidateSpawnPoint
                {
                    Position = new Vector3(gridPoint.x, height, gridPoint.z),
                    MeadowsPatchId = patchId,
                    AdjacentForestPatchId = map.FindNearestBlackForestPatchId(gridPoint, forestProximity),
                    IslandId = islandId,
                    NearbyBurialChambers = chamberCount,
                    MeadowsAreaSquareMeters = map.GetPatchAreaSquareMeters(patchId),
                    ExistingEikthyr = locations.FindEikthyrNear(gridPoint, config.EikthyrReach.Value)
                });
            }

            return candidates;
        }
    }
}
