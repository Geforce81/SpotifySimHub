# SpotifySimHub installation guide

This guide creates a personal SpotifySimHub build that can read the current playback state from your Spotify account, including playback on supported Spotify Connect devices such as a PlayStation 5.

## Why this setup is necessary

SpotifySimHub reads the Spotify Web API rather than the Windows audio session. This is intentional: local audio detection cannot reliably see music playing on another device.

Spotify's authorization process requires:

- a normal Spotify account, which owns the playback session;
- a registered Spotify application, identified by a Client ID;
- user approval for the two read-only playback scopes requested by SpotifySimHub.

SpotifySimHub uses Authorization Code Flow with PKCE. It does not use a Client Secret, and you should never copy a Client Secret into the project.

New applications run in Spotify Development Mode. Under Spotify's current rules, the app owner must have Spotify Premium and each app is intended for personal use or a small allowlist. Creating a personal application avoids depending on another developer's allowlist.

Official references:

- [Spotify Web API](https://developer.spotify.com/documentation/web-api)
- [Authorization Code with PKCE](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow)
- [Development and extended quota modes](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
- [Redirect URI requirements](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)

## Before you begin

You need:

- Windows;
- SimHub 9.x installed;
- a Spotify Premium account;
- access to the Spotify Developer Dashboard;
- Visual Studio or Build Tools with .NET Framework 4.8 MSBuild;
- NuGet command-line restore support.

## Step 1: Create the Spotify application

1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard).
2. Sign in with the same Spotify account whose playback you want SpotifySimHub to read.
3. Accept the current Spotify Developer Terms if Spotify asks you to complete developer account setup.
4. Select **Create app**.
5. Use an app name such as:

   ```text
   SpotifySimHub
   ```

6. Use a description such as:

   ```text
   Personal Spotify playback data for SimHub
   ```

7. Add this exact redirect URI:

   ```text
   http://127.0.0.1:9877/callback
   ```

8. Select **Web API** when asked which API or SDK the app will use.
9. Accept Spotify's terms and create the app.
10. Open the application's settings.
11. Copy the **Client ID**.

Do not select or copy **View client secret**. SpotifySimHub does not need it.

### Redirect URI checklist

The redirect URI must:

- start with `http://127.0.0.1`;
- use port `9877`;
- end with `/callback`;
- match the value above exactly;
- not use `localhost`.

An incorrect redirect URI prevents Spotify from returning control to the plugin after login.

## Step 2: Get the source

Clone the repository or download its source archive. Open PowerShell in the repository root.

The expected structure is:

```text
SpotifySimHub\
├── README.md
├── INSTALLATION.md
└── SpotifySimHub\
    ├── SpotifySimHub.csproj
    ├── SpotifySimHub.sln
    └── SpotifyClientId.example.props
```

## Step 3: Create the private local configuration

Copy the example file:

```powershell
Copy-Item `
  .\SpotifySimHub\SpotifyClientId.example.props `
  .\SpotifySimHub\SpotifyClientId.local.props
```

Open `SpotifyClientId.local.props` in a text editor.

Replace:

```xml
<SpotifyClientId>YOUR_SPOTIFY_CLIENT_ID</SpotifyClientId>
```

with the Client ID copied from your Spotify application.

Set `SimHubInstallPath` to the directory containing `SimHub.Plugins.dll`. A typical installation uses:

```xml
<SimHubInstallPath>C:\Program Files (x86)\SimHub\</SimHubInstallPath>
```

Keep the trailing directory separator.

`SpotifyClientId.local.props` is ignored by Git. Do not commit it, upload it to an issue, or include it in a screenshot.

## Step 4: Restore dependencies

```powershell
nuget restore .\SpotifySimHub\SpotifySimHub.sln
```

This restores the plugin-owned Newtonsoft.Json dependency. SimHub's own assemblies are resolved from `SimHubInstallPath`.

## Step 5: Build

Release build:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  ".\SpotifySimHub\SpotifySimHub.csproj" `
  /t:Rebuild `
  /p:Configuration=Release `
  /p:Platform=AnyCPU `
  /m
```

Expected result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Output directory:

```text
SpotifySimHub\bin\Release\
```

The required plugin-owned files are:

```text
SpotifySimHub.dll
Newtonsoft.Json.dll
```

Do not deploy `GameReaderCommon.dll`, `SimHub.Logging.dll`, `SimHub.Plugins.dll`, or `log4net.dll` from a build directory. SimHub owns those assemblies.

## Step 6: Install into SimHub

1. Close SimHub.
2. Back up an older `SpotifySimHub.dll` if one is already installed.
3. Copy `SpotifySimHub.dll` to the SimHub installation directory.
4. Copy the adjacent plugin-owned `Newtonsoft.Json.dll` if the required version is not already installed for the plugin.
5. Start SimHub.
6. Open **Additional plugins**.
7. Select **SpotifySimHub**.

The project does not deploy automatically during build.

## Step 7: Connect the Spotify account

1. Press **Connect** in SpotifySimHub.
2. A browser opens Spotify's authorization page.
3. Confirm that the page names the application you created.
4. Sign in with the same Spotify account used for playback.
5. Approve the requested read-only playback access.
6. Spotify redirects the browser to the local callback.
7. Return to SimHub and confirm that the status is `Connected`.

The plugin stores the refresh token under:

```text
%LOCALAPPDATA%\SpotifySimHub
```

The token is protected with Windows DPAPI for the current Windows user.

## Step 8: Verify remote-device playback

To verify the use case that local media detection cannot cover:

1. Sign in to Spotify on the PlayStation 5 or another Spotify Connect device using the same Spotify account.
2. Start playing a track on that device.
3. Keep SimHub and SpotifySimHub running on the Windows computer.
4. Press **Refresh status**, or wait for the normal polling interval.
5. Confirm that artist, track, album, and cover art update in SimHub.

Spotify's currently-playing endpoint reads the item playing on the user's Spotify account and can report the active Spotify Connect playback state. Some restricted device models or private-session states may expose less information.

## Sharing a Development Mode application

You normally do not need this section for a personal build.

If you intentionally share one Client ID with a small number of testers:

1. open the application in the Spotify Developer Dashboard;
2. open **Settings**;
3. open **Users Management**;
4. add each tester's name and Spotify account email;
5. keep within Spotify's current Development Mode user limit.

A user may complete the login page without being allowlisted, but subsequent API calls can return HTTP `403`.

## Troubleshooting

### Build says the local configuration is missing

Confirm that this file exists:

```text
SpotifySimHub\SpotifyClientId.local.props
```

Confirm that the placeholder was replaced and that `SimHubInstallPath` points to the real SimHub directory.

### Spotify reports an invalid client

Confirm that the value came from **Client ID**, not Client Secret, and that it belongs to the application you created.

### Spotify reports an invalid redirect URI

Compare the Developer Dashboard value character-for-character with:

```text
http://127.0.0.1:9877/callback
```

Do not substitute `localhost`.

### Login succeeds but SimHub reports Login required

The token may have been revoked, expired, or rejected. Press Disconnect, then Connect, and authorize again.

### Login succeeds but playback requests return HTTP 403

Common causes include:

- the authenticated user is not allowed to use the selected Development Mode application;
- the application owner no longer has the required Spotify Premium subscription;
- Spotify has changed or restricted access for that application.

For a personal build, verify that the application and playback account belong to the same Spotify account.

### Nothing is playing

Start playback on the target device and press **Refresh status**. Spotify can return no content when there is no active playback state.

### PlayStation 5 playback is not shown

- Confirm that the PS5 uses the same Spotify account authorized in SpotifySimHub.
- Confirm that Spotify shows the PS5 as the active playback device.
- Disable Spotify private session while testing.
- Try Refresh status after playback has started.
- Reconnect the plugin if the saved authorization is no longer valid.

## Updating the plugin

Keep `SpotifyClientId.local.props` when updating source. It is intentionally untracked and should survive normal Git pulls.

Rebuild Release, close SimHub, back up the previous plugin DLL, and replace the plugin-owned files.

## Uninstall

1. Press Disconnect to remove the saved refresh token.
2. Close SimHub.
3. Remove `SpotifySimHub.dll` and its plugin-owned dependency from the SimHub directory.
4. Optionally remove:

   ```text
   %LOCALAPPDATA%\SpotifySimHub
   ```

Do not remove shared SimHub assemblies.

## Usage policy

SpotifySimHub is intended for personal, non-commercial dashboard use. Review Spotify's current Developer Terms and Web API policy before distributing, broadcasting, or commercializing an integration.
