[CmdletBinding()]
param(
    [string]$Version,
    [string]$MSBuildPath,
    [switch]$SkipInstaller,
    [switch]$RequireInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$projectDirectory = Join-Path $repoRoot "SpotifySimHub"
$projectPath = Join-Path $projectDirectory "SpotifySimHub.csproj"
$assemblyInfoPath =
    Join-Path $projectDirectory "Properties\AssemblyInfo.cs"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$packageRoot = Join-Path $artifactsRoot "package"
$distDirectory = Join-Path $repoRoot "dist"

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Parent,
        [Parameter(Mandatory = $true)]
        [string]$Child
    )

    $resolvedParent =
        [System.IO.Path]::GetFullPath($Parent)
    $resolvedParent =
        $resolvedParent.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedChild =
        [System.IO.Path]::GetFullPath($Child)

    if (!$resolvedChild.StartsWith(
            $resolvedParent +
            [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refusing to modify a path outside the packaging directory."
    }
}

function Reset-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Parent,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-ChildPath -Parent $Parent -Child $Path

    if (Test-Path -LiteralPath $Path)
    {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force |
        Out-Null
}

function Find-MSBuild {
    if (![string]::IsNullOrWhiteSpace($MSBuildPath))
    {
        return $MSBuildPath
    }

    $candidates =
        @(
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

    $command =
        Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue

    if ($command)
    {
        return $command.Source
    }

    throw "MSBuild could not be found."
}

function Find-InnoCompiler {
    $command =
        Get-Command "ISCC.exe" -ErrorAction SilentlyContinue

    if ($command)
    {
        return $command.Source
    }

    $candidates =
        @(
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

if ([string]::IsNullOrWhiteSpace($Version))
{
    $assemblyInfo =
        Get-Content -LiteralPath $assemblyInfoPath -Raw
    $versionMatch =
        [regex]::Match(
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

$assemblyInfo =
    Get-Content -LiteralPath $assemblyInfoPath -Raw
$assemblyVersionMatch =
    [regex]::Match(
        $assemblyInfo,
        'AssemblyFileVersion\("(\d+\.\d+\.\d+)\.\d+"\)')

if (!$assemblyVersionMatch.Success -or
    $assemblyVersionMatch.Groups[1].Value -ne $Version)
{
    throw "The package version must match AssemblyFileVersion."
}

$readmePath = Join-Path $repoRoot "README.md"
$readme = Get-Content -LiteralPath $readmePath -Raw

if ($readme.Contains([char]0x2014))
{
    throw "README.md must not contain em dash characters."
}

$msbuild = Find-MSBuild

& $msbuild `
    $projectPath `
    /t:Rebuild `
    /p:Configuration=Release `
    /p:Platform=AnyCPU `
    /p:EmbedSpotifyClientId=false `
    /p:PostBuildEvent= `
    /m `
    /nodeReuse:false `
    /nologo `
    /verbosity:minimal `
    /clp:Summary

if ($LASTEXITCODE -ne 0)
{
    throw "The Release build failed."
}

$releaseDirectory =
    Join-Path $projectDirectory "bin\Release"
$generatedConfiguration =
    Join-Path $projectDirectory (
        "obj\Release\SpotifyBuildConfiguration.g.cs")
$pluginDll =
    Join-Path $releaseDirectory "SpotifySimHub.dll"
$newtonsoftDll =
    Join-Path $releaseDirectory "Newtonsoft.Json.dll"

foreach ($requiredFile in @(
        $generatedConfiguration,
        $pluginDll,
        $newtonsoftDll))
{
    if (!(Test-Path -LiteralPath $requiredFile -PathType Leaf))
    {
        throw "Required package input is missing."
    }
}

$generatedSource =
    Get-Content -LiteralPath $generatedConfiguration -Raw
$clientIdMatch =
    [regex]::Match(
        $generatedSource,
        'ClientId\s*=\s*"([^"]*)";')

if (!$clientIdMatch.Success -or
    $clientIdMatch.Groups[1].Value.Length -ne 0)
{
    throw "Release packaging requires a neutral Client ID configuration."
}

$forbiddenAssemblies =
    @(
        "GameReaderCommon.dll",
        "SimHub.Logging.dll",
        "SimHub.Plugins.dll",
        "log4net.dll"
    )

foreach ($forbiddenAssembly in $forbiddenAssemblies)
{
    if (Test-Path -LiteralPath (
            Join-Path $releaseDirectory $forbiddenAssembly))
    {
        throw "A SimHub-owned assembly was found in the Release output."
    }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force |
    Out-Null
New-Item -ItemType Directory -Path $packageRoot -Force |
    Out-Null
New-Item -ItemType Directory -Path $distDirectory -Force |
    Out-Null

$stagingDirectory =
    Join-Path $packageRoot ("SpotifySimHub-" + $Version)

Reset-Directory `
    -Parent $packageRoot `
    -Path $stagingDirectory

Copy-Item -LiteralPath $pluginDll -Destination $stagingDirectory
Copy-Item -LiteralPath $newtonsoftDll -Destination $stagingDirectory

$installTemplate =
    Get-Content -LiteralPath (
        Join-Path $repoRoot "packaging\INSTALL.txt") -Raw
$installText =
    $installTemplate.Replace("{VERSION}", $Version)
[System.IO.File]::WriteAllText(
    (Join-Path $stagingDirectory "INSTALL.txt"),
    $installText,
    [System.Text.UTF8Encoding]::new($false))

Copy-Item `
    -LiteralPath (
        Join-Path $repoRoot "packaging\THIRD-PARTY-NOTICES.txt") `
    -Destination $stagingDirectory

$stagedFiles =
    @(
        Get-ChildItem -LiteralPath $stagingDirectory -File |
            Select-Object -ExpandProperty Name |
            Sort-Object
    )
$expectedFiles =
    @(
        "INSTALL.txt",
        "Newtonsoft.Json.dll",
        "SpotifySimHub.dll",
        "THIRD-PARTY-NOTICES.txt"
    )

if (@(
        Compare-Object $stagedFiles $expectedFiles
    ).Count -ne 0)
{
    throw "The staged package contains unexpected files."
}

$zipPath =
    Join-Path $distDirectory (
        "SpotifySimHub-" + $Version + "-win.zip")

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
$zipHashPath = $zipPath + ".sha256"
[System.IO.File]::WriteAllText(
    $zipHashPath,
    $zipHash.Hash.ToLowerInvariant() +
    "  " +
    [System.IO.Path]::GetFileName($zipPath) +
    [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$installerPath = $null

if (!$SkipInstaller)
{
    $innoCompiler = Find-InnoCompiler

    if ($innoCompiler)
    {
        $installerScript =
            Join-Path $repoRoot "packaging\SpotifySimHub.iss"

        & $innoCompiler `
            ("/DAppVersion=" + $Version) `
            ("/DSourceDir=" + $stagingDirectory) `
            ("/DOutputDir=" + $distDirectory) `
            $installerScript

        if ($LASTEXITCODE -ne 0)
        {
            throw "The installer build failed."
        }

        $installerPath =
            Join-Path $distDirectory (
                "SpotifySimHub-" +
                $Version +
                "-Setup.exe")

        if (!(Test-Path -LiteralPath $installerPath -PathType Leaf))
        {
            throw "The installer output is missing."
        }

        $installerHash =
            Get-FileHash `
                -LiteralPath $installerPath `
                -Algorithm SHA256
        $installerHashPath =
            $installerPath + ".sha256"

        [System.IO.File]::WriteAllText(
            $installerHashPath,
            $installerHash.Hash.ToLowerInvariant() +
            "  " +
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
        Write-Warning (
            "Inno Setup was not found. " +
            "The ZIP package was created without an EXE installer.")
    }
}

Write-Output ("ZIP: " + $zipPath)

if ($installerPath)
{
    Write-Output ("Installer: " + $installerPath)
}
