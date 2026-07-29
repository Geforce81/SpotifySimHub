using System;
using System.Net;
using System.Windows.Media;

namespace SpotifySimHub
{
    internal sealed class SpotifyTokenResult
    {
        public string AccessToken { get; set; } = "";

        public string RefreshToken { get; set; } = "";

        public int ExpiresInSeconds { get; set; } = 3600;
    }

    internal enum SpotifyPlaybackStatus
    {
        Success,
        NoContent,
        Unauthorized,
        RateLimited,
        Error
    }

    internal sealed class SpotifyPlaybackResult
    {
        public SpotifyPlaybackStatus Status { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public string TrackName { get; set; } = "";

        public string ArtistName { get; set; } = "";

        public string AlbumName { get; set; } = "";

        public string CoverUrl { get; set; } = "";
    }

    internal sealed class SpotifyCoverArtResult
    {
        public string CoverPath { get; set; } = "";

        public string CoverUrl { get; set; } = "";

        public ImageSource CoverImage { get; set; }
    }

    internal enum SpotifyAuthenticationErrorKind
    {
        Failed,
        Cancelled,
        TimedOut
    }

    internal sealed class SpotifyAuthenticationException : Exception
    {
        public SpotifyAuthenticationException(string message)
            : this(
                message,
                SpotifyAuthenticationErrorKind.Failed)
        {
        }

        public SpotifyAuthenticationException(
            string message,
            SpotifyAuthenticationErrorKind kind)
            : base(message)
        {
            Kind = kind;
        }

        public SpotifyAuthenticationException(
            string message,
            Exception innerException)
            : this(
                message,
                SpotifyAuthenticationErrorKind.Failed,
                innerException)
        {
        }

        public SpotifyAuthenticationException(
            string message,
            SpotifyAuthenticationErrorKind kind,
            Exception innerException)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public SpotifyAuthenticationErrorKind Kind { get; }
    }

    internal sealed class SpotifyApiException : Exception
    {
        public SpotifyApiException(string message)
            : base(message)
        {
        }

        public SpotifyApiException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
