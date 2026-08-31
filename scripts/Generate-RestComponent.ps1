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

dotnet run --project (Join-Path $repositoryRoot "tools/RestCodeGenerator") -- $SwaggerPath $ApiName $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "RestCodeGenerator failed with exit code $LASTEXITCODE." }

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Paste $OutputDirectory/$($ApiName)RestUtils.cs into a Robot Studio Script component."
Write-Host "     The per-endpoint methods are compiled and persisted there - the swagger file is"
Write-Host "     not needed at runtime."
Write-Host "  (Alternative) dotnet build $OutputDirectory/$($ApiName)RestUtils.csproj and load the"
Write-Host "     built DLL into Robot Studio."