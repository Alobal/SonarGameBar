namespace SonarGameBar.Core;

public sealed class MixerState
{
    public bool Connected { get; init; } = true;

    public string Mode { get; init; } = "classic";

    public IReadOnlyList<MixerChannelState> Channels { get; init; } = Array.Empty<MixerChannelState>();

    public IReadOnlyList<DeviceBatteryState> Batteries { get; init; } = Array.Empty<DeviceBatteryState>();

    public ChannelState Master { get; init; } = new();

    public ChannelState Game { get; init; } = new();

    public ChannelState Chat { get; init; } = new();

    public double ChatMix { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public MixerState WithEndpointChannels(ISet<string> activeChannels, ISet<string> availableChannels)
    {
        var channels = Channels
            .Where(channel =>
                SonarChannels.IsAlwaysVisible(channel.Id) ||
                (SonarChannels.RequiresActivityToShow(channel.Id)
                    ? activeChannels.Contains(channel.Id)
                    : availableChannels.Contains(channel.Id)))
            .Select(channel => channel with { Active = activeChannels.Contains(channel.Id) })
            .ToArray();

        return new MixerState
        {
            Connected = Connected,
            Mode = Mode,
            Channels = channels,
            Batteries = Batteries,
            Master = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Master) ?? Master,
            Game = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Game) ?? Game,
            Chat = channels.FirstOrDefault(channel => channel.Id == SonarChannels.Chat) ?? Chat,
            ChatMix = ChatMix,
            UpdatedAt = UpdatedAt,
        };
    }

    public MixerState WithBatteries(IReadOnlyList<DeviceBatteryState> batteries)
    {
        return new MixerState
        {
            Connected = Connected,
            Mode = Mode,
            Channels = Channels,
            Batteries = batteries,
            Master = Master,
            Game = Game,
            Chat = Chat,
            ChatMix = ChatMix,
            UpdatedAt = UpdatedAt,
        };
    }
}

public record ChannelState
{
    public double Volume { get; init; }

    public bool Muted { get; init; }
}

public sealed record MixerChannelState : ChannelState
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Group { get; init; } = "devices";

    public bool Controllable { get; init; } = true;

    public bool Active { get; init; }

    public int SortOrder { get; init; } = 1000;
}

public sealed record DeviceBatteryState
{
    public string DeviceName { get; init; } = string.Empty;

    public int? Percentage { get; init; }

    public bool? Charging { get; init; }

    public string Status { get; init; } = "unknown";
}
