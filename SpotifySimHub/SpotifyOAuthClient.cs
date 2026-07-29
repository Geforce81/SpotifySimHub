using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifySimHub
{
    internal sealed class SpotifyOAuthClient
    {
        private readonly HttpClient httpClient;
        private readonly string clientId;
        private readonly string redirectUri;
        private readonly string listenerPrefix;
        private readonly TimeSpan authorizationTimeout;

        public SpotifyOAuthClient(
            HttpClient httpClient,
            string clientId,
            string redirectUri,
            string listenerPrefix,
            TimeSpan authorizationTimeout)
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
            this.redirectUri =
                string.IsNullOrWhiteSpace(redirectUri)
                    ? throw new ArgumentException(
                        "A redirect URI is required.",
                        nameof(redirectUri))
                    : redirectUri;
            this.listenerPrefix =
                string.IsNullOrWhiteSpace(listenerPrefix)
                    ? throw new ArgumentException(
                        "A listener prefix is required.",
                        nameof(listenerPrefix))
                    : listenerPrefix;

            if (authorizationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authorizationTimeout));
            }

            this.authorizationTimeout = authorizationTimeout;
        }

        public async Task<SpotifyTokenResult> AuthorizeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (CancellationTokenSource timeoutCancellation =
                   CancellationTokenSource
                       .CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCancellation.CancelAfter(
                    authorizationTimeout);

                try
                {
                    string codeVerifier = CreateCodeVerifier();
                    string codeChallenge =
                        CreateCodeChallenge(codeVerifier);
                    string expectedState = CreateState();

                    string scope =
                        "user-read-currently-playing user-read-playback-state";

                    string authorizationUrl =
                        "https://accounts.spotify.com/authorize" +
                        "?response_type=code" +
                        "&client_id=" +
                        Uri.EscapeDataString(clientId) +
                        "&scope=" +
                        Uri.EscapeDataString(scope) +
                        "&redirect_uri=" +
                        Uri.EscapeDataString(redirectUri) +
                        "&code_challenge_method=S256" +
                        "&code_challenge=" +
                        Uri.EscapeDataString(codeChallenge) +
                        "&state=" +
                        Uri.EscapeDataString(expectedState) +
                        "&show_dialog=true";

                    string authorizationCode =
                        await ReceiveAuthorizationCodeAsync(
                                authorizationUrl,
                                expectedState,
                                timeoutCancellation.Token)
                            .ConfigureAwait(false);

                    return await ExchangeAuthorizationCodeAsync(
                            authorizationCode,
                            codeVerifier,
                            timeoutCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested &&
                          timeoutCancellation.IsCancellationRequested)
                {
                    throw new SpotifyAuthenticationException(
                        "Spotify authorization timed out.",
                        SpotifyAuthenticationErrorKind.TimedOut);
                }
                catch (OperationCanceledException ex)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new SpotifyAuthenticationException(
                        "A Spotify authorization request timed out.",
                        SpotifyAuthenticationErrorKind.Failed,
                        ex);
                }
            }
        }

        internal static string CreateCodeVerifier()
        {
            byte[] randomBytes = new byte[64];

            using (RandomNumberGenerator random =
                   RandomNumberGenerator.Create())
            {
                random.GetBytes(randomBytes);
            }

            return Base64UrlEncode(randomBytes);
        }

        internal static string CreateCodeChallenge(
            string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
            {
                throw new ArgumentException(
                    "A PKCE verifier is required.",
                    nameof(verifier));
            }

            byte[] verifierBytes =
                Encoding.ASCII.GetBytes(verifier);

            using (SHA256 sha256 = SHA256.Create())
            {
                return Base64UrlEncode(
                    sha256.ComputeHash(verifierBytes));
            }
        }

        internal static string CreateState()
        {
            byte[] randomBytes = new byte[24];

            using (RandomNumberGenerator random =
                   RandomNumberGenerator.Create())
            {
                random.GetBytes(randomBytes);
            }

            return Base64UrlEncode(randomBytes);
        }

        private async Task<string> ReceiveAuthorizationCodeAsync(
            string authorizationUrl,
            string expectedState,
            CancellationToken cancellationToken)
        {
            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(listenerPrefix);
                listener.Start();

                try
                {
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = authorizationUrl,
                            UseShellExecute = true
                        });

                    while (true)
                    {
                        HttpListenerContext context =
                            await GetContextAsync(
                                    listener,
                                    cancellationToken)
                                .ConfigureAwait(false);

                        string receivedState =
                            context.Request.QueryString["state"];

                        if (!string.Equals(
                                receivedState,
                                expectedState,
                                StringComparison.Ordinal))
                        {
                            await WriteBrowserResponseAsync(
                                    context,
                                    "Invalid Spotify callback",
                                    "An outdated callback was ignored. " +
                                    "Continue in the most recently opened Spotify tab.",
                                    false)
                                .ConfigureAwait(false);

                            continue;
                        }

                        string error =
                            context.Request.QueryString["error"];
                        string code =
                            context.Request.QueryString["code"];

                        await WriteBrowserResponseAsync(
                                context,
                                string.IsNullOrEmpty(error)
                                    ? "Spotify is connected"
                                    : "Authorization failed",
                                string.IsNullOrEmpty(error)
                                    ? "Your Spotify connection to SimHub is ready."
                                    : "Spotify authorization was not completed.",
                                string.IsNullOrEmpty(error))
                            .ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(error))
                        {
                            throw new SpotifyAuthenticationException(
                                "Spotify authorization was cancelled.",
                                SpotifyAuthenticationErrorKind.Cancelled);
                        }

                        if (string.IsNullOrEmpty(code))
                        {
                            throw new SpotifyAuthenticationException(
                                "Spotify returned no authorization code.");
                        }

                        return code;
                    }
                }
                finally
                {
                    if (listener.IsListening)
                    {
                        listener.Stop();
                    }
                }
            }
        }

        private async Task<SpotifyTokenResult>
            ExchangeAuthorizationCodeAsync(
                string authorizationCode,
                string codeVerifier,
                CancellationToken cancellationToken)
        {
            var tokenData =
                new[]
                {
                    new KeyValuePair<string, string>(
                        "client_id",
                        clientId),
                    new KeyValuePair<string, string>(
                        "grant_type",
                        "authorization_code"),
                    new KeyValuePair<string, string>(
                        "code",
                        authorizationCode),
                    new KeyValuePair<string, string>(
                        "redirect_uri",
                        redirectUri),
                    new KeyValuePair<string, string>(
                        "code_verifier",
                        codeVerifier)
                };

            using (HttpRequestMessage request =
                   new HttpRequestMessage(
                       HttpMethod.Post,
                       "https://accounts.spotify.com/api/token"))
            {
                request.Content =
                    new FormUrlEncodedContent(tokenData);

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
                        throw new SpotifyAuthenticationException(
                            "Spotify authorization failed with HTTP status " +
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

        private static async Task<HttpListenerContext>
            GetContextAsync(
                HttpListener listener,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (cancellationToken.Register(
                       () =>
                       {
                           try
                           {
                               listener.Stop();
                           }
                           catch
                           {
                           }
                       }))
            {
                try
                {
                    return await listener
                        .GetContextAsync()
                        .ConfigureAwait(false);
                }
                catch (HttpListenerException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        cancellationToken);
                }
                catch (ObjectDisposedException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        cancellationToken);
                }
            }
        }

        private static async Task WriteBrowserResponseAsync(
            HttpListenerContext context,
            string title,
            string message,
            bool success)
        {
            string accent =
                success ? "#7FE8A8" : "#FF6B6B";
            string icon = success ? "✓" : "!";
            string statusText =
                success ? "CONNECTED" : "ACTION REQUIRED";

            string html =
                "<!DOCTYPE html>" +
                "<html lang=\"en\">" +
                "<head>" +
                "<meta charset=\"utf-8\">" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<title>" +
                WebUtility.HtmlEncode(title) +
                "</title>" +
                "<style>" +
                "*{box-sizing:border-box}" +
                "html,body{margin:0;min-height:100%;background:#000;color:#fff;" +
                "font-family:Segoe UI,Arial,sans-serif}" +
                "body{display:flex;align-items:center;justify-content:center;padding:24px;" +
                "background:radial-gradient(circle at 50% 20%,#13231c 0,#050706 38%,#000 75%)}" +
                ".card{width:min(680px,100%);padding:54px 48px 46px;border-radius:28px;" +
                "background:rgba(10,12,11,.92);border:1px solid rgba(255,255,255,.10);" +
                "box-shadow:0 28px 90px rgba(0,0,0,.65);text-align:center}" +
                ".brand{font-size:13px;letter-spacing:.24em;color:#8e9691;font-weight:700;" +
                "margin-bottom:30px}" +
                ".icon{width:76px;height:76px;margin:0 auto 26px;border-radius:50%;" +
                "display:flex;align-items:center;justify-content:center;font-size:42px;" +
                "font-weight:700;color:#07100b;background:" +
                accent +
                "}" +
                ".status{font-size:12px;letter-spacing:.22em;font-weight:800;color:" +
                accent +
                ";margin-bottom:14px}" +
                "h1{font-size:38px;line-height:1.12;margin:0 0 16px;font-weight:650}" +
                "p{margin:0 auto;color:#b5bcb8;font-size:18px;line-height:1.6;max-width:520px}" +
                ".hint{margin-top:30px;font-size:14px;color:#737b76}" +
                "</style>" +
                "</head>" +
                "<body>" +
                "<main class=\"card\">" +
                "<div class=\"brand\">SPOTIFYSIMHUB</div>" +
                "<div class=\"icon\">" +
                icon +
                "</div>" +
                "<div class=\"status\">" +
                statusText +
                "</div>" +
                "<h1>" +
                WebUtility.HtmlEncode(title) +
                "</h1>" +
                "<p>" +
                WebUtility.HtmlEncode(message) +
                "</p>" +
                "<div class=\"hint\">" +
                (success
                    ? "You may close this window."
                    : "Return to SimHub and review the connection status.") +
                "</div>" +
                "</main>" +
                "</body>" +
                "</html>";

            byte[] responseBytes =
                Encoding.UTF8.GetBytes(html);

            context.Response.ContentType =
                "text/html; charset=utf-8";
            context.Response.ContentLength64 =
                responseBytes.Length;

            await context.Response.OutputStream.WriteAsync(
                    responseBytes,
                    0,
                    responseBytes.Length)
                .ConfigureAwait(false);

            context.Response.OutputStream.Close();
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
