using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SpotifySimHub
{
    internal sealed class SpotifyTokenStore
    {
        private static readonly byte[] OptionalEntropy =
            Encoding.UTF8.GetBytes(
                "SpotifySimHub.RefreshToken.v1");

        private readonly object syncRoot = new object();
        private readonly string protectedTokenFilePath;
        private readonly string legacyTokenFilePath;
        private readonly string temporaryTokenFilePath;

        public SpotifyTokenStore(string dataFolder)
        {
            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new ArgumentException(
                    "A Spotify data folder is required.",
                    nameof(dataFolder));
            }

            protectedTokenFilePath =
                Path.Combine(
                    dataFolder,
                    "refresh_token.dat");
            legacyTokenFilePath =
                Path.Combine(
                    dataFolder,
                    "refresh_token.txt");
            temporaryTokenFilePath =
                protectedTokenFilePath + ".tmp";
        }

        public bool MigrationFailed { get; private set; }

        public bool HasSavedToken
        {
            get
            {
                lock (syncRoot)
                {
                    return
                        File.Exists(
                            protectedTokenFilePath) ||
                        File.Exists(
                            legacyTokenFilePath);
                }
            }
        }

        public string Load()
        {
            lock (syncRoot)
            {
                MigrationFailed = false;

                if (File.Exists(
                        protectedTokenFilePath))
                {
                    try
                    {
                        return LoadProtectedToken();
                    }
                    catch when (
                        File.Exists(
                            legacyTokenFilePath))
                    {
                        MigrationFailed = true;

                        return LoadLegacyToken();
                    }
                }

                if (!File.Exists(
                        legacyTokenFilePath))
                {
                    return "";
                }

                string legacyToken =
                    LoadLegacyToken();

                if (string.IsNullOrEmpty(
                        legacyToken))
                {
                    return "";
                }

                try
                {
                    Save(legacyToken);

                    string verifiedToken =
                        LoadProtectedToken();

                    if (!string.Equals(
                            legacyToken,
                            verifiedToken,
                            StringComparison.Ordinal))
                    {
                        throw new CryptographicException(
                            "The protected Spotify token could not be verified.");
                    }

                    File.Delete(
                        legacyTokenFilePath);
                }
                catch
                {
                    MigrationFailed = true;
                }

                return legacyToken;
            }
        }

        public void Save(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException(
                    "A refresh token is required.",
                    nameof(refreshToken));
            }

            lock (syncRoot)
            {
                string folder =
                    Path.GetDirectoryName(
                        protectedTokenFilePath);

                Directory.CreateDirectory(folder);

                byte[] plainText =
                    Encoding.UTF8.GetBytes(
                        refreshToken);
                byte[] protectedData =
                    ProtectedData.Protect(
                        plainText,
                        OptionalEntropy,
                        DataProtectionScope.CurrentUser);

                WriteProtectedTokenAtomically(
                    protectedData);
            }
        }

        public void Delete()
        {
            lock (syncRoot)
            {
                DeleteIfExists(
                    protectedTokenFilePath);
                DeleteIfExists(
                    legacyTokenFilePath);
                DeleteIfExists(
                    temporaryTokenFilePath);
            }
        }

        private string LoadProtectedToken()
        {
            byte[] protectedData =
                File.ReadAllBytes(
                    protectedTokenFilePath);
            byte[] plainText =
                ProtectedData.Unprotect(
                    protectedData,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);

            return Encoding.UTF8
                .GetString(plainText)
                .Trim();
        }

        private string LoadLegacyToken()
        {
            return File.ReadAllText(
                    legacyTokenFilePath,
                    Encoding.UTF8)
                .Trim();
        }

        private void WriteProtectedTokenAtomically(
            byte[] protectedData)
        {
            DeleteIfExists(
                temporaryTokenFilePath);

            try
            {
                File.WriteAllBytes(
                    temporaryTokenFilePath,
                    protectedData);

                if (File.Exists(
                        protectedTokenFilePath))
                {
                    File.Replace(
                        temporaryTokenFilePath,
                        protectedTokenFilePath,
                        null);
                }
                else
                {
                    File.Move(
                        temporaryTokenFilePath,
                        protectedTokenFilePath);
                }
            }
            finally
            {
                DeleteIfExists(
                    temporaryTokenFilePath);
            }
        }

        private static void DeleteIfExists(
            string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
