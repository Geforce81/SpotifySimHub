#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\package\SpotifySimHub-" + AppVersion
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "SpotifySimHub"
#define AppPublisher "Gustavius"
#define AppExeName "SimHubWPF.exe"

[Setup]
AppId={{6E44BF4B-5F9B-4DDD-91F6-AFEE6802D07E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={code:GetDefaultSimHubDirectory}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDir}
OutputBaseFilename=SpotifySimHub-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86compatible x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName={#AppName}
UninstallFilesDir={commonappdata}\SpotifySimHub\Installer
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Spotify playback data plugin for SimHub
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#SourceDir}\SpotifySimHub.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#SourceDir}\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "{#SourceDir}\INSTALL.txt"; DestDir: "{commonappdata}\SpotifySimHub\Documentation"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{commonappdata}\SpotifySimHub\Documentation"; Flags: ignoreversion

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch SimHub"; Flags: postinstall nowait skipifsilent unchecked; Check: SimHubExecutableExists

[Code]
function GetDefaultSimHubDirectory(Param: String): String;
var
  Candidate: String;
begin
  Candidate := ExpandConstant('{pf32}\SimHub');
  if FileExists(AddBackslash(Candidate) + '{#AppExeName}') then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := ExpandConstant('{pf}\SimHub');
  if FileExists(AddBackslash(Candidate) + '{#AppExeName}') then
  begin
    Result := Candidate;
    Exit;
  end;

  Result := ExpandConstant('{pf32}\SimHub');
end;

function SimHubExecutableExists(): Boolean;
begin
  Result :=
    FileExists(
      AddBackslash(ExpandConstant('{app}')) +
      '{#AppExeName}');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = wpSelectDir then
  begin
    if not SimHubExecutableExists() then
    begin
      MsgBox(
        'Select the SimHub installation folder that contains ' +
        '{#AppExeName}.',
        mbError,
        MB_OK);
      Result := False;
    end;
  end;
end;
