namespace SonarGameBar.Core;

public static class SonarChannels
{
    public const string Master = "master";
    public const string Game = "game";
    public const string Chat = "chatRender";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Master,
        Game,
        Chat,
    };

    public static bool IsSupported(string channel) => Supported.Contains(channel);
}
