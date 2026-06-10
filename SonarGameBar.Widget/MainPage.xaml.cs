using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace SonarGameBar.Widget
{
    public sealed partial class MainPage : Page
    {
        private readonly DispatcherTimer _pollTimer = new DispatcherTimer();
        private readonly Dictionary<string, CancellationTokenSource> _pendingChanges =
            new Dictionary<string, CancellationTokenSource>();
        private readonly Dictionary<string, DateTimeOffset> _localOverrideUntil =
            new Dictionary<string, DateTimeOffset>();
        private readonly Dictionary<string, ChannelControls> _channelControls =
            new Dictionary<string, ChannelControls>();
        private readonly Dictionary<string, bool> _mutedByChannel =
            new Dictionary<string, bool>();
        private readonly List<string> _renderedChannelOrder = new List<string>();

        private bool _applyingState;
        private bool _refreshing;
        private string _lastNoticeMessage;
        private DateTimeOffset _lastNoticeAt;

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

        private void ChatMixQuickButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null || button.Tag == null)
            {
                return;
            }

            var value = Convert.ToDouble(button.Tag, CultureInfo.InvariantCulture);
            ChatMixSlider.Value = value;
            QueueChatMixChange(value);
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
                var channels = ReadChannels(state);
                RenderChannelRows(channels);
                ApplyBatteries(ReadBatteries(state));

                foreach (var channel in channels)
                {
                    if (!IsPollingSuppressed(channel.Id))
                    {
                        ApplyChannel(channel);
                    }
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
                    ShowNotice("当前是 " + mode + " 模式，当前版本仅写入 classic 模式。");
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

        private static List<ChannelView> ReadChannels(JsonObject state)
        {
            var channels = new List<ChannelView>();
            if (!state.ContainsKey("channels"))
            {
                return channels;
            }

            foreach (var value in state.GetNamedArray("channels"))
            {
                var channel = value.GetObject();
                var id = channel.GetNamedString("id", string.Empty);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var active = channel.GetNamedBoolean("active", false);
                if (!IsAlwaysVisibleChannel(id) && !active)
                {
                    continue;
                }

                channels.Add(new ChannelView
                {
                    Id = id,
                    DisplayName = channel.GetNamedString("displayName", id),
                    Volume = Math.Round(channel.GetNamedNumber("volume", 0) * 100),
                    Muted = channel.GetNamedBoolean("muted", false),
                    Controllable = channel.GetNamedBoolean("controllable", true),
                    Active = active,
                    SortOrder = (int)channel.GetNamedNumber("sortOrder", 1000),
                });
            }

            channels.Sort((left, right) =>
            {
                var order = left.SortOrder.CompareTo(right.SortOrder);
                return order != 0
                    ? order
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCulture);
            });

            return channels;
        }

        private void RenderChannelRows(IList<ChannelView> channels)
        {
            var desiredOrder = channels.Select(channel => channel.Id).ToList();
            foreach (var entry in _channelControls)
            {
                if (!desiredOrder.Contains(entry.Key))
                {
                    entry.Value.Container.Visibility = Visibility.Collapsed;
                }
            }

            foreach (var channel in channels)
            {
                EnsureChannelControls(channel).Container.Visibility = Visibility.Visible;
            }

            if (_renderedChannelOrder.SequenceEqual(desiredOrder))
            {
                return;
            }

            ChannelRowsPanel.Children.Clear();
            foreach (var channel in channels)
            {
                ChannelRowsPanel.Children.Add(_channelControls[channel.Id].Container);
            }

            _renderedChannelOrder.Clear();
            _renderedChannelOrder.AddRange(desiredOrder);
        }

        private static List<BatteryView> ReadBatteries(JsonObject state)
        {
            var batteries = new List<BatteryView>();
            if (!state.ContainsKey("batteries"))
            {
                return batteries;
            }

            foreach (var value in state.GetNamedArray("batteries"))
            {
                var battery = value.GetObject();
                int? percentage = null;
                if (battery.ContainsKey("percentage") &&
                    battery.GetNamedValue("percentage").ValueType == JsonValueType.Number)
                {
                    percentage = (int)Math.Round(battery.GetNamedNumber("percentage"));
                }

                batteries.Add(new BatteryView
                {
                    DeviceName = battery.GetNamedString("deviceName", "设备"),
                    Percentage = percentage,
                    Charging = battery.ContainsKey("charging") &&
                        battery.GetNamedValue("charging").ValueType == JsonValueType.Boolean
                            ? battery.GetNamedBoolean("charging")
                            : (bool?)null,
                });
            }

            return batteries;
        }

        private void ApplyBatteries(IList<BatteryView> batteries)
        {
            var battery = batteries.FirstOrDefault(candidate => candidate.Percentage.HasValue);
            if (battery == null)
            {
                BatteryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryPanel.Visibility = Visibility.Visible;
            BatteryText.Text = battery.Percentage.Value + "%" + (battery.Charging == true ? " 充电" : string.Empty);
            ToolTipService.SetToolTip(BatteryPanel, battery.DeviceName + " 电量");
        }

        private ChannelControls EnsureChannelControls(ChannelView channel)
        {
            ChannelControls controls;
            if (_channelControls.TryGetValue(channel.Id, out controls))
            {
                controls.Label.Text = channel.DisplayName;
                controls.Slider.IsEnabled = channel.Controllable;
                controls.MuteButton.IsEnabled = channel.Controllable;
                return controls;
            }

            var border = new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["PanelLineBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 7, 0, 7),
            };

            var grid = new Grid();
            var activeColumn = new ColumnDefinition { Width = new GridLength(12) };
            grid.ColumnDefinitions.Add(activeColumn);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var activeDot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(activeDot, "正在出声");

            var label = new TextBlock
            {
                Text = channel.DisplayName,
                Style = (Style)Application.Current.Resources["ChannelLabelStyle"],
            };

            var slider = new Slider
            {
                Tag = channel.Id,
                Minimum = 0,
                Maximum = 100,
                StepFrequency = 1,
                Foreground = (Brush)Application.Current.Resources["SonarAccentBrush"],
                IsEnabled = channel.Controllable,
            };
            slider.ValueChanged += ChannelSlider_ValueChanged;
            AutomationProperties.SetName(slider, channel.DisplayName + "音量");

            var valueText = new TextBlock
            {
                Text = "--%",
                Style = (Style)Application.Current.Resources["ValueLabelStyle"],
            };

            var muteButton = new Button
            {
                Tag = channel.Id,
                Style = (Style)Application.Current.Resources["IconButtonStyle"],
                IsEnabled = channel.Controllable,
            };
            muteButton.Click += MuteButton_Click;
            ToolTipService.SetToolTip(muteButton, "静音" + channel.DisplayName);
            AutomationProperties.SetName(muteButton, "静音" + channel.DisplayName);

            Grid.SetColumn(activeDot, 0);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(slider, 2);
            Grid.SetColumn(valueText, 3);
            Grid.SetColumn(muteButton, 4);

            grid.Children.Add(activeDot);
            grid.Children.Add(label);
            grid.Children.Add(slider);
            grid.Children.Add(valueText);
            grid.Children.Add(muteButton);
            border.Child = grid;

            controls = new ChannelControls
            {
                Container = border,
                Label = label,
                Slider = slider,
                ValueText = valueText,
                MuteButton = muteButton,
                ActiveDot = activeDot,
            };

            _channelControls[channel.Id] = controls;
            return controls;
        }

        private void ApplyChannel(ChannelView channel)
        {
            ChannelControls controls;
            if (!_channelControls.TryGetValue(channel.Id, out controls))
            {
                return;
            }

            _mutedByChannel[channel.Id] = channel.Muted;
            controls.Slider.Value = channel.Volume;
            controls.ValueText.Text = channel.Volume + "%";
            controls.Slider.IsEnabled = channel.Controllable;
            controls.MuteButton.IsEnabled = channel.Controllable;
            SetMuteVisual(controls.MuteButton, channel.Muted);
            SetActiveVisual(controls.ActiveDot, channel.Active);
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
                QueueChatMixChange(e.NewValue);
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

        private void QueueChatMixChange(double value)
        {
            SuppressPolling("chatMix");
            QueueChange(
                "chatMix",
                new ValueSet
                {
                    ["action"] = "setChatMix",
                    ["value"] = value / 100,
                });
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
            if (channel == null)
            {
                return;
            }

            ChannelControls controls;
            if (_channelControls.TryGetValue(channel, out controls))
            {
                controls.ValueText.Text = Math.Round(value) + "%";
            }
        }

        private bool GetMuted(string channel)
        {
            bool muted;
            return _mutedByChannel.TryGetValue(channel, out muted) && muted;
        }

        private void SetMuted(string channel, bool muted)
        {
            _mutedByChannel[channel] = muted;
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
                : Color.FromArgb(255, 244, 247, 250));
        }

        private static void SetActiveVisual(Ellipse dot, bool active)
        {
            dot.Fill = new SolidColorBrush(active
                ? Color.FromArgb(255, 53, 214, 199)
                : Colors.Transparent);
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

        private static bool IsAlwaysVisibleChannel(string id)
        {
            return id == "master" || id == "game" || id == "chatRender";
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
            var now = DateTimeOffset.UtcNow;
            if (message == _lastNoticeMessage && now - _lastNoticeAt < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastNoticeMessage = message;
            _lastNoticeAt = now;
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

        private sealed class ChannelView
        {
            public string Id { get; set; }

            public string DisplayName { get; set; }

            public double Volume { get; set; }

            public bool Muted { get; set; }

            public bool Controllable { get; set; }

            public bool Active { get; set; }

            public int SortOrder { get; set; }
        }

        private sealed class BatteryView
        {
            public string DeviceName { get; set; }

            public int? Percentage { get; set; }

            public bool? Charging { get; set; }
        }

        private sealed class ChannelControls
        {
            public Border Container { get; set; }

            public TextBlock Label { get; set; }

            public Slider Slider { get; set; }

            public TextBlock ValueText { get; set; }

            public Button MuteButton { get; set; }

            public Ellipse ActiveDot { get; set; }
        }
    }
}
