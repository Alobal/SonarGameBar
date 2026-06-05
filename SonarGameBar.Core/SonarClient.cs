using System.Globalization;
using System.Net;
using System.Text.Json;

namespace SonarGameBar.Core;

public sealed class SonarClient : IDisposable
{
    private const string DefaultMode = "classic";

    private readonly HttpClient _httpClient;
    private readonly string _corePropsPath;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);

    private Uri? _sonarBaseUri;

    public SonarClient(string? corePropsPath = null)
    {
        _corePropsPath = corePropsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SteelSeries",
            "SteelSeries Engine 3",
            "coreProps.json");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is not null && IsLoopbackUri(request.RequestUri),
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(4),
        };
    }

    public async Task<MixerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRediscoveryAsync(async baseUri =>
        {
            var volumesTask = GetStringAsync(baseUri, "volumeSettings/classic", cancellationToken);
            var chatMixTask = GetStringAsync(baseUri, "chatMix", cancellationToken);
            var modeTask = GetStringAsync(baseUri, "mode/", cancellationToken);

            await Task.WhenAll(volumesTask, chatMixTask, modeTask);

            return ParseState(
                await volumesTask,
                await chatMixTask,
                await modeTask);
        }, cancellationToken);
    }

    public Task SetVolumeAsync(
        string channel,
        double volume,
        CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);

        if (volume is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
        }

        var formattedVolume = volume.ToString("0.####", CultureInfo.InvariantCulture);
        return PutAsync($"volumeSettings/{DefaultMode}/{channel}/Volume/{formattedVolume}", cancellationToken);
    }

    public Task SetMuteAsync(
        string channel,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        ValidateChannel(channel);
        return PutAsync($"volumeSettings/{DefaultMode}/{channel}/Mute/{muted.ToString().ToLowerInvariant()}", cancellationToken);
    }

    public Task SetChatMixAsync(double balance, CancellationToken cancellationToken = default)
    {
        if (balance is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(balance), "ChatMix balance must be between -1 and 1.");
        }

        var formattedBalance = balance.ToString("0.####", CultureInfo.InvariantCulture);
        return PutAsync($"chatMix?balance={formattedBalance}", cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _discoveryLock.Dispose();
    }

    private async Task PutAsync(string relativeUri, CancellationToken cancellationToken)
    {
        await ExecuteWithRediscoveryAsync(async baseUri =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(baseUri, relativeUri))
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }, cancellationToken);
    }

    private async Task<string> GetStringAsync(
        Uri baseUri,
        string relativeUri,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(baseUri, relativeUri), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<T> ExecuteWithRediscoveryAsync<T>(
        Func<Uri, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Exception? firstFailure = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await operation(await GetSonarBaseUriAsync(cancellationToken));
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException &&
                !cancellationToken.IsCancellationRequested)
            {
                firstFailure ??= exception;
                _sonarBaseUri = null;
            }
        }

        throw new SonarApiException(
            "Could not connect to SteelSeries Sonar. Make sure GG and Sonar are running.",
            firstFailure!);
    }

    private async Task<Uri> GetSonarBaseUriAsync(CancellationToken cancellationToken)
    {
        if (_sonarBaseUri is not null)
        {
            return _sonarBaseUri;
        }

        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (_sonarBaseUri is not null)
            {
                return _sonarBaseUri;
            }

            _sonarBaseUri = await DiscoverSonarBaseUriAsync(cancellationToken);
            return _sonarBaseUri;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private async Task<Uri> DiscoverSonarBaseUriAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_corePropsPath))
        {
            throw new SonarApiException($"SteelSeries GG configuration was not found at '{_corePropsPath}'.");
        }

        try
        {
            var corePropsJson = await File.ReadAllTextAsync(_corePropsPath, cancellationToken);
            using var coreProps = JsonDocument.Parse(corePropsJson);
            var ggAddress = coreProps.RootElement.GetProperty("ggEncryptedAddress").GetString();
            var ggBaseUri = CreateLoopbackBaseUri(ggAddress, Uri.UriSchemeHttps);

            var subAppsJson = await GetStringAsync(ggBaseUri, "subApps", cancellationToken);
            using var subApps = JsonDocument.Parse(subAppsJson);
            var sonarAddress = subApps.RootElement
                .GetProperty("subApps")
                .GetProperty("sonar")
                .GetProperty("metadata")
                .GetProperty("webServerAddress")
                .GetString();

            return CreateLoopbackBaseUri(sonarAddress, Uri.UriSchemeHttp);
        }
        catch (SonarApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            throw new SonarApiException("SteelSeries GG returned an invalid Sonar connection configuration.", exception);
        }
    }

    private static MixerState ParseState(string volumesJson, string chatMixJson, string modeJson)
    {
        try
        {
            using var volumes = JsonDocument.Parse(volumesJson);
            using var chatMix = JsonDocument.Parse(chatMixJson);

            var root = volumes.RootElement;
            return new MixerState
            {
                Mode = JsonSerializer.Deserialize<string>(modeJson) ?? DefaultMode,
                Master = ParseChannel(root.GetProperty("masters")),
                Game = ParseChannel(root.GetProperty("devices").GetProperty(SonarChannels.Game)),
                Chat = ParseChannel(root.GetProperty("devices").GetProperty(SonarChannels.Chat)),
                ChatMix = chatMix.RootElement.GetProperty("balance").GetDouble(),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new SonarApiException("SteelSeries Sonar returned an unexpected mixer response.", exception);
        }
    }

    private static ChannelState ParseChannel(JsonElement channel)
    {
        var classic = channel.GetProperty(DefaultMode);
        return new ChannelState
        {
            Volume = classic.GetProperty("volume").GetDouble(),
            Muted = classic.GetProperty("muted").GetBoolean(),
        };
    }

    private static Uri CreateLoopbackBaseUri(string? address, string defaultScheme)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new SonarApiException("SteelSeries GG did not provide a Sonar server address.");
        }

        var candidate = address.Contains("://", StringComparison.Ordinal)
            ? address
            : $"{defaultScheme}://{address}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !IsLoopbackUri(uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SonarApiException("SteelSeries GG provided an unsafe Sonar server address.");
        }

        return new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
        }.Uri;
    }

    private static bool IsLoopbackUri(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static void ValidateChannel(string channel)
    {
        if (!SonarChannels.IsSupported(channel))
        {
            throw new ArgumentException($"Unsupported Sonar channel '{channel}'.", nameof(channel));
        }
    }
}
