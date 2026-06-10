namespace SonarGameBar.Core;

public static class SonarChannels
{
    public const string Master = "master";
    public const string Game = "game";
    public const string Chat = "chatRender";
    public const string ChatCapture = "chatCapture";
    public const string Media = "media";
    public const string Aux = "aux";

    private static readonly Dictionary<string, ChannelMetadata> Known = new(StringComparer.Ordinal)
    {
        [Master] = new("总音量", "masters", 0),
        [Game] = new("游戏", "devices", 10),
        [Chat] = new("聊天", "devices", 20),
        [ChatCapture] = new("麦克风", "devices", 30),
        [Media] = new("媒体", "devices", 40),
        [Aux] = new("Aux", "devices", 50),
    };

    public static bool IsKnown(string channel) => Known.ContainsKey(channel);

    public static bool IsAlwaysVisible(string channel)
    {
        return channel is Master or Game or Chat;
    }

    public static bool RequiresActivityToShow(string channel)
    {
        return !IsAlwaysVisible(channel);
    }

    public static string GetDisplayName(string channel)
    {
        return Known.TryGetValue(channel, out var metadata)
            ? metadata.DisplayName
            : MakeDisplayName(channel);
    }

    public static string GetGroup(string channel)
    {
        return Known.TryGetValue(channel, out var metadata)
            ? metadata.Group
            : "devices";
    }

    public static int GetSortOrder(string channel)
    {
        return Known.TryGetValue(channel, out var metadata)
            ? metadata.SortOrder
            : 500 + Math.Abs(StringComparer.Ordinal.GetHashCode(channel) % 400);
    }

    public static bool IsSafeChannelId(string channel)
    {
        return !string.IsNullOrWhiteSpace(channel) &&
            channel.Length <= 64 &&
            channel.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }

    private static string MakeDisplayName(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return "未知";
        }

        return channel[0].ToString().ToUpperInvariant() + channel[1..];
    }

    private sealed record ChannelMetadata(string DisplayName, string Group, int SortOrder);
}
