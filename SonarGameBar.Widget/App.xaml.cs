using System;
using Microsoft.Gaming.XboxGameBar;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.AppService;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SonarGameBar.Widget
{
    sealed partial class App : Application
    {
        private XboxGameBarWidget _widget;

        internal static BridgeConnection Bridge { get; private set; }

        public App()
        {
            InitializeComponent();
            Bridge = new BridgeConnection();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            ShowMainPage(null);
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            XboxGameBarWidgetActivatedEventArgs widgetArgs = null;

            if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as IProtocolActivatedEventArgs;
                if (protocolArgs != null && protocolArgs.Uri.Scheme == "ms-gamebarwidget")
                {
                    widgetArgs = args as XboxGameBarWidgetActivatedEventArgs;
                }
            }

            if (widgetArgs == null || !widgetArgs.IsLaunchActivation || widgetArgs.AppExtensionId != "SonarMixer")
            {
                return;
            }

            var rootFrame = new Frame();
            Window.Current.Content = rootFrame;
            _widget = new XboxGameBarWidget(widgetArgs, Window.Current.CoreWindow, rootFrame);
            rootFrame.Navigate(typeof(MainPage), _widget);
            Window.Current.Activate();
        }

        protected override void OnBackgroundActivated(BackgroundActivatedEventArgs args)
        {
            var details = args.TaskInstance.TriggerDetails as AppServiceTriggerDetails;
            if (details != null && details.Name == BridgeConnection.ServiceName)
            {
                Bridge.Attach(details.AppServiceConnection, args.TaskInstance.GetDeferral());
            }
        }

        private static void ShowMainPage(object parameter)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage), parameter);
            }

            Window.Current.Activate();
        }
    }
}
