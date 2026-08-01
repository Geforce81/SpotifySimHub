#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\package\Gothia-Racing-Performance-Combo-" + AppVersion
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Gothia Racing Performance Combo"
#define AppPublisher "Gustavius"
#define AppExeName "SimHubWPF.exe"
#define DashboardName "Gothia Racing Performance"

[Setup]
AppId={{B337D249-AE4D-4D60-B880-C6250B8C4F6A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={code:GetDefaultSimHubDirectory}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir={#OutputDir}
#ifdef TestMode
OutputBaseFilename=Gothia-Racing-Performance-Combo-{#AppVersion}-InternalTest
#else
OutputBaseFilename=Gothia-Racing-Performance-Combo-{#AppVersion}-Setup
#endif
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86compatible x64compatible
MinVersion=10.0
CloseApplications=yes
CloseApplicationsFilter=SpotifySimHub.dll,GothiaGripPlugin.dll
RestartApplications=no
RestartIfNeededByRun=no
Uninstallable=yes
UninstallDisplayName={#AppName}
UninstallFilesDir={commonappdata}\GothiaRacingPerformanceCombo\Installer
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=SimHub plugins and Gothia Racing Performance dashboard
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Files]
Source: "{#SourceDir}\SimHub\SpotifySimHub.dll"; DestDir: "{app}"; Flags: ignoreversion uninsneveruninstall
Source: "{#SourceDir}\SimHub\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion uninsneveruninstall
Source: "{#SourceDir}\SimHub\GothiaGripPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\SimHub\DashTemplates\{#DashboardName}\*"; DestDir: "{app}\DashTemplates\{#DashboardName}"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
Source: "{#SourceDir}\SimHub\ImageLibrary\GothiaRacingPerformance\*"; DestDir: "{app}\ImageLibrary\GothiaRacingPerformance"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
Source: "{#SourceDir}\COMBO-INSTALL.txt"; DestDir: "{commonappdata}\GothiaRacingPerformanceCombo\Documentation"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{commonappdata}\GothiaRacingPerformanceCombo\Documentation"; Flags: ignoreversion

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

function GetSimHubProcessCount(): Integer;
var
  Locator: Variant;
  Services: Variant;
  Processes: Variant;
begin
#ifdef TestMode
  Result := 0;
  Exit;
#endif

  Result := -1;

  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery(
      'SELECT ProcessId FROM Win32_Process WHERE Name="' +
      '{#AppExeName}"');
    Result := Processes.Count;
  except
    Log('Could not query the SimHub process state.');
  end;
end;

function WaitForSimHubToClose(TimeoutMilliseconds: Integer): Boolean;
var
  ElapsedMilliseconds: Integer;
  ProcessCount: Integer;
begin
  Result := False;
  ElapsedMilliseconds := 0;

  while ElapsedMilliseconds <= TimeoutMilliseconds do
  begin
    ProcessCount := GetSimHubProcessCount();

    if ProcessCount = 0 then
    begin
      Result := True;
      Exit;
    end;

    if ProcessCount < 0 then
      Exit;

    Sleep(250);
    ElapsedMilliseconds := ElapsedMilliseconds + 250;
  end;
end;

function StopSimHub(ForceClose: Boolean): Boolean;
var
  Parameters: String;
  ResultCode: Integer;
begin
  Parameters := '/IM {#AppExeName} /T';
  if ForceClose then
    Parameters := Parameters + ' /F';

  if not Exec(
      ExpandConstant('{sys}\taskkill.exe'),
      Parameters,
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
  begin
    Log('Could not start taskkill.exe. Error code: ' + IntToStr(ResultCode));
  end
  else
  begin
    Log('taskkill.exe finished with exit code: ' + IntToStr(ResultCode));
  end;

  Result := WaitForSimHubToClose(10000);
end;

procedure RegisterExtraCloseApplicationsResources;
var
  SimHubExecutable: String;
begin
  SimHubExecutable :=
    AddBackslash(ExpandConstant('{app}')) +
    '{#AppExeName}';

#if Ver >= EncodeVer(7, 0, 0)
  RegisterExtraCloseApplicationsResource(SimHubExecutable);
#else
  RegisterExtraCloseApplicationsResource(False, SimHubExecutable);
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProcessCount: Integer;
begin
  Result := '';
  NeedsRestart := False;
  ProcessCount := GetSimHubProcessCount();

  if ProcessCount < 0 then
  begin
    Log('Falling back to Inno Setup application closing.');
    Exit;
  end;

  if ProcessCount = 0 then
    Exit;

  if MsgBox(
      'SimHub is running.' + #13#10 + #13#10 +
      'Save any dashboard changes before continuing. ' +
      'Setup will now close SimHub.',
      mbConfirmation,
      MB_OKCANCEL) <> IDOK then
  begin
    Result :=
      'Installation was paused. Close SimHub completely ' +
      'before continuing.';
    Exit;
  end;

  if StopSimHub(False) then
    Exit;

  if MsgBox(
      'SimHub did not close normally.' + #13#10 + #13#10 +
      'Do you want Setup to force it to close? ' +
      'Unsaved changes in SimHub may be lost.',
      mbConfirmation,
      MB_YESNO) = IDYES then
  begin
    if StopSimHub(True) then
      Exit;
  end;

  Result :=
    'SimHub is still running. Exit SimHub from the system tray ' +
    'or Task Manager, then click Install again. ' +
    'A Windows restart is not required.';
end;

procedure BackupDashboard;
var
  SourceDirectory: String;
  BackupDirectory: String;
  SourcePath: String;
  FileNames: array[0..5] of String;
  Index: Integer;
begin
  SourceDirectory :=
    AddBackslash(ExpandConstant('{app}\DashTemplates\{#DashboardName}'));

  if not FileExists(SourceDirectory + '{#DashboardName}.djson') then
    Exit;

  BackupDirectory :=
    SourceDirectory + '_Backups\ComboSetup-' +
    GetDateTimeString('yyyymmdd-hhnnss', '-', ':') + '\';

  if not ForceDirectories(BackupDirectory) then
    RaiseException('Could not create the dashboard backup folder.');

  FileNames[0] := '{#DashboardName}.djson';
  FileNames[1] := '{#DashboardName}.djson.00.png';
  FileNames[2] := '{#DashboardName}.djson.carclasses';
  FileNames[3] := '{#DashboardName}.djson.metadata';
  FileNames[4] := '{#DashboardName}.djson.png';
  FileNames[5] := '{#DashboardName}.djson.ressources';

  for Index := 0 to 5 do
  begin
    SourcePath := SourceDirectory + FileNames[Index];
    if FileExists(SourcePath) then
    begin
      if not CopyFile(SourcePath, BackupDirectory + FileNames[Index], False) then
        RaiseException('Could not back up ' + FileNames[Index] + '.');
    end;
  end;

  Log('Dashboard backup created at ' + BackupDirectory);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    BackupDashboard;
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
