using System.Collections.Generic;
using UnityEngine;

namespace VegvisirCompass
{
    /// <summary>One of Hildir's three quest dungeons.</summary>
    internal sealed class HildirQuestDef
    {
        /// <summary>Location the dungeon occupies, e.g. "Hildir_crypt".</summary>
        internal string LocationName;

        /// <summary>Shown on the compass: "Vegvisir Compass - Brass".</summary>
        internal string DisplayName;
    }

    /// <summary>
    /// The three compasses granted by reading Hildir's map table, one per quest
    /// dungeon. They are ordinary Vegvisir Compasses - same item, same single use and
    /// same range - distinguished only by the target baked into each one.
    /// </summary>
    internal static class HildirQuestCatalog
    {
        internal static readonly HildirQuestDef[] All =
        {
            // Smouldering Tomb
            new HildirQuestDef { LocationName = "Hildir_crypt",          DisplayName = "Brass" },
            // Howling Cavern
            new HildirQuestDef { LocationName = "Hildir_cave",           DisplayName = "Silver" },
            // Sealed Tower
            new HildirQuestDef { LocationName = "Hildir_plainsfortress", DisplayName = "Bronze" },
        };

        /// <summary>
        /// The Brass/Silver/Bronze name for a quest dungeon, or empty for anything
        /// else. Vanilla labels these with pin tokens such as "$hud_pin_hildir3",
        /// which say nothing useful in an inventory.
        /// </summary>
        internal static string LabelFor(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return "";

            foreach (HildirQuestDef def in All)
            {
                if (string.Equals(locationName, def.LocationName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return def.DisplayName;
                }
            }
            return "";
        }
    }
}
