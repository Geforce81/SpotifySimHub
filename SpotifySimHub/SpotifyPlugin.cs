using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        private sealed class PlaybackSnapshot
        {
            public static readonly PlaybackSnapshot Empty =
                new PlaybackSnapshot(
                    progressMs: 0,
                    durationMs: 0,
                    isPlaying: false,
                    hasProgress: false,
                    capturedTimestamp: 0);

            public PlaybackSnapshot(
                long progressMs,
                long durationMs,
                bool isPlaying,
                bool hasProgress,
                long capturedTimestamp)
            {
                ProgressMs = progressMs;
                DurationMs = durationMs;
                IsPlaying = isPlaying;
                HasProgress = hasProgress;
                CapturedTimestamp = capturedTimestamp;
            }

            public long ProgressMs { get; }

            public long DurationMs { get; }

            public bool IsPlaying { get; }

            public bool HasProgress { get; }

            public long CapturedTimestamp { get; }
        }

        private enum PlaybackControlRequest
        {
            Toggle,
            Next,
            Previous,
            Seek
        }

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
        private string playbackControlStatus = "Disconnected";

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
            private set
            {
                if (SetProperty(ref isConnected, value))
                {
                    PlaybackControlStatus =
                        value
                            ? "Ready"
                            : "Playback controls unavailable";
                }
            }
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

        public string PlaybackControlStatus
        {
            get => playbackControlStatus;
            private set =>
                SetProperty(
                    ref playbackControlStatus,
                    value);
        }

        public long ProgressMs =>
            GetInterpolatedProgressMs();

        public long DurationMs =>
            Volatile.Read(
                ref playbackSnapshot).DurationMs;

        public double ProgressPercent
        {
            get
            {
                PlaybackSnapshot snapshot =
                    Volatile.Read(
                        ref playbackSnapshot);
                long durationMs =
                    snapshot.DurationMs;

                if (durationMs <= 0)
                {
                    return 0;
                }

                return Math.Max(
                    0,
                    Math.Min(
                        100,
                        GetInterpolatedProgressMs(
                            snapshot) *
                        100.0 /
                        durationMs));
            }
        }

        public string ProgressText =>
            FormatPlaybackTime(ProgressMs);

        public string DurationText =>
            FormatPlaybackTime(DurationMs);

        public string PlaybackTime
        {
            get
            {
                PlaybackSnapshot snapshot =
                    Volatile.Read(
                        ref playbackSnapshot);

                return
                    FormatPlaybackTime(
                        GetInterpolatedProgressMs(
                            snapshot)) +
                    " / " +
                    FormatPlaybackTime(
                        snapshot.DurationMs);
            }
        }

        public bool IsPlaying =>
            Volatile.Read(
                ref playbackSnapshot).IsPlaying;

        public string PlayPauseText =>
            IsPlaying ? "Pause" : "Play";

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
        private readonly object stateCommitLock = new object();
        private readonly object tokenLock = new object();
        private readonly HashSet<Task> liveOperations =
            new HashSet<Task>();
        private readonly List<CancellationTokenSource>
            retiredSessionSources =
                new List<CancellationTokenSource>();
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
        private Task playbackControlTask = Task.CompletedTask;
        private PlaybackSnapshot playbackSnapshot =
            PlaybackSnapshot.Empty;
        private long lastProgressNotificationTimestamp;
        private long lastProgressTextSecond = -1;
        private DateTime lastTrackRequest = DateTime.MinValue;
        private long nextPlaybackRequestUtcTicks;
        private long nextPlaybackControlRequestUtcTicks;
        private int temporaryFailureCount;
        private int authenticationGeneration;
        private int busyOperationCount;
        private int automaticReauthorizationStarted;
        private volatile bool ending;

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

        private bool TryCommitState(
            int operationGeneration,
            CancellationToken cancellationToken,
            Action commit)
        {
            if (commit == null)
            {
                throw new ArgumentNullException(
                    nameof(commit));
            }

            lock (stateCommitLock)
            {
                if (ending ||
                    cancellationToken.IsCancellationRequested ||
                    operationGeneration !=
                    Volatile.Read(
                        ref authenticationGeneration))
                {
                    return false;
                }

                commit();
                return true;
            }
        }

        private Task StartTrackedOperation(
            Func<Task> operationFactory)
        {
            if (operationFactory == null)
            {
                throw new ArgumentNullException(
                    nameof(operationFactory));
            }

            TaskCompletionSource<bool> startSignal;
            Task task;

            lock (lifecycleLock)
            {
                if (ending ||
                    pluginCancellation == null ||
                    pluginCancellation.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                startSignal =
                    new TaskCompletionSource<bool>();
                task =
                    RunTrackedOperationAsync(
                        operationFactory,
                        startSignal.Task);
                RegisterLiveOperationLocked(
                    task);
            }

            startSignal.SetResult(true);
            return task;
        }

        private static async Task RunTrackedOperationAsync(
            Func<Task> operationFactory,
            Task startSignal)
        {
            await startSignal.ConfigureAwait(false);

            Task operation =
                operationFactory();

            if (operation != null)
            {
                await operation.ConfigureAwait(false);
            }
        }

        private void RegisterLiveOperationLocked(
            Task task)
        {
            liveOperations.Add(task);

            task.ContinueWith(
                completedTask =>
                {
                    lock (lifecycleLock)
                    {
                        liveOperations.Remove(
                            completedTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private CancellationToken BeginNewSession(
            Action resetState)
        {
            int operationGeneration;

            return BeginNewSession(
                resetState,
                out operationGeneration);
        }

        private CancellationToken BeginNewSession(
            Action resetState,
            out int operationGeneration)
        {
            CancellationToken cancellationToken;

            TryBeginNewSession(
                expectedGeneration: null,
                operationCancellationToken:
                    CancellationToken.None,
                resetState: resetState,
                cancellationToken:
                    out cancellationToken,
                operationGeneration:
                    out operationGeneration);

            return cancellationToken;
        }

        private bool TryBeginNewSession(
            int? expectedGeneration,
            CancellationToken operationCancellationToken,
            Action resetState,
            out CancellationToken cancellationToken,
            out int operationGeneration)
        {
            CancellationTokenSource previous = null;
            bool started;

            lock (stateCommitLock)
            {
                started =
                    !ending &&
                    pluginCancellation != null &&
                    !pluginCancellation
                        .IsCancellationRequested &&
                    !operationCancellationToken
                        .IsCancellationRequested &&
                    (!expectedGeneration.HasValue ||
                     expectedGeneration.Value ==
                     Volatile.Read(
                         ref authenticationGeneration));

                if (started)
                {
                    Interlocked.Increment(
                        ref authenticationGeneration);
                    operationGeneration =
                        Volatile.Read(
                            ref authenticationGeneration);
                }
                else
                {
                    operationGeneration =
                        Volatile.Read(
                            ref authenticationGeneration);
                }

                if (!started)
                {
                    cancellationToken =
                        new CancellationToken(true);
                    return false;
                }

                lock (lifecycleLock)
                {
                    previous = sessionCancellation;
                    sessionCancellation = null;

                    if (previous != null)
                    {
                        retiredSessionSources.Add(
                            previous);
                    }
                }

                if (previous != null)
                {
                    previous.Cancel();
                }

                resetState?.Invoke();

                lock (lifecycleLock)
                {
                    sessionCancellation =
                        CancellationTokenSource
                            .CreateLinkedTokenSource(
                                pluginCancellation.Token);
                    cancellationToken =
                        sessionCancellation.Token;
                }
            }

            return true;
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

            BeginNewSession(
                () =>
                {
                    IsAuthorizationInProgress = false;

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
                    Interlocked.Exchange(
                        ref nextPlaybackControlRequestUtcTicks,
                        0);

                    ClearPlaybackAndCover();
                    ResetPlaybackBackoff();
                    HasSavedLogin = false;
                    IsConnected = false;
                    PlaybackControlStatus = "Disconnected";
                    ConnectionStatus =
                        "Client ID saved. Press Connect.";
                });

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
            CancellationToken cancellationToken,
            int refreshGeneration)
        {
            await refreshSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (!TryCommitState(
                        refreshGeneration,
                        cancellationToken,
                        () => { }))
                {
                    return false;
                }

                string savedRefreshToken;

                lock (tokenLock)
                {
                    savedRefreshToken = refreshToken;
                }

                if (string.IsNullOrEmpty(savedRefreshToken))
                {
                    TryCommitState(
                        refreshGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Login required";
                        });
                    return false;
                }

                if (!HasConfiguredClientId ||
                    apiClient == null)
                {
                    TryCommitState(
                        refreshGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Spotify Client ID required";
                        });
                    return false;
                }

                if (!TryCommitState(
                        refreshGeneration,
                        cancellationToken,
                        () =>
                        {
                            ConnectionStatus =
                                "Refreshing Spotify session...";
                        }))
                {
                    return false;
                }

                SpotifyTokenResult tokenResult =
                    await apiClient.RefreshAccessTokenAsync(
                            savedRefreshToken,
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(
                        tokenResult.AccessToken))
                {
                    TryCommitState(
                        refreshGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Login required";
                        });
                    return false;
                }

                string tokenToPersist =
                    string.IsNullOrEmpty(
                        tokenResult.RefreshToken)
                        ? savedRefreshToken
                        : tokenResult.RefreshToken;

                return TryCommitState(
                    refreshGeneration,
                    cancellationToken,
                    () =>
                    {
                        tokenStore.Save(
                            tokenToPersist);
                        ApplyTokenResult(
                            tokenResult,
                            tokenToPersist);

                        HasSavedLogin = true;
                        IsConnected = true;
                        PlaybackControlStatus = "Ready";
                        ConnectionStatus = "Connected";
                    });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryCommitState(
                    refreshGeneration,
                    cancellationToken,
                    () =>
                    {
                        ConnectionStatus =
                            "Spotify is temporarily unavailable";
                        ScheduleTemporaryFailureBackoff();
                    });

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
                if (!HandleExpiredAuthorization(
                        refreshGeneration,
                        cancellationToken))
                {
                    return false;
                }

                SimHub.Logging.Current.Info(
                    "The saved Spotify authorization expired; " +
                    "reauthorization was requested.");

                return false;
            }
            catch (SpotifyApiException ex)
            {
                TryCommitState(
                    refreshGeneration,
                    cancellationToken,
                    () =>
                    {
                        if (IsConnected)
                        {
                            ScheduleTemporaryFailureBackoff();
                            ConnectionStatus =
                                "Spotify is temporarily unavailable";
                        }
                        else
                        {
                            ConnectionStatus =
                                "Login required";
                        }
                    });

                SimHub.Logging.Current.Error(
                    "Spotify refresh failed. " +
                    ex.GetType().Name);

                return false;
            }
            catch (Exception ex)
            {
                TryCommitState(
                    refreshGeneration,
                    cancellationToken,
                    () =>
                    {
                        ScheduleTemporaryFailureBackoff();
                        ConnectionStatus =
                            "Spotify is temporarily unavailable";
                    });

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

        private bool HandleExpiredAuthorization(
            int expectedGeneration,
            CancellationToken cancellationToken)
        {
            CancellationToken newCancellationToken;
            int newGeneration;

            bool transitioned =
                TryBeginNewSession(
                expectedGeneration,
                cancellationToken,
                () =>
                {
                    IsAuthorizationInProgress = false;

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
                    Interlocked.Exchange(
                        ref nextPlaybackControlRequestUtcTicks,
                        0);
                    PlaybackControlStatus =
                        "Spotify login required";
                    ConnectionStatus =
                        "Spotify login expired. " +
                        "Reauthorization required.";
                },
                out newCancellationToken,
                out newGeneration);

            if (transitioned)
            {
                QueueAutomaticReauthorization(
                    newCancellationToken,
                    newGeneration);
            }

            return transitioned;
        }

        private void QueueAutomaticReauthorization(
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            bool shouldStart = false;

            if (!TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        shouldStart =
                            HasConfiguredClientId &&
                            Interlocked.CompareExchange(
                                ref automaticReauthorizationStarted,
                                1,
                                0) == 0;
                    }) ||
                !shouldStart)
            {
                return;
            }

            Task task =
                StartTrackedOperation(
                    () =>
                        AutomaticReauthorizationAsync(
                            cancellationToken,
                            operationGeneration));

            lock (lifecycleLock)
            {
                connectionTask = task;
            }
        }

        private async Task AutomaticReauthorizationAsync(
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            await Task.Yield();
            BeginBusy();

            try
            {
                if (!TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            ConnectionStatus =
                                "Spotify login expired. " +
                                "Opening authorization...";
                        }))
                {
                    return;
                }

                await LoginToSpotifyAsync(
                        cancellationToken,
                        operationGeneration)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        Interlocked.Exchange(
                            ref automaticReauthorizationStarted,
                            0);
                    });
                EndBusy();
            }
        }

        private async Task<bool> LoginToSpotifyAsync(
            CancellationToken cancellationToken,
            int loginGeneration)
        {
            await loginSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            bool authorizationStarted = false;
            bool refreshSemaphoreHeld = false;

            try
            {
                await refreshSemaphore
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                refreshSemaphoreHeld = true;

                if (!HasConfiguredClientId ||
                    oauthClient == null)
                {
                    TryCommitState(
                        loginGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Spotify Client ID required";
                        });
                    return false;
                }

                if (!TryCommitState(
                        loginGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsAuthorizationInProgress = true;
                            ConnectionStatus =
                                "Connecting to Spotify...";
                        }))
                {
                    return false;
                }

                authorizationStarted = true;

                SpotifyTokenResult tokenResult =
                    await oauthClient.AuthorizeAsync(
                            cancellationToken)
                        .ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(
                        tokenResult.AccessToken) ||
                    string.IsNullOrEmpty(
                        tokenResult.RefreshToken))
                {
                    TryCommitState(
                        loginGeneration,
                        cancellationToken,
                        () =>
                        {
                            ConnectionStatus =
                                "Spotify authorization failed";
                        });
                    return false;
                }

                if (!TryCommitState(
                        loginGeneration,
                        cancellationToken,
                        () =>
                        {
                            tokenStore.Save(
                                tokenResult.RefreshToken);
                            ApplyTokenResult(
                                tokenResult,
                                tokenResult.RefreshToken);

                            HasSavedLogin = true;
                            IsConnected = true;
                            PlaybackControlStatus = "Ready";
                            ConnectionStatus = "Connected";
                        }))
                {
                    return false;
                }

                refreshSemaphore.Release();
                refreshSemaphoreHeld = false;

                await UpdatePlaybackAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryCommitState(
                    loginGeneration,
                    cancellationToken,
                    () =>
                    {
                        IsConnected = false;
                        ConnectionStatus =
                            "Spotify authorization failed";
                    });

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
                TryCommitState(
                    loginGeneration,
                    cancellationToken,
                    () =>
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
                    });

                SimHub.Logging.Current.Error(
                    "Spotify authorization did not complete. " +
                    ex.Kind +
                    ". " +
                    ex.GetType().Name);

                return false;
            }
            catch (Exception ex)
            {
                TryCommitState(
                    loginGeneration,
                    cancellationToken,
                    () =>
                    {
                        IsConnected = false;
                        ConnectionStatus =
                            "Spotify authorization failed";
                    });

                SimHub.Logging.Current.Error(
                    "Spotify login failed. " +
                    ex.GetType().Name);

                return false;
            }
            finally
            {
                if (authorizationStarted)
                {
                    TryCommitState(
                        loginGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsAuthorizationInProgress = false;
                        });
                }

                if (refreshSemaphoreHeld)
                {
                    refreshSemaphore.Release();
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

            CancellationToken cancellationToken =
                BeginNewSession(
                    () =>
                    {
                        IsAuthorizationInProgress = false;
                        Interlocked.Exchange(
                            ref automaticReauthorizationStarted,
                            0);
                        Interlocked.Exchange(
                            ref nextPlaybackControlRequestUtcTicks,
                            0);
                    },
                    out int operationGeneration);
            Task task =
                StartTrackedOperation(
                    () =>
                        ConnectCoreAsync(
                            cancellationToken,
                            operationGeneration));

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

            BeginNewSession(
                () =>
                {
                    IsAuthorizationInProgress = false;
                    Interlocked.Exchange(
                        ref automaticReauthorizationStarted,
                        0);
                    Interlocked.Exchange(
                        ref nextPlaybackControlRequestUtcTicks,
                        0);

                    ConnectionStatus =
                        IsConnected
                            ? "Connected"
                            : HasSavedLogin
                                ? "Spotify authorization was cancelled; " +
                                  "saved login retained"
                                : "Spotify authorization was cancelled";
                });
        }

        public void Disconnect()
        {
            BeginNewSession(
                () =>
                {
                    IsAuthorizationInProgress = false;
                    IsConnected = false;
                    Interlocked.Exchange(
                        ref automaticReauthorizationStarted,
                        0);
                    Interlocked.Exchange(
                        ref nextPlaybackControlRequestUtcTicks,
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
                    PlaybackControlStatus = "Disconnected";
                    ConnectionStatus = "Disconnected";
                });
        }

        public Task RefreshStatusAsync()
        {
            if (ending)
            {
                return Task.CompletedTask;
            }

            CancellationToken cancellationToken;
            int operationGeneration;

            lock (lifecycleLock)
            {
                cancellationToken =
                    sessionCancellation?.Token ??
                    new CancellationToken(true);
                operationGeneration =
                    Volatile.Read(
                        ref authenticationGeneration);
            }

            Task task =
                StartTrackedOperation(
                    () =>
                        RefreshStatusCoreAsync(
                            cancellationToken,
                            operationGeneration));

            lock (lifecycleLock)
            {
                connectionTask = task;
            }

            return task;
        }

        public async Task TogglePlaybackAsync()
        {
            await QueuePlaybackControlAsync(
                    PlaybackControlRequest.Toggle)
                .ConfigureAwait(false);
        }

        public async Task NextTrackAsync()
        {
            await QueuePlaybackControlAsync(
                    PlaybackControlRequest.Next)
                .ConfigureAwait(false);
        }

        public async Task PreviousTrackAsync()
        {
            await QueuePlaybackControlAsync(
                    PlaybackControlRequest.Previous)
                .ConfigureAwait(false);
        }

        public async Task SeekToPercentAsync(
            int seekPercent)
        {
            await QueuePlaybackControlAsync(
                    PlaybackControlRequest.Seek,
                    seekPercent)
                .ConfigureAwait(false);
        }

        private void QueuePlaybackControlFromAction(
            PlaybackControlRequest request)
        {
            QueuePlaybackControlAsync(
                request);
        }

        private void QueueSeekFromAction(
            int seekPercent)
        {
            SimHub.Logging.Current.Info(
                "Spotify seek action triggered: " +
                seekPercent +
                "%");

            QueuePlaybackControlAsync(
                PlaybackControlRequest.Seek,
                seekPercent);
        }

        private Task QueuePlaybackControlAsync(
            PlaybackControlRequest request,
            int seekPercent = -1)
        {
            lock (lifecycleLock)
            {
                if (ending ||
                    pluginCancellation == null ||
                    pluginCancellation.IsCancellationRequested)
                {
                    return Task.CompletedTask;
                }

                CancellationToken cancellationToken =
                    sessionCancellation?.Token ??
                    new CancellationToken(true);
                int operationGeneration =
                    Volatile.Read(
                        ref authenticationGeneration);
                Task previousTask =
                    playbackControlTask ??
                    Task.CompletedTask;

                playbackControlTask =
                    previousTask
                        .ContinueWith(
                            ignored =>
                                RunPlaybackControlAsync(
                                    request,
                                    seekPercent,
                                    cancellationToken,
                                    operationGeneration),
                            CancellationToken.None,
                            TaskContinuationOptions.None,
                            TaskScheduler.Default)
                        .Unwrap();

                RegisterLiveOperationLocked(
                    playbackControlTask);

                return playbackControlTask;
            }
        }

        private async Task RunPlaybackControlAsync(
            PlaybackControlRequest request,
            int seekPercent,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsConnected ||
                    apiClient == null)
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            PlaybackControlStatus =
                                "Connect Spotify before using " +
                                "playback controls";
                        });
                    return;
                }

                double retrySeconds;

                if (TryGetPlaybackControlRetryDelay(
                        out retrySeconds))
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            PlaybackControlStatus =
                                "Spotify rate limit. Try again in " +
                                Math.Ceiling(
                                    retrySeconds) +
                                " seconds.";
                        });
                    return;
                }

                bool isSeek =
                    request == PlaybackControlRequest.Seek;
                SpotifyPlaybackCommand command =
                    SpotifyPlaybackCommand.Play;
                long seekPositionMs = 0;
                string controlName;

                if (isSeek)
                {
                    PlaybackSnapshot snapshot =
                        Volatile.Read(
                            ref playbackSnapshot);

                    if (snapshot.DurationMs <= 0)
                    {
                        TryCommitState(
                            operationGeneration,
                            cancellationToken,
                            () =>
                            {
                                PlaybackControlStatus =
                                    "Spotify track duration unavailable";
                            });
                        return;
                    }

                    int safeSeekPercent =
                        Math.Max(
                            0,
                            Math.Min(
                                100,
                                seekPercent));
                    seekPositionMs =
                        snapshot.DurationMs *
                        safeSeekPercent /
                        100;
                    controlName =
                        "Seek to " +
                        FormatPlaybackTime(
                            seekPositionMs);
                }
                else
                {
                    command =
                        ResolvePlaybackCommand(request);
                    controlName =
                        GetPlaybackCommandName(command);
                }

                if (!TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            PlaybackControlStatus =
                                controlName +
                                "...";
                        }))
                {
                    return;
                }

                string currentAccessToken =
                    await GetPlaybackControlAccessTokenAsync(
                            cancellationToken,
                            operationGeneration)
                        .ConfigureAwait(false);

                if (string.IsNullOrEmpty(
                        currentAccessToken))
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            PlaybackControlStatus =
                                "Spotify login required";
                        });
                    return;
                }

                if (TryGetPlaybackControlRetryDelay(
                        out retrySeconds))
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            PlaybackControlStatus =
                                "Spotify rate limit. Try again in " +
                                Math.Ceiling(
                                    retrySeconds) +
                                " seconds.";
                        });
                    return;
                }

                if (!TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () => { }))
                {
                    return;
                }

                SpotifyPlaybackCommandResult result =
                    await SendPlaybackControlRequestAsync(
                            currentAccessToken,
                            isSeek,
                            command,
                            seekPositionMs,
                            cancellationToken)
                        .ConfigureAwait(false);

                if ((int)result.StatusCode == 401)
                {
                    bool refreshed =
                        await RefreshAccessTokenAsync(
                                cancellationToken,
                                operationGeneration)
                            .ConfigureAwait(false);

                    if (!refreshed)
                    {
                        TryCommitState(
                            operationGeneration,
                            cancellationToken,
                            () =>
                            {
                                PlaybackControlStatus =
                                    "Spotify login required";
                            });
                        return;
                    }

                    lock (tokenLock)
                    {
                        currentAccessToken = accessToken;
                    }

                    if (string.IsNullOrEmpty(
                            currentAccessToken))
                    {
                        TryCommitState(
                            operationGeneration,
                            cancellationToken,
                            () =>
                            {
                                PlaybackControlStatus =
                                    "Spotify login required";
                            });
                        return;
                    }

                    if (!TryCommitState(
                            operationGeneration,
                            cancellationToken,
                            () => { }))
                    {
                        return;
                    }

                    result =
                        await SendPlaybackControlRequestAsync(
                                currentAccessToken,
                                isSeek,
                                command,
                                seekPositionMs,
                                cancellationToken)
                            .ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!result.IsSuccess)
                {
                    SetPlaybackControlFailureStatus(
                        result,
                        operationGeneration,
                        cancellationToken);
                    return;
                }

                if (!TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            if (isSeek)
                            {
                                ApplySuccessfulSeek(
                                    seekPositionMs);
                            }
                            else
                            {
                                ApplySuccessfulPlaybackCommand(
                                    command);
                            }

                            PlaybackControlStatus =
                                controlName +
                                " sent";

                            lastTrackRequest =
                                DateTime.MinValue;
                            ResetPlaybackBackoff();
                            Interlocked.Exchange(
                                ref nextPlaybackControlRequestUtcTicks,
                                0);
                        }))
                {
                    return;
                }

                await Task.Delay(
                        250,
                        cancellationToken)
                    .ConfigureAwait(false);

                await UpdatePlaybackAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        PlaybackControlStatus = "Ready";
                    });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        PlaybackControlStatus =
                            "Spotify playback control timed out";
                    });

                SimHub.Logging.Current.Error(
                    "Spotify playback control timed out.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        PlaybackControlStatus =
                            "Spotify playback control failed";
                    });

                SimHub.Logging.Current.Error(
                    "Spotify playback control failed. " +
                    ex.GetType().Name);
            }
        }

        private Task<SpotifyPlaybackCommandResult>
            SendPlaybackControlRequestAsync(
                string currentAccessToken,
                bool isSeek,
                SpotifyPlaybackCommand command,
                long seekPositionMs,
                CancellationToken cancellationToken)
        {
            if (isSeek)
            {
                return apiClient.SeekPlaybackAsync(
                    currentAccessToken,
                    seekPositionMs,
                    cancellationToken);
            }

            return apiClient.SendPlaybackCommandAsync(
                currentAccessToken,
                command,
                cancellationToken);
        }

        private async Task<string>
            GetPlaybackControlAccessTokenAsync(
                CancellationToken cancellationToken,
                int operationGeneration)
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
                            cancellationToken,
                            operationGeneration)
                        .ConfigureAwait(false);

                if (!refreshed)
                {
                    return "";
                }

                lock (tokenLock)
                {
                    currentAccessToken = accessToken;
                }
            }

            return currentAccessToken ?? "";
        }

        private SpotifyPlaybackCommand
            ResolvePlaybackCommand(
                PlaybackControlRequest request)
        {
            switch (request)
            {
                case PlaybackControlRequest.Next:
                    return SpotifyPlaybackCommand.Next;

                case PlaybackControlRequest.Previous:
                    return SpotifyPlaybackCommand.Previous;

                default:
                    return IsPlaying
                        ? SpotifyPlaybackCommand.Pause
                        : SpotifyPlaybackCommand.Play;
            }
        }

        private static string GetPlaybackCommandName(
            SpotifyPlaybackCommand command)
        {
            switch (command)
            {
                case SpotifyPlaybackCommand.Play:
                    return "Play";

                case SpotifyPlaybackCommand.Pause:
                    return "Pause";

                case SpotifyPlaybackCommand.Next:
                    return "Next track";

                default:
                    return "Previous track";
            }
        }

        private void SetPlaybackControlFailureStatus(
            SpotifyPlaybackCommandResult result,
            int operationGeneration,
            CancellationToken cancellationToken)
        {
            TryCommitState(
                operationGeneration,
                cancellationToken,
                () =>
                {
                    int statusCode =
                        (int)result.StatusCode;

                    if (statusCode == 403)
                    {
                        PlaybackControlStatus =
                            "Spotify denied playback control. " +
                            "Select Reconnect to approve access, " +
                            "and check Premium/device support.";
                        return;
                    }

                    if (statusCode == 404)
                    {
                        PlaybackControlStatus =
                            "No active Spotify playback device";
                        return;
                    }

                    if (statusCode == 429)
                    {
                        SchedulePlaybackControlRateLimit(
                            result.RetryAfter);
                        ScheduleRateLimitBackoff(
                            result.RetryAfter);

                        double retrySeconds;
                        TryGetPlaybackControlRetryDelay(
                            out retrySeconds);

                        PlaybackControlStatus =
                            "Spotify rate limit. Try again in " +
                            Math.Ceiling(
                                retrySeconds) +
                            " seconds.";
                        return;
                    }

                    if (statusCode == 401)
                    {
                        PlaybackControlStatus =
                            "Spotify login required";
                        return;
                    }

                    PlaybackControlStatus =
                        statusCode > 0
                            ? "Spotify playback control failed (HTTP " +
                              statusCode +
                              ")"
                            : "Spotify playback control failed";
                });
        }

        private void SchedulePlaybackControlRateLimit(
            TimeSpan? retryAfter)
        {
            long deadline =
                GetRetryDeadlineUtcTicks(
                    retryAfter);
            long current =
                Interlocked.Read(
                    ref nextPlaybackControlRequestUtcTicks);

            while (deadline > current)
            {
                long observed =
                    Interlocked.CompareExchange(
                        ref nextPlaybackControlRequestUtcTicks,
                        deadline,
                        current);

                if (observed == current)
                {
                    break;
                }

                current = observed;
            }
        }

        private bool TryGetPlaybackControlRetryDelay(
            out double retrySeconds)
        {
            long remainingTicks =
                Interlocked.Read(
                    ref nextPlaybackControlRequestUtcTicks) -
                DateTime.UtcNow.Ticks;

            if (remainingTicks <= 0)
            {
                retrySeconds = 0;
                return false;
            }

            retrySeconds =
                TimeSpan.FromTicks(
                    remainingTicks).TotalSeconds;
            return true;
        }

        private static long GetRetryDeadlineUtcTicks(
            TimeSpan? retryAfter)
        {
            long nowTicks =
                DateTime.UtcNow.Ticks;
            long delayTicks =
                retryAfter.HasValue
                    ? Math.Max(
                        TimeSpan.TicksPerSecond,
                        retryAfter.Value.Ticks)
                    : TimeSpan.FromSeconds(5).Ticks;
            long maximumDelay =
                DateTime.MaxValue.Ticks -
                nowTicks;

            return nowTicks +
                   Math.Min(
                       delayTicks,
                       maximumDelay);
        }

        private void ApplySuccessfulPlaybackCommand(
            SpotifyPlaybackCommand command)
        {
            PlaybackSnapshot snapshot =
                Volatile.Read(
                    ref playbackSnapshot);
            long progressMs =
                GetInterpolatedProgressMs(
                    snapshot);

            switch (command)
            {
                case SpotifyPlaybackCommand.Play:
                    PublishPlaybackSnapshot(
                        progressMs,
                        snapshot.DurationMs,
                        true,
                        snapshot.HasProgress);
                    break;

                case SpotifyPlaybackCommand.Pause:
                    PublishPlaybackSnapshot(
                        progressMs,
                        snapshot.DurationMs,
                        false,
                        snapshot.HasProgress);
                    break;

                case SpotifyPlaybackCommand.Next:
                case SpotifyPlaybackCommand.Previous:
                    PublishPlaybackSnapshot(
                        0,
                        snapshot.DurationMs,
                        snapshot.IsPlaying,
                        false);
                    break;
            }
        }

        private void ApplySuccessfulSeek(
            long seekPositionMs)
        {
            PlaybackSnapshot snapshot =
                Volatile.Read(
                    ref playbackSnapshot);

            PublishPlaybackSnapshot(
                seekPositionMs,
                snapshot.DurationMs,
                snapshot.IsPlaying,
                true);
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
            PublishPlaybackSnapshot(
                0,
                0,
                false,
                false);

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

        private void PublishPlaybackSnapshot(
            long progressMs,
            long durationMs,
            bool isPlaying,
            bool hasProgress)
        {
            long safeDurationMs =
                Math.Max(
                    0,
                    durationMs);
            long safeProgressMs =
                Math.Max(
                    0,
                    progressMs);

            if (safeDurationMs > 0)
            {
                safeProgressMs =
                    Math.Min(
                        safeProgressMs,
                        safeDurationMs);
            }

            Volatile.Write(
                ref playbackSnapshot,
                new PlaybackSnapshot(
                    safeProgressMs,
                    safeDurationMs,
                    isPlaying,
                    hasProgress,
                    Stopwatch.GetTimestamp()));

            OnPropertyChanged(
                nameof(ProgressMs));
            OnPropertyChanged(
                nameof(DurationMs));
            OnPropertyChanged(
                nameof(ProgressPercent));
            OnPropertyChanged(
                nameof(ProgressText));
            OnPropertyChanged(
                nameof(DurationText));
            OnPropertyChanged(
                nameof(PlaybackTime));
            OnPropertyChanged(
                nameof(IsPlaying));
            OnPropertyChanged(
                nameof(PlayPauseText));
        }

        private long GetInterpolatedProgressMs()
        {
            return GetInterpolatedProgressMs(
                Volatile.Read(
                    ref playbackSnapshot));
        }

        private static long GetInterpolatedProgressMs(
            PlaybackSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            long progressMs =
                snapshot.ProgressMs;

            if (snapshot.HasProgress &&
                snapshot.IsPlaying &&
                snapshot.CapturedTimestamp > 0)
            {
                long elapsedTicks =
                    Stopwatch.GetTimestamp() -
                    snapshot.CapturedTimestamp;

                if (elapsedTicks > 0)
                {
                    progressMs +=
                        (long)(
                            elapsedTicks *
                            1000.0 /
                            Stopwatch.Frequency);
                }
            }

            progressMs =
                Math.Max(
                    0,
                    progressMs);

            if (snapshot.DurationMs > 0)
            {
                progressMs =
                    Math.Min(
                        progressMs,
                        snapshot.DurationMs);
            }

            return progressMs;
        }

        private static string FormatPlaybackTime(
            long milliseconds)
        {
            TimeSpan time =
                TimeSpan.FromMilliseconds(
                    Math.Max(
                        0,
                        milliseconds));

            if (time.TotalHours >= 1)
            {
                return
                    ((long)time.TotalHours) +
                    ":" +
                    time.Minutes.ToString("00") +
                    ":" +
                    time.Seconds.ToString("00");
            }

            return
                ((long)time.TotalMinutes) +
                ":" +
                time.Seconds.ToString("00");
        }

        private void NotifyPlaybackProgressIfNeeded()
        {
            PlaybackSnapshot snapshot =
                Volatile.Read(
                    ref playbackSnapshot);

            if (!snapshot.HasProgress ||
                !snapshot.IsPlaying)
            {
                return;
            }

            long now =
                Stopwatch.GetTimestamp();
            long previous =
                Interlocked.Read(
                    ref lastProgressNotificationTimestamp);
            long interval =
                Math.Max(
                    1,
                    Stopwatch.Frequency / 4);

            if (now - previous < interval ||
                Interlocked.CompareExchange(
                    ref lastProgressNotificationTimestamp,
                    now,
                    previous) != previous)
            {
                return;
            }

            long progressMs =
                GetInterpolatedProgressMs(
                    snapshot);

            OnPropertyChanged(
                nameof(ProgressMs));
            OnPropertyChanged(
                nameof(ProgressPercent));

            long progressSecond =
                progressMs / 1000;

            if (Interlocked.Exchange(
                    ref lastProgressTextSecond,
                    progressSecond) != progressSecond)
            {
                OnPropertyChanged(
                    nameof(ProgressText));
                OnPropertyChanged(
                    nameof(PlaybackTime));
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
            ExtendPlaybackRequestDeadline(
                GetRetryDeadlineUtcTicks(
                    retryAfter));
        }

        private void ExtendPlaybackRequestDeadline(
            long deadline)
        {
            long current =
                Interlocked.Read(
                    ref nextPlaybackRequestUtcTicks);

            while (deadline > current)
            {
                long observed =
                    Interlocked.CompareExchange(
                        ref nextPlaybackRequestUtcTicks,
                        deadline,
                        current);

                if (observed == current)
                {
                    break;
                }

                current = observed;
            }
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

            ExtendPlaybackRequestDeadline(
                DateTime.UtcNow
                    .AddSeconds(delaySeconds)
                    .Ticks);
        }

        private void ScheduleIdlePolling()
        {
            ExtendPlaybackRequestDeadline(
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

                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        Cover = result.CoverPath;
                        CoverDash = result.DashCoverPath;
                        CoverImage = result.CoverImage;
                    });
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
            int playbackGeneration =
                Volatile.Read(
                    ref authenticationGeneration);

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
                                cancellationToken,
                                playbackGeneration)
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
                                cancellationToken,
                                playbackGeneration)
                            .ConfigureAwait(false);

                    if (!refreshed)
                    {
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
                    TryCommitState(
                        playbackGeneration,
                        cancellationToken,
                        () =>
                        {
                            ResetPlaybackBackoff();
                            ScheduleIdlePolling();
                            ClearPlaybackAndCover();
                            CurrentTrack =
                                "No music is currently playing";
                            IsConnected = true;
                            ConnectionStatus = "Connected";
                        });
                    return;
                }

                if (result.Status ==
                    SpotifyPlaybackStatus.RateLimited)
                {
                    TryCommitState(
                        playbackGeneration,
                        cancellationToken,
                        () =>
                        {
                            ScheduleRateLimitBackoff(
                                result.RetryAfter);
                            SchedulePlaybackControlRateLimit(
                                result.RetryAfter);
                            ConnectionStatus =
                                "Spotify rate limit reached; retrying shortly";
                        });

                    SimHub.Logging.Current.Error(
                        "Spotify playback request was rate limited.");

                    return;
                }

                if (result.Status ==
                        SpotifyPlaybackStatus.Error ||
                    result.Status ==
                        SpotifyPlaybackStatus.Unauthorized)
                {
                    TryCommitState(
                        playbackGeneration,
                        cancellationToken,
                        () =>
                        {
                            ScheduleTemporaryFailureBackoff();
                            ConnectionStatus =
                                "Spotify is temporarily unavailable";
                        });

                    SimHub.Logging.Current.Error(
                        "Spotify playback request failed with HTTP status " +
                        (int)result.StatusCode);

                    return;
                }

                if (string.IsNullOrEmpty(
                        result.TrackName))
                {
                    TryCommitState(
                        playbackGeneration,
                        cancellationToken,
                        () =>
                        {
                            ResetPlaybackBackoff();
                            ScheduleIdlePolling();
                            ClearPlaybackAndCover();
                            CurrentTrack =
                                "No music is currently playing";
                            IsConnected = true;
                            ConnectionStatus = "Connected";
                        });
                    return;
                }

                string currentTrackText =
                    string.IsNullOrEmpty(
                        result.ArtistName)
                        ? result.TrackName
                        : result.ArtistName +
                          " - " +
                          result.TrackName;

                if (!TryCommitState(
                        playbackGeneration,
                        cancellationToken,
                        () =>
                        {
                            Track = result.TrackName;
                            Artist = result.ArtistName;
                            Album = result.AlbumName;
                            PublishPlaybackSnapshot(
                                result.ProgressMs,
                                result.DurationMs,
                                result.IsPlaying,
                                result.HasProgress);
                            IsConnected = true;
                            ConnectionStatus = "Connected";
                            CurrentTrack = currentTrackText;
                            ResetPlaybackBackoff();

                            if (!result.IsPlaying)
                            {
                                ScheduleIdlePolling();
                            }
                        }))
                {
                    return;
                }

                await UpdateCoverArtAsync(
                        result.CoverUrl,
                        playbackGeneration,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryCommitState(
                    playbackGeneration,
                    cancellationToken,
                    () =>
                    {
                        ScheduleTemporaryFailureBackoff();
                        ConnectionStatus =
                            "Spotify is temporarily unavailable";
                    });

                SimHub.Logging.Current.Error(
                    "Spotify playback request timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TryCommitState(
                    playbackGeneration,
                    cancellationToken,
                    () =>
                    {
                        ScheduleTemporaryFailureBackoff();
                        ConnectionStatus =
                            "Spotify is temporarily unavailable";
                    });

                SimHub.Logging.Current.Error(
                    "Spotify playback update failed. " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private async Task ConnectCoreAsync(
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            BeginBusy();

            try
            {
                await LoginToSpotifyAsync(
                        cancellationToken,
                        operationGeneration)
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
            CancellationToken cancellationToken,
            int operationGeneration)
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
                    if (!LoadSavedRefreshToken(
                            operationGeneration,
                            cancellationToken))
                    {
                        return;
                    }
                }

                lock (tokenLock)
                {
                    currentRefreshToken = refreshToken;
                }

                if (string.IsNullOrEmpty(
                        currentRefreshToken))
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Login required";
                        });
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
                            cancellationToken,
                            operationGeneration)
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
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                await Task.Delay(
                        5000,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!HasConfiguredClientId)
                {
                    TryCommitState(
                        operationGeneration,
                        cancellationToken,
                        () =>
                        {
                            IsConnected = false;
                            ConnectionStatus =
                                "Spotify Client ID required";
                        });
                    return;
                }

                BeginBusy();

                try
                {
                    if (!LoadSavedRefreshToken(
                            operationGeneration,
                            cancellationToken))
                    {
                        return;
                    }

                    string savedRefreshToken;

                    lock (tokenLock)
                    {
                        savedRefreshToken = refreshToken;
                    }

                    if (string.IsNullOrEmpty(
                            savedRefreshToken))
                    {
                        TryCommitState(
                            operationGeneration,
                            cancellationToken,
                            () =>
                            {
                                IsConnected = false;
                                ConnectionStatus =
                                    "Login required";
                            });
                        return;
                    }

                    if (await RefreshAccessTokenAsync(
                                cancellationToken,
                                operationGeneration)
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

        private bool LoadSavedRefreshToken(
            int operationGeneration,
            CancellationToken cancellationToken)
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

                return TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        lock (tokenLock)
                        {
                            refreshToken = savedToken;
                        }

                        HasSavedLogin =
                            !string.IsNullOrEmpty(savedToken);
                    });
            }
            catch (Exception ex)
            {
                TryCommitState(
                    operationGeneration,
                    cancellationToken,
                    () =>
                    {
                        HasSavedLogin = false;
                        ConnectionStatus =
                            "Login required";
                    });

                SimHub.Logging.Current.Error(
                    "Could not load the saved Spotify login. " +
                    ex.GetType().Name);

                return false;
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

        public void DataUpdate(
            PluginManager pluginManager,
            ref GameData data)
        {
            NotifyPlaybackProgressIfNeeded();

            int pollIntervalSeconds =
                Math.Max(
                    3,
                    Settings?.PollIntervalSeconds ?? 3);

            TaskCompletionSource<bool> startSignal;

            lock (lifecycleLock)
            {
                if (ending ||
                    pluginCancellation == null ||
                    pluginCancellation.IsCancellationRequested ||
                    !IsConnected ||
                    (DateTime.UtcNow - lastTrackRequest)
                        .TotalSeconds <
                        pollIntervalSeconds ||
                    DateTime.UtcNow.Ticks <
                        Interlocked.Read(
                            ref nextPlaybackRequestUtcTicks) ||
                    !playbackSemaphore.Wait(0))
                {
                    return;
                }

                lastTrackRequest = DateTime.UtcNow;
                CancellationToken cancellationToken =
                    sessionCancellation?.Token ??
                    new CancellationToken(true);
                startSignal =
                    new TaskCompletionSource<bool>();
                Task task =
                    RunTrackedOperationAsync(
                        () =>
                            RunPlaybackUpdateAsync(
                                cancellationToken),
                        startSignal.Task);

                playbackTask = task;
                RegisterLiveOperationLocked(
                    task);
            }

            startSignal.SetResult(true);
        }

        public void End(PluginManager pluginManager)
        {
            this.SaveCommonSettings(
                "GeneralSettings",
                Settings);

            CancellationTokenSource pluginSource;
            CancellationTokenSource sessionSource;
            CancellationTokenSource[] retiredSources;
            Task[] operations;

            lock (stateCommitLock)
            {
                ending = true;
                Interlocked.Increment(
                    ref authenticationGeneration);
                IsAuthorizationInProgress = false;

                lock (lifecycleLock)
                {
                    pluginSource = pluginCancellation;
                    sessionSource = sessionCancellation;
                    retiredSources =
                        retiredSessionSources.ToArray();
                    retiredSessionSources.Clear();
                    operations =
                        new Task[liveOperations.Count];
                    liveOperations.CopyTo(
                        operations);
                }
            }

            sessionSource?.Cancel();
            pluginSource?.Cancel();

            foreach (CancellationTokenSource source in
                retiredSources)
            {
                source.Cancel();
            }

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

            foreach (CancellationTokenSource source in
                retiredSources)
            {
                source.Dispose();
            }
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

            this.AttachDelegate(
                name: "Spotify.ProgressMs",
                valueProvider: () => ProgressMs);

            this.AttachDelegate(
                name: "Spotify.DurationMs",
                valueProvider: () => DurationMs);

            this.AttachDelegate(
                name: "Spotify.ProgressPercent",
                valueProvider: () => ProgressPercent);

            this.AttachDelegate(
                name: "Spotify.ProgressText",
                valueProvider: () => ProgressText);

            this.AttachDelegate(
                name: "Spotify.DurationText",
                valueProvider: () => DurationText);

            this.AttachDelegate(
                name: "Spotify.PlaybackTime",
                valueProvider: () => PlaybackTime);

            this.AttachDelegate(
                name: "Spotify.IsPlaying",
                valueProvider: () => IsPlaying);

            this.AttachDelegate(
                name: "Spotify.PlayPauseText",
                valueProvider: () => PlayPauseText);

            this.AttachDelegate(
                name: "Spotify.PlaybackControlStatus",
                valueProvider: () => PlaybackControlStatus);

            this.AddAction(
                actionName: "Spotify.PlayPause",
                actionStart: (manager, argument) =>
                    QueuePlaybackControlFromAction(
                        PlaybackControlRequest.Toggle));

            this.AddAction(
                actionName: "Spotify.Next",
                actionStart: (manager, argument) =>
                    QueuePlaybackControlFromAction(
                        PlaybackControlRequest.Next));

            this.AddAction(
                actionName: "Spotify.Previous",
                actionStart: (manager, argument) =>
                    QueuePlaybackControlFromAction(
                        PlaybackControlRequest.Previous));

            for (int seekPercent = 0;
                 seekPercent <= 100;
                 seekPercent++)
            {
                int capturedSeekPercent = seekPercent;

                this.AddAction(
                    actionName:
                        "Spotify.Seek." +
                        capturedSeekPercent,
                    actionStart: (manager, argument) =>
                        QueueSeekFromAction(
                            capturedSeekPercent));
            }

            CancellationToken startupCancellationToken;
            int startupGeneration;

            lock (lifecycleLock)
            {
                startupCancellationToken =
                    sessionCancellation.Token;
                startupGeneration =
                    Volatile.Read(
                        ref authenticationGeneration);
            }

            startupTask =
                StartTrackedOperation(
                    () =>
                        StartSpotifyDelayedAsync(
                            startupCancellationToken,
                            startupGeneration));
        }
    }
}
