namespace SeparateSpawns
{
    internal static class ModLog
    {
        public static void Info(string message) => Plugin.Instance.LogInfo(message);
        public static void Warning(string message) => Plugin.Instance.LogWarning(message);
        public static void Error(string message) => Plugin.Instance.LogError(message);
    }
}
