# SpotifySimHub

SpotifySimHub is a Windows plugin that exposes the current Spotify playback state to SimHub dashboards. It supports Spotify Authorization Code Flow with PKCE, automatic refresh-token reuse, DPAPI-protected local token storage, and cached cover art.

## Requirements

- SimHub 9.x
- A Spotify account
- A Spotify developer application
- Windows with .NET Framework 4.8
- Visual Studio or Build Tools with the .NET Framework MSBuild toolchain

The Spotify application must allow this redirect URI:

```text
http://127.0.0.1:9877/callback
```

## Local configuration

Copy `SpotifyClientId.example.props` to `SpotifyClientId.local.props` inside the project directory. Replace the placeholder with your Spotify client ID and set `SimHubInstallPath` to the SimHub installation directory.

```powershell
Copy-Item .\SpotifySimHub\SpotifyClientId.example.props .\SpotifySimHub\SpotifyClientId.local.props
```

`SpotifyClientId.local.props` is ignored by Git. Never commit it, paste its value into source code, or include it in logs and issue reports. The build stops with a clear error when the local configuration is missing or invalid.

## Build

From the repository root:

```powershell
nuget restore .\SpotifySimHub\SpotifySimHub.sln
```

Then build the selected configuration:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  ".\SpotifySimHub\SpotifySimHub.csproj" `
  /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /m
```

Replace `Debug` with `Release` for a release build.

Build output:

- `SpotifySimHub\bin\Debug\SpotifySimHub.dll`
- `SpotifySimHub\bin\Release\SpotifySimHub.dll`

Newtonsoft.Json is restored through `packages.config`. SimHub-hosted assemblies are referenced with `Private=False` and are not copied into plugin output.

## Install

Close SimHub before manually installing or replacing a plugin. Copy `SpotifySimHub.dll` and the plugin-owned `Newtonsoft.Json.dll` from the selected build output directory to the SimHub installation directory. Do not copy SimHub's own assemblies from the build environment.

No automatic deployment runs as part of the build.

## Connect and disconnect

Open SpotifySimHub under SimHub's additional plugins:

- **Connect** starts the Spotify PKCE login in the browser.
- **Refresh status** refreshes the saved session and playback state.
- **Disconnect** cancels active work, clears playback data, removes the saved refresh token, and does not reopen the browser.

At startup the plugin attempts to reuse a saved login. It never starts a new browser login automatically when a saved login is missing or invalid.

Refresh tokens are protected for the current Windows user with DPAPI and stored under `%LOCALAPPDATA%\SpotifySimHub`. Existing plaintext token files are migrated only after the protected copy has been verified.

## SimHub properties

- `Spotify.CurrentTrack`
- `Spotify.Artist`
- `Spotify.Track`
- `Spotify.Album`
- `Spotify.Cover`
- `Spotify.CoverImage`

These names are compatibility contracts and should not be changed.

## Troubleshooting

- **Build reports missing local configuration:** create `SpotifyClientId.local.props` from the example and fill in both properties.
- **SimHub assemblies cannot be resolved:** verify that `SimHubInstallPath` ends with a directory separator and points to the directory containing `SimHub.Plugins.dll`.
- **Login callback fails:** confirm that the Spotify application contains the exact loopback redirect URI and that local port `9877` is available.
- **Login required after restart:** use Connect again. A rejected or revoked refresh token cannot be repaired locally.
- **No track is shown:** Spotify may have no active playback. Use Refresh status after starting playback.
- **Cover art is unavailable:** the plugin accepts only HTTPS image responses up to 5 MB and keeps the last valid cache file when a replacement is invalid.

See [MANUAL_TESTING.md](MANUAL_TESTING.md) for the release verification checklist.
