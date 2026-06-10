using SonarGameBar.Core;

var tests = new (string Name, Action Run)[]
{
    ("解析 classic 模式下的动态通道", ParseDynamicClassicChannels),
    ("仅显示常驻或正在活动的通道", FilterInactiveOptionalChannels),
    ("拒绝非本机回环 Sonar 地址", RejectNonLoopbackEndpoint),
    ("接受 localhost 与 127.0.0.1 地址", AcceptLoopbackEndpoints),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void ParseDynamicClassicChannels()
{
    const string volumesJson = """
        {
          "masters": {
            "stream": {},
            "classic": { "volume": 0.8, "muted": false }
          },
          "devices": {
            "game": { "stream": {}, "classic": { "volume": 1.0, "muted": false } },
            "chatRender": { "stream": {}, "classic": { "volume": 0.6, "muted": true } },
            "chatCapture": { "stream": {}, "classic": { "volume": 0.7, "muted": false } },
            "media": { "stream": {}, "classic": { "volume": 0.5, "muted": false } },
            "aux": { "stream": {}, "classic": { "volume": 0.4, "muted": false } }
          }
        }
        """;

    var state = SonarStateParser.ParseState(volumesJson, "{\"balance\":0.25}", "\"classic\"");

    AssertEqual("classic", state.Mode, "mode");
    AssertEqual(6, state.Channels.Count, "channel count");
    AssertEqual(0.25, state.ChatMix, "chatMix");
    AssertChannel(state, SonarChannels.Master, "总音量", 0.8, false);
    AssertChannel(state, SonarChannels.Game, "游戏", 1.0, false);
    AssertChannel(state, SonarChannels.Chat, "聊天", 0.6, true);
    AssertChannel(state, SonarChannels.ChatCapture, "麦克风", 0.7, false);
    AssertChannel(state, SonarChannels.Media, "媒体", 0.5, false);
    AssertChannel(state, SonarChannels.Aux, "Aux", 0.4, false);
}

static void FilterInactiveOptionalChannels()
{
    var state = new MixerState
    {
        Channels = new MixerChannelState[]
        {
            NewChannel(SonarChannels.Master),
            NewChannel(SonarChannels.Game),
            NewChannel(SonarChannels.Chat),
            NewChannel(SonarChannels.ChatCapture),
            NewChannel(SonarChannels.Media),
            NewChannel(SonarChannels.Aux),
        },
    };
    var available = new HashSet<string>(StringComparer.Ordinal)
    {
        SonarChannels.Game,
        SonarChannels.Chat,
        SonarChannels.ChatCapture,
        SonarChannels.Media,
        SonarChannels.Aux,
    };

    var inactive = state.WithEndpointChannels(new HashSet<string>(StringComparer.Ordinal), available);
    AssertEqual(3, inactive.Channels.Count, "inactive channel count");
    AssertHasChannel(inactive, SonarChannels.Master, true);
    AssertHasChannel(inactive, SonarChannels.Game, true);
    AssertHasChannel(inactive, SonarChannels.Chat, true);
    AssertHasChannel(inactive, SonarChannels.Aux, false);

    var active = state.WithEndpointChannels(
        new HashSet<string>(StringComparer.Ordinal) { SonarChannels.Aux },
        available);
    AssertHasChannel(active, SonarChannels.Aux, true);
    AssertEqual(true, active.Channels.First(channel => channel.Id == SonarChannels.Aux).Active, "aux active");
}

static void RejectNonLoopbackEndpoint()
{
    AssertThrows<SonarApiException>(() =>
        SonarEndpoint.CreateLoopbackBaseUri("192.168.1.10:12345", Uri.UriSchemeHttp));
}

static void AcceptLoopbackEndpoints()
{
    var localhost = SonarEndpoint.CreateLoopbackBaseUri("localhost:12345", Uri.UriSchemeHttps);
    var loopback = SonarEndpoint.CreateLoopbackBaseUri("http://127.0.0.1:54321", Uri.UriSchemeHttp);

    AssertEqual("https://localhost:12345/", localhost.ToString(), "localhost uri");
    AssertEqual("http://127.0.0.1:54321/", loopback.ToString(), "loopback uri");
}

static void AssertChannel(
    MixerState state,
    string id,
    string displayName,
    double volume,
    bool muted)
{
    var channel = state.Channels.FirstOrDefault(candidate => candidate.Id == id);
    if (channel is null)
    {
        throw new InvalidOperationException($"Missing channel '{id}'.");
    }

    AssertEqual(displayName, channel.DisplayName, $"{id} displayName");
    AssertEqual(volume, channel.Volume, $"{id} volume");
    AssertEqual(muted, channel.Muted, $"{id} muted");
    AssertEqual(true, channel.Controllable, $"{id} controllable");
}

static MixerChannelState NewChannel(string id)
{
    return new MixerChannelState
    {
        Id = id,
        DisplayName = SonarChannels.GetDisplayName(id),
        SortOrder = SonarChannels.GetSortOrder(id),
    };
}

static void AssertHasChannel(MixerState state, string id, bool expected)
{
    AssertEqual(expected, state.Channels.Any(channel => channel.Id == id), $"{id} exists");
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
