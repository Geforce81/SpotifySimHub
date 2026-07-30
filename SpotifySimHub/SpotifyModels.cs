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

        public bool IsPlaying { get; set; }

        public bool HasProgress { get; set; }

        public long ProgressMs { get; set; }

        public long DurationMs { get; set; }

        public string TrackName { get; set; } = "";

        public string ArtistName { get; set; } = "";

        public string AlbumName { get; set; } = "";

        public string CoverUrl { get; set; } = "";
    }

    internal enum SpotifyPlaybackCommand
    {
        Play,
        Pause,
        Next,
        Previous
    }

    internal sealed class SpotifyPlaybackCommandResult
    {
        public HttpStatusCode StatusCode { get; set; }

        public TimeSpan? RetryAfter { get; set; }

        public bool IsSuccess
        {
            get
            {
                return StatusCode == HttpStatusCode.NoContent;
            }
        }
    }

    internal sealed class SpotifyCoverArtResult
    {
        public string CoverPath { get; set; } = "";

        public string DashCoverPath { get; set; } = "";

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

    internal enum SpotifyApiErrorKind
    {
        Failed,
        InvalidGrant
    }

    internal sealed class SpotifyApiException : Exception
    {
        public SpotifyApiException(string message)
            : this(
                message,
                SpotifyApiErrorKind.Failed)
        {
        }

        public SpotifyApiException(
            string message,
            SpotifyApiErrorKind kind)
            : base(message)
        {
            Kind = kind;
        }

        public SpotifyApiException(
            string message,
            Exception innerException)
            : this(
                message,
                SpotifyApiErrorKind.Failed,
                innerException)
        {
        }

        public SpotifyApiException(
            string message,
            SpotifyApiErrorKind kind,
            Exception innerException)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public SpotifyApiErrorKind Kind { get; }
    }
}
