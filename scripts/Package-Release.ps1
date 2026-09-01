[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$ArchivePath = "artifacts/AwesomeRpaUtils-{tfm}.zip",

    [string]$SupportArchivePath = "artifacts/AwesomeRpaUtils-SupportLibraries.zip",

    [string]$GeneratorArchivePath = "artifacts/AwesomeRpaUtils-RestCodeGenerator.zip",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot "src/AwesomeRpaUtils.sln"
$buildRoot = Join-Path $repositoryRoot "src/bin/$Configuration"
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

# Every component project multi-targets these two TFMs (OcrUtils uses the versioned
# net8.0-windows10.0.19041.0 / net10.0-windows10.0.19041.0 monikers instead, for its
# WinRT OCR dependency, hence the wildcard rather than an exact folder-name match).
# Each channel becomes its own archive on the release page.
$targetFrameworkChannels = @(
    [pscustomobject]@{ Label = "net8.0"; DirectoryFilter = "net8.0-windows*" }
    [pscustomobject]@{ Label = "net10.0"; DirectoryFilter = "net10.0-windows*" }
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

$supportAssemblies = @(
    "system.serviceprocess.servicecontroller/9.0.0/runtimes/win/lib/net9.0/System.ServiceProcess.ServiceController.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/net9.0/System.Diagnostics.EventLog.dll"
    "system.diagnostics.eventlog/9.0.0/runtimes/win/lib/net9.0/System.Diagnostics.EventLog.Messages.dll"
)

# The RestCodeGenerator is a design-time command-line tool, not a component the
# Robot Studio runtime loads, so it ships in its own archive rather than alongside
# the component DLLs. It targets net10.0 only and runs with
# `dotnet RestCodeGenerator.dll <swaggerPath> <apiName> <outputDirectory> [--component]`.
# The archive holds exactly the three files needed to run it that way plus the
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

        $archiveDirectory = Split-Path -Parent $channelArchiveFullPath
        New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null

        if (Test-Path -LiteralPath $channelArchiveFullPath) {
            Remove-Item -LiteralPath $channelArchiveFullPath -Force
        }

        Compress-Archive -Path (Join-Path $stagingDirectory "*.dll") -DestinationPath $channelArchiveFullPath
        Write-Host "Created $channelArchiveFullPath with $($releaseAssemblies.Count) project DLLs ($($channel.Label))."
    }
    finally {
        if (Test-Path -LiteralPath $stagingDirectory) {
            Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
        }
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

if ([string]::IsNullOrWhiteSpace($GeneratorArchivePath)) {
    Write-Host "GeneratorArchivePath is empty; skipping the RestCodeGenerator archive."
}
else {
    # Unlike the components, the generator lives outside src/AwesomeRpaUtils.sln,
    # so the solution build above never produces its output. -NoBuild therefore
    # reuses whatever the tool's own bin output already holds instead of compiling.
    $generatorProjectPath = Join-Path $repositoryRoot "tools/RestCodeGenerator/RestCodeGenerator.csproj"
    $generatorArchiveFullPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $GeneratorArchivePath))
    $generatorStagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Generator-" + [guid]::NewGuid().ToString("N"))
    $generatorBuildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AwesomeRpaUtils-Generator-Build-" + [guid]::NewGuid().ToString("N"))

    try {
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

        $generatorArchiveDirectory = Split-Path -Parent $generatorArchiveFullPath
        New-Item -ItemType Directory -Path $generatorArchiveDirectory -Force | Out-Null

        if (Test-Path -LiteralPath $generatorArchiveFullPath) {
            Remove-Item -LiteralPath $generatorArchiveFullPath -Force
        }

        Compress-Archive -Path (Join-Path $generatorStagingDirectory "*") -DestinationPath $generatorArchiveFullPath
        Write-Host "Created $generatorArchiveFullPath with the RestCodeGenerator tool ($($generatorFiles.Count) files plus README)."
    }
    finally {
        if (Test-Path -LiteralPath $generatorStagingDirectory) {
            Remove-Item -LiteralPath $generatorStagingDirectory -Recurse -Force
        }

        if (Test-Path -LiteralPath $generatorBuildDirectory) {
            Remove-Item -LiteralPath $generatorBuildDirectory -Recurse -Force
        }
    }
}
