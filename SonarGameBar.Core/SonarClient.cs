using System.Globalization;
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
                request.RequestUri is not null && SonarEndpoint.IsLoopbackUri(request.RequestUri),
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

            return SonarStateParser.ParseState(
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
        if (volume is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
        }

        var formattedVolume = volume.ToString("0.####", CultureInfo.InvariantCulture);
        return PutChannelAsync(channel, $"Volume/{formattedVolume}", cancellationToken);
    }

    public Task SetMuteAsync(
        string channel,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        return PutChannelAsync(channel, $"Mute/{muted.ToString().ToLowerInvariant()}", cancellationToken);
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

    private async Task PutChannelAsync(
        string channel,
        string operation,
        CancellationToken cancellationToken)
    {
        await EnsureChannelControllableAsync(channel, cancellationToken);
        await PutAsync($"volumeSettings/{DefaultMode}/{channel}/{operation}", cancellationToken);
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
            var ggBaseUri = SonarEndpoint.CreateLoopbackBaseUri(ggAddress, Uri.UriSchemeHttps);

            var subAppsJson = await GetStringAsync(ggBaseUri, "subApps", cancellationToken);
            using var subApps = JsonDocument.Parse(subAppsJson);
            var sonarAddress = subApps.RootElement
                .GetProperty("subApps")
                .GetProperty("sonar")
                .GetProperty("metadata")
                .GetProperty("webServerAddress")
                .GetString();

            return SonarEndpoint.CreateLoopbackBaseUri(sonarAddress, Uri.UriSchemeHttp);
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

    private async Task EnsureChannelControllableAsync(string channel, CancellationToken cancellationToken)
    {
        if (!SonarChannels.IsSafeChannelId(channel))
        {
            throw new ArgumentException($"Unsafe Sonar channel id '{channel}'.", nameof(channel));
        }

        var state = await GetStateAsync(cancellationToken);
        var discovered = state.Channels.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, channel, StringComparison.Ordinal));

        if (discovered is null)
        {
            throw new ArgumentException($"Sonar channel '{channel}' is not available.", nameof(channel));
        }

        if (!discovered.Controllable)
        {
            throw new SonarApiException($"Sonar channel '{channel}' cannot be controlled in mode '{state.Mode}'.");
        }
    }
}
