[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ArchivePath = "artifacts/AwesomeRpaUtils.zip",

    [string]$SupportArchivePath = "artifacts/AwesomeRpaUtils-SupportLibraries.zip",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "src/AwesomeRpaUtils.sln"
$buildRoot = Join-Path $repositoryRoot "src/bin/$Configuration"
$archiveFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ArchivePath))
$supportArchiveFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $SupportArchivePath))

# This is the release contract. Only assemblies produced by this repository belong
# in the archive; test assemblies, package dependencies, PDBs, and XML docs do not.
$releaseAssemblies = @(
    "MouseAutomation.dll"
    "ScreenCaptureAutomation.dll"
    "KeyboardAutomation.dll"
    "WindowAutomation.dll"
    "OcrAutomation.dll"
    "DialogAutomation.dll"
    "UIAutomation.dll"
    "CommandLineAutomation.dll"
    "ServiceAutomation.dll"
    "EventAutomation.dll"
)

$supportAssemblies = @(
    "system.serviceprocess.servicecontroller/9.0.0/runtimes/win/lib/net9.0/System.ServiceProcess.ServiceController.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/net9.0/System.Diagnostics.EventLog.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/net9.0/System.Diagnostics.EventLog.Messages.dll"
)

if (-not $NoBuild) {
    & dotnet build $solutionPath --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Solution build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $buildRoot -PathType Container)) {
    throw "Build output was not found at '$buildRoot'. Build the solution first or omit -NoBuild."
}

$stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-" + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

    foreach ($assembly in $releaseAssemblies) {
        $matches = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Filter $assembly)

        if ($matches.Count -ne 1) {
            throw "Expected exactly one '$assembly' under '$buildRoot', but found $($matches.Count)."
        }

        Copy-Item -LiteralPath $matches[0].FullName -Destination (Join-Path $stagingDirectory $assembly)
    }

    $archiveDirectory = Split-Path -Parent $archiveFullPath
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null

    if (Test-Path -LiteralPath $archiveFullPath) {
        Remove-Item -LiteralPath $archiveFullPath -Force
    }

    Compress-Archive -Path (Join-Path $stagingDirectory "*.dll") -DestinationPath $archiveFullPath
    Write-Host "Created $archiveFullPath with $($releaseAssemblies.Count) project DLLs."
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

$nugetPackagesOutput = (& dotnet nuget locals global-packages --list | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $nugetPackagesOutput -notmatch "^[^:]+:\s*(.+)$") {
    throw "Could not determine the NuGet global-packages directory."
}

$nugetPackagesRoot = $Matches[1].Trim()
$supportStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Support-" + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $supportStagingDirectory | Out-Null

    foreach ($relativePath in $supportAssemblies) {
        $sourcePath = Join-Path $nugetPackagesRoot $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required support assembly was not found at '$sourcePath'. Restore the solution first."
        }

        Copy-Item -LiteralPath $sourcePath -Destination $supportStagingDirectory
    }

    $supportArchiveDirectory = Split-Path -Parent $supportArchiveFullPath
    New-Item -ItemType Directory -Path $supportArchiveDirectory -Force | Out-Null

    if (Test-Path -LiteralPath $supportArchiveFullPath) {
        Remove-Item -LiteralPath $supportArchiveFullPath -Force
    }

    Compress-Archive -Path (Join-Path $supportStagingDirectory "*.dll") -DestinationPath $supportArchiveFullPath
    Write-Host "Created $supportArchiveFullPath with $($supportAssemblies.Count) support DLLs."
}
finally {
    if (Test-Path -LiteralPath $supportStagingDirectory) {
        Remove-Item -LiteralPath $supportStagingDirectory -Recurse -Force
    }
}
