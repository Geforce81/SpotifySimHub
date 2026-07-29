# SpotifySimHub installation guide

This guide creates a personal SpotifySimHub build that can read the current playback state from your Spotify account, including playback on supported Spotify Connect devices such as a PlayStation 5.

## Why this setup is necessary

SpotifySimHub reads the Spotify Web API rather than the Windows audio session. This is intentional: local audio detection cannot reliably see music playing on another device.

To connect Spotify you need:

- your normal Spotify account;
- a small personal app created in Spotify for Developers;
- the Client ID shown on that app's Settings page.

New applications run in Spotify Development Mode. Under Spotify's current rules, the app owner must have Spotify Premium and each app is intended for personal use or a small allowlist. Creating a personal application avoids depending on another developer's allowlist.

Official references:

- [Spotify Web API](https://developer.spotify.com/documentation/web-api)
- [Authorization Code with PKCE](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow)
- [Refreshing tokens](https://developer.spotify.com/documentation/web-api/tutorials/refreshing-tokens)
- [Rate limits](https://developer.spotify.com/documentation/web-api/concepts/rate-limits)
- [Development and extended quota modes](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
- [Redirect URI requirements](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)

## Before you begin

You need:

- Windows;
- SimHub 9.x installed;
- a Spotify Premium account;
- access to the Spotify Developer Dashboard;
- Visual Studio or Build Tools with .NET Framework 4.8 MSBuild only when building from source;
- NuGet command-line restore support only when building from source.

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

7. Leave **Website** blank if Spotify marks it as optional.
8. Add this exact redirect URI:

   ```text
   http://127.0.0.1:9877/callback
   ```

9. Select **Web API** when asked which API or SDK the app will use.
10. Accept Spotify's terms and create the app.
11. Open the application's settings.
12. Copy the value labelled **Client ID**.

The Client ID remains the same, so this setup only needs to be completed once.

### Redirect URI checklist

The redirect URI must:

- start with `http://127.0.0.1`;
- use port `9877`;
- end with `/callback`;
- match the value above exactly;
- not use `localhost`.

An incorrect redirect URI prevents Spotify from returning control to the plugin after login.

## Step 2: Install SpotifySimHub

Choose either release format:

### EXE installer

1. Download `SpotifySimHub-<version>-Setup.exe`.
2. Close SimHub.
3. Run the installer.
4. Select the folder containing `SimHubWPF.exe` if SimHub is not in its default location.
5. Finish the installation and start SimHub.

The installer supports upgrades and uninstall. A locally generated installer is not Authenticode-signed, so Windows may show an unknown-publisher warning until the project has a trusted code-signing certificate.

### ZIP package

1. Download `SpotifySimHub-<version>-win.zip`.
2. Close SimHub.
3. Open the SimHub installation folder containing `SimHubWPF.exe`.
4. Extract all files from the ZIP directly into that folder.
5. Do not create an extra SpotifySimHub subfolder.
6. Start SimHub and open **Additional plugins**.

No verified release archive has been published yet. Until manual release verification is complete, build the current source as described below.

### Build the current source

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

## Step 3: Configure the local build

Copy the example file:

```powershell
Copy-Item `
  .\SpotifySimHub\SpotifyClientId.example.props `
  .\SpotifySimHub\SpotifyClientId.local.props
```

Open `SpotifyClientId.local.props` in a text editor.

Set `SimHubInstallPath` to the directory containing `SimHub.Plugins.dll`. A typical installation uses:

```xml
<SimHubInstallPath>C:\Program Files (x86)\SimHub\</SimHubInstallPath>
```

Keep the trailing directory separator.

Leave `SpotifyClientId` set to its placeholder to build a neutral DLL. Developers may configure it as a local Debug fallback, but Release builds are neutral by default and normal users enter their Client ID in SimHub after installation.

`SpotifyClientId.local.props` is ignored by Git. Do not commit a configured value, upload it to an issue, or include it in a screenshot.

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

The required plugin-owned file is:

```text
SpotifySimHub.dll
```

Do not deploy `GameReaderCommon.dll`, `SimHub.Logging.dll`, `SimHub.Plugins.dll`, or `log4net.dll` from a build directory. SimHub owns those assemblies.

Newtonsoft.Json is copied beside `SpotifySimHub.dll` as a plugin-owned runtime dependency. SimHub reflects over plugin types before constructing the plugin, so the dependency must be available at assembly-load time.

## Step 6: Install into SimHub

1. Close SimHub.
2. Back up an older `SpotifySimHub.dll` if one is already installed.
3. Copy `SpotifySimHub.dll` and `Newtonsoft.Json.dll` to the SimHub installation directory.
4. Start SimHub.
5. Open **Additional plugins**.
6. Select **SpotifySimHub**.

The project does not deploy automatically during build.

## Step 7: Connect the Spotify account

1. Select **Open setup guide** if the Spotify application has not been created yet.
2. Paste the Client ID into the masked field in step 3 of the guide.
3. Press **Save Client ID**.
4. Close the guide and press **Connect**.
5. A browser opens Spotify's authorization page.
6. Confirm that the page names the application you created.
7. Sign in with the same Spotify account used for playback.
8. Approve the requested read-only playback access.
9. Spotify redirects the browser to the local callback.
10. Return to SimHub and confirm that the status is `Connected`.

The connection attempt waits for at most two minutes. If the browser is closed or you decide not to continue, select **Cancel connection** in SimHub.

Your connection is saved locally. If Spotify asks you to connect again later, simply approve the browser prompt.

The separate Client ID field on the SpotifySimHub settings page remains available for manual setup and later changes.

## Step 8: Verify remote-device playback

To verify the use case that local media detection cannot cover:

1. Sign in to Spotify on the PlayStation 5 or another Spotify Connect device using the same Spotify account.
2. Start playing a track on that device.
3. Keep SimHub and SpotifySimHub running on the Windows computer.
4. Press **Refresh status**, or wait for the normal polling interval.
5. Confirm that artist, track, album, and cover art update in SimHub.

Spotify's currently-playing endpoint reads the item playing on the user's Spotify account and can report the active Spotify Connect playback state. Some restricted device models or private-session states may expose less information.

### Dash Studio properties

The following properties are available in Dash Studio after the plugin loads:

| Property | Value |
| --- | --- |
| `[SpotifyPlugin.Spotify.Album]` | Album name |
| `[SpotifyPlugin.Spotify.Artist]` | Artist name |
| `[SpotifyPlugin.Spotify.Cover]` | Local path to the cached JPG cover image |
| `[SpotifyPlugin.Spotify.CoverDash]` | Changing JPG path for Dash Studio on a computer, phone, or tablet |
| `[SpotifyPlugin.Spotify.CoverImage]` | `BitmapImage` for local WPF image controls |
| `[SpotifyPlugin.Spotify.CurrentTrack]` | `Artist - Track` |
| `[SpotifyPlugin.Spotify.Track]` | Track name only |

#### Add text

1. Edit the dashboard and add a **Text** component.
2. Open the `fx` binding for **Text**.
3. Select **Formula** and use `[SpotifyPlugin.Spotify.CurrentTrack]`.
4. Use `[SpotifyPlugin.Spotify.Artist]`, `[SpotifyPlugin.Spotify.Track]`, or `[SpotifyPlugin.Spotify.Album]` for separate fields.

#### Add cover art

1. Add an **Image from file** component.
2. Open the `fx` binding for **Image Path**.
3. Select **Formula** and use `[SpotifyPlugin.Spotify.CoverDash]`.
4. Save the dashboard and open it on the computer or mobile device.

Use `[SpotifyPlugin.Spotify.CoverDash]` for Dash Studio. `[SpotifyPlugin.Spotify.Cover]` is the stable local JPG path for other PC integrations, while `[SpotifyPlugin.Spotify.CoverImage]` is intended for local WPF controls.

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

Confirm that `SimHubInstallPath` points to the real SimHub directory. The Client ID placeholder may remain unchanged.

### Spotify reports an invalid client

Confirm that the value saved on the SpotifySimHub settings page came from **Client ID**, not Client Secret, and that it belongs to the application you created.

### Spotify reports an invalid redirect URI

Compare the Developer Dashboard value character-for-character with:

```text
http://127.0.0.1:9877/callback
```

Do not substitute `localhost`.

### A browser opens after months of normal use

This is expected after Spotify expires the six-month refresh token. Approve the login prompt. The browser is opened automatically only after the token endpoint explicitly returns `invalid_grant`, not after an ordinary network error.

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

Keep `SpotifyClientId.local.props` when updating source if it contains the local SimHub build path. It is intentionally untracked and should survive normal Git pulls. A Client ID entered in SimHub remains in the local SimHub settings.

Rebuild Release, close SimHub, back up the previous plugin DLL, and replace it.

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
