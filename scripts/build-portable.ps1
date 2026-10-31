#Requires -Version 5.1
<#
.SYNOPSIS
    Gera o MOUSEMAPPER.exe portable (self-contained, arquivo único) em .\dist.

.EXAMPLE
    .\scripts\build-portable.ps1
    .\scripts\build-portable.ps1 -Runtime win-arm64 -Output dist-arm64
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "dist",
    [switch]$NoCompression
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\MouseMapper\MouseMapper.csproj"
$outputPath = Join-Path $root $Output

Write-Host ""
Write-Host "  MOUSEMAPPER - build portable" -ForegroundColor Cyan
Write-Host "  Projeto : $project"
Write-Host "  Runtime : $Runtime"
Write-Host "  Destino : $outputPath"
Write-Host ""

# Falha cedo se o SDK não estiver instalado.
$sdk = dotnet --version
Write-Host "  SDK .NET: $sdk" -ForegroundColor DarkGray

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-o", $outputPath
)

if (-not $NoCompression) {
    $publishArgs += "-p:EnableCompressionInSingleFile=true"
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Falha na publicação (exit code $LASTEXITCODE)."
}

$exe = Join-Path $outputPath "MouseMapper.exe"
if (-not (Test-Path $exe)) {
    throw "Publicação concluída, mas $exe não foi encontrado."
}

$info = Get-Item $exe
Write-Host ""
Write-Host ("  EXE gerado: {0}" -f $info.FullName) -ForegroundColor Green
Write-Host ("  Tamanho   : {0:N1} MB" -f ($info.Length / 1MB)) -ForegroundColor Green
Write-Host ""
Write-Host "  Copie o MouseMapper.exe para qualquer pasta (ou pendrive) e execute." -ForegroundColor DarkGray
Write-Host "  As automações ficam em automations.json, na mesma pasta do executável." -ForegroundColor DarkGray
Write-Host ""
