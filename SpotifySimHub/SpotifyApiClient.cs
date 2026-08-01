using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifySimHub
{
    internal sealed class SpotifyApiClient
    {
        private readonly HttpClient httpClient;
        private readonly string clientId;

        public SpotifyApiClient(
            HttpClient httpClient,
            string clientId)
        {
            this.httpClient =
                httpClient ??
                throw new ArgumentNullException(nameof(httpClient));
            this.clientId =
                string.IsNullOrWhiteSpace(clientId)
                    ? throw new ArgumentException(
                        "A Spotify client ID is required.",
                        nameof(clientId))
                    : clientId;
        }

        public async Task<SpotifyTokenResult>
            RefreshAccessTokenAsync(
                string refreshToken,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException(
                    "A refresh token is required.",
                    nameof(refreshToken));
            }

            var refreshData =
                new[]
                {
                    new KeyValuePair<string, string>(
                        "client_id",
                        clientId),
                    new KeyValuePair<string, string>(
                        "grant_type",
                        "refresh_token"),
                    new KeyValuePair<string, string>(
                        "refresh_token",
                        refreshToken)
                };

            using (HttpRequestMessage request =
                   new HttpRequestMessage(
                       HttpMethod.Post,
                       "https://accounts.spotify.com/api/token"))
            {
                request.Content =
                    new FormUrlEncodedContent(refreshData);

                using (HttpResponseMessage response =
                       await httpClient.SendAsync(
                               request,
                               cancellationToken)
                           .ConfigureAwait(false))
                {
                    string json =
                        await response.Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        SpotifyApiErrorKind errorKind =
                            SpotifyApiErrorKind.Failed;

                        try
                        {
                            JObject errorObject =
                                JObject.Parse(json);

                            if (string.Equals(
                                    errorObject["error"]
                                        ?.ToString(),
                                    "invalid_grant",
                                    StringComparison.Ordinal))
                            {
                                errorKind =
                                    SpotifyApiErrorKind.InvalidGrant;
                            }
                        }
                        catch (Newtonsoft.Json.JsonException)
                        {
                        }

                        throw new SpotifyApiException(
                            "Spotify refresh failed with HTTP status " +
                            (int)response.StatusCode +
                            ".",
                            errorKind);
                    }

                    JObject tokenObject = JObject.Parse(json);

                    return new SpotifyTokenResult
                    {
                        AccessToken =
                            tokenObject["access_token"]
                                ?.ToString() ?? "",
                        RefreshToken =
                            tokenObject["refresh_token"]
                                ?.ToString() ?? "",
                        ExpiresInSeconds =
                            tokenObject["expires_in"]
                                ?.ToObject<int>() ?? 3600
                    };
                }
            }
        }

        public async Task<SpotifyPlaybackResult>
            GetCurrentlyPlayingAsync(
                string accessToken,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException(
                    "An access token is required.",
                    nameof(accessToken));
            }

            using (HttpRequestMessage request =
                   new HttpRequestMessage(
                       HttpMethod.Get,
                       "https://api.spotify.com/v1/me/player/currently-playing"))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken);

                using (HttpResponseMessage response =
                       await httpClient.SendAsync(
                               request,
                               cancellationToken)
                           .ConfigureAwait(false))
                {
                    if (response.StatusCode ==
                        HttpStatusCode.NoContent)
                    {
                        return new SpotifyPlaybackResult
                        {
                            Status =
                                SpotifyPlaybackStatus.NoContent,
                            StatusCode = response.StatusCode
                        };
                    }

                    if (response.StatusCode ==
                        HttpStatusCode.Unauthorized)
                    {
                        return new SpotifyPlaybackResult
                        {
                            Status =
                                SpotifyPlaybackStatus.Unauthorized,
                            StatusCode = response.StatusCode
                        };
                    }

                    if ((int)response.StatusCode == 429)
                    {
                        TimeSpan? retryAfter = null;

                        if (response.Headers.RetryAfter != null)
                        {
                            retryAfter =
                                response.Headers.RetryAfter.Delta;

                            if (!retryAfter.HasValue &&
                                response.Headers.RetryAfter.Date
                                    .HasValue)
                            {
                                retryAfter =
                                    response.Headers.RetryAfter.Date
                                        .Value -
                                    DateTimeOffset.UtcNow;
                            }
                        }

                        return new SpotifyPlaybackResult
                        {
                            Status =
                                SpotifyPlaybackStatus.RateLimited,
                            StatusCode = response.StatusCode,
                            RetryAfter = retryAfter
                        };
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return new SpotifyPlaybackResult
                        {
                            Status = SpotifyPlaybackStatus.Error,
                            StatusCode = response.StatusCode
                        };
                    }

                    string json =
                        await response.Content
                            .ReadAsStringAsync()
                            .ConfigureAwait(false);
                    JObject root = JObject.Parse(json);
                    JToken progressToken =
                        root["progress_ms"];
                    bool hasProgress =
                        progressToken != null &&
                        progressToken.Type !=
                        JTokenType.Null;

                    return new SpotifyPlaybackResult
                    {
                        Status = SpotifyPlaybackStatus.Success,
                        StatusCode = response.StatusCode,
                        IsPlaying =
                            root["is_playing"]
                                ?.ToObject<bool>() ?? false,
                        HasProgress = hasProgress,
                        ProgressMs =
                            hasProgress
                                ? progressToken.ToObject<long>()
                                : 0,
                        DurationMs =
                            root["item"]?["duration_ms"]
                                ?.ToObject<long>() ?? 0,
                        TrackName =
                            root["item"]?["name"]
                                ?.ToString() ?? "",
                        ArtistName =
                            root.SelectToken(
                                "item.artists[0].name")
                                ?.ToString() ?? "",
                        AlbumName =
                            root["item"]?["album"]?["name"]
                                ?.ToString() ?? "",
                        CoverUrl =
                            root.SelectToken(
                                "item.album.images[0].url")
                                ?.ToString() ?? ""
                    };
                }
            }
        }

        public async Task<SpotifyPlaybackCommandResult>
            SendPlaybackCommandAsync(
                string accessToken,
                SpotifyPlaybackCommand command,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException(
                    "An access token is required.",
                    nameof(accessToken));
            }

            HttpMethod method;
            string endpoint;

            switch (command)
            {
                case SpotifyPlaybackCommand.Play:
                    method = HttpMethod.Put;
                    endpoint =
                        "https://api.spotify.com/v1/me/player/play";
                    break;

                case SpotifyPlaybackCommand.Pause:
                    method = HttpMethod.Put;
                    endpoint =
                        "https://api.spotify.com/v1/me/player/pause";
                    break;

                case SpotifyPlaybackCommand.Next:
                    method = HttpMethod.Post;
                    endpoint =
                        "https://api.spotify.com/v1/me/player/next";
                    break;

                case SpotifyPlaybackCommand.Previous:
                    method = HttpMethod.Post;
                    endpoint =
                        "https://api.spotify.com/v1/me/player/previous";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(command),
                        command,
                        "Unknown Spotify playback command.");
            }

            using (HttpRequestMessage request =
                   new HttpRequestMessage(
                       method,
                       endpoint))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken);

                using (HttpResponseMessage response =
                       await httpClient.SendAsync(
                               request,
                               cancellationToken)
                           .ConfigureAwait(false))
                {
                    TimeSpan? retryAfter = null;

                    if ((int)response.StatusCode == 429 &&
                        response.Headers.RetryAfter != null)
                    {
                        retryAfter =
                            response.Headers.RetryAfter.Delta;

                        if (!retryAfter.HasValue &&
                            response.Headers.RetryAfter.Date
                                .HasValue)
                        {
                            retryAfter =
                                response.Headers.RetryAfter.Date
                                    .Value -
                                DateTimeOffset.UtcNow;
                        }
                    }

                    return new SpotifyPlaybackCommandResult
                    {
                        StatusCode = response.StatusCode,
                        RetryAfter = retryAfter
                    };
                }
            }
        }

        public async Task<SpotifyPlaybackCommandResult>
            SeekPlaybackAsync(
                string accessToken,
                long positionMs,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException(
                    "An access token is required.",
                    nameof(accessToken));
            }

            string endpoint =
                "https://api.spotify.com/v1/me/player/seek" +
                "?position_ms=" +
                Math.Max(
                    0,
                    positionMs);

            using (HttpRequestMessage request =
                   new HttpRequestMessage(
                       HttpMethod.Put,
                       endpoint))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken);

                using (HttpResponseMessage response =
                       await httpClient.SendAsync(
                               request,
                               cancellationToken)
                           .ConfigureAwait(false))
                {
                    TimeSpan? retryAfter = null;

                    if ((int)response.StatusCode == 429 &&
                        response.Headers.RetryAfter != null)
                    {
                        retryAfter =
                            response.Headers.RetryAfter.Delta;

                        if (!retryAfter.HasValue &&
                            response.Headers.RetryAfter.Date
                                .HasValue)
                        {
                            retryAfter =
                                response.Headers.RetryAfter.Date
                                    .Value -
                                DateTimeOffset.UtcNow;
                        }
                    }

                    return new SpotifyPlaybackCommandResult
                    {
                        StatusCode = response.StatusCode,
                        RetryAfter = retryAfter
                    };
                }
            }
        }
    }
}
