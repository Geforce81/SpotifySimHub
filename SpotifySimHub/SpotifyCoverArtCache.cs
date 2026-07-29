using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpotifySimHub
{
    internal sealed class SpotifyCoverArtCache : IDisposable
    {
        private const int MaximumCoverSizeBytes =
            5 * 1024 * 1024;

        private readonly HttpClient httpClient;
        private readonly string coverPath;
        private readonly string[] dashCoverPaths;
        private readonly SemaphoreSlim coverSemaphore =
            new SemaphoreSlim(1, 1);

        private string currentCoverUrl = "";
        private string currentDashCoverPath = "";
        private int nextDashCoverIndex;
        private ImageSource currentCoverImage;
        private bool disposed;

        public SpotifyCoverArtCache(
            HttpClient httpClient,
            string dataFolder)
        {
            this.httpClient =
                httpClient ??
                throw new ArgumentNullException(nameof(httpClient));

            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentException(
                    "A Spotify data folder is required.",
                    nameof(dataFolder));
            }

            coverPath =
                Path.Combine(
                    dataFolder,
                    "cover.jpg");
            dashCoverPaths =
                new[]
                {
                    Path.Combine(
                        dataFolder,
                        "cover-dash-a.jpg"),
                    Path.Combine(
                        dataFolder,
                        "cover-dash-b.jpg")
                };
        }

        public async Task<SpotifyCoverArtResult> GetAsync(
            string coverUrl,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            await coverSemaphore
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (string.IsNullOrEmpty(coverUrl))
                {
                    ClearCore();
                    return new SpotifyCoverArtResult();
                }

                if (string.Equals(
                        coverUrl,
                        currentCoverUrl,
                        StringComparison.Ordinal) &&
                    currentCoverImage != null)
                {
                    return new SpotifyCoverArtResult
                    {
                        CoverPath = coverPath,
                        DashCoverPath =
                            currentDashCoverPath,
                        CoverUrl = currentCoverUrl,
                        CoverImage = currentCoverImage
                    };
                }

                Uri coverUri;

                if (!Uri.TryCreate(
                        coverUrl,
                        UriKind.Absolute,
                        out coverUri) ||
                    !string.Equals(
                        coverUri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SpotifyApiException(
                        "Spotify returned an invalid cover URL.");
                }

                using (CancellationTokenSource requestCancellation =
                       CancellationTokenSource
                           .CreateLinkedTokenSource(cancellationToken))
                using (HttpRequestMessage request =
                       new HttpRequestMessage(
                           HttpMethod.Get,
                           coverUri))
                {
                    if (httpClient.Timeout !=
                        Timeout.InfiniteTimeSpan)
                    {
                        requestCancellation.CancelAfter(
                            httpClient.Timeout);
                    }

                    using (HttpResponseMessage response =
                           await httpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseHeadersRead,
                                   requestCancellation.Token)
                               .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new SpotifyApiException(
                                "Spotify cover download failed with HTTP status " +
                                (int)response.StatusCode +
                                ".");
                        }

                        string mediaType =
                            response.Content.Headers.ContentType
                                ?.MediaType;

                        if (string.IsNullOrEmpty(mediaType) ||
                            !mediaType.StartsWith(
                                "image/",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new SpotifyApiException(
                                "Spotify cover response was not an image.");
                        }

                        long? contentLength =
                            response.Content.Headers.ContentLength;

                        if (contentLength.HasValue &&
                            contentLength.Value >
                            MaximumCoverSizeBytes)
                        {
                            throw new SpotifyApiException(
                                "Spotify cover image exceeded the size limit.");
                        }

                        byte[] imageBytes =
                            await ReadImageBytesAsync(
                                    response,
                                    requestCancellation.Token)
                                .ConfigureAwait(false);

                        BitmapImage bitmap =
                            CreateBitmap(imageBytes);

                        string folder =
                            Path.GetDirectoryName(coverPath);
                        Directory.CreateDirectory(folder);

                        WriteAllBytesAtomically(
                            coverPath,
                            imageBytes);

                        string nextDashCoverPath =
                            dashCoverPaths[
                                nextDashCoverIndex];

                        WriteAllBytesAtomically(
                            nextDashCoverPath,
                            imageBytes);

                        currentCoverUrl = coverUrl;
                        currentDashCoverPath =
                            nextDashCoverPath;
                        nextDashCoverIndex =
                            (nextDashCoverIndex + 1) %
                            dashCoverPaths.Length;
                        currentCoverImage = bitmap;

                        return new SpotifyCoverArtResult
                        {
                            CoverPath = coverPath,
                            DashCoverPath =
                                currentDashCoverPath,
                            CoverUrl = coverUrl,
                            CoverImage = bitmap
                        };
                    }
                }
            }
            finally
            {
                coverSemaphore.Release();
            }
        }

        public void Clear()
        {
            ThrowIfDisposed();

            coverSemaphore.Wait();

            try
            {
                ClearCore();
            }
            finally
            {
                coverSemaphore.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            coverSemaphore.Dispose();
        }

        private void ClearCore()
        {
            currentCoverUrl = "";
            currentDashCoverPath = "";
            currentCoverImage = null;

            DeleteIfExists(
                coverPath);
            DeleteIfExists(
                coverPath + ".tmp");

            foreach (string dashCoverPath in
                     dashCoverPaths)
            {
                DeleteIfExists(
                    dashCoverPath);
                DeleteIfExists(
                    dashCoverPath + ".tmp");
            }
        }

        private static void WriteAllBytesAtomically(
            string filePath,
            byte[] imageBytes)
        {
            string temporaryFilePath =
                filePath + ".tmp";

            DeleteIfExists(
                temporaryFilePath);

            try
            {
                File.WriteAllBytes(
                    temporaryFilePath,
                    imageBytes);

                if (File.Exists(filePath))
                {
                    File.Replace(
                        temporaryFilePath,
                        filePath,
                        null);
                }
                else
                {
                    File.Move(
                        temporaryFilePath,
                        filePath);
                }
            }
            finally
            {
                DeleteIfExists(
                    temporaryFilePath);
            }
        }

        private static async Task<byte[]> ReadImageBytesAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            using (Stream responseStream =
                   await response.Content
                       .ReadAsStreamAsync()
                       .ConfigureAwait(false))
            using (MemoryStream buffer =
                   new MemoryStream())
            {
                byte[] chunk = new byte[81920];

                while (true)
                {
                    int bytesRead =
                        await responseStream.ReadAsync(
                                chunk,
                                0,
                                chunk.Length,
                                cancellationToken)
                            .ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    if (buffer.Length + bytesRead >
                        MaximumCoverSizeBytes)
                    {
                        throw new SpotifyApiException(
                            "Spotify cover image exceeded the size limit.");
                    }

                    buffer.Write(
                        chunk,
                        0,
                        bytesRead);
                }

                if (buffer.Length == 0)
                {
                    throw new SpotifyApiException(
                        "Spotify returned an empty cover image.");
                }

                return buffer.ToArray();
            }
        }

        private static BitmapImage CreateBitmap(
            byte[] imageBytes)
        {
            BitmapImage bitmap = new BitmapImage();

            using (MemoryStream stream =
                   new MemoryStream(imageBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption =
                    BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            return bitmap;
        }

        private static void DeleteIfExists(
            string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(SpotifyCoverArtCache));
            }
        }
    }
}
