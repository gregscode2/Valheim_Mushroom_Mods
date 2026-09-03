using BepInEx.Configuration;

namespace SeparateSpawns
{
    internal sealed class ModConfig
    {
        public ConfigEntry<float> InnerRadius { get; private set; }
        public ConfigEntry<float> BiomeStep { get; private set; }
        public ConfigEntry<float> BiomeSplitGapDistance { get; private set; }
        public ConfigEntry<float> IslandSplitGapDistance { get; private set; }
        public ConfigEntry<float> MinPatchArea { get; private set; }
        public ConfigEntry<float> GridStep { get; private set; }
        public ConfigEntry<float> MinSpawnDistance { get; private set; }
        public ConfigEntry<float> MinStonesDistance { get; private set; }
        public ConfigEntry<float> BurialChamberReach { get; private set; }
        public ConfigEntry<float> BlackForestProximity { get; private set; }
        public ConfigEntry<int> MinBurialChambers { get; private set; }
        public ConfigEntry<float> EikthyrReach { get; private set; }
        public ConfigEntry<int> MaxLayouts { get; private set; }
        public ConfigEntry<bool> EnableLayoutReports { get; private set; }
        public ConfigEntry<int> TopReportCount { get; private set; }
        public ConfigEntry<float> ReportRadius { get; private set; }
        public ConfigEntry<int> ScoreIslands { get; private set; }
        public ConfigEntry<int> ScoreDistance { get; private set; }
        public ConfigEntry<int> ScoreMeadowsSize { get; private set; }
        public ConfigEntry<float> LayoutDiversityDistance { get; private set; }
        public ConfigEntry<int> PortalCoreCost { get; private set; }
        public ConfigEntry<float> PortalStonesRadius { get; private set; }
        public ConfigEntry<int> MaxSeedRerolls { get; private set; }
        public ConfigEntry<string> BurialChamberLocationNames { get; private set; }
        public ConfigEntry<string> EikthyrLocationName { get; private set; }
        public ConfigEntry<string> StartTempleLocationName { get; private set; }
        public ConfigEntry<string> SurtlingCoreItemName { get; private set; }

        public static ModConfig Bind(ConfigFile config)
        {
            var values = new ModConfig();
            values.InnerRadius = config.Bind("Placement", "InnerRadius", 3000f, "Maximum radius from world center for spawn search.");
            values.BiomeStep = config.Bind("Placement", "BiomeStep", 5f, "Biome sampling grid spacing in meters for patch detection.");
            values.BiomeSplitGapDistance = config.Bind("Placement", "BiomeSplitGapDistance", 50f, "Merge same-biome land patches separated by water gaps up to this many meters.");
            values.IslandSplitGapDistance = config.Bind("Placement", "IslandSplitGapDistance", 100f, "Landmasses separated by water gaps up to this many meters count as the same island.");
            values.MinPatchArea = config.Bind("Placement", "MinPatchArea", 5000f, "Land patches smaller than this (m2) are absorbed into their largest neighboring patch.");
            values.GridStep = config.Bind("Placement", "GridStep", 25f, "Candidate spawn grid spacing in meters.");
            values.MinSpawnDistance = config.Bind("Placement", "MinSpawnDistance", 500f, "Minimum distance between group spawns.");
            values.MinStonesDistance = config.Bind("Placement", "MinStonesDistance", 1000f, "Minimum distance from sacrificial stones.");
            values.BurialChamberReach = config.Bind("Placement", "BurialChamberReach", 1000f, "Legacy setting; burial chambers now use BlackForestProximity.");
            values.BlackForestProximity = config.Bind("Placement", "BlackForestProximity", 400f, "Max distance from spawn to require Black Forest and count burial chambers.");
            values.MinBurialChambers = config.Bind("Placement", "MinBurialChambers", 3, "Minimum burial chambers within BlackForestProximity that lie in Black Forest.");
            values.EikthyrReach = config.Bind("Placement", "EikthyrReach", 200f, "Radius to require or place an Eikthyr altar.");
            values.MaxLayouts = config.Bind("Layout", "MaxLayouts", 100000, "Maximum layouts to sample.");
            values.EnableLayoutReports = config.Bind("Layout", "EnableLayoutReports", true, "Write layout report images and summary files. Disable on production servers.");
            values.TopReportCount = config.Bind("Layout", "TopReportCount", 1, "Number of top layouts to render when EnableLayoutReports is true.");
            values.ReportRadius = config.Bind("Layout", "ReportRadius", 10500f, "Radius shown in layout report images (Valheim world edge is ~10500m).");
            values.ScoreIslands = config.Bind("Scoring", "IslandsWeight", 16, "Score weight for different islands.");
            values.ScoreDistance = config.Bind("Scoring", "DistanceWeight", 10, "Score weight for how far apart the closest two spawns are (best layout = full weight, worst = 0).");
            values.ScoreMeadowsSize = config.Bind("Scoring", "MeadowsSizeWeight", 9, "Score weight for larger meadows patches.");
            values.LayoutDiversityDistance = config.Bind("Layout", "DiversityDistance", 150f, "Top layouts closer than this (meters) on every spawn are treated as duplicates.");
            values.PortalCoreCost = config.Bind("Portal", "CoreCost", 2, "Surtling cores required to activate a group portal.");
            values.PortalStonesRadius = config.Bind("Portal", "StonesRadius", 28f, "Radius of the circle around the sacrificial stones where group portals are placed.");
            values.MaxSeedRerolls = config.Bind("Seed", "MaxRerolls", 10, "Maximum automatic seed rerolls for infeasible worlds.");
            values.BurialChamberLocationNames = config.Bind("Locations", "BurialChamberNames", "Crypt2,Crypt3,Crypt4", "Comma-separated burial chamber location prefab names.");
            values.EikthyrLocationName = config.Bind("Locations", "EikthyrName", "Eikthyrnir", "Eikthyr altar location prefab name.");
            values.StartTempleLocationName = config.Bind("Locations", "StartTempleName", "StartTemple", "Sacrificial stones location prefab name.");
            values.SurtlingCoreItemName = config.Bind("Items", "SurtlingCoreName", "SurtlingCore", "Item name for portal activation cost.");
            return values;
        }

        public string[] GetBurialChamberNames()
        {
            return BurialChamberLocationNames.Value.Split(',');
        }
    }
}
