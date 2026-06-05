using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace SonarGameBar.Widget
{
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer();
        private readonly Dictionary<string, CancellationTokenSource> _pendingChanges =
            new Dictionary<string, CancellationTokenSource>();
        private readonly Dictionary<string, DateTimeOffset> _localOverrideUntil =
            new Dictionary<string, DateTimeOffset>();

        private bool _applyingState;
        private bool _refreshing;
        private bool _masterMuted;
        private bool _gameMuted;
        private bool _chatMuted;

        public MainPage()
        {
            InitializeComponent();
            _pollTimer.Interval = TimeSpan.FromMilliseconds(750);
            _pollTimer.Tick += PollTimer_Tick;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _pollTimer.Start();
            await RefreshStateAsync();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _pollTimer.Stop();

            foreach (var pending in _pendingChanges.Values)
            {
                pending.Cancel();
                pending.Dispose();
            }

            _pendingChanges.Clear();
        }

        private async void PollTimer_Tick(object sender, object e)
        {
            await RefreshStateAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStateAsync();
        }

        private async Task RefreshStateAsync()
        {
            if (_refreshing)
            {
                return;
            }

            _refreshing = true;
            try
            {
                var request = new ValueSet { ["action"] = "getState" };
                var response = await App.Bridge.SendAsync(request);
                var json = response["json"] as string;

                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidOperationException("Bridge 返回了空状态。");
                }

                ApplyState(JsonObject.Parse(json));
                SetConnected(true, "已连接");
            }
            catch (Exception exception)
            {
                SetConnected(false, "未连接");
                ShowNotice(exception.Message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void ApplyState(JsonObject state)
        {
            _applyingState = true;
            try
            {
                if (!IsPollingSuppressed("master"))
                {
                    _masterMuted = ApplyChannel(
                        state.GetNamedObject("master"),
                        MasterSlider,
                        MasterValueText,
                        MasterMuteButton);
                }

                if (!IsPollingSuppressed("game"))
                {
                    _gameMuted = ApplyChannel(
                        state.GetNamedObject("game"),
                        GameSlider,
                        GameValueText,
                        GameMuteButton);
                }

                if (!IsPollingSuppressed("chatRender"))
                {
                    _chatMuted = ApplyChannel(
                        state.GetNamedObject("chat"),
                        ChatSlider,
                        ChatValueText,
                        ChatMuteButton);
                }

                var chatMix = Math.Round(state.GetNamedNumber("chatMix") * 100);
                if (!IsPollingSuppressed("chatMix"))
                {
                    ChatMixSlider.Value = chatMix;
                    ChatMixValueText.Text = FormatChatMix(chatMix);
                }

                var mode = state.GetNamedString("mode", "classic");
                if (mode != "classic")
                {
                    ShowNotice("当前是主播模式，第一版仅控制经典模式。");
                }
                else
                {
                    HideNotice();
                }
            }
            finally
            {
                _applyingState = false;
            }
        }

        private static bool ApplyChannel(
            JsonObject channel,
            Slider slider,
            TextBlock valueText,
            Button muteButton)
        {
            var value = Math.Round(channel.GetNamedNumber("volume") * 100);
            var muted = channel.GetNamedBoolean("muted");
            slider.Value = value;
            valueText.Text = value + "%";
            SetMuteVisual(muteButton, muted);
            return muted;
        }

        private void ChannelSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var slider = sender as Slider;
            if (slider == null)
            {
                return;
            }

            var channel = slider.Tag as string;
            UpdateValueLabel(channel, e.NewValue);

            if (!_applyingState && channel != null)
            {
                SuppressPolling(channel);
                QueueChange(
                    "volume:" + channel,
                    new ValueSet
                    {
                        ["action"] = "setVolume",
                        ["channel"] = channel,
                        ["value"] = e.NewValue / 100,
                    });
            }
        }

        private void ChatMixSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ChatMixValueText.Text = FormatChatMix(e.NewValue);

            if (!_applyingState)
            {
                SuppressPolling("chatMix");
                QueueChange(
                    "chatMix",
                    new ValueSet
                    {
                        ["action"] = "setChatMix",
                        ["value"] = e.NewValue / 100,
                    });
            }
        }

        private async void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var channel = button == null ? null : button.Tag as string;
            if (button == null || channel == null)
            {
                return;
            }

            var muted = !GetMuted(channel);
            SuppressPolling(channel);
            SetMuted(channel, muted);
            SetMuteVisual(button, muted);

            try
            {
                await App.Bridge.SendAsync(
                    new ValueSet
                    {
                        ["action"] = "setMute",
                        ["channel"] = channel,
                        ["value"] = muted,
                    });
            }
            catch (Exception exception)
            {
                ShowNotice(exception.Message);
                await RefreshStateAsync();
            }
        }

        private async void QueueChange(string key, ValueSet request)
        {
            CancellationTokenSource previous;
            if (_pendingChanges.TryGetValue(key, out previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            var cancellation = new CancellationTokenSource();
            _pendingChanges[key] = cancellation;

            try
            {
                await Task.Delay(100, cancellation.Token);
                await App.Bridge.SendAsync(request);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ShowNotice(exception.Message);
            }
            finally
            {
                CancellationTokenSource current;
                if (_pendingChanges.TryGetValue(key, out current) && current == cancellation)
                {
                    _pendingChanges.Remove(key);
                    cancellation.Dispose();
                }
            }
        }

        private void UpdateValueLabel(string channel, double value)
        {
            var text = Math.Round(value) + "%";
            if (channel == "master")
            {
                MasterValueText.Text = text;
            }
            else if (channel == "game")
            {
                GameValueText.Text = text;
            }
            else if (channel == "chatRender")
            {
                ChatValueText.Text = text;
            }
        }

        private bool GetMuted(string channel)
        {
            if (channel == "master")
            {
                return _masterMuted;
            }

            if (channel == "game")
            {
                return _gameMuted;
            }

            return _chatMuted;
        }

        private void SetMuted(string channel, bool muted)
        {
            if (channel == "master")
            {
                _masterMuted = muted;
            }
            else if (channel == "game")
            {
                _gameMuted = muted;
            }
            else
            {
                _chatMuted = muted;
            }
        }

        private static void SetMuteVisual(Button button, bool muted)
        {
            button.Content = new FontIcon
            {
                Glyph = muted ? "\uE74F" : "\uE767",
                FontSize = 15,
            };
            button.Foreground = new SolidColorBrush(muted
                ? Color.FromArgb(255, 255, 107, 114)
                : Color.FromArgb(255, 170, 178, 189));
        }

        private static string FormatChatMix(double value)
        {
            var rounded = Math.Round(value);
            if (rounded == 0)
            {
                return "居中";
            }

            return rounded < 0
                ? "游戏 " + Math.Abs(rounded) + "%"
                : "聊天 " + rounded + "%";
        }

        private void SetConnected(bool connected, string text)
        {
            ConnectionStatusText.Text = text;
            ConnectionDot.Fill = new SolidColorBrush(connected
                ? Color.FromArgb(255, 53, 214, 199)
                : Color.FromArgb(255, 255, 107, 114));
        }

        private void ShowNotice(string message)
        {
            NoticeText.Text = message;
            NoticePanel.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            NoticePanel.Visibility = Visibility.Collapsed;
        }

        private void SuppressPolling(string key)
        {
            _localOverrideUntil[key] = DateTimeOffset.UtcNow.AddSeconds(1);
        }

        private bool IsPollingSuppressed(string key)
        {
            DateTimeOffset until;
            if (!_localOverrideUntil.TryGetValue(key, out until))
            {
                return false;
            }

            if (until > DateTimeOffset.UtcNow)
            {
                return true;
            }

            _localOverrideUntil.Remove(key);
            return false;
        }
    }
}
