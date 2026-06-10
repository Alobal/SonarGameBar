using System.Text.Json;

namespace SonarGameBar.Core;

public static class SonarStateParser
{
    private const string DefaultMode = "classic";

    public static MixerState ParseState(string volumesJson, string chatMixJson, string modeJson)
    {
        try
        {
            using var volumes = JsonDocument.Parse(volumesJson);
            using var chatMix = JsonDocument.Parse(chatMixJson);

            var mode = JsonSerializer.Deserialize<string>(modeJson) ?? DefaultMode;
            var channels = ParseChannels(volumes.RootElement, mode).ToArray();

            return new MixerState
            {
                Mode = mode,
                Channels = channels,
                Master = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Master) ?? new ChannelState(),
                Game = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Game) ?? new ChannelState(),
                Chat = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Chat) ?? new ChannelState(),
                ChatMix = chatMix.RootElement.TryGetProperty("balance", out var balance)
                    ? balance.GetDouble()
                    : 0,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new SonarApiException("SteelSeries Sonar returned an unexpected mixer response.", exception);
        }
    }

    private static IEnumerable<MixerChannelState> ParseChannels(JsonElement root, string mode)
    {
        if (root.TryGetProperty("masters", out var masters) &&
            TryParseChannel(SonarChannels.Master, masters, mode, out var master))
        {
            yield return master;
        }

        if (!root.TryGetProperty("devices", out var devices) ||
            devices.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var device in devices.EnumerateObject())
        {
            if (!SonarChannels.IsSafeChannelId(device.Name))
            {
                continue;
            }

            if (TryParseChannel(device.Name, device.Value, mode, out var channel))
            {
                yield return channel;
            }
        }
    }

    private static bool TryParseChannel(
        string id,
        JsonElement channel,
        string currentMode,
        out MixerChannelState state)
    {
        state = new MixerChannelState();

        if (!channel.TryGetProperty(DefaultMode, out var classic) ||
            classic.ValueKind != JsonValueKind.Object ||
            !classic.TryGetProperty("volume", out var volume) ||
            !classic.TryGetProperty("muted", out var muted))
        {
            return false;
        }

        state = new MixerChannelState
        {
            Id = id,
            DisplayName = SonarChannels.GetDisplayName(id),
            Group = SonarChannels.GetGroup(id),
            SortOrder = SonarChannels.GetSortOrder(id),
            Volume = volume.GetDouble(),
            Muted = muted.GetBoolean(),
            Controllable = string.Equals(currentMode, DefaultMode, StringComparison.OrdinalIgnoreCase),
        };

        return true;
    }
}
