using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class SpawnDifficultyRanker
    {
        private const float MaxMeadowPoints = 4f;
        private const float PlainsPoints = 6f;
        private const float SwampPoints = 2f;
        private const float DefaultDangerRadiusMeters = 200f;

        public static Dictionary<string, int> RankGroups(LayoutAssignment layout, BiomeMapBuilder map,
            float dangerRadiusMeters = DefaultDangerRadiusMeters)
        {
            if (layout?.GroupSpawns == null || layout.GroupSpawns.Count == 0 || map == null)
            {
                return new Dictionary<string, int>();
            }

            var meadowAreas = layout.GroupSpawns.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.MeadowsAreaSquareMeters);
            var positions = layout.GroupSpawns.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Position);
            return RankGroups(positions, meadowAreas, map, dangerRadiusMeters);
        }

        public static Dictionary<string, int> RankGroups(IReadOnlyDictionary<string, Vector3> spawnPositions,
            BiomeMapBuilder map, float dangerRadiusMeters = DefaultDangerRadiusMeters)
        {
            if (spawnPositions == null || spawnPositions.Count == 0 || map == null)
            {
                return new Dictionary<string, int>();
            }

            var meadowAreas = spawnPositions.ToDictionary(
                pair => pair.Key,
                pair => ResolveMeadowsArea(map, pair.Value));
            return RankGroups(spawnPositions, meadowAreas, map, dangerRadiusMeters);
        }

        private static Dictionary<string, int> RankGroups(
            IReadOnlyDictionary<string, Vector3> spawnPositions,
            IReadOnlyDictionary<string, float> meadowAreas,
            BiomeMapBuilder map,
            float dangerRadiusMeters)
        {
            var rawScores = new Dictionary<string, float>();
            var minArea = meadowAreas.Values.Min();
            var maxArea = meadowAreas.Values.Max();
            var areaRange = maxArea - minArea;

            foreach (var pair in spawnPositions)
            {
                meadowAreas.TryGetValue(pair.Key, out var meadowArea);
                var meadowScore = ScoreMeadowSize(meadowArea, minArea, maxArea, areaRange);
                var biomeScore = ScoreNearbyDangerBiome(map, pair.Value, dangerRadiusMeters);
                rawScores[pair.Key] = meadowScore + biomeScore;
                ModLog.Info(
                    $"Spawn difficulty raw score for {pair.Key}: total={rawScores[pair.Key]:F2} (meadow={meadowScore:F2}, biome={biomeScore:F0}, meadowArea={meadowArea:F0}m2).");
            }

            var ranked = rawScores
                .OrderBy(pair => pair.Value)
                .ThenBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => pair.Key)
                .ToList();

            var difficulties = new Dictionary<string, int>();
            for (var i = 0; i < ranked.Count; i++)
            {
                difficulties[ranked[i]] = i + 1;
            }

            return difficulties;
        }

        private static float ScoreMeadowSize(float meadowArea, float minArea, float maxArea, float areaRange)
        {
            if (areaRange <= 0.01f)
            {
                return MaxMeadowPoints;
            }

            return (1f - (meadowArea - minArea) / areaRange) * MaxMeadowPoints;
        }

        private static float ScoreNearbyDangerBiome(BiomeMapBuilder map, Vector3 spawnPosition, float dangerRadiusMeters)
        {
            if (map.HasBiomeWithin(spawnPosition, dangerRadiusMeters, Heightmap.Biome.Plains))
            {
                return PlainsPoints;
            }

            if (map.HasBiomeWithin(spawnPosition, dangerRadiusMeters, Heightmap.Biome.Swamp))
            {
                return SwampPoints;
            }

            return 0f;
        }

        private static float ResolveMeadowsArea(BiomeMapBuilder map, Vector3 position)
        {
            if (!map.TryGetCell(position, out _, out var biome, out var patchId, out _, out _) ||
                biome != Heightmap.Biome.Meadows ||
                patchId < 0)
            {
                return 0f;
            }

            return map.GetPatchAreaSquareMeters(patchId);
        }
    }
}
