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
    [string]$OutputPath = "artifacts/nuget"
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
# ".Tests" subfolder and set IsPackable=false anyway). OcrUtils packs like the
# rest - it simply has no net48 TFM, since its WinRT OCR dependency cannot
# target .NET Framework (see its csproj).
# Package metadata (PackageId, authors, license, readme, repository) comes from
# src/Directory.Build.targets; the version is deliberately not stored in any
# csproj and is supplied here via -p:Version, exactly like the release archives
# take their name from the release tag.
$componentProjects = Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter *.csproj -File |
    Where-Object { $_.BaseName -notlike "*.Tests" } |
    Sort-Object -Property Name

if ($componentProjects.Count -eq 0) {
    throw "No component projects found under '$srcRoot'."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

foreach ($project in $componentProjects) {
    & dotnet pack $project.FullName --configuration $Configuration -p:Version=$Version --output $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($project.Name)' failed with exit code $LASTEXITCODE."
    }
}

# The UiPath activities package is packed after the components: it is not under
# src/, and its nuspec's dependency versions come from the referenced component
# projects, which need the same -p:Version so the whole feed stays consistent.
# It restores against its own nuget.config (the UiPath Official feed).
$activitiesProject = Join-Path $repositoryRoot "integrations/uipath/AwesomeRpaUtils.Activities/AwesomeRpaUtils.Activities.csproj"
if (Test-Path -LiteralPath $activitiesProject) {
    & dotnet pack $activitiesProject --configuration $Configuration -p:Version=$Version --output $outputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Packing 'AwesomeRpaUtils.Activities.csproj' failed with exit code $LASTEXITCODE."
    }
}

$packages = Get-ChildItem -LiteralPath $outputDirectory -Filter "*.nupkg" -File | Sort-Object -Property Name
if ($packages.Count -lt $componentProjects.Count) {
    throw "Expected at least $($componentProjects.Count) packages in '$outputDirectory', but found $($packages.Count)."
}

Write-Host ""
Write-Host "Packed $($componentProjects.Count) component(s) as $($packages.Count) package(s) into '$outputDirectory' (version $Version):"
foreach ($package in $packages) {
    Write-Host "  $($package.Name)"
}