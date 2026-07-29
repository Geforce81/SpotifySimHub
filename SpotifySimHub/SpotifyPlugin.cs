using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SpotifySimHub
{
    [PluginDescription("Displays the current Spotify track in SimHub")]
    [PluginAuthor("Gustavius")]
    [PluginName("SpotifySimHub")]
    public class SpotifyPlugin :
        IPlugin,
        IDataPlugin,
        IWPFSettingsV2,
        INotifyPropertyChanged
    {
        public SpotifyPluginSettings Settings;

        public PluginManager PluginManager { get; set; }

        public ImageSource PictureIcon =>
            this.ToIcon(Properties.Resources.SpotifySimHubIcon);

        public string LeftMenuTitle => "SpotifySimHub";

        private string currentTrack = "";
        private string artist = "";
        private string track = "";
        private string album = "";
        private string cover = "";
        private string coverDash = "";
        private ImageSource coverImage;
        private string connectionStatus = "Disconnected";
        private bool isConnected;
        private bool hasSavedLogin;
        private bool isBusy;
        private bool isAuthorizationInProgress;
        private bool hasConfiguredClientId;
        private string clientIdConfigurationStatus =
            "Spotify Client ID required";

        public string CurrentTrack
        {
            get => currentTrack;
            private set => SetProperty(ref currentTrack, value);
        }

        public string Artist
        {
            get => artist;
            private set => SetProperty(ref artist, value);
        }

        public string Track
        {
            get => track;
            private set => SetProperty(ref track, value);
        }

        public string Album
        {
            get => album;
            private set => SetProperty(ref album, value);
        }

        public string Cover
        {
            get => cover;
            private set => SetProperty(ref cover, value);
        }

        public string CoverDash
        {
            get => coverDash;
            private set => SetProperty(ref coverDash, value);
        }

        public ImageSource CoverImage
        {
            get => coverImage;
            private set => SetProperty(ref coverImage, value);
        }

        public string ConnectionStatus
        {
            get => connectionStatus;
            private set => SetProperty(ref connectionStatus, value);
        }

        public bool IsConnected
        {
            get => isConnected;
            private set => SetProperty(ref isConnected, value);
        }

        public bool HasSavedLogin
        {
            get => hasSavedLogin;
            private set
            {
                if (SetProperty(ref hasSavedLogin, value))
                {
                    OnPropertyChanged(nameof(SavedLoginStatus));
                }
            }
        }

        public string SavedLoginStatus =>
            HasSavedLogin ? "Yes" : "No";

        public bool IsBusy
        {
            get => isBusy;
            private set => SetProperty(ref isBusy, value);
        }

        public bool IsAuthorizationInProgress
        {
            get => isAuthorizationInProgress;
            private set =>
                SetProperty(
                    ref isAuthorizationInProgress,
                    value);
        }

        public bool HasConfiguredClientId
        {
            get => hasConfiguredClientId;
            private set =>
                SetProperty(
                    ref hasConfiguredClientId,
                    value);
        }

        public string ClientIdConfigurationStatus
        {
            get => clientIdConfigurationStatus;
            private set =>
                SetProperty(
                    ref clientIdConfigurationStatus,
                    value);
        }

        private readonly HttpClient httpClient = new HttpClient();

        private static readonly string BuildClientId =
            SpotifyBuildConfiguration.ClientId;
        private const string RedirectUri = "http://127.0.0.1:9877/callback";
        private const string ListenerPrefix = "http://127.0.0.1:9877/";
        private static readonly TimeSpan HttpRequestTimeout =
            TimeSpan.FromSeconds(20);
        private static readonly TimeSpan AuthorizationTimeout =
            TimeSpan.FromMinutes(2);

        private string accessToken = "";
        private string refreshToken = "";
        private string configuredClientId = "";
        private DateTime accessTokenExpiresUtc = DateTime.MinValue;

        private readonly object lifecycleLock = new object();
        private readonly object tokenLock = new object();
        private readonly SemaphoreSlim loginSemaphore =
            new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim refreshSemaphore =
            new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim playbackSemaphore =
            new SemaphoreSlim(1, 1);

        private SpotifyOAuthClient oauthClient;
        private SpotifyApiClient apiClient;
        private SpotifyTokenStore tokenStore;
        private SpotifyCoverArtCache coverArtCache;
        private CancellationTokenSource pluginCancellation;
        private CancellationTokenSource sessionCancellation;
        private Task startupTask = Task.CompletedTask;
        private Task connectionTask = Task.CompletedTask;
        private Task playbackTask = Task.CompletedTask;
        private DateTime lastTrackRequest = DateTime.MinValue;
        private long nextPlaybackRequestUtcTicks;
        private int temporaryFailureCount;
        private int authenticationGeneration;
        private int busyOperationCount;
        private int automaticReauthorizationStarted;
        private bool ending;

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        private string SpotifyDataFolder =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SpotifySimHub");

        public bool ConfigureClientId(string clientId)
        {
            if (ending)
            {
                return false;
            }

            string normalizedClientId =
                (clientId ?? "").Trim();

            if (!IsUsableClientId(normalizedClientId))
            {
                ClientIdConfigurationStatus =
                    "Enter a valid Spotify Client ID";
                return false;
            }

            bool clientChanged =
                !string.Equals(
                    configuredClientId,
                    normalizedClientId,
                    StringComparison.Ordinal);

            Settings.SpotifyClientId =
                normalizedClientId;

            this.SaveCommonSettings(
                "GeneralSettings",
                Settings);

            if (!clientChanged)
            {
                ClientIdConfigurationStatus =
                    "Spotify Client ID is configured";
                HasConfiguredClientId = true;
                return true;
            }

            Interlocked.Increment(
                ref authenticationGeneration);
            RenewSessionCancellation();

            lock (tokenLock)
            {
                accessToken = "";
                refreshToken = "";
                accessTokenExpiresUtc =
                    DateTime.MinValue;
            }

            try
            {
                tokenStore?.Delete();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(
                    "Could not clear the previous Spotify login. " +
                    ex.GetType().Name);
            }

            ConfigureSpotifyClients(
                normalizedClientId);
            Interlocked.Exchange(
                ref automaticReauthorizationStarted,
                0);

            ClearPlaybackAndCover();
            ResetPlaybackBackoff();
            HasSavedLogin = false;
            IsConnected = false;
            ConnectionStatus =
                "Client ID saved. Press Connect.";
            return true;
        }

        private static bool IsUsableClientId(
            string clientId)
        {
            return
                !string.IsNullOrWhiteSpace(clientId) &&
                !string.Equals(
                    clientId,
                    "YOUR_SPOTIFY_CLIENT_ID",
                    StringComparison.Ordinal);
        }

        private void ConfigureSpotifyClients(
            string clientId)
        {
            configuredClientId =
                IsUsableClientId(clientId)
                    ? clientId.Trim()
                    : "";

            HasConfiguredClientId =
                !string.IsNullOrEmpty(
                    configuredClientId);
            ClientIdConfigurationStatus =
                HasConfiguredClientId
                    ? "Spotify Client ID is configured"
                    : "Spotify Client ID required";

            if (!HasConfiguredClientId)
            {
                oauthClient = null;
                apiClient = null;
                return;
            }

            oauthClient =
                new SpotifyOAuthClient(
                    httpClient,
                    configuredClientId,
                    RedirectUri,
                    ListenerPrefix,
                    AuthorizationTimeout);
            apiClient =
                new SpotifyApiClient(
                    httpClient,
                    configuredClientId);
        }

        private async Task<bool> RefreshAccessTokenAsync(
            CancellationToken cancellationToken)
        {
            int refreshGeneration = authenticationGeneration;

            await refreshSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                string savedRefreshToken;

                lock (tokenLock)
                {
                    savedRefreshToken = refreshToken;
                }

                if (string.IsNullOrEmpty(savedRefreshToken))
                {
                    IsConnected = false;
                    ConnectionStatus = "Login required";
                    return false;
                }

                if (!HasConfiguredClientId ||
                    apiClient == null)
                {
                    IsConnected = false;
                    ConnectionStatus =
                        "Spotify Client ID required";
                    return false;
                }

                ConnectionStatus =
                    "Refreshing Spotify session...";

                SpotifyTokenResult tokenResult =
                    await apiClient.RefreshAccessTokenAsync(
                            savedRefreshToken,
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (refreshGeneration !=
                    authenticationGeneration)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(
                        tokenResult.AccessToken))
                {
                    IsConnected = false;
                    ConnectionStatus = "Login required";
                    return false;
                }

                string tokenToPersist =
                    string.IsNullOrEmpty(
                        tokenResult.RefreshToken)
                        ? savedRefreshToken
                        : tokenResult.RefreshToken;

                tokenStore.Save(tokenToPersist);
                ApplyTokenResult(
                    tokenResult,
                    tokenToPersist);

                HasSavedLogin = true;
                IsConnected = true;
                ConnectionStatus = "Connected";
                return true;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                ConnectionStatus =
                    "Spotify is temporarily unavailable";
                ScheduleTemporaryFailureBackoff();

                SimHub.Logging.Current.Error(
                    "Spotify refresh request timed out.");

                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SpotifyApiException ex)
                when (ex.Kind ==
                      SpotifyApiErrorKind.InvalidGrant)
            {
                if (ending ||
                    cancellationToken.IsCancellationRequested ||
                    refreshGeneration !=
                    authenticationGeneration)
                {
                    return false;
                }

                HandleExpiredAuthorization();
                QueueAutomaticReauthorization();

                SimHub.Logging.Current.Info(
                    "The saved Spotify authorization expired; " +
                    "reauthorization was requested.");

                return false;
            }
            catch (SpotifyApiException ex)
            {
                IsConnected = false;
                ConnectionStatus = "Login required";

                SimHub.Logging.Current.Error(
                    "Spotify refresh failed. " +
                    ex.GetType().Name);

                return false;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStatus =
                    "Spotify is temporarily unavailable";

                SimHub.Logging.Current.Error(
                    "Spotify refresh failed. " +
                    ex.GetType().Name);

                return false;
            }
            finally
            {
                refreshSemaphore.Release();
            }
        }

        private void HandleExpiredAuthorization()
        {
            Interlocked.Increment(
                ref authenticationGeneration);
            RenewSessionCancellation();

            lock (tokenLock)
            {
                accessToken = "";
                refreshToken = "";
                accessTokenExpiresUtc =
                    DateTime.MinValue;
            }

            try
            {
                tokenStore?.Delete();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(
                    "Could not remove the expired Spotify login. " +
                    ex.GetType().Name);
            }

            HasSavedLogin = false;
            IsConnected = false;
            ClearPlaybackAndCover();
            ResetPlaybackBackoff();
            ConnectionStatus =
                "Spotify login expired. Reauthorization required.";
        }

        private void QueueAutomaticReauthorization()
        {
            if (ending ||
                !HasConfiguredClientId ||
                Interlocked.CompareExchange(
                    ref automaticReauthorizationStarted,
                    1,
                    0) != 0)
            {
                return;
            }

            Task task =
                AutomaticReauthorizationAsync(
                    GetSessionCancellationToken());

            lock (lifecycleLock)
            {
                connectionTask = task;
            }
        }

        private async Task AutomaticReauthorizationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            BeginBusy();

            try
            {
                ConnectionStatus =
                    "Spotify login expired. Opening authorization...";

                await LoginToSpotifyAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(
                    ref automaticReauthorizationStarted,
                    0);
                EndBusy();
            }
        }

        private async Task<bool> LoginToSpotifyAsync(
            CancellationToken cancellationToken)
        {
            await loginSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            int loginGeneration = authenticationGeneration;
            bool authorizationStarted = false;

            try
            {
                if (!HasConfiguredClientId ||
                    oauthClient == null)
                {
                    IsConnected = false;
                    ConnectionStatus =
                        "Spotify Client ID required";
                    return false;
                }

                IsAuthorizationInProgress = true;
                authorizationStarted = true;
                ConnectionStatus = "Connecting to Spotify...";

                SpotifyTokenResult tokenResult =
                    await oauthClient.AuthorizeAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (loginGeneration != authenticationGeneration)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(
                        tokenResult.AccessToken) ||
                    string.IsNullOrEmpty(
                        tokenResult.RefreshToken))
                {
                    ConnectionStatus =
                        "Spotify authorization failed";
                    return false;
                }

                tokenStore.Save(
                    tokenResult.RefreshToken);
                ApplyTokenResult(
                    tokenResult,
                    tokenResult.RefreshToken);

                HasSavedLogin = true;
                IsConnected = true;
                ConnectionStatus = "Connected";

                await UpdatePlaybackAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                IsConnected = false;
                ConnectionStatus =
                    "Spotify authorization failed";

                SimHub.Logging.Current.Error(
                    "Spotify login request timed out.");

                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SpotifyAuthenticationException ex)
            {
                IsConnected = false;
                ConnectionStatus =
                    ex.Kind ==
                    SpotifyAuthenticationErrorKind.TimedOut
                        ? "Spotify authorization timed out"
                        : ex.Kind ==
                          SpotifyAuthenticationErrorKind.Cancelled
                            ? "Spotify authorization was cancelled"
                            : "Spotify authorization failed";

                SimHub.Logging.Current.Error(
                    "Spotify authorization did not complete. " +
                    ex.Kind +
                    ". " +
                    ex.GetType().Name);

                return false;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStatus =
                    "Spotify authorization failed";

                SimHub.Logging.Current.Error(
                    "Spotify login failed. " +
                    ex.GetType().Name);

                return false;
            }
            finally
            {
                if (authorizationStarted)
                {
                    IsAuthorizationInProgress = false;
                }

                loginSemaphore.Release();
            }
        }

        public Task ConnectAsync()
        {
            if (ending)
            {
                return Task.CompletedTask;
            }

            Task task =
                ConnectCoreAsync(
                    GetSessionCancellationToken());

            lock (lifecycleLock)
            {
                connectionTask = task;
            }

            return task;
        }

        public void CancelConnectionAttempt()
        {
            if (ending ||
                !IsAuthorizationInProgress)
            {
                return;
            }

            Interlocked.Increment(
                ref authenticationGeneration);
            RenewSessionCancellation();
            Interlocked.Exchange(
                ref automaticReauthorizationStarted,
                0);

            ConnectionStatus =
                IsConnected
                    ? "Connected"
                    : HasSavedLogin
                        ? "Spotify authorization was cancelled; " +
                          "saved login retained"
                        : "Spotify authorization was cancelled";
        }

        public void Disconnect()
        {
            Interlocked.Increment(
                ref authenticationGeneration);

            IsConnected = false;
            RenewSessionCancellation();
            Interlocked.Exchange(
                ref automaticReauthorizationStarted,
                0);

            lock (tokenLock)
            {
                accessToken = "";
                refreshToken = "";
                accessTokenExpiresUtc =
                    DateTime.MinValue;
            }

            try
            {
                tokenStore?.Delete();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(
                    "Could not delete the saved Spotify login. " +
                    ex.GetType().Name);
            }

            ClearPlaybackAndCover();
            ResetPlaybackBackoff();

            HasSavedLogin = false;
            ConnectionStatus = "Disconnected";
        }

        public Task RefreshStatusAsync()
        {
            if (ending)
            {
                return Task.CompletedTask;
            }

            Task task =
                RefreshStatusCoreAsync(
                    GetSessionCancellationToken());

            lock (lifecycleLock)
            {
                connectionTask = task;
            }

            return task;
        }

        private void ClearPlaybackAndCover()
        {
            Artist = "";
            Track = "";
            Album = "";
            CurrentTrack = "";
            Cover = "";
            CoverDash = "";
            CoverImage = null;

            try
            {
                coverArtCache?.Clear();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(
                    "Could not clear cached Spotify cover art. " +
                    ex.GetType().Name);
            }
        }

        private void ResetPlaybackBackoff()
        {
            Interlocked.Exchange(
                ref temporaryFailureCount,
                0);
            Interlocked.Exchange(
                ref nextPlaybackRequestUtcTicks,
                0);
        }

        private void ScheduleRateLimitBackoff(
            TimeSpan? retryAfter)
        {
            double requestedSeconds =
                retryAfter?.TotalSeconds ?? 5;
            double delaySeconds =
                Math.Max(
                    1,
                    Math.Min(
                        60,
                        requestedSeconds));

            Interlocked.Exchange(
                ref nextPlaybackRequestUtcTicks,
                DateTime.UtcNow
                    .AddSeconds(delaySeconds)
                    .Ticks);
        }

        private void ScheduleTemporaryFailureBackoff()
        {
            int failureCount =
                Math.Min(
                    5,
                    Interlocked.Increment(
                        ref temporaryFailureCount));
            int delaySeconds =
                Math.Min(
                    30,
                    1 << failureCount);

            Interlocked.Exchange(
                ref nextPlaybackRequestUtcTicks,
                DateTime.UtcNow
                    .AddSeconds(delaySeconds)
                    .Ticks);
        }

        private void ScheduleIdlePolling()
        {
            Interlocked.Exchange(
                ref nextPlaybackRequestUtcTicks,
                DateTime.UtcNow
                    .AddSeconds(5)
                    .Ticks);
        }

        private async Task UpdateCoverArtAsync(
            string coverUrl,
            int operationGeneration,
            CancellationToken cancellationToken)
        {
            try
            {
                SpotifyCoverArtResult result =
                    await coverArtCache.GetAsync(
                            coverUrl,
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (operationGeneration !=
                    authenticationGeneration)
                {
                    return;
                }

                Cover = result.CoverPath;
                CoverDash = result.DashCoverPath;
                CoverImage = result.CoverImage;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                SimHub.Logging.Current.Error(
                    "Spotify cover download timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error(
                    "Spotify cover download failed. " +
                    ex.GetType().Name);
            }
        }

        private async Task UpdatePlaybackAsync(
            CancellationToken cancellationToken)
        {
            await playbackSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await UpdatePlaybackCoreAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                playbackSemaphore.Release();
            }
        }

        private async Task RunPlaybackUpdateAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await UpdatePlaybackCoreAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                playbackSemaphore.Release();
            }
        }

        private async Task UpdatePlaybackCoreAsync(
            CancellationToken cancellationToken)
        {
            int playbackGeneration = authenticationGeneration;

            try
            {
                string currentAccessToken;
                DateTime expiresUtc;

                lock (tokenLock)
                {
                    currentAccessToken = accessToken;
                    expiresUtc = accessTokenExpiresUtc;
                }

                if (string.IsNullOrEmpty(
                        currentAccessToken) ||
                    DateTime.UtcNow >= expiresUtc)
                {
                    bool refreshed =
                        await RefreshAccessTokenAsync(
                                cancellationToken)
                            .ConfigureAwait(false);

                    if (!refreshed)
                    {
                        return;
                    }

                    lock (tokenLock)
                    {
                        currentAccessToken = accessToken;
                    }
                }

                SpotifyPlaybackResult result =
                    await apiClient.GetCurrentlyPlayingAsync(
                            currentAccessToken,
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (playbackGeneration !=
                    authenticationGeneration)
                {
                    return;
                }

                if (result.Status ==
                    SpotifyPlaybackStatus.Unauthorized)
                {
                    bool refreshed =
                        await RefreshAccessTokenAsync(
                                cancellationToken)
                            .ConfigureAwait(false);

                    if (!refreshed)
                    {
                        IsConnected = false;
                        ConnectionStatus = "Login required";
                        return;
                    }

                    lock (tokenLock)
                    {
                        currentAccessToken = accessToken;
                    }

                    result =
                        await apiClient.GetCurrentlyPlayingAsync(
                                currentAccessToken,
                                cancellationToken)
                            .ConfigureAwait(false);

                    cancellationToken
                        .ThrowIfCancellationRequested();

                    if (playbackGeneration !=
                        authenticationGeneration)
                    {
                        return;
                    }
                }

                if (result.Status ==
                    SpotifyPlaybackStatus.NoContent)
                {
                    ResetPlaybackBackoff();
                    ScheduleIdlePolling();
                    ClearPlaybackAndCover();
                    CurrentTrack =
                        "No music is currently playing";
                    IsConnected = true;
                    ConnectionStatus = "Connected";
                    return;
                }

                if (result.Status ==
                    SpotifyPlaybackStatus.RateLimited)
                {
                    ScheduleRateLimitBackoff(
                        result.RetryAfter);

                    ConnectionStatus =
                        "Spotify rate limit reached; retrying shortly";

                    SimHub.Logging.Current.Error(
                        "Spotify playback request was rate limited.");

                    return;
                }

                if (result.Status ==
                        SpotifyPlaybackStatus.Error ||
                    result.Status ==
                        SpotifyPlaybackStatus.Unauthorized)
                {
                    ScheduleTemporaryFailureBackoff();

                    ConnectionStatus =
                        "Spotify is temporarily unavailable";

                    SimHub.Logging.Current.Error(
                        "Spotify playback request failed with HTTP status " +
                        (int)result.StatusCode);

                    return;
                }

                Track = result.TrackName;
                Artist = result.ArtistName;
                Album = result.AlbumName;
                IsConnected = true;
                ConnectionStatus = "Connected";
                ResetPlaybackBackoff();

                if (!result.IsPlaying)
                {
                    ScheduleIdlePolling();
                }

                if (string.IsNullOrEmpty(
                        result.TrackName))
                {
                    ScheduleIdlePolling();
                    ClearPlaybackAndCover();
                    CurrentTrack =
                        "No music is currently playing";
                    return;
                }

                CurrentTrack =
                    string.IsNullOrEmpty(
                        result.ArtistName)
                        ? result.TrackName
                        : result.ArtistName +
                          " - " +
                          result.TrackName;

                await UpdateCoverArtAsync(
                        result.CoverUrl,
                        playbackGeneration,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                ScheduleTemporaryFailureBackoff();
                ConnectionStatus =
                    "Spotify is temporarily unavailable";

                SimHub.Logging.Current.Error(
                    "Spotify playback request timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ScheduleTemporaryFailureBackoff();

                ConnectionStatus =
                    "Spotify is temporarily unavailable";

                SimHub.Logging.Current.Error(
                    "Spotify playback update failed. " +
                    ex.GetType().Name);
            }
        }

        private async Task ConnectCoreAsync(
            CancellationToken cancellationToken)
        {
            BeginBusy();

            try
            {
                await LoginToSpotifyAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task RefreshStatusCoreAsync(
            CancellationToken cancellationToken)
        {
            BeginBusy();

            try
            {
                string currentRefreshToken;

                lock (tokenLock)
                {
                    currentRefreshToken = refreshToken;
                }

                if (string.IsNullOrEmpty(
                        currentRefreshToken))
                {
                    LoadSavedRefreshToken();
                }

                lock (tokenLock)
                {
                    currentRefreshToken = refreshToken;
                }

                if (string.IsNullOrEmpty(
                        currentRefreshToken))
                {
                    IsConnected = false;
                    ConnectionStatus = "Login required";
                    return;
                }

                bool requiresRefresh;

                lock (tokenLock)
                {
                    requiresRefresh =
                        string.IsNullOrEmpty(accessToken) ||
                        DateTime.UtcNow >=
                        accessTokenExpiresUtc;
                }

                if (requiresRefresh &&
                    !await RefreshAccessTokenAsync(
                            cancellationToken)
                        .ConfigureAwait(false))
                {
                    return;
                }

                await UpdatePlaybackAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task StartSpotifyDelayedAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(
                        5000,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!HasConfiguredClientId)
                {
                    IsConnected = false;
                    ConnectionStatus =
                        "Spotify Client ID required";
                    return;
                }

                BeginBusy();

                try
                {
                    LoadSavedRefreshToken();

                    string savedRefreshToken;

                    lock (tokenLock)
                    {
                        savedRefreshToken = refreshToken;
                    }

                    if (string.IsNullOrEmpty(
                            savedRefreshToken))
                    {
                        IsConnected = false;
                        ConnectionStatus = "Login required";
                        return;
                    }

                    if (await RefreshAccessTokenAsync(
                                cancellationToken)
                            .ConfigureAwait(false))
                    {
                        await UpdatePlaybackAsync(
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    EndBusy();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void LoadSavedRefreshToken()
        {
            try
            {
                string savedToken = tokenStore.Load();

                if (tokenStore.MigrationFailed)
                {
                    SimHub.Logging.Current.Error(
                        "The saved Spotify login could not be migrated; " +
                        "the legacy token was retained.");
                }

                lock (tokenLock)
                {
                    refreshToken = savedToken;
                }

                HasSavedLogin =
                    !string.IsNullOrEmpty(savedToken);
            }
            catch (Exception ex)
            {
                HasSavedLogin = false;
                ConnectionStatus = "Login required";

                SimHub.Logging.Current.Error(
                    "Could not load the saved Spotify login. " +
                    ex.GetType().Name);
            }
        }

        private void ApplyTokenResult(
            SpotifyTokenResult tokenResult,
            string tokenToPersist)
        {
            lock (tokenLock)
            {
                accessToken =
                    tokenResult.AccessToken ?? "";
                refreshToken =
                    tokenToPersist ?? "";
                accessTokenExpiresUtc =
                    DateTime.UtcNow.AddSeconds(
                        Math.Max(
                            60,
                            tokenResult.ExpiresInSeconds -
                            60));
            }
        }

        private void BeginBusy()
        {
            if (Interlocked.Increment(
                    ref busyOperationCount) == 1)
            {
                IsBusy = true;
            }
        }

        private void EndBusy()
        {
            if (Interlocked.Decrement(
                    ref busyOperationCount) <= 0)
            {
                Interlocked.Exchange(
                    ref busyOperationCount,
                    0);
                IsBusy = false;
            }
        }

        private CancellationToken
            GetSessionCancellationToken()
        {
            lock (lifecycleLock)
            {
                return sessionCancellation?.Token ??
                       new CancellationToken(true);
            }
        }

        private void RenewSessionCancellation()
        {
            CancellationTokenSource previous;

            lock (lifecycleLock)
            {
                previous = sessionCancellation;

                sessionCancellation =
                    pluginCancellation == null ||
                    pluginCancellation.IsCancellationRequested
                        ? null
                        : CancellationTokenSource
                            .CreateLinkedTokenSource(
                                pluginCancellation.Token);
            }

            if (previous != null)
            {
                try
                {
                    previous.Cancel();
                }
                finally
                {
                    previous.Dispose();
                }
            }
        }

        public void DataUpdate(
            PluginManager pluginManager,
            ref GameData data)
        {
            if (ending ||
                pluginCancellation == null ||
                pluginCancellation.IsCancellationRequested ||
                !IsConnected)
            {
                return;
            }

            int pollIntervalSeconds =
                Math.Max(
                    3,
                    Settings?.PollIntervalSeconds ?? 3);

            if ((DateTime.UtcNow - lastTrackRequest).TotalSeconds <
                pollIntervalSeconds)
            {
                return;
            }

            if (DateTime.UtcNow.Ticks <
                Interlocked.Read(
                    ref nextPlaybackRequestUtcTicks))
            {
                return;
            }

            if (!playbackSemaphore.Wait(0))
            {
                return;
            }

            lastTrackRequest = DateTime.UtcNow;

            try
            {
                Task task =
                    RunPlaybackUpdateAsync(
                        GetSessionCancellationToken());

                lock (lifecycleLock)
                {
                    playbackTask = task;
                }
            }
            catch
            {
                playbackSemaphore.Release();
                throw;
            }
        }

        public void End(PluginManager pluginManager)
        {
            ending = true;

            this.SaveCommonSettings(
                "GeneralSettings",
                Settings);

            Interlocked.Increment(
                ref authenticationGeneration);

            CancellationTokenSource pluginSource;
            CancellationTokenSource sessionSource;
            Task[] operations;

            lock (lifecycleLock)
            {
                pluginSource = pluginCancellation;
                sessionSource = sessionCancellation;
                operations =
                    new[]
                    {
                        startupTask,
                        connectionTask,
                        playbackTask
                    };
            }

            sessionSource?.Cancel();
            pluginSource?.Cancel();

            bool operationsCompleted = true;

            try
            {
                operationsCompleted =
                    Task.WaitAll(
                        operations,
                        TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                operationsCompleted = true;
            }

            if (!operationsCompleted)
            {
                SimHub.Logging.Current.Error(
                    "SpotifySimHub shutdown timed out; " +
                    "HTTP resources were left alive to avoid a disposal race.");
                return;
            }

            coverArtCache?.Dispose();
            httpClient.Dispose();
            loginSemaphore.Dispose();
            refreshSemaphore.Dispose();
            playbackSemaphore.Dispose();
            sessionSource?.Dispose();
            pluginSource?.Dispose();
        }

        public System.Windows.Controls.Control
            GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SpotifySettingsControl(this);
        }

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info(
                "Starting SpotifySimHub plugin");

            PluginManager = pluginManager;
            ending = false;
            httpClient.Timeout = HttpRequestTimeout;
            ResetPlaybackBackoff();

            Settings =
                this.ReadCommonSettings<SpotifyPluginSettings>(
                    "GeneralSettings",
                    () => new SpotifyPluginSettings());

            pluginCancellation =
                new CancellationTokenSource();
            sessionCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        pluginCancellation.Token);

            tokenStore =
                new SpotifyTokenStore(
                    SpotifyDataFolder);
            coverArtCache =
                new SpotifyCoverArtCache(
                    httpClient,
                    SpotifyDataFolder);

            string settingsClientId =
                Settings?.SpotifyClientId ?? "";
            string initialClientId =
                IsUsableClientId(settingsClientId)
                    ? settingsClientId
                    : BuildClientId;

            ConfigureSpotifyClients(
                initialClientId);

            if (!HasConfiguredClientId)
            {
                ConnectionStatus =
                    "Spotify Client ID required";
            }

            this.AttachDelegate(
                name: "Spotify.CurrentTrack",
                valueProvider: () => CurrentTrack);

            this.AttachDelegate(
                name: "Spotify.Artist",
                valueProvider: () => Artist);

            this.AttachDelegate(
                name: "Spotify.Track",
                valueProvider: () => Track);

            this.AttachDelegate(
                name: "Spotify.Album",
                valueProvider: () => Album);

            this.AttachDelegate(
                name: "Spotify.Cover",
                valueProvider: () => Cover);

            this.AttachDelegate(
                name: "Spotify.CoverDash",
                valueProvider: () => CoverDash);

            this.AttachDelegate(
                name: "Spotify.CoverImage",
                valueProvider: () => CoverImage);

            startupTask =
                StartSpotifyDelayedAsync(
                    pluginCancellation.Token);
        }
    }
}
