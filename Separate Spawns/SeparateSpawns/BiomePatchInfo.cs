using UnityEngine;

namespace SeparateSpawns
{
    internal sealed class BiomePatchInfo
    {
        public int PatchId { get; set; }
        public string Name { get; set; }
        public Heightmap.Biome Biome { get; set; }
        public int CellCount { get; set; }
        public float ApproximateAreaSquareMeters { get; set; }
        public Vector2 Center { get; set; }
        public int BurialChamberCount { get; set; }
    }
}
