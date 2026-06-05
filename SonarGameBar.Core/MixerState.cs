namespace SonarGameBar.Core;

public sealed class MixerState
{
    public bool Connected { get; init; } = true;

    public string Mode { get; init; } = "classic";

    public ChannelState Master { get; init; } = new();

    public ChannelState Game { get; init; } = new();

    public ChannelState Chat { get; init; } = new();

    public double ChatMix { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ChannelState
{
    public double Volume { get; init; }

    public bool Muted { get; init; }
}
