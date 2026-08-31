[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerPath,

    [Parameter(Mandatory = $true)]
    [string]$ApiName,

    [string]$OutputDirectory = "generated/$ApiName"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$generatorOutput = dotnet run --project (Join-Path $repositoryRoot "tools/RestCodeGenerator") -- $SwaggerPath $ApiName $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "RestCodeGenerator failed with exit code $LASTEXITCODE." }
$generatorOutput | Write-Host

$fileNameBase = "$($ApiName)RestUtils"
if ("$generatorOutput" -match '^Generated (\S+)\.cs and (\S+)\.csproj in (.+)$') { $fileNameBase = $Matches[1] }

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Paste $OutputDirectory/$fileNameBase.cs into a Robot Studio Script component."
Write-Host "     The per-endpoint methods are compiled and persisted there - the swagger file is"
Write-Host "     not needed at runtime."
Write-Host "  (Alternative) dotnet build $OutputDirectory/$fileNameBase.csproj and load the"
Write-Host "     built DLL into Robot Studio."