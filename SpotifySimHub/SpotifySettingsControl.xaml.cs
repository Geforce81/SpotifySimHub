using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SpotifySimHub
{
    public partial class SpotifySettingsControl : UserControl
    {
        public SpotifyPlugin Plugin { get; private set; }

        public SpotifySettingsControl()
        {
            InitializeComponent();
            Loaded += SpotifySettingsControl_Loaded;
            Unloaded += SpotifySettingsControl_Unloaded;
        }

        public SpotifySettingsControl(SpotifyPlugin plugin)
            : this()
        {
            Plugin = plugin;
            DataContext = plugin;
            UpdateButtonState();
        }

        private void SpotifySettingsControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin == null)
            {
                return;
            }

            Plugin.PropertyChanged -= Plugin_PropertyChanged;
            Plugin.PropertyChanged += Plugin_PropertyChanged;
            UpdateButtonState();
        }

        private void SpotifySettingsControl_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                Plugin.PropertyChanged -= Plugin_PropertyChanged;
            }
        }

        private void Plugin_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new System.Action(UpdateButtonState));
                return;
            }

            UpdateButtonState();
        }

        private async void ConnectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                await Plugin.ConnectAsync();
            }
        }

        private void DisconnectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Plugin?.Disconnect();
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                await Plugin.RefreshStatusAsync();
            }
        }

        private void UpdateButtonState()
        {
            if (Plugin == null)
            {
                return;
            }

            ConnectButton.Content =
                Plugin.IsConnected || Plugin.HasSavedLogin
                    ? "Reconnect"
                    : "Connect";

            ConnectButton.IsEnabled = !Plugin.IsBusy;
            DisconnectButton.IsEnabled =
                !Plugin.IsBusy &&
                (Plugin.IsConnected || Plugin.HasSavedLogin);
            RefreshButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.HasSavedLogin;
        }
    }
}
