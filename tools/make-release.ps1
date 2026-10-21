# Builds the mod and packs the download zip into dist\WtdMorePlayers.zip.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 [-GameDir "D:\...\WhatTheDub"]

param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = @('build', (Join-Path $root 'src\WtdMorePlayers.csproj'), '-c', 'Release')
if ($GameDir) { $build += "-p:GameDir=$GameDir" }
dotnet @build
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'WtdMorePlayers'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null

Copy-Item (Join-Path $root 'src\bin\Release\WtdMorePlayers.dll') $stage
Copy-Item (Join-Path $root 'install\*') $stage
Copy-Item (Join-Path $root 'tools\clip-packs\*') $stage -Exclude '__pycache__'
Copy-Item (Join-Path $root 'README.md') $stage
Copy-Item (Join-Path $root 'LICENSE') $stage

Compress-Archive (Join-Path $stage '*') (Join-Path $dist 'WtdMorePlayers.zip')
Copy-Item (Join-Path $root 'src\bin\Release\WtdMorePlayers.dll') $dist
Write-Host "Release files in $dist"
Get-ChildItem $dist -File | Select-Object Name, Length
