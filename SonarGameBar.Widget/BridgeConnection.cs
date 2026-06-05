using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;

namespace SonarGameBar.Widget
{
    internal sealed class BridgeConnection
    {
        public const string ServiceName = "SonarGameBar.BridgeService";

        private readonly object _gate = new object();
        private readonly SemaphoreSlim _launchLock = new SemaphoreSlim(1, 1);

        private AppServiceConnection _connection;
        private BackgroundTaskDeferral _backgroundDeferral;
        private TaskCompletionSource<AppServiceConnection> _connectionReady = NewCompletionSource();

        public void Attach(AppServiceConnection connection, BackgroundTaskDeferral backgroundDeferral)
        {
            lock (_gate)
            {
                if (_connection != null)
                {
                    backgroundDeferral.Complete();
                    connection.Dispose();
                    return;
                }

                _connection = connection;
                _backgroundDeferral = backgroundDeferral;
                _connection.ServiceClosed += Connection_ServiceClosed;
                _connectionReady.TrySetResult(_connection);
            }
        }

        public async Task<ValueSet> SendAsync(ValueSet request)
        {
            var connection = await EnsureConnectedAsync();
            var response = await connection.SendMessageAsync(request);

            if (response.Status != AppServiceResponseStatus.Success)
            {
                throw new InvalidOperationException("Bridge 没有响应。");
            }

            object okValue;
            if (!response.Message.TryGetValue("ok", out okValue) || !(okValue is bool) || !(bool)okValue)
            {
                object errorValue;
                var message = response.Message.TryGetValue("error", out errorValue)
                    ? errorValue as string
                    : "Bridge 请求失败。";
                throw new InvalidOperationException(message ?? "Bridge 请求失败。");
            }

            return response.Message;
        }

        private async Task<AppServiceConnection> EnsureConnectedAsync()
        {
            lock (_gate)
            {
                if (_connection != null)
                {
                    return _connection;
                }
            }

            await _launchLock.WaitAsync();
            try
            {
                lock (_gate)
                {
                    if (_connection != null)
                    {
                        return _connection;
                    }
                }

                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("SonarBridge");

                Task<AppServiceConnection> readyTask;
                lock (_gate)
                {
                    readyTask = _connectionReady.Task;
                }

                var completed = await Task.WhenAny(readyTask, Task.Delay(TimeSpan.FromSeconds(8)));
                if (completed != readyTask)
                {
                    throw new TimeoutException("启动 Sonar Bridge 超时。");
                }

                return await readyTask;
            }
            finally
            {
                _launchLock.Release();
            }
        }

        private void Connection_ServiceClosed(AppServiceConnection sender, AppServiceClosedEventArgs args)
        {
            lock (_gate)
            {
                if (_connection != sender)
                {
                    return;
                }

                _connection.ServiceClosed -= Connection_ServiceClosed;
                _connection.Dispose();
                _connection = null;
                _connectionReady = NewCompletionSource();

                if (_backgroundDeferral != null)
                {
                    _backgroundDeferral.Complete();
                    _backgroundDeferral = null;
                }
            }
        }

        private static TaskCompletionSource<AppServiceConnection> NewCompletionSource()
        {
            return new TaskCompletionSource<AppServiceConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
