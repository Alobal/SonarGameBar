using System.Globalization;
using System.Text.Json;
using SonarGameBar.Core;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace SonarGameBar.Bridge;

internal sealed class AppServiceBridgeHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SonarClient _sonar = new();
    private readonly TaskCompletionSource _serviceClosed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private AppServiceConnection? _connection;

    public async Task<int> RunAsync()
    {
        BridgeLog.Write($"Opening App Service for package family '{Package.Current.Id.FamilyName}'.");

        _connection = new AppServiceConnection
        {
            AppServiceName = BridgeProtocol.ServiceName,
            PackageFamilyName = Package.Current.Id.FamilyName,
        };

        _connection.RequestReceived += OnRequestReceived;
        _connection.ServiceClosed += OnServiceClosed;

        var status = await _connection.OpenAsync();
        BridgeLog.Write($"App Service open status: {status}.");
        if (status != AppServiceConnectionStatus.Success)
        {
            return 1;
        }

        await _serviceClosed.Task;
        return 0;
    }

    public void Dispose()
    {
        if (_connection is not null)
        {
            _connection.RequestReceived -= OnRequestReceived;
            _connection.ServiceClosed -= OnServiceClosed;
            _connection.Dispose();
        }

        _sonar.Dispose();
    }

    private async void OnRequestReceived(
        AppServiceConnection sender,
        AppServiceRequestReceivedEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            var response = await HandleRequestAsync(args.Request.Message);
            await args.Request.SendResponseAsync(response);
        }
        catch
        {
            // The widget may disappear while a Sonar request is still in flight.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<ValueSet> HandleRequestAsync(ValueSet request)
    {
        try
        {
            var action = GetRequiredString(request, "action");
            if (action != BridgeProtocol.GetState)
            {
                BridgeLog.Write($"Request: {action}.");
            }

            switch (action)
            {
                case BridgeProtocol.GetState:
                    var state = await _sonar.GetStateAsync();
                    return Success(JsonSerializer.Serialize(state, JsonOptions));

                case BridgeProtocol.SetVolume:
                    await _sonar.SetVolumeAsync(
                        GetRequiredString(request, "channel"),
                        GetRequiredDouble(request, "value"));
                    return Success();

                case BridgeProtocol.SetMute:
                    await _sonar.SetMuteAsync(
                        GetRequiredString(request, "channel"),
                        GetRequiredBoolean(request, "value"));
                    return Success();

                case BridgeProtocol.SetChatMix:
                    await _sonar.SetChatMixAsync(GetRequiredDouble(request, "value"));
                    return Success();

                default:
                    throw new ArgumentException($"Unknown Bridge action '{action}'.");
            }
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Request failed: {exception}");
            return new ValueSet
            {
                ["ok"] = false,
                ["error"] = exception.Message,
            };
        }
    }

    private void OnServiceClosed(
        AppServiceConnection sender,
        AppServiceClosedEventArgs args)
    {
        BridgeLog.Write($"App Service closed: {args.Status}.");
        _serviceClosed.TrySetResult();
    }

    private static ValueSet Success(string? json = null)
    {
        var response = new ValueSet
        {
            ["ok"] = true,
        };

        if (json is not null)
        {
            response["json"] = json;
        }

        return response;
    }

    private static string GetRequiredString(ValueSet values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is not string text)
        {
            throw new ArgumentException($"Bridge request is missing '{key}'.");
        }

        return text;
    }

    private static double GetRequiredDouble(ValueSet values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"Bridge request is missing '{key}'.");
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static bool GetRequiredBoolean(ValueSet values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"Bridge request is missing '{key}'.");
        }

        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }
}
