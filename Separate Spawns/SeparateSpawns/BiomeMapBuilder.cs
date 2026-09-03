using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class BiomeMapBuilder
    {
        private readonly float _biomeStep;
        private readonly float _gridStep;
        private readonly float _innerRadius;
        private readonly int _width;
        private readonly int _height;
        private readonly Vector2 _origin;
        private readonly Heightmap.Biome[] _biomes;
        private readonly bool[] _land;
        private readonly int[] _biomePatchIds;
        private readonly int[] _islandIds;
        private readonly Dictionary<int, BiomePatchInfo> _patchesById = new Dictionary<int, BiomePatchInfo>();

        public IReadOnlyDictionary<int, HashSet<int>> MeadowsPatchNeighbors { get; private set; }
        public IReadOnlyDictionary<int, HashSet<int>> ForestPatchNeighbors { get; private set; }
        public IReadOnlyCollection<int> MeadowsPatchesTouchingCoast { get; private set; } = Array.Empty<int>();
        public IReadOnlyList<BiomePatchInfo> PatchStatistics { get; private set; } = Array.Empty<BiomePatchInfo>();

        private BiomeMapBuilder(float biomeStep, float gridStep, float innerRadius, int width, int height, Vector2 origin,
            Heightmap.Biome[] biomes, bool[] land, int[] biomePatchIds, int[] islandIds,
            Dictionary<int, HashSet<int>> meadowsNeighbors, Dictionary<int, HashSet<int>> forestNeighbors,
            HashSet<int> meadowsTouchingCoast)
        {
            _biomeStep = biomeStep;
            _gridStep = gridStep;
            _innerRadius = innerRadius;
            _width = width;
            _height = height;
            _origin = origin;
            _biomes = biomes;
            _land = land;
            _biomePatchIds = biomePatchIds;
            _islandIds = islandIds;
            MeadowsPatchNeighbors = meadowsNeighbors;
            ForestPatchNeighbors = forestNeighbors;
            MeadowsPatchesTouchingCoast = meadowsTouchingCoast;
        }

        public static BiomeMapBuilder Build(float biomeStep, float gridStep, float innerRadius, float biomeSplitGapDistance,
            float islandSplitGapDistance, float minPatchArea)
        {
            var generator = WorldGenerator.instance;
            var width = Mathf.FloorToInt((innerRadius * 2f) / biomeStep) + 1;
            var height = width;
            var origin = new Vector2(-innerRadius, -innerRadius);
            var biomes = new Heightmap.Biome[width * height];
            var land = new bool[width * height];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var wx = origin.x + x * biomeStep;
                    var wz = origin.y + y * biomeStep;
                    var index = y * width + x;
                    if (new Vector2(wx, wz).magnitude > innerRadius)
                    {
                        biomes[index] = Heightmap.Biome.None;
                        land[index] = false;
                        continue;
                    }

                    biomes[index] = generator.GetBiome(wx, wz);
                    var cellHeight = generator.GetHeight(wx, wz);
                    land[index] = biomes[index] != Heightmap.Biome.Ocean && cellHeight > ValheimHeights.WaterSurface;
                }
            }

            var biomePatchIds = FloodFillLandPatches(biomes, land, width, height);
            MergeLandPatchesAcrossNarrowGaps(biomePatchIds, biomes, land, width, height, biomeStep, biomeSplitGapDistance);
            AbsorbSmallPatches(biomePatchIds, biomes, land, width, height, biomeStep, minPatchArea);
            var islandIds = FloodFillIslands(land, width, height);
            MergeIslandsAcrossNarrowGaps(islandIds, land, width, height, biomeStep, islandSplitGapDistance);
            BuildAdjacency(biomePatchIds, biomes, land, width, height, out var meadowsNeighbors, out var forestNeighbors);
            var meadowsTouchingCoast = FindMeadowsTouchingCoast(biomePatchIds, biomes, land, width, height);

            var builder = new BiomeMapBuilder(biomeStep, gridStep, innerRadius, width, height, origin, biomes, land, biomePatchIds, islandIds,
                meadowsNeighbors, forestNeighbors, meadowsTouchingCoast);
            builder.BuildPatchStatistics();
            return builder;
        }

        public void CountBurialChambers(LocationCatalog locations)
        {
            foreach (var chamber in locations.BurialChambers)
            {
                if (!TryGetCell(chamber, out _, out _, out var patchId, out _, out _) || patchId < 0)
                {
                    continue;
                }

                if (_patchesById.TryGetValue(patchId, out var patch))
                {
                    patch.BurialChamberCount++;
                }
            }
        }

        public string GetPatchName(int patchId)
        {
            return _patchesById.TryGetValue(patchId, out var patch) ? patch.Name : $"patch_{patchId}";
        }

        public static void AppendPatchStatistics(StringBuilder summary, IReadOnlyList<BiomePatchInfo> patches)
        {
            summary.AppendLine("Biome patch statistics:");
            summary.AppendLine("  (land patches of the same biome; patches under MinPatchArea absorbed into their largest neighbor; merged across water gaps up to BiomeSplitGapDistance; height <= 30 treated as water)");
            foreach (var group in patches.GroupBy(p => p.Biome).OrderBy(g => g.Key.ToString()))
            {
                summary.AppendLine($"  {group.Key}: {group.Count()} patches");
            }

            summary.AppendLine("  name | biome | land_area_m2 | burial_chambers | center_x | center_z");
            foreach (var patch in patches.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                summary.AppendLine(
                    $"  {patch.Name} | {patch.Biome} | {patch.ApproximateAreaSquareMeters:F0} | {patch.BurialChamberCount} | {patch.Center.x:F0} | {patch.Center.y:F0}");
            }

            summary.AppendLine();
        }

        public void LogPatchStatistics()
        {
            foreach (var patch in PatchStatistics)
            {
                ModLog.Info(
                    $"Biome {patch.Name}: ~{patch.ApproximateAreaSquareMeters:F0}m², burial_chambers={patch.BurialChamberCount}, center=({patch.Center.x:F0}, {patch.Center.y:F0})");
            }
        }

        private void BuildPatchStatistics()
        {
            var accumulators = new Dictionary<int, (Heightmap.Biome biome, int landCellCount, float sumX, float sumZ)>();

            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var index = y * _width + x;
                    var patchId = _biomePatchIds[index];
                    if (patchId < 0 || !IsBiomePatchCell(_biomes[index]) || !_land[index])
                    {
                        continue;
                    }

                    var wx = _origin.x + x * _biomeStep;
                    var wz = _origin.y + y * _biomeStep;
                    if (!accumulators.TryGetValue(patchId, out var accumulator))
                    {
                        accumulator = (_biomes[index], 0, 0f, 0f);
                    }

                    accumulator.landCellCount++;
                    accumulator.sumX += wx;
                    accumulator.sumZ += wz;
                    accumulators[patchId] = accumulator;
                }
            }

            var biomeCounters = new Dictionary<Heightmap.Biome, int>();
            var patches = new List<BiomePatchInfo>();
            var cellArea = _biomeStep * _biomeStep;

            foreach (var pair in accumulators.OrderBy(entry => entry.Key))
            {
                var patchId = pair.Key;
                var accumulator = pair.Value;
                if (accumulator.landCellCount <= 0)
                {
                    continue;
                }

                if (!biomeCounters.TryGetValue(accumulator.biome, out var biomeIndex))
                {
                    biomeIndex = 0;
                }

                biomeIndex++;
                biomeCounters[accumulator.biome] = biomeIndex;

                var patch = new BiomePatchInfo
                {
                    PatchId = patchId,
                    Name = FormatPatchName(accumulator.biome, biomeIndex),
                    Biome = accumulator.biome,
                    CellCount = accumulator.landCellCount,
                    ApproximateAreaSquareMeters = accumulator.landCellCount * cellArea,
                    Center = new Vector2(accumulator.sumX / accumulator.landCellCount, accumulator.sumZ / accumulator.landCellCount)
                };

                patches.Add(patch);
                _patchesById[patchId] = patch;
            }

            PatchStatistics = patches;
        }

        private static bool IsBiomePatchCell(Heightmap.Biome biome)
        {
            return biome != Heightmap.Biome.None && biome != Heightmap.Biome.Ocean;
        }

        private static string FormatPatchName(Heightmap.Biome biome, int index)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                    return $"meadows_{index}";
                case Heightmap.Biome.BlackForest:
                    return $"blackforest_{index}";
                case Heightmap.Biome.Swamp:
                    return $"swamp_{index}";
                case Heightmap.Biome.Mountain:
                    return $"mountain_{index}";
                case Heightmap.Biome.Plains:
                    return $"plains_{index}";
                case Heightmap.Biome.Mistlands:
                    return $"mistlands_{index}";
                case Heightmap.Biome.AshLands:
                    return $"ashlands_{index}";
                case Heightmap.Biome.DeepNorth:
                    return $"deepnorth_{index}";
                case Heightmap.Biome.Ocean:
                    return $"ocean_{index}";
                default:
                    return $"{biome.ToString().ToLowerInvariant()}_{index}";
            }
        }

        public bool TryGetCell(Vector3 worldPosition, out int index, out Heightmap.Biome biome, out int biomePatchId, out int islandId, out bool isLand)
        {
            index = -1;
            biome = Heightmap.Biome.None;
            biomePatchId = -1;
            islandId = -1;
            isLand = false;

            var x = Mathf.RoundToInt((worldPosition.x - _origin.x) / _biomeStep);
            var y = Mathf.RoundToInt((worldPosition.z - _origin.y) / _biomeStep);
            if (x < 0 || y < 0 || x >= _width || y >= _height)
            {
                return false;
            }

            index = y * _width + x;
            biome = _biomes[index];
            biomePatchId = _biomePatchIds[index];
            islandId = _islandIds[index];
            isLand = _land[index];
            return true;
        }

        public bool HasAdjacentBlackForest(int meadowsPatchId)
        {
            return MeadowsPatchNeighbors.TryGetValue(meadowsPatchId, out var neighbors) && neighbors.Count > 0;
        }

        public bool HasBiomeWithin(Vector3 worldPosition, float radiusMeters, Heightmap.Biome targetBiome)
        {
            if (radiusMeters <= 0f || targetBiome == Heightmap.Biome.None || targetBiome == Heightmap.Biome.Ocean)
            {
                return false;
            }

            var centerX = Mathf.RoundToInt((worldPosition.x - _origin.x) / _biomeStep);
            var centerY = Mathf.RoundToInt((worldPosition.z - _origin.y) / _biomeStep);
            var cellRadius = Mathf.CeilToInt(radiusMeters / _biomeStep);
            var radiusSquared = radiusMeters * radiusMeters;

            for (var dy = -cellRadius; dy <= cellRadius; dy++)
            {
                for (var dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;
                    if (x < 0 || y < 0 || x >= _width || y >= _height)
                    {
                        continue;
                    }

                    var index = y * _width + x;
                    if (!_land[index] || _biomes[index] != targetBiome)
                    {
                        continue;
                    }

                    var wx = _origin.x + x * _biomeStep;
                    var wz = _origin.y + y * _biomeStep;
                    var deltaX = wx - worldPosition.x;
                    var deltaZ = wz - worldPosition.z;
                    if (deltaX * deltaX + deltaZ * deltaZ <= radiusSquared)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool HasBlackForestWithin(Vector3 worldPosition, float radiusMeters)
        {
            return HasBiomeWithin(worldPosition, radiusMeters, Heightmap.Biome.BlackForest);
        }

        public int FindNearestBlackForestPatchId(Vector3 worldPosition, float radiusMeters)
        {
            if (radiusMeters <= 0f)
            {
                return -1;
            }

            var centerX = Mathf.RoundToInt((worldPosition.x - _origin.x) / _biomeStep);
            var centerY = Mathf.RoundToInt((worldPosition.z - _origin.y) / _biomeStep);
            var cellRadius = Mathf.CeilToInt(radiusMeters / _biomeStep);
            var radiusSquared = radiusMeters * radiusMeters;
            var bestPatchId = -1;
            var bestDistanceSquared = float.MaxValue;

            for (var dy = -cellRadius; dy <= cellRadius; dy++)
            {
                for (var dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;
                    if (x < 0 || y < 0 || x >= _width || y >= _height)
                    {
                        continue;
                    }

                    var index = y * _width + x;
                    if (!_land[index] || _biomes[index] != Heightmap.Biome.BlackForest || _biomePatchIds[index] < 0)
                    {
                        continue;
                    }

                    var wx = _origin.x + x * _biomeStep;
                    var wz = _origin.y + y * _biomeStep;
                    var deltaX = wx - worldPosition.x;
                    var deltaZ = wz - worldPosition.z;
                    var distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
                    if (distanceSquared <= radiusSquared && distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        bestPatchId = _biomePatchIds[index];
                    }
                }
            }

            return bestPatchId;
        }

        public bool HasAdjacentCoast(int meadowsPatchId)
        {
            return MeadowsPatchesTouchingCoast.Contains(meadowsPatchId);
        }

        public float GetPatchAreaSquareMeters(int patchId)
        {
            return _patchesById.TryGetValue(patchId, out var patch) ? patch.ApproximateAreaSquareMeters : 0f;
        }

        public bool IsOnCandidateGrid(Vector3 position)
        {
            var snappedX = Mathf.Round((position.x - _origin.x) / _gridStep) * _gridStep + _origin.x;
            var snappedZ = Mathf.Round((position.z - _origin.y) / _gridStep) * _gridStep + _origin.y;
            return Mathf.Abs(position.x - snappedX) < 0.01f && Mathf.Abs(position.z - snappedZ) < 0.01f;
        }

        public IEnumerable<Vector3> EnumerateGridPoints()
        {
            var width = Mathf.FloorToInt((_innerRadius * 2f) / _gridStep) + 1;
            var height = width;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var wx = _origin.x + x * _gridStep;
                    var wz = _origin.y + y * _gridStep;
                    if (new Vector2(wx, wz).magnitude > _innerRadius)
                    {
                        continue;
                    }

                    yield return new Vector3(wx, 0f, wz);
                }
            }
        }

        public int GetIslandId(Vector3 position)
        {
            return TryGetCell(position, out _, out _, out _, out var islandId, out _) ? islandId : -1;
        }

        public int GetMeadowsPatchId(Vector3 position)
        {
            if (!TryGetCell(position, out _, out var biome, out var patchId, out _, out _))
            {
                return -1;
            }

            return biome == Heightmap.Biome.Meadows ? patchId : -1;
        }

        private static int[] FloodFillLandPatches(Heightmap.Biome[] biomes, bool[] land, int width, int height)
        {
            var patchIds = new int[width * height];
            for (var i = 0; i < patchIds.Length; i++)
            {
                patchIds[i] = -1;
            }

            var nextId = 0;
            var queue = new Queue<int>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var start = y * width + x;
                    if (patchIds[start] != -1 || !land[start] || !IsBiomePatchCell(biomes[start]))
                    {
                        continue;
                    }

                    var biome = biomes[start];
                    patchIds[start] = nextId;
                    queue.Enqueue(start);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        var cx = current % width;
                        var cy = current / width;
                        TryEnqueueLandPatchNeighbor(cx - 1, cy, biome, biomes, land, patchIds, width, height, nextId, queue);
                        TryEnqueueLandPatchNeighbor(cx + 1, cy, biome, biomes, land, patchIds, width, height, nextId, queue);
                        TryEnqueueLandPatchNeighbor(cx, cy - 1, biome, biomes, land, patchIds, width, height, nextId, queue);
                        TryEnqueueLandPatchNeighbor(cx, cy + 1, biome, biomes, land, patchIds, width, height, nextId, queue);
                    }

                    nextId++;
                }
            }

            return patchIds;
        }

        private static void TryEnqueueLandPatchNeighbor(int x, int y, Heightmap.Biome biome, Heightmap.Biome[] biomes, bool[] land,
            int[] patchIds, int width, int height, int patchId, Queue<int> queue)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (!land[index] || patchIds[index] != -1 || biomes[index] != biome)
            {
                return;
            }

            patchIds[index] = patchId;
            queue.Enqueue(index);
        }

        private static void AbsorbSmallPatches(int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height,
            float biomeStep, float minAreaSquareMeters)
        {
            if (minAreaSquareMeters <= 0f)
            {
                return;
            }

            var cellArea = biomeStep * biomeStep;
            var minCells = Mathf.CeilToInt(minAreaSquareMeters / cellArea);

            var cellsByPatch = new Dictionary<int, List<int>>();
            for (var i = 0; i < patchIds.Length; i++)
            {
                if (!land[i] || patchIds[i] < 0)
                {
                    continue;
                }

                if (!cellsByPatch.TryGetValue(patchIds[i], out var cells))
                {
                    cells = new List<int>();
                    cellsByPatch[patchIds[i]] = cells;
                }

                cells.Add(i);
            }

            // Smallest patches first, so specks merge into real patches before those are considered.
            var smallPatchIds = cellsByPatch
                .Where(pair => pair.Value.Count < minCells)
                .OrderBy(pair => pair.Value.Count)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var patchId in smallPatchIds)
            {
                var cells = cellsByPatch[patchId];
                if (cells.Count == 0 || cells.Count >= minCells)
                {
                    continue;
                }

                var borderCounts = new Dictionary<int, int>();
                foreach (var cell in cells)
                {
                    var cx = cell % width;
                    var cy = cell / width;
                    CountBorderNeighbor(cx - 1, cy, patchId, patchIds, land, width, height, borderCounts);
                    CountBorderNeighbor(cx + 1, cy, patchId, patchIds, land, width, height, borderCounts);
                    CountBorderNeighbor(cx, cy - 1, patchId, patchIds, land, width, height, borderCounts);
                    CountBorderNeighbor(cx, cy + 1, patchId, patchIds, land, width, height, borderCounts);
                }

                if (borderCounts.Count == 0)
                {
                    // Tiny islet with no land neighbor; leave as-is.
                    continue;
                }

                var targetPatchId = -1;
                var bestBorder = -1;
                foreach (var pair in borderCounts)
                {
                    if (pair.Value > bestBorder)
                    {
                        bestBorder = pair.Value;
                        targetPatchId = pair.Key;
                    }
                }

                var targetBiome = biomes[cellsByPatch[targetPatchId][0]];
                foreach (var cell in cells)
                {
                    patchIds[cell] = targetPatchId;
                    biomes[cell] = targetBiome;
                }

                cellsByPatch[targetPatchId].AddRange(cells);
                cells.Clear();
            }
        }

        private static void CountBorderNeighbor(int x, int y, int patchId, int[] patchIds, bool[] land, int width, int height,
            Dictionary<int, int> borderCounts)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (!land[index] || patchIds[index] < 0 || patchIds[index] == patchId)
            {
                return;
            }

            borderCounts.TryGetValue(patchIds[index], out var count);
            borderCounts[patchIds[index]] = count + 1;
        }

        private static void MergeLandPatchesAcrossNarrowGaps(int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height,
            float biomeStep, float maxGapMeters)
        {
            if (maxGapMeters <= 0f)
            {
                return;
            }

            var patchBiomes = new Dictionary<int, Heightmap.Biome>();
            var maxPatchId = -1;
            for (var i = 0; i < patchIds.Length; i++)
            {
                if (patchIds[i] < 0 || !land[i])
                {
                    continue;
                }

                patchBiomes[patchIds[i]] = biomes[i];
                maxPatchId = Math.Max(maxPatchId, patchIds[i]);
            }

            if (maxPatchId < 0)
            {
                return;
            }

            var unionFind = new UnionFind(maxPatchId + 1);
            foreach (var patchId in patchBiomes.Keys)
            {
                var reachable = FindSameBiomePatchesWithinGap(
                    patchIds,
                    biomes,
                    land,
                    width,
                    height,
                    biomeStep,
                    maxGapMeters,
                    patchId,
                    patchBiomes[patchId]);

                foreach (var otherPatchId in reachable)
                {
                    unionFind.Union(patchId, otherPatchId);
                }
            }

            var remap = new Dictionary<int, int>();
            var nextId = 0;
            for (var i = 0; i < patchIds.Length; i++)
            {
                if (patchIds[i] < 0)
                {
                    continue;
                }

                var root = unionFind.Find(patchIds[i]);
                if (!remap.TryGetValue(root, out var mappedId))
                {
                    mappedId = nextId++;
                    remap[root] = mappedId;
                }

                patchIds[i] = mappedId;
            }
        }

        private static HashSet<int> FindSameBiomePatchesWithinGap(int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height,
            float biomeStep, float maxGapMeters, int sourcePatchId, Heightmap.Biome targetBiome)
        {
            var reachable = new HashSet<int>();
            var visited = new bool[width * height];
            var queue = new Queue<(int index, float distance)>();

            for (var i = 0; i < patchIds.Length; i++)
            {
                if (patchIds[i] != sourcePatchId || !land[i])
                {
                    continue;
                }

                visited[i] = true;
                queue.Enqueue((i, 0f));
            }

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();
                var cx = current % width;
                var cy = current / width;
                EnqueueGapSearchNeighbor(cx - 1, cy, distance, sourcePatchId, targetBiome, patchIds, biomes, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueGapSearchNeighbor(cx + 1, cy, distance, sourcePatchId, targetBiome, patchIds, biomes, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueGapSearchNeighbor(cx, cy - 1, distance, sourcePatchId, targetBiome, patchIds, biomes, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueGapSearchNeighbor(cx, cy + 1, distance, sourcePatchId, targetBiome, patchIds, biomes, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
            }

            return reachable;
        }

        private static void EnqueueGapSearchNeighbor(int x, int y, float distance, int sourcePatchId, Heightmap.Biome targetBiome,
            int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height, float biomeStep, float maxGapMeters,
            bool[] visited, Queue<(int index, float distance)> queue, HashSet<int> reachable)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (visited[index])
            {
                return;
            }

            if (land[index])
            {
                if (biomes[index] != targetBiome)
                {
                    return;
                }

                var neighborPatchId = patchIds[index];
                if (neighborPatchId >= 0 && neighborPatchId != sourcePatchId)
                {
                    // Water path length to the far bank must be within the gap; do not charge the landing cell.
                    if (distance <= maxGapMeters)
                    {
                        reachable.Add(neighborPatchId);
                    }

                    return;
                }
            }

            var nextDistance = distance + biomeStep;
            if (nextDistance > maxGapMeters)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue((index, nextDistance));
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;

            public UnionFind(int size)
            {
                _parent = new int[size];
                for (var i = 0; i < size; i++)
                {
                    _parent[i] = i;
                }
            }

            public int Find(int value)
            {
                if (_parent[value] != value)
                {
                    _parent[value] = Find(_parent[value]);
                }

                return _parent[value];
            }

            public void Union(int a, int b)
            {
                _parent[Find(a)] = Find(b);
            }
        }

        private static int[] FloodFillIslands(bool[] land, int width, int height)
        {
            var islandIds = new int[width * height];
            for (var i = 0; i < islandIds.Length; i++)
            {
                islandIds[i] = -1;
            }

            var nextId = 0;
            var queue = new Queue<int>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var start = y * width + x;
                    if (!land[start] || islandIds[start] != -1)
                    {
                        continue;
                    }

                    islandIds[start] = nextId;
                    queue.Enqueue(start);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        var cx = current % width;
                        var cy = current / width;
                        TryEnqueueLand(cx - 1, cy, land, islandIds, width, height, nextId, queue);
                        TryEnqueueLand(cx + 1, cy, land, islandIds, width, height, nextId, queue);
                        TryEnqueueLand(cx, cy - 1, land, islandIds, width, height, nextId, queue);
                        TryEnqueueLand(cx, cy + 1, land, islandIds, width, height, nextId, queue);
                    }

                    nextId++;
                }
            }

            return islandIds;
        }

        private static void MergeIslandsAcrossNarrowGaps(int[] islandIds, bool[] land, int width, int height, float biomeStep, float maxGapMeters)
        {
            if (maxGapMeters <= 0f)
            {
                return;
            }

            var islandIdSet = new HashSet<int>();
            var maxIslandId = -1;
            for (var i = 0; i < islandIds.Length; i++)
            {
                if (!land[i] || islandIds[i] < 0)
                {
                    continue;
                }

                islandIdSet.Add(islandIds[i]);
                maxIslandId = Math.Max(maxIslandId, islandIds[i]);
            }

            if (maxIslandId < 0)
            {
                return;
            }

            var unionFind = new UnionFind(maxIslandId + 1);
            foreach (var islandId in islandIdSet)
            {
                var reachable = FindIslandsWithinGap(islandIds, land, width, height, biomeStep, maxGapMeters, islandId);
                foreach (var otherIslandId in reachable)
                {
                    unionFind.Union(islandId, otherIslandId);
                }
            }

            var remap = new Dictionary<int, int>();
            var nextId = 0;
            for (var i = 0; i < islandIds.Length; i++)
            {
                if (islandIds[i] < 0)
                {
                    continue;
                }

                var root = unionFind.Find(islandIds[i]);
                if (!remap.TryGetValue(root, out var mappedId))
                {
                    mappedId = nextId++;
                    remap[root] = mappedId;
                }

                islandIds[i] = mappedId;
            }
        }

        private static HashSet<int> FindIslandsWithinGap(int[] islandIds, bool[] land, int width, int height, float biomeStep,
            float maxGapMeters, int sourceIslandId)
        {
            var reachable = new HashSet<int>();
            var visited = new bool[width * height];
            var queue = new Queue<(int index, float distance)>();

            for (var i = 0; i < islandIds.Length; i++)
            {
                if (islandIds[i] != sourceIslandId || !land[i])
                {
                    continue;
                }

                visited[i] = true;
                queue.Enqueue((i, 0f));
            }

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();
                var cx = current % width;
                var cy = current / width;
                EnqueueIslandGapSearchNeighbor(cx - 1, cy, distance, sourceIslandId, islandIds, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueIslandGapSearchNeighbor(cx + 1, cy, distance, sourceIslandId, islandIds, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueIslandGapSearchNeighbor(cx, cy - 1, distance, sourceIslandId, islandIds, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
                EnqueueIslandGapSearchNeighbor(cx, cy + 1, distance, sourceIslandId, islandIds, land, width, height, biomeStep, maxGapMeters, visited, queue, reachable);
            }

            return reachable;
        }

        private static void EnqueueIslandGapSearchNeighbor(int x, int y, float distance, int sourceIslandId, int[] islandIds, bool[] land,
            int width, int height, float biomeStep, float maxGapMeters, bool[] visited, Queue<(int index, float distance)> queue,
            HashSet<int> reachable)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (visited[index])
            {
                return;
            }

            if (land[index])
            {
                var neighborIslandId = islandIds[index];
                if (neighborIslandId >= 0 && neighborIslandId != sourceIslandId)
                {
                    if (distance <= maxGapMeters)
                    {
                        reachable.Add(neighborIslandId);
                    }

                    return;
                }
            }

            var nextDistance = distance + biomeStep;
            if (nextDistance > maxGapMeters)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue((index, nextDistance));
        }

        private static void TryEnqueueLand(int x, int y, bool[] land, int[] islandIds, int width, int height, int islandId, Queue<int> queue)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            var index = y * width + x;
            if (!land[index] || islandIds[index] != -1)
            {
                return;
            }

            islandIds[index] = islandId;
            queue.Enqueue(index);
        }

        private static void BuildAdjacency(int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height,
            out Dictionary<int, HashSet<int>> meadowsNeighbors, out Dictionary<int, HashSet<int>> forestNeighbors)
        {
            meadowsNeighbors = new Dictionary<int, HashSet<int>>();
            forestNeighbors = new Dictionary<int, HashSet<int>>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (!land[index])
                    {
                        continue;
                    }

                    var patchId = patchIds[index];
                    if (patchId < 0)
                    {
                        continue;
                    }

                    CheckEdge(index, x - 1, y, patchId, biomes[index], patchIds, biomes, land, width, height, meadowsNeighbors, forestNeighbors);
                    CheckEdge(index, x + 1, y, patchId, biomes[index], patchIds, biomes, land, width, height, meadowsNeighbors, forestNeighbors);
                    CheckEdge(index, x, y - 1, patchId, biomes[index], patchIds, biomes, land, width, height, meadowsNeighbors, forestNeighbors);
                    CheckEdge(index, x, y + 1, patchId, biomes[index], patchIds, biomes, land, width, height, meadowsNeighbors, forestNeighbors);
                }
            }
        }

        private static void CheckEdge(int index, int nx, int ny, int patchId, Heightmap.Biome biome, int[] patchIds, Heightmap.Biome[] biomes,
            bool[] land, int width, int height, Dictionary<int, HashSet<int>> meadowsNeighbors, Dictionary<int, HashSet<int>> forestNeighbors)
        {
            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
            {
                return;
            }

            var neighborIndex = ny * width + nx;
            if (!land[neighborIndex])
            {
                return;
            }

            var neighborPatch = patchIds[neighborIndex];
            if (neighborPatch < 0 || neighborPatch == patchId)
            {
                return;
            }

            if (biome == Heightmap.Biome.Meadows && biomes[neighborIndex] == Heightmap.Biome.BlackForest)
            {
                AddNeighbor(meadowsNeighbors, patchId, neighborPatch);
                AddNeighbor(forestNeighbors, neighborPatch, patchId);
            }
            else if (biome == Heightmap.Biome.BlackForest && biomes[neighborIndex] == Heightmap.Biome.Meadows)
            {
                AddNeighbor(forestNeighbors, patchId, neighborPatch);
                AddNeighbor(meadowsNeighbors, neighborPatch, patchId);
            }
        }

        private static HashSet<int> FindMeadowsTouchingCoast(int[] patchIds, Heightmap.Biome[] biomes, bool[] land, int width, int height)
        {
            var touching = new HashSet<int>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (!land[index] || biomes[index] != Heightmap.Biome.Meadows || patchIds[index] < 0)
                    {
                        continue;
                    }

                    if (IsWaterNeighbor(x - 1, y, land, width, height) ||
                        IsWaterNeighbor(x + 1, y, land, width, height) ||
                        IsWaterNeighbor(x, y - 1, land, width, height) ||
                        IsWaterNeighbor(x, y + 1, land, width, height))
                    {
                        touching.Add(patchIds[index]);
                    }
                }
            }

            return touching;
        }

        private static bool IsWaterNeighbor(int x, int y, bool[] land, int width, int height)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            return !land[y * width + x];
        }

        private static void AddNeighbor(Dictionary<int, HashSet<int>> map, int from, int to)
        {
            if (!map.TryGetValue(from, out var set))
            {
                set = new HashSet<int>();
                map[from] = set;
            }

            set.Add(to);
        }
    }
}
