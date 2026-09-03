using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class LayoutGenerator
    {
        public static LayoutGenerationResult GenerateLayouts(IReadOnlyList<string> groupNames, List<CandidateSpawnPoint> candidates, ModConfig config)
        {
            var result = new LayoutGenerationResult();
            if (groupNames.Count == 0 || candidates.Count == 0)
            {
                return result;
            }

            var maxLayouts = config.MaxLayouts.Value;
            var minDistance = config.MinSpawnDistance.Value;
            // Deterministic seed so the same world always produces the same layouts.
            var random = new System.Random(1337);
            var chosen = new List<CandidateSpawnPoint>();

            for (var attempt = 0; attempt < maxLayouts; attempt++)
            {
                result.TotalAttempts++;
                chosen.Clear();

                foreach (var _ in groupNames)
                {
                    var picked = PickRandomValidCandidate(candidates, chosen, minDistance, random);
                    if (picked == null)
                    {
                        break;
                    }

                    chosen.Add(picked);
                }

                var assignment = BuildAssignment(groupNames, chosen);
                result.LastAttempt = assignment;
                if (assignment.GroupsPlaced > result.BestPartialAttempt.GroupsPlaced)
                {
                    result.BestPartialAttempt = assignment;
                }

                if (chosen.Count < groupNames.Count)
                {
                    continue;
                }

                ScoreLayout(assignment, config);
                result.Layouts.Add(assignment);
                result.ValidLayouts++;
            }

            result.Layouts = result.Layouts.OrderByDescending(layout => layout.Score).ToList();
            return result;
        }

        private static CandidateSpawnPoint PickRandomValidCandidate(List<CandidateSpawnPoint> candidates,
            List<CandidateSpawnPoint> alreadyChosen, float minDistance, System.Random random)
        {
            const int maxTries = 40;
            for (var i = 0; i < maxTries; i++)
            {
                var candidate = candidates[random.Next(candidates.Count)];
                if (alreadyChosen.Contains(candidate))
                {
                    continue;
                }

                if (IsFarEnoughFromOthers(candidate, alreadyChosen, minDistance))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static LayoutAssignment BuildAssignment(IReadOnlyList<string> groupNames, List<CandidateSpawnPoint> chosen)
        {
            var groupSpawns = new Dictionary<string, CandidateSpawnPoint>();
            for (var i = 0; i < chosen.Count && i < groupNames.Count; i++)
            {
                groupSpawns[groupNames[i]] = chosen[i];
            }

            return new LayoutAssignment
            {
                GroupSpawns = groupSpawns,
                GroupsPlaced = groupSpawns.Count,
                Complete = groupSpawns.Count == groupNames.Count
            };
        }

        private static bool IsFarEnoughFromOthers(CandidateSpawnPoint candidate, IEnumerable<CandidateSpawnPoint> others, float minDistance)
        {
            foreach (var other in others)
            {
                if (Vector3.Distance(candidate.Position, other.Position) < minDistance)
                {
                    return false;
                }
            }

            return true;
        }

        public static void ScoreLayout(LayoutAssignment layout, ModConfig config)
        {
            var groups = layout.GroupSpawns.Keys.ToList();
            if (groups.Count == 0)
            {
                layout.Score = 0f;
                return;
            }

            var meadowsAreaSum = 0f;
            foreach (var spawn in layout.GroupSpawns.Values)
            {
                meadowsAreaSum += spawn.MeadowsAreaSquareMeters;
            }

            layout.AverageMeadowsAreaSquareMeters = meadowsAreaSum / groups.Count;

            if (groups.Count < 2)
            {
                layout.Score = 0f;
                return;
            }

            var pairCount = 0;
            var islandMatches = 0f;
            var closestPairDistance = float.MaxValue;

            for (var i = 0; i < groups.Count; i++)
            {
                for (var j = i + 1; j < groups.Count; j++)
                {
                    var a = layout.GroupSpawns[groups[i]];
                    var b = layout.GroupSpawns[groups[j]];
                    pairCount++;

                    if (a.IslandId >= 0 && b.IslandId >= 0 && a.IslandId != b.IslandId)
                    {
                        islandMatches++;
                    }

                    var pairDistance = Vector3.Distance(a.Position, b.Position);
                    if (pairDistance < closestPairDistance)
                    {
                        closestPairDistance = pairDistance;
                    }
                }
            }

            layout.ClosestSpawnDistance = closestPairDistance;
            layout.IslandScore = islandMatches / pairCount * config.ScoreIslands.Value;
            layout.DistanceScore = 0f;
            layout.MeadowsSizeScore = 0f;
            layout.Score = layout.IslandScore;
        }

        public static void ApplyRelativeScores(IReadOnlyList<LayoutAssignment> layouts, ModConfig config)
        {
            if (layouts.Count == 0)
            {
                return;
            }

            var bestClosestDistance = layouts.Max(layout => layout.ClosestSpawnDistance);
            var worstClosestDistance = layouts.Min(layout => layout.ClosestSpawnDistance);
            var bestMeadowsArea = layouts.Max(layout => layout.AverageMeadowsAreaSquareMeters);
            var distanceRange = bestClosestDistance - worstClosestDistance;

            foreach (var layout in layouts)
            {
                // Best (farthest closest pair) → full DistanceWeight; worst (nearest closest pair) → 0.
                layout.DistanceScore = distanceRange > 0.01f
                    ? (layout.ClosestSpawnDistance - worstClosestDistance) / distanceRange * config.ScoreDistance.Value
                    : config.ScoreDistance.Value;

                layout.MeadowsSizeScore = bestMeadowsArea > 0.01f
                    ? layout.AverageMeadowsAreaSquareMeters / bestMeadowsArea * config.ScoreMeadowsSize.Value
                    : 0f;

                layout.Score = layout.IslandScore + layout.DistanceScore + layout.MeadowsSizeScore;
            }
        }

        public static List<LayoutAssignment> SelectDiverseLayouts(IReadOnlyList<LayoutAssignment> sortedLayouts, int count, float diversityDistance)
        {
            var selected = new List<LayoutAssignment>();
            if (count <= 0 || sortedLayouts == null || sortedLayouts.Count == 0)
            {
                return selected;
            }

            foreach (var layout in sortedLayouts)
            {
                var duplicate = false;
                foreach (var existing in selected)
                {
                    if (AreSpatiallySimilar(existing, layout, diversityDistance))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                selected.Add(layout);
                if (selected.Count >= count)
                {
                    break;
                }
            }

            return selected;
        }

        private static bool AreSpatiallySimilar(LayoutAssignment a, LayoutAssignment b, float diversityDistance)
        {
            var positionsA = a.GroupSpawns.Values.Select(spawn => spawn.Position).ToList();
            var positionsB = b.GroupSpawns.Values.Select(spawn => spawn.Position).ToList();
            if (positionsA.Count != positionsB.Count)
            {
                return false;
            }

            var used = new bool[positionsB.Count];
            foreach (var position in positionsA)
            {
                var bestIndex = -1;
                var bestDistance = float.MaxValue;
                for (var i = 0; i < positionsB.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(position, positionsB[i]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || bestDistance > diversityDistance)
                {
                    return false;
                }

                used[bestIndex] = true;
            }

            return true;
        }
    }
}
