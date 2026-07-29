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
                        throw new SpotifyApiException(
                            "Spotify refresh failed with HTTP status " +
                            (int)response.StatusCode +
                            ".");
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

                    return new SpotifyPlaybackResult
                    {
                        Status = SpotifyPlaybackStatus.Success,
                        StatusCode = response.StatusCode,
                        TrackName =
                            root["item"]?["name"]
                                ?.ToString() ?? "",
                        ArtistName =
                            root["item"]?["artists"]?[0]?["name"]
                                ?.ToString() ?? "",
                        AlbumName =
                            root["item"]?["album"]?["name"]
                                ?.ToString() ?? "",
                        CoverUrl =
                            root["item"]?["album"]?["images"]?[0]?["url"]
                                ?.ToString() ?? ""
                    };
                }
            }
        }
    }
}
