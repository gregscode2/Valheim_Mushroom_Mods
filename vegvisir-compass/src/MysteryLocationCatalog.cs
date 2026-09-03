namespace VegvisirCompass
{
    /// <summary>
    /// The three Ashlands Mysterious Locations of the Dyrnwyn chain.
    ///
    /// Vanilla labels all three with the same pin token, "$placeofmystery", which
    /// localizes to "Mysterious Location" - so without this every compass in the chain
    /// would carry an identical name despite pointing somewhere different. Numbering
    /// them keeps the steps apart.
    /// </summary>
    internal static class MysteryLocationCatalog
    {
        /// <summary>Location name to the label shown on the compass.</summary>
        private static readonly string[][] Entries =
        {
            new[] { "PlaceofMystery1", "Mysterious Location 1" },
            new[] { "PlaceofMystery2", "Mysterious Location 2" },
            new[] { "PlaceofMystery3", "Mysterious Location 3" },
        };

        /// <summary>The numbered label for a Mysterious Location, or empty otherwise.</summary>
        internal static string LabelFor(string locationName)
        {
            if (string.IsNullOrEmpty(locationName)) return "";

            foreach (string[] entry in Entries)
            {
                if (string.Equals(locationName, entry[0], System.StringComparison.OrdinalIgnoreCase))
                {
                    return entry[1];
                }
            }
            return "";
        }
    }
}
