#!/usr/bin/env pwsh
# Publish a single-file, self-contained win-x64 .exe to ./publish.
# WPF does not support Native AOT or trimming, so leave trimming off.
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = "$projectRoot\publish"
}

Write-Host "Publishing TypeGent.App..."
Write-Host "  Configuration: $Configuration"
Write-Host "  Runtime:       $Runtime"
Write-Host "  Output:        $OutputPath"

dotnet publish "$projectRoot\src\TypeGent.App" `
    -c $Configuration `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -o $OutputPath

$exe = Join-Path $OutputPath "TypeGent.App.exe"
if (-not (Test-Path $exe)) {
    throw "Publish did not produce $exe"
}

$size = (Get-Item $exe).Length
$mb = [math]::Round($size / 1MB, 2)
Write-Host ""
Write-Host "Published: $exe ($size bytes, $mb MB)"
