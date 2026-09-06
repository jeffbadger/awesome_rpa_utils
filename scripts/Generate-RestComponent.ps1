[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Alias("SwaggerPath")]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$ApiName,

    # openapi|postman|bruno|curl - omit to auto-detect from InputPath's extension/content.
    [string]$Format,

    # Emit the class as `... : System.ComponentModel.Component` (Robot Studio component-tray shape).
    [switch]$Component,

    # Also run `dotnet build` on the generated .csproj, so this one command produces a
    # ready-to-load DLL instead of leaving the build as a separate manual step.
    [switch]$Build,

    [string]$OutputDirectory = "generated/$ApiName"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$generatorArgs = @($InputPath, $ApiName, $OutputDirectory)
if ($Format) { $generatorArgs += @("--format", $Format) }
if ($Component) { $generatorArgs += "--component" }
if ($Build) { $generatorArgs += "--build" }
dotnet run --project (Join-Path $repositoryRoot "tools/RestCodeGenerator") -- @generatorArgs | Tee-Object -Variable generatorOutput
if ($LASTEXITCODE -ne 0) { throw "RestCodeGenerator failed with exit code $LASTEXITCODE." }

$fileNameBase = "$($ApiName)RestUtils"
# Match the ARRAY element-wise (-match on the joined string would anchor ^ only
# at the very first line) and take the last matching "Generated ..." line.
foreach ($line in @($generatorOutput)) {
    if ($line -match '^Generated (\S+)\.cs and (\S+)\.csproj in (.+)$') { $fileNameBase = $Matches[1] }
}

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Paste $OutputDirectory/$fileNameBase.cs into a Robot Studio Script component."
if ($Build) {
    Write-Host "  (Alternative) The DLL was already built by --build - load it into Robot Studio"
    Write-Host "     from $OutputDirectory/bin/<Configuration>/<framework>/."
} else {
    Write-Host "  (Alternative) dotnet build $OutputDirectory/$fileNameBase.csproj and load the built DLL"
    Write-Host "     into Robot Studio."
}