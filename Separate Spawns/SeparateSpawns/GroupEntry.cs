using System.Collections.Generic;

namespace SeparateSpawns
{
    internal sealed class GroupEntry
    {
        public List<string> Players { get; set; } = new List<string>();

        public int? Difficulty { get; set; }

        public bool HasDifficulty => Difficulty.HasValue && Difficulty.Value > 0;

        public static GroupEntry FromPlayers(IEnumerable<string> players)
        {
            return new GroupEntry
            {
                Players = players != null ? new List<string>(players) : new List<string>()
            };
        }
    }
}
