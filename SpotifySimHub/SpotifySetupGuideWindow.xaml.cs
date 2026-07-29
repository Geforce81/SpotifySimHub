using System;
using System.Diagnostics;
using System.Windows;

namespace SpotifySimHub
{
    public partial class SpotifySetupGuideWindow : Window
    {
        private readonly SpotifyPlugin plugin;

        private const string DeveloperDashboardUrl =
            "https://developer.spotify.com/dashboard";
        private const string RedirectUri =
            "http://127.0.0.1:9877/callback";

        public SpotifySetupGuideWindow()
        {
            InitializeComponent();
            UpdateClientIdState();
        }

        public SpotifySetupGuideWindow(SpotifyPlugin plugin)
            : this()
        {
            this.plugin = plugin;
            UpdateClientIdState();
        }

        private void GuideClientIdBox_PasswordChanged(
            object sender,
            RoutedEventArgs e)
        {
            UpdateClientIdState();
        }

        private void GuideSaveClientIdButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (plugin == null)
            {
                return;
            }

            if (plugin.ConfigureClientId(
                    GuideClientIdBox.Password))
            {
                GuideClientIdBox.Clear();
                GuideClientIdStatusText.Text =
                    "Client ID saved. Continue to step 4.";
                return;
            }

            GuideClientIdStatusText.Text =
                "Check the Client ID and try again.";
            UpdateClientIdState();
        }

        private void UpdateClientIdState()
        {
            if (GuideSaveClientIdButton == null)
            {
                return;
            }

            GuideSaveClientIdButton.IsEnabled =
                plugin != null &&
                !plugin.IsBusy &&
                !string.IsNullOrWhiteSpace(
                    GuideClientIdBox.Password);

            if (plugin != null &&
                plugin.HasConfiguredClientId &&
                string.IsNullOrWhiteSpace(
                    GuideClientIdBox.Password))
            {
                GuideClientIdStatusText.Text =
                    "Client ID is saved. Continue to step 4.";
            }
        }

        private void OpenDashboardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = DeveloperDashboardUrl,
                        UseShellExecute = true
                    });
            }
            catch (Exception)
            {
                MessageBox.Show(
                    this,
                    "The Spotify Developer Dashboard could not be opened. " +
                    "Open developer.spotify.com/dashboard manually.",
                    "SpotifySimHub",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CopyRedirectUriButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RedirectUri);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    this,
                    "The redirect URI could not be copied. " +
                    "Select it in the guide and copy it manually.",
                    "SpotifySimHub",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
