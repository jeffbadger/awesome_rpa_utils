[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerPath,

    [Parameter(Mandatory = $true)]
    [string]$ApiName,

    # Emit the class as `... : System.ComponentModel.Component` (Robot Studio component-tray shape).
    [switch]$Component,

    [string]$OutputDirectory = "generated/$ApiName"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$generatorArgs = @($SwaggerPath, $ApiName, $OutputDirectory)
if ($Component) { $generatorArgs += "--component" }
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
Write-Host "  (Alternative) dotnet build $OutputDirectory/$fileNameBase.csproj and load the built DLL"
Write-Host "     into Robot Studio."