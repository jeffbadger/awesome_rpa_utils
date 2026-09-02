[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ArchivePath = "artifacts/AwesomeRpaUtils-{tfm}.zip",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "src/AwesomeRpaUtils.sln"
$buildRoot = Join-Path $repositoryRoot "src/bin/$Configuration"

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
    "EventLogAutomation.dll"
    "SessionAutomation.dll"
    "FileWatchAutomation.dll"
    "ArchiveAutomation.dll"
)

# Every component project multi-targets these two TFMs (OcrUtils uses the versioned
# net8.0-windows10.0.19041.0 / net10.0-windows10.0.19041.0 monikers instead, for its
# WinRT OCR dependency, hence the wildcard rather than an exact folder-name match).
# Each channel becomes its own self-contained archive on the release page: the
# component DLLs for that TFM at the archive root plus every framework-shared or
# design-time artifact bundled inside as its own zip, so a channel archive alone
# is a complete release.
# Each channel becomes its own self-contained archive on the release page: the
# component DLLs for that TFM at the archive root plus every framework-shared or
# design-time artifact bundled inside as its own zip, so a channel archive alone
# is a complete release. SupportTfm picks the Windows support-DLL flavor embedded
# for that channel: the support packages ship net8.0 and net9.0 flavors, the
# net9.0 one being the newest (a net10.0 runtime loads it fine; a net8.0
# runtime only loads the net8.0 flavor).
$targetFrameworkChannels = @(
    [pscustomobject]@{ Label = "net8.0"; DirectoryFilter = "net8.0-windows*"; SupportTfm = "net8.0" }
    [pscustomobject]@{ Label = "net10.0"; DirectoryFilter = "net10.0-windows*"; SupportTfm = "net9.0" }
)

# The archives built once below into $bundledStagingDirectory and embedded in
# every channel archive. The support-libraries zip is channel-specific (see
# SupportTfm above), so it is built inside the channel loop instead.
$bundledArchives = @(
    "AwesomeRpaUtils-Documentation.zip"
    "AwesomeRpaUtils-RestCodeGenerator.zip"
)

# $ArchivePath must contain a literal "{tfm}" placeholder, substituted with each
# channel's Label below (e.g. "AwesomeRpaUtils-{tfm}-v1.0.0.zip" ->
# "AwesomeRpaUtils-net8.0-v1.0.0.zip" / "AwesomeRpaUtils-net10.0-v1.0.0.zip").
function Get-ChannelArchivePath([string]$BasePath, [string]$Label) {
    if ($BasePath -notmatch [regex]::Escape("{tfm}")) {
        throw "ArchivePath '$BasePath' must contain a '{tfm}' placeholder so each target framework gets its own archive."
    }
    return $BasePath.Replace("{tfm}", $Label)
}

# {tfm} in these NuGet-package paths is substituted with each channel's
# SupportTfm below, so every channel embeds the support-DLL flavor that
# matches its target framework.
$supportAssemblies = @(
    "system.serviceprocess.servicecontroller/9.0.0/runtimes/win/lib/{tfm}/System.ServiceProcess.ServiceController.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/{tfm}/System.Diagnostics.EventLog.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/{tfm}/System.Diagnostics.EventLog.Messages.dll"
)

# The RestCodeGenerator is a design-time command-line tool, not a component the
# Robot Studio runtime loads. It targets net10.0 only and runs with
# `dotnet RestCodeGenerator.dll <swaggerPath> <apiName> <outputDirectory> [--component]`.
# Its archive holds exactly the three files needed to run it that way plus the
# tool's README; PDBs, ref assemblies, and build intermediates are left out.
$generatorFiles = @(
    "RestCodeGenerator.dll"
    "RestCodeGenerator.deps.json"
    "RestCodeGenerator.runtimeconfig.json"
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

$bundledStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Bundled-" + [guid]::NewGuid().ToString("N"))
$supportStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Support-" + [guid]::NewGuid().ToString("N"))
$generatorStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Generator-" + [guid]::NewGuid().ToString("N"))
$generatorBuildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Generator-Build-" + [guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $bundledStagingDirectory | Out-Null

    # The documentation bundle comes from the dedicated script so the bundling
    # rules (which files belong, which links get rewritten) only exist there.
    & (Join-Path $PSScriptRoot "Package-Documentation.ps1") -ArchivePath (Join-Path $bundledStagingDirectory "AwesomeRpaUtils-Documentation.zip")

    $nugetPackagesOutput = (& dotnet nuget locals global-packages --list | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $nugetPackagesOutput -notmatch "^[^:]+:\s*(.+)$") {
        throw "Could not determine the NuGet global-packages directory."
    }

    $nugetPackagesRoot = $Matches[1].Trim()

    # Unlike the components, the generator lives outside src/AwesomeRpaUtils.sln,
    # so the solution build above never produces its output. -NoBuild therefore
    # reuses whatever the tool's own bin output already holds instead of compiling.
    $generatorProjectPath = Join-Path $repositoryRoot "tools/RestCodeGenerator/RestCodeGenerator.csproj"
    if ($NoBuild) {
        $generatorOutputDirectory = Join-Path $repositoryRoot "tools/RestCodeGenerator/bin/$Configuration/net10.0"
        if (-not (Test-Path -LiteralPath (Join-Path $generatorOutputDirectory "RestCodeGenerator.dll") -PathType Leaf)) {
            throw "Generator output was not found at '$generatorOutputDirectory'. Build the generator first or omit -NoBuild."
        }
    }
    else {
        $generatorOutputDirectory = $generatorBuildDirectory
        & dotnet publish $generatorProjectPath --configuration $Configuration --framework net10.0 -p:UseAppHost=false --output $generatorOutputDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "RestCodeGenerator publish failed with exit code $LASTEXITCODE."
        }
    }

    New-Item -ItemType Directory -Path $generatorStagingDirectory | Out-Null

    foreach ($generatorFile in $generatorFiles) {
        $sourcePath = Join-Path $generatorOutputDirectory $generatorFile
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Expected generator file '$generatorFile' was not found at '$generatorOutputDirectory'."
        }

        Copy-Item -LiteralPath $sourcePath -Destination $generatorStagingDirectory
    }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot "tools/README.md") -Destination (Join-Path $generatorStagingDirectory "README.md")

    Compress-Archive -Path (Join-Path $generatorStagingDirectory "*") -DestinationPath (Join-Path $bundledStagingDirectory "AwesomeRpaUtils-RestCodeGenerator.zip")

    foreach ($bundledArchive in $bundledArchives) {
        $bundledArchivePath = Join-Path $bundledStagingDirectory $bundledArchive
        if (-not (Test-Path -LiteralPath $bundledArchivePath -PathType Leaf)) {
            throw "Bundled archive '$bundledArchive' was not created in '$bundledStagingDirectory'."
        }
    }

    foreach ($channel in $targetFrameworkChannels) {
        $channelArchivePath = Get-ChannelArchivePath -BasePath $ArchivePath -Label $channel.Label
        $channelArchiveFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $channelArchivePath))
        $stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-" + [guid]::NewGuid().ToString("N"))

        try {
            New-Item -ItemType Directory -Path $stagingDirectory | Out-Null

            foreach ($assembly in $releaseAssemblies) {
                $matches = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Filter $assembly |
                    Where-Object { $_.Directory.Name -like $channel.DirectoryFilter })

                if ($matches.Count -ne 1) {
                    throw "Expected exactly one '$assembly' under '$buildRoot' matching '$($channel.DirectoryFilter)', but found $($matches.Count)."
                }

                Copy-Item -LiteralPath $matches[0].FullName -Destination (Join-Path $stagingDirectory $assembly)
            }

            foreach ($bundledArchive in $bundledArchives) {
                Copy-Item -LiteralPath (Join-Path $bundledStagingDirectory $bundledArchive) -Destination (Join-Path $stagingDirectory $bundledArchive)
            }

            # Support libraries staged with the Windows DLL flavor matching
            # this channel's target framework (see SupportTfm above).
            if (Test-Path -LiteralPath $supportStagingDirectory) {
                Remove-Item -LiteralPath $supportStagingDirectory -Recurse -Force
            }

            New-Item -ItemType Directory -Path $supportStagingDirectory | Out-Null

            foreach ($relativePath in $supportAssemblies) {
                $sourcePath = Join-Path $nugetPackagesRoot $relativePath.Replace("{tfm}", $channel.SupportTfm)
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "Required support assembly was not found at '$sourcePath'. Restore the solution first."
                }

                Copy-Item -LiteralPath $sourcePath -Destination $supportStagingDirectory
            }

            Compress-Archive -Path (Join-Path $supportStagingDirectory "*.dll") -DestinationPath (Join-Path $stagingDirectory "AwesomeRpaUtils-SupportLibraries.zip")

            $archiveDirectory = Split-Path -Parent $channelArchiveFullPath
            New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null

            if (Test-Path -LiteralPath $channelArchiveFullPath) {
                Remove-Item -LiteralPath $channelArchiveFullPath -Force
            }

            Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $channelArchiveFullPath
            Write-Host "Created $channelArchiveFullPath with $($releaseAssemblies.Count) project DLLs ($($channel.Label)), TFM-matched support DLLs, and $($bundledArchives.Count) other bundled archives."
        }
        finally {
            if (Test-Path -LiteralPath $stagingDirectory) {
                Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
            }
        }
    }
}
finally {
    foreach ($temporaryDirectory in @(
        $bundledStagingDirectory
        $supportStagingDirectory
        $generatorStagingDirectory
        $generatorBuildDirectory
    )) {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}