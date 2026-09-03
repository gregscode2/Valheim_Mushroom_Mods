using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SeparateSpawns
{
    internal static class LayoutReportWriter
    {
        public static void WriteReports(
            long worldUid,
            string worldName,
            string seedName,
            BiomeMapBuilder map,
            LocationCatalog locations,
            IReadOnlyList<LayoutAssignment> topLayouts,
            int candidateCount,
            CandidateSpawnFinder.RejectionStats rejections,
            ModConfig config)
        {
            var folder = Path.Combine(BepInEx.Paths.PluginPath, "SeparateSpawns", "reports", worldUid.ToString());
            Directory.CreateDirectory(folder);

            var summary = new StringBuilder();
            summary.AppendLine($"Separate Spawns layout report for world {worldUid}");
            summary.AppendLine($"World name: {worldName}");
            summary.AppendLine($"Seed: {seedName}");
            summary.AppendLine($"Generated: {DateTime.UtcNow:u}");
            summary.AppendLine($"Biome sample step: {config.BiomeStep.Value}m");
            summary.AppendLine($"Biome split gap distance: {config.BiomeSplitGapDistance.Value}m");
            summary.AppendLine($"Island split gap distance: {config.IslandSplitGapDistance.Value}m");
            summary.AppendLine($"Min patch area: {config.MinPatchArea.Value}m2");
            summary.AppendLine($"Black forest proximity: {config.BlackForestProximity.Value}m");
            summary.AppendLine($"Candidate grid step: {config.GridStep.Value}m");
            summary.AppendLine($"Report radius: {config.ReportRadius.Value}m (inner search radius {config.InnerRadius.Value}m drawn as white circle)");
            summary.AppendLine($"Eligible candidates: {candidateCount}");
            summary.AppendLine(
                $"Rejections: checked={rejections.TotalChecked}, accepted={rejections.Accepted}, meadows={rejections.NotMeadows}, forest={rejections.NoNearbyForest}, coast={rejections.NoAdjacentCoast}, stones={rejections.TooCloseToStones}, chambers={rejections.NotEnoughChambers}, water={rejections.Underwater}");
            summary.AppendLine($"Layout diversity distance: {config.LayoutDiversityDistance.Value}m");
            summary.AppendLine();
            BiomeMapBuilder.AppendPatchStatistics(summary, map.PatchStatistics);

            for (var i = 0; i < topLayouts.Count; i++)
            {
                var layout = topLayouts[i];
                summary.AppendLine($"#{i + 1} score={layout.Score:F2} islands={layout.IslandScore:F2} distance={layout.DistanceScore:F2} meadowsSize={layout.MeadowsSizeScore:F2} closest={layout.ClosestSpawnDistance:F0}m avgMeadowsArea={layout.AverageMeadowsAreaSquareMeters:F0}m2");
                foreach (var pair in layout.GroupSpawns)
                {
                    summary.AppendLine($"  {pair.Key}: ({pair.Value.Position.x:F0}, {pair.Value.Position.z:F0})");
                }

                var texture = RenderLayout(map, locations, layout, config);
                var png = TextureEncoder.EncodeToPng(texture);
                UnityEngine.Object.Destroy(texture);
                var extension = png.Length > 2 && png[0] == (byte)'B' && png[1] == (byte)'M' ? "bmp" : "png";
                File.WriteAllBytes(Path.Combine(folder, $"layout_{i + 1:D2}.{extension}"), png);
            }

            File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString());
            ModLog.Info($"Wrote layout report to {folder}");
        }

        public static void WriteFailureReport(
            long worldUid,
            string worldName,
            string seedName,
            int seedRerollAttempt,
            int maxSeedRerolls,
            BiomeMapBuilder map,
            LocationCatalog locations,
            LayoutGenerationResult generation,
            int candidateCount,
            CandidateSpawnFinder.RejectionStats rejections,
            ModConfig config,
            string reason)
        {
            var folder = Path.Combine(BepInEx.Paths.PluginPath, "SeparateSpawns", "reports", worldUid.ToString(), "failures");
            Directory.CreateDirectory(folder);

            var layoutToRender = generation.LastAttempt.GroupSpawns.Count > 0
                ? generation.LastAttempt
                : generation.BestPartialAttempt;

            var summary = new StringBuilder();
            summary.AppendLine($"Separate Spawns FAILED layout report for world {worldUid}");
            summary.AppendLine($"World name: {worldName}");
            summary.AppendLine($"Seed: {seedName}");
            summary.AppendLine($"Generated: {DateTime.UtcNow:u}");
            summary.AppendLine($"Biome sample step: {config.BiomeStep.Value}m");
            summary.AppendLine($"Biome split gap distance: {config.BiomeSplitGapDistance.Value}m");
            summary.AppendLine($"Island split gap distance: {config.IslandSplitGapDistance.Value}m");
            summary.AppendLine($"Min patch area: {config.MinPatchArea.Value}m2");
            summary.AppendLine($"Black forest proximity: {config.BlackForestProximity.Value}m");
            summary.AppendLine($"Candidate grid step: {config.GridStep.Value}m");
            summary.AppendLine($"Report radius: {config.ReportRadius.Value}m (inner search radius {config.InnerRadius.Value}m drawn as white circle)");
            summary.AppendLine($"Seed reroll attempt: {seedRerollAttempt}/{maxSeedRerolls}");
            summary.AppendLine($"Layout attempts: {generation.TotalAttempts}");
            summary.AppendLine($"Valid layouts found: {generation.ValidLayouts}");
            summary.AppendLine($"Eligible candidates: {candidateCount}");
            summary.AppendLine($"Last attempt groups placed: {generation.LastAttempt.GroupsPlaced} (complete={generation.LastAttempt.Complete})");
            summary.AppendLine($"Best partial groups placed: {generation.BestPartialAttempt.GroupsPlaced}");
            summary.AppendLine($"Rejections: checked={rejections.TotalChecked}, accepted={rejections.Accepted}, meadows={rejections.NotMeadows}, forest={rejections.NoNearbyForest}, coast={rejections.NoAdjacentCoast}, stones={rejections.TooCloseToStones}, chambers={rejections.NotEnoughChambers}, water={rejections.Underwater}");
            summary.AppendLine($"Reason: {reason}");
            summary.AppendLine();
            BiomeMapBuilder.AppendPatchStatistics(summary, map.PatchStatistics);
            foreach (var pair in layoutToRender.GroupSpawns)
            {
                summary.AppendLine($"  {pair.Key}: ({pair.Value.Position.x:F0}, {pair.Value.Position.z:F0})");
            }

            var texture = RenderLayout(map, locations, layoutToRender, config);
            var imageBytes = TextureEncoder.EncodeToPng(texture);
            UnityEngine.Object.Destroy(texture);
            var extension = imageBytes.Length > 2 && imageBytes[0] == (byte)'B' && imageBytes[1] == (byte)'M' ? "bmp" : "png";
            var fileName = $"failure_reroll{seedRerollAttempt:D2}_last_attempt.{extension}";
            File.WriteAllBytes(Path.Combine(folder, fileName), imageBytes);
            File.WriteAllText(Path.Combine(folder, $"failure_reroll{seedRerollAttempt:D2}_summary.txt"), summary.ToString());

            ModLog.Info($"Wrote failure layout report to {folder}\\{fileName}");
        }

        private static Texture2D RenderLayout(BiomeMapBuilder map, LocationCatalog locations, LayoutAssignment layout, ModConfig config)
        {
            const int size = 1024;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var radius = config.ReportRadius.Value;
            var pixels = new Color32[size * size];
            var generator = WorldGenerator.instance;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Match Valheim minimap axes: +X right, +Z up (north).
                    var wx = Mathf.Lerp(-radius, radius, x / (float)(size - 1));
                    var wz = Mathf.Lerp(-radius, radius, y / (float)(size - 1));
                    pixels[y * size + x] = SampleBiomeColor(generator, wx, wz);
                }
            }

            // Placement search area (inner radius) — helps compare against full-world seed viewers.
            DrawCircle(pixels, size, radius, Vector3.zero, config.InnerRadius.Value, new Color32(255, 255, 255, 255), 1);

            DrawMarker(pixels, size, radius, locations.SacrificialStones, new Color32(255, 255, 255, 255), 4);
            foreach (var chamber in locations.BurialChambers)
            {
                // Only mark chambers inside the report so the image stays readable.
                if (new Vector2(chamber.x, chamber.z).magnitude > radius)
                {
                    continue;
                }

                DrawMarker(pixels, size, radius, chamber, new Color32(180, 120, 60, 255), 1);
            }

            foreach (var altar in locations.EikthyrAltars)
            {
                DrawMarker(pixels, size, radius, altar, new Color32(120, 220, 255, 255), 3);
            }

            var groupColors = new[]
            {
                new Color32(255, 64, 64, 255),
                new Color32(64, 255, 64, 255),
                new Color32(64, 128, 255, 255),
                new Color32(255, 128, 255, 255),
                new Color32(255, 220, 64, 255),
                new Color32(64, 255, 220, 255)
            };

            var colorIndex = 0;
            foreach (var spawn in layout.GroupSpawns.Values)
            {
                DrawMarker(pixels, size, radius, spawn.Position, groupColors[colorIndex % groupColors.Length], 5);
                colorIndex++;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Color32 SampleBiomeColor(WorldGenerator generator, float wx, float wz)
        {
            var biome = generator.GetBiome(wx, wz);
            var underwater = generator.GetHeight(wx, wz) <= ValheimHeights.WaterSurface;
            if (biome == Heightmap.Biome.Ocean || underwater)
            {
                return new Color32(20, 40, 90, 255);
            }

            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                    return new Color32(119, 153, 76, 255);
                case Heightmap.Biome.BlackForest:
                    return new Color32(64, 96, 48, 255);
                case Heightmap.Biome.Swamp:
                    return new Color32(80, 90, 55, 255);
                case Heightmap.Biome.Mountain:
                    return new Color32(200, 200, 210, 255);
                case Heightmap.Biome.Plains:
                    return new Color32(180, 160, 90, 255);
                case Heightmap.Biome.Mistlands:
                    return new Color32(90, 70, 110, 255);
                case Heightmap.Biome.AshLands:
                    return new Color32(140, 60, 40, 255);
                case Heightmap.Biome.DeepNorth:
                    return new Color32(170, 190, 210, 255);
                default:
                    return new Color32(100, 100, 100, 255);
            }
        }

        private static void DrawCircle(Color32[] pixels, int size, float reportRadius, Vector3 center, float worldRadius, Color32 color, int thickness)
        {
            if (worldRadius <= 0f || reportRadius <= 0f)
            {
                return;
            }

            var steps = Mathf.Clamp(Mathf.CeilToInt(worldRadius * 2f), 128, 2048);
            for (var i = 0; i < steps; i++)
            {
                var angle = (i / (float)steps) * Mathf.PI * 2f;
                var point = center + new Vector3(Mathf.Cos(angle) * worldRadius, 0f, Mathf.Sin(angle) * worldRadius);
                DrawMarker(pixels, size, reportRadius, point, color, thickness);
            }
        }

        private static void DrawMarker(Color32[] pixels, int size, float radius, Vector3 world, Color32 color, int markerRadius)
        {
            var px = Mathf.RoundToInt(Mathf.InverseLerp(-radius, radius, world.x) * (size - 1));
            var py = Mathf.RoundToInt(Mathf.InverseLerp(-radius, radius, world.z) * (size - 1));

            for (var y = -markerRadius; y <= markerRadius; y++)
            {
                for (var x = -markerRadius; x <= markerRadius; x++)
                {
                    if (x * x + y * y > markerRadius * markerRadius)
                    {
                        continue;
                    }

                    var tx = px + x;
                    var ty = py + y;
                    if (tx < 0 || ty < 0 || tx >= size || ty >= size)
                    {
                        continue;
                    }

                    pixels[ty * size + tx] = color;
                }
            }
        }
    }
}
