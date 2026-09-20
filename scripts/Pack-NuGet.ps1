[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    # Package version (e.g. "0.4.0"). Defaults to $env:RELEASE_TAG with any
    # leading "v" stripped, matching how Package-Release.ps1 names its archives.
    [string]$Version,

    # Output directory for the .nupkg files, relative to the repository root.
    # This folder doubles as a local NuGet folder feed (see the README's
    # "Consuming the NuGet packages" section).
    [string]$OutputPath = "artifacts/nuget",

    # Pack the outputs of an earlier `dotnet build` of the solution instead of
    # building again (what the release workflow does after its build step).
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$srcRoot = Join-Path $repositoryRoot "src"
$outputDirectory = Join-Path $repositoryRoot $OutputPath

if ([string]::IsNullOrWhiteSpace($Version)) {
    if ([string]::IsNullOrWhiteSpace($env:RELEASE_TAG)) {
        throw "No version supplied: pass -Version (e.g. -Version 0.4.0) or set RELEASE_TAG."
    }
    $Version = $env:RELEASE_TAG.TrimStart("v")
}

# Every component project under src/, excluding the test projects (they live in a
# ".Tests" subfolder and set IsPackable=false anyway). Package metadata (PackageId,
# authors, license, readme, repository) comes from src/Directory.Build.targets; the
# version is deliberately not stored in any csproj and is supplied here via
# -p:Version, exactly like the release archives take their name from the release tag.
$componentProjects = Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter *.csproj -File |
    Where-Object { $_.BaseName -notlike "*.Tests" } |
    Sort-Object -Property Name

if ($componentProjects.Count -eq 0) {
    throw "No component projects found under '$srcRoot'."
}

# Start from an empty output folder so a stale package from an earlier run can
# neither satisfy the checks below nor end up uploaded with a release.
if (Test-Path -LiteralPath $outputDirectory) {
    Get-ChildItem -LiteralPath $outputDirectory -Filter "*.nupkg" -File | Remove-Item -Force
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

foreach ($project in $componentProjects) {
    $packArguments = @($project.FullName, "--configuration", $Configuration, "-p:Version=$Version", "--output", $outputDirectory)
    if ($NoBuild) {
        $packArguments += "--no-build"
    }

    & dotnet pack @packArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($project.Name)' failed with exit code $LASTEXITCODE."
    }
}

$packages = Get-ChildItem -LiteralPath $outputDirectory -Filter "*.nupkg" -File | Sort-Object -Property Name
if ($packages.Count -ne $componentProjects.Count) {
    throw "Expected exactly $($componentProjects.Count) packages (one per component) in '$outputDirectory', but found $($packages.Count)."
}

# Every package must carry the requested version and hold the component's README
# and at least one library; catching a bad package here is far cheaper than after
# a release has been published.
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in $packages) {
    if ($package.Name -notlike "AwesomeRpaUtils.*.$Version.nupkg") {
        throw "Package '$($package.Name)' does not look like AwesomeRpaUtils.<name>.$Version.nupkg."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entryNames = $archive.Entries | ForEach-Object { $_.FullName }
    }
    finally {
        $archive.Dispose()
    }

    if (-not ($entryNames | Where-Object { $_ -like "lib/*/*.dll" })) {
        throw "Package '$($package.Name)' contains no lib/<tfm>/*.dll."
    }
    if ($entryNames -notcontains "README.md") {
        throw "Package '$($package.Name)' does not contain README.md."
    }
}

Write-Host ""
Write-Host "Packed $($componentProjects.Count) component(s) into '$outputDirectory' (version $Version):"
foreach ($package in $packages) {
    Write-Host "  $($package.Name)"
}
