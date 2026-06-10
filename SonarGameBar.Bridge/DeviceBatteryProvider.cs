using System.Text.Json;
using SonarGameBar.Core;

namespace SonarGameBar.Bridge;

internal sealed class DeviceBatteryProvider : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _corePropsPath;

    private Uri? _engineBaseUri;

    public DeviceBatteryProvider(string? corePropsPath = null)
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
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    public async Task<IReadOnlyList<DeviceBatteryState>> GetBatteriesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var engineBaseUri = await GetEngineBaseUriAsync(cancellationToken);
            var devicesJson = await GetStringAsync(engineBaseUri, "devices", cancellationToken);
            using var devices = JsonDocument.Parse(devicesJson);

            if (!devices.RootElement.TryGetProperty("devices", out var deviceList) ||
                deviceList.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DeviceBatteryState>();
            }

            var batteries = new List<DeviceBatteryState>();
            foreach (var device in deviceList.EnumerateArray())
            {
                var battery = await TryReadBatteryAsync(engineBaseUri, device, cancellationToken);
                if (battery is not null)
                {
                    batteries.Add(battery);
                }
            }

            return batteries;
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Battery detection failed: {exception.Message}");
            return Array.Empty<DeviceBatteryState>();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<DeviceBatteryState?> TryReadBatteryAsync(
        Uri engineBaseUri,
        JsonElement summary,
        CancellationToken cancellationToken)
    {
        if (summary.TryGetProperty("connected", out var connected) &&
            connected.ValueKind == JsonValueKind.Number &&
            connected.GetInt32() == 0)
        {
            return null;
        }

        var deviceName = GetString(summary, "display_name") ??
            GetString(summary, "full_name") ??
            GetString(summary, "name") ??
            "SteelSeries 设备";

        JsonElement? detail = null;
        if (summary.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.Number)
        {
            try
            {
                var detailJson = await GetStringAsync(engineBaseUri, $"device/{id.GetInt32()}", cancellationToken);
                using var document = JsonDocument.Parse(detailJson);
                if (document.RootElement.TryGetProperty("device", out var device))
                {
                    detail = device.Clone();
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                BridgeLog.Write($"Battery detail read failed for {deviceName}: {exception.Message}");
            }
        }

        var supportsBattery = HasBatteryCapability(summary) ||
            (detail.HasValue && HasBatteryCapability(detail.Value));
        var percentage = FindBatteryPercentage(summary) ??
            (detail.HasValue ? FindBatteryPercentage(detail.Value) : null);
        var charging = FindChargingState(summary) ??
            (detail.HasValue ? FindChargingState(detail.Value) : null);

        if (!supportsBattery && percentage is null)
        {
            return null;
        }

        return new DeviceBatteryState
        {
            DeviceName = deviceName,
            Percentage = percentage,
            Charging = charging,
            Status = percentage is null ? "unknown" : "available",
        };
    }

    private async Task<Uri> GetEngineBaseUriAsync(CancellationToken cancellationToken)
    {
        if (_engineBaseUri is not null)
        {
            return _engineBaseUri;
        }

        if (!File.Exists(_corePropsPath))
        {
            throw new SonarApiException($"SteelSeries GG configuration was not found at '{_corePropsPath}'.");
        }

        var corePropsJson = await File.ReadAllTextAsync(_corePropsPath, cancellationToken);
        using var coreProps = JsonDocument.Parse(corePropsJson);
        var ggAddress = coreProps.RootElement.GetProperty("ggEncryptedAddress").GetString();
        var ggBaseUri = SonarEndpoint.CreateLoopbackBaseUri(ggAddress, Uri.UriSchemeHttps);

        var subAppsJson = await GetStringAsync(ggBaseUri, "subApps", cancellationToken);
        using var subApps = JsonDocument.Parse(subAppsJson);
        var engineAddress = subApps.RootElement
            .GetProperty("subApps")
            .GetProperty("engine")
            .GetProperty("metadata")
            .GetProperty("encryptedWebServerAddress")
            .GetString();

        _engineBaseUri = SonarEndpoint.CreateLoopbackBaseUri(engineAddress, Uri.UriSchemeHttps);
        return _engineBaseUri;
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

    private static bool HasBatteryCapability(JsonElement element)
    {
        var found = false;
        Walk(element, (name, value) =>
        {
            if (found)
            {
                return;
            }

            if (name.Contains("battery", StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind == JsonValueKind.String &&
                value.GetString()?.Contains("battery", StringComparison.OrdinalIgnoreCase) == true)
            {
                found = true;
            }
        });

        return found;
    }

    private static int? FindBatteryPercentage(JsonElement element)
    {
        int? result = null;
        Walk(element, (name, value) =>
        {
            if (result is not null ||
                !name.Contains("battery", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("charge", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("level", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var number = TryGetNumber(value);
            if (number is >= 0 and <= 100)
            {
                result = (int)Math.Round(number.Value);
            }
        });

        return result;
    }

    private static bool? FindChargingState(JsonElement element)
    {
        bool? result = null;
        Walk(element, (name, value) =>
        {
            if (result is not null ||
                !name.Contains("charg", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                result = value.GetBoolean();
                return;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                if (text.Contains("charging", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                }
            }
        });

        return result;
    }

    private static double? TryGetNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static void Walk(JsonElement element, Action<string, JsonElement> visitor)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    visitor(property.Name, property.Value);
                    Walk(property.Value, visitor);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    visitor(string.Empty, item);
                    Walk(item, visitor);
                }
                break;
        }
    }
}
