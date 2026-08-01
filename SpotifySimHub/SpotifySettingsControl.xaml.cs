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
                if (Dispatcher.HasShutdownStarted ||
                    Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(
                        new System.Action(UpdateButtonState));
                }
                catch (System.InvalidOperationException)
                {
                    // The settings view can be unloaded while an update is
                    // already queued. Playback must continue unaffected.
                }
                return;
            }

            if (!IsLoaded)
            {
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

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Plugin?.CancelConnectionAttempt();
        }

        private void SetupGuideButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SpotifySetupGuideWindow guide =
                new SpotifySetupGuideWindow(Plugin)
                {
                    Owner = Window.GetWindow(this)
                };

            guide.ShowDialog();
        }

        private void ClientIdBox_PasswordChanged(
            object sender,
            RoutedEventArgs e)
        {
            UpdateButtonState();
        }

        private void SaveClientIdButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null &&
                Plugin.ConfigureClientId(
                    ClientIdBox.Password))
            {
                ClientIdBox.Clear();
            }

            UpdateButtonState();
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

        private async void PreviousTrackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                await Plugin.PreviousTrackAsync();
            }
        }

        private async void TogglePlaybackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                await Plugin.TogglePlaybackAsync();
            }
        }

        private async void NextTrackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Plugin != null)
            {
                await Plugin.NextTrackAsync();
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

            ConnectButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.HasConfiguredClientId;
            DisconnectButton.IsEnabled =
                !Plugin.IsBusy &&
                (Plugin.IsConnected || Plugin.HasSavedLogin);
            RefreshButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.HasSavedLogin &&
                Plugin.HasConfiguredClientId;
            SaveClientIdButton.IsEnabled =
                !Plugin.IsBusy &&
                !string.IsNullOrWhiteSpace(
                    ClientIdBox.Password);
            CancelButton.IsEnabled =
                Plugin.IsAuthorizationInProgress;
            PreviousTrackButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.IsConnected;
            TogglePlaybackButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.IsConnected;
            NextTrackButton.IsEnabled =
                !Plugin.IsBusy &&
                Plugin.IsConnected;
        }
    }
}
