[CmdletBinding()]
param(
    [string]$Version,
    [string]$MSBuildPath,
    [string]$SimHubDirectory = "C:\Program Files (x86)\SimHub",
    [string]$DashboardSource,
    [switch]$SkipInstaller,
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$spotifyProject = Join-Path $repoRoot "SpotifySimHub\SpotifySimHub.csproj"
$gripProject = Join-Path $repoRoot "GothiaGripPlugin\GothiaGripPlugin.csproj"
$gripTests = Join-Path $repoRoot "GothiaGripPlugin\Tests\GothiaGripDetectorTests.csproj"
$assemblyInfoPath = Join-Path $repoRoot "SpotifySimHub\Properties\AssemblyInfo.cs"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$packageRoot = Join-Path $artifactsRoot "package"
$distDirectory = Join-Path $repoRoot "dist"
$dashboardName = "Gothia Racing Performance"

if ([string]::IsNullOrWhiteSpace($DashboardSource))
{
    $DashboardSource =
        Join-Path $SimHubDirectory "DashTemplates\$dashboardName"
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $resolvedParent =
        [System.IO.Path]::GetFullPath($Parent).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild = [System.IO.Path]::GetFullPath($Child)

    if (!$resolvedChild.StartsWith(
            $resolvedParent + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to modify a path outside the packaging directory."
    }
}

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Path
    )

    Assert-ChildPath -Parent $Parent -Child $Path
    if (Test-Path -LiteralPath $Path)
    {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Find-MSBuild {
    if (![string]::IsNullOrWhiteSpace($MSBuildPath))
    {
        return $MSBuildPath
    }

    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates)
    {
        if (Test-Path -LiteralPath $candidate)
        {
            return $candidate
        }
    }

    $command = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue
    if ($command)
    {
        return $command.Source
    }

    throw "MSBuild could not be found."
}

function Find-InnoCompiler {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command)
    {
        return $command.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 7\ISCC.exe"
    )

    foreach ($candidate in $candidates)
    {
        if (Test-Path -LiteralPath $candidate)
        {
            return $candidate
        }
    }

    return $null
}

function Invoke-Build {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [Parameter(Mandatory = $true)][string]$MSBuild,
        [string[]]$ExtraArguments = @()
    )

    $arguments = @(
        $Project,
        "/t:Rebuild",
        "/p:Configuration=Release",
        "/p:Platform=AnyCPU",
        "/p:PostBuildEvent=",
        "/m",
        "/nodeReuse:false",
        "/nologo",
        "/verbosity:minimal",
        "/clp:Summary"
    ) + $ExtraArguments

    & $MSBuild @arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "The Release build failed for $Project."
    }
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw
    $versionMatch = [regex]::Match(
        $assemblyInfo,
        'AssemblyFileVersion\("(\d+)\.(\d+)\.(\d+)\.\d+"\)')
    if (!$versionMatch.Success)
    {
        throw "AssemblyFileVersion could not be read."
    }
    $Version =
        $versionMatch.Groups[1].Value + "." +
        $versionMatch.Groups[2].Value + "." +
        $versionMatch.Groups[3].Value
}

if ($Version -notmatch '^\d+\.\d+\.\d+$')
{
    throw "Version must use major.minor.patch format."
}

if (!(Test-Path -LiteralPath $DashboardSource -PathType Container))
{
    throw "The dashboard source folder was not found: $DashboardSource"
}

$simHubPlugins = Join-Path $SimHubDirectory "SimHub.Plugins.dll"
if (!(Test-Path -LiteralPath $simHubPlugins -PathType Leaf))
{
    throw "The SimHub folder does not contain SimHub.Plugins.dll."
}

foreach ($textFile in @(
        (Join-Path $repoRoot "README.md"),
        (Join-Path $repoRoot "packaging\COMBO-INSTALL.txt")))
{
    $text = Get-Content -LiteralPath $textFile -Raw
    if ($text.Contains([char]0x2014))
    {
        throw "$textFile must not contain em dash characters."
    }
}

$msbuild = Find-MSBuild
$simHubBuildPath = $SimHubDirectory.TrimEnd('\') + '/'

Invoke-Build `
    -Project $spotifyProject `
    -MSBuild $msbuild `
    -ExtraArguments @(
        "/p:EmbedSpotifyClientId=false",
        "/p:SimHubInstallPath=$simHubBuildPath")

Invoke-Build `
    -Project $gripProject `
    -MSBuild $msbuild `
    -ExtraArguments @("/p:SIMHUB_INSTALL_PATH=$simHubBuildPath")

Invoke-Build -Project $gripTests -MSBuild $msbuild

$testExecutable =
    Join-Path $repoRoot "GothiaGripPlugin\Tests\bin\GothiaGripDetectorTests.exe"
if (!(Test-Path -LiteralPath $testExecutable -PathType Leaf))
{
    throw "The Gothia Grip detector test executable is missing."
}

& $testExecutable
if ($LASTEXITCODE -ne 0)
{
    throw "The Gothia Grip detector tests failed."
}

$spotifyRelease = Join-Path $repoRoot "SpotifySimHub\bin\Release"
$gripRelease = Join-Path $repoRoot "GothiaGripPlugin\bin\Release"
$pluginFiles = @{
    "SpotifySimHub.dll" = Join-Path $spotifyRelease "SpotifySimHub.dll"
    "Newtonsoft.Json.dll" = Join-Path $spotifyRelease "Newtonsoft.Json.dll"
    "GothiaGripPlugin.dll" = Join-Path $gripRelease "GothiaGripPlugin.dll"
}

foreach ($pluginFile in $pluginFiles.Values)
{
    if (!(Test-Path -LiteralPath $pluginFile -PathType Leaf))
    {
        throw "A required plugin file is missing: $pluginFile"
    }
}

$generatedConfiguration =
    Join-Path $repoRoot "SpotifySimHub\obj\Release\SpotifyBuildConfiguration.g.cs"
$generatedSource = Get-Content -LiteralPath $generatedConfiguration -Raw
$clientIdMatch = [regex]::Match(
    $generatedSource,
    'ClientId\s*=\s*"([^"]*)";')
if (!$clientIdMatch.Success -or $clientIdMatch.Groups[1].Value.Length -ne 0)
{
    throw "Release packaging requires a neutral Spotify Client ID."
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distDirectory -Force | Out-Null

$packageName = "Gothia-Racing-Performance-Combo-$Version"
$stagingDirectory = Join-Path $packageRoot $packageName
Reset-Directory -Parent $packageRoot -Path $stagingDirectory

$simHubStage = Join-Path $stagingDirectory "SimHub"
$dashboardStage =
    Join-Path $simHubStage "DashTemplates\$dashboardName"
New-Item -ItemType Directory -Path $dashboardStage -Force | Out-Null

foreach ($entry in $pluginFiles.GetEnumerator())
{
    Copy-Item -LiteralPath $entry.Value -Destination (
        Join-Path $simHubStage $entry.Key)
}

$dashboardFiles = @(
    "$dashboardName.djson",
    "$dashboardName.djson.00.png",
    "$dashboardName.djson.carclasses",
    "$dashboardName.djson.metadata",
    "$dashboardName.djson.png",
    "$dashboardName.djson.ressources"
)

foreach ($dashboardFile in $dashboardFiles)
{
    $sourcePath = Join-Path $DashboardSource $dashboardFile
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf))
    {
        throw "A required dashboard file is missing: $sourcePath"
    }
    Copy-Item -LiteralPath $sourcePath -Destination $dashboardStage
}

$dashboardJsonPath = Join-Path $dashboardStage "$dashboardName.djson"
Add-Type -Path $pluginFiles["Newtonsoft.Json.dll"]
$dashboardJson = [Newtonsoft.Json.Linq.JObject]::Parse(
    [System.IO.File]::ReadAllText(
        $dashboardJsonPath,
        [System.Text.Encoding]::UTF8))

$manufacturerObjects = @(
    $dashboardJson.Descendants() |
        Where-Object {
            $_ -is [Newtonsoft.Json.Linq.JObject] -and
            (([string]$_["Name"]) -match '(?i)volvo' -or
             ([string]$_["Image"]) -match '(?i)volvo')
        }
)

foreach ($manufacturerObject in $manufacturerObjects)
{
    $manufacturerObject.Remove()
}

$imageLibrarySource = Join-Path $SimHubDirectory "ImageLibrary"
$resolvedImageLibrarySource =
    [System.IO.Path]::GetFullPath($imageLibrarySource).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)

$libraryProperties = @(
    $dashboardJson.Descendants() |
        Where-Object {
            $_ -is [Newtonsoft.Json.Linq.JProperty] -and
            $_.Value.Type -eq [Newtonsoft.Json.Linq.JTokenType]::String -and
            ([string]$_.Value).StartsWith(
                "library:",
                [System.StringComparison]::OrdinalIgnoreCase)
        }
)

foreach ($libraryProperty in $libraryProperties)
{
    $relativeSource = ([string]$libraryProperty.Value).Substring(8)
    $resolvedSource = [System.IO.Path]::GetFullPath(
        (Join-Path $imageLibrarySource $relativeSource))

    if (!$resolvedSource.StartsWith(
            $resolvedImageLibrarySource +
            [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "An unsafe image-library path was found in the dashboard."
    }

    if (!(Test-Path -LiteralPath $resolvedSource -PathType Leaf))
    {
        throw "A dashboard image-library file is missing: $resolvedSource"
    }

    $relativeDestination =
        Join-Path "GothiaRacingPerformance" $relativeSource
    $destinationPath = Join-Path (
        (Join-Path $simHubStage "ImageLibrary")) $relativeDestination
    New-Item -ItemType Directory -Path (
        Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $resolvedSource -Destination $destinationPath

    $libraryProperty.Value = [Newtonsoft.Json.Linq.JValue]::new(
        "library:" + $relativeDestination)
}

[System.IO.File]::WriteAllText(
    $dashboardJsonPath,
    $dashboardJson.ToString([Newtonsoft.Json.Formatting]::None),
    [System.Text.UTF8Encoding]::new($false))

$stagedDashboardText =
    [System.IO.File]::ReadAllText($dashboardJsonPath)
if ($stagedDashboardText -match '(?i)volvo|Spread_Word')
{
    throw "The staged dashboard still contains a manufacturer logo reference."
}

if ($stagedDashboardText -match 'library:Icons\\')
{
    throw "A dashboard image reference was not isolated for the combo package."
}

$validatedDashboard = [Newtonsoft.Json.Linq.JObject]::Parse(
    $stagedDashboardText)
if ([int]$validatedDashboard["BaseWidth"] -ne 1480 -or
    [int]$validatedDashboard["BaseHeight"] -ne 720)
{
    throw "The staged dashboard dimensions are unexpected."
}

$installTemplate = Get-Content -LiteralPath (
    Join-Path $repoRoot "packaging\COMBO-INSTALL.txt") -Raw
$installText = $installTemplate.Replace("{VERSION}", $Version)
[System.IO.File]::WriteAllText(
    (Join-Path $stagingDirectory "COMBO-INSTALL.txt"),
    $installText,
    [System.Text.UTF8Encoding]::new($false))

Copy-Item -LiteralPath (
    Join-Path $repoRoot "packaging\THIRD-PARTY-NOTICES.txt") `
    -Destination $stagingDirectory

$forbiddenNames = @(
    "GameReaderCommon.dll",
    "SimHub.Logging.dll",
    "SimHub.Plugins.dll",
    "log4net.dll"
)
foreach ($forbiddenName in $forbiddenNames)
{
    if (Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File |
        Where-Object Name -eq $forbiddenName)
    {
        throw "A SimHub-owned assembly was staged: $forbiddenName"
    }
}

$manufacturerFiles = @(
    Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File |
        Where-Object { $_.Name -match '(?i)volvo|Spread_Word' }
)
if ($manufacturerFiles.Count -ne 0)
{
    throw "A manufacturer logo file was staged."
}

$zipPath = Join-Path $distDirectory "$packageName-win.zip"
if (Test-Path -LiteralPath $zipPath)
{
    Assert-ChildPath -Parent $distDirectory -Child $zipPath
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive `
    -Path (Join-Path $stagingDirectory "*") `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

$zipHash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
[System.IO.File]::WriteAllText(
    ($zipPath + ".sha256"),
    $zipHash.Hash.ToLowerInvariant() + "  " +
    [System.IO.Path]::GetFileName($zipPath) +
    [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$installerPath = $null
if (!$SkipInstaller)
{
    $innoCompiler = Find-InnoCompiler
    if ($innoCompiler)
    {
        $installerScript = Join-Path $repoRoot (
            "packaging\GothiaRacingPerformanceCombo.iss")
        & $innoCompiler `
            ("/DAppVersion=" + $Version) `
            ("/DSourceDir=" + $stagingDirectory) `
            ("/DOutputDir=" + $distDirectory) `
            $installerScript

        if ($LASTEXITCODE -ne 0)
        {
            throw "The combo installer build failed."
        }

        $installerPath = Join-Path $distDirectory (
            "$packageName-Setup.exe")
        if (!(Test-Path -LiteralPath $installerPath -PathType Leaf))
        {
            throw "The combo installer output is missing."
        }

        $installerHash =
            Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
        [System.IO.File]::WriteAllText(
            ($installerPath + ".sha256"),
            $installerHash.Hash.ToLowerInvariant() + "  " +
            [System.IO.Path]::GetFileName($installerPath) +
            [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))
    }
    elseif ($RequireInstaller)
    {
        throw "Inno Setup ISCC.exe could not be found."
    }
    else
    {
        Write-Warning "Inno Setup was not found. Only the ZIP was created."
    }
}

Write-Output ("Dashboard source: " + $DashboardSource)
Write-Output ("Removed manufacturer objects: " + $manufacturerObjects.Count)
Write-Output ("Packaged image-library files: " + $libraryProperties.Count)
Write-Output ("ZIP: " + $zipPath)
if ($installerPath)
{
    Write-Output ("Installer: " + $installerPath)
}
