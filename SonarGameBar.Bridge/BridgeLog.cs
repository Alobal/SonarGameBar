namespace SonarGameBar.Bridge;

internal static class BridgeLog
{
    private static readonly string[] LogPaths =
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SonarGameBar",
            "Bridge.log"),
        Path.Combine(Path.GetTempPath(), "SonarGameBar.Bridge.log"),
    };

    public static void Write(string message)
    {
        foreach (var logPath in LogPaths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(
                    logPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never stop the Bridge.
            }
        }
    }
}
