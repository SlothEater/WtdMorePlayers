# Builds the mod and packs the downloads into dist\:
#   WtdMorePlayers-windows.zip        install.bat etc.
#   WtdMorePlayers-mac-linux.tar.gz   install.sh etc., executable, in a WtdMorePlayers/ folder
#   WtdMorePlayers.dll                what install.ps1 / install.sh download when run from a clone
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 [-GameDir "D:\...\WhatTheDub"]

param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = @('build', (Join-Path $root 'src\WtdMorePlayers.csproj'), '-c', 'Release')
if ($GameDir) { $build += "-p:GameDir=$GameDir" }
dotnet @build
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null
$dll = Join-Path $root 'src\bin\Release\WtdMorePlayers.dll'
$common = @($dll, (Join-Path $root 'tools\clip-packs\wtd_pack.py'), (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'))

# Windows: flat zip.
$win = Join-Path $dist 'windows'
New-Item -ItemType Directory $win | Out-Null
Copy-Item ($common + (Get-ChildItem (Join-Path $root 'install') -Include *.bat, *.ps1 -Recurse).FullName +
           (Join-Path $root 'tools\clip-packs\Add Clip Pack.bat')) $win
Compress-Archive (Join-Path $win '*') (Join-Path $dist 'WtdMorePlayers-windows.zip')

# Mac/Linux: tar.gz with LF scripts marked executable (bsdtar reads the file modes from an mtree list).
$unix = Join-Path $dist 'mac-linux'
New-Item -ItemType Directory $unix | Out-Null
Copy-Item $common $unix
$scripts = (Get-ChildItem (Join-Path $root 'install') -Filter *.sh).FullName + (Join-Path $root 'tools\clip-packs\add-clip-pack.sh')
foreach ($s in $scripts) {
    $text = [IO.File]::ReadAllText($s) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $unix (Split-Path $s -Leaf)), $text, (New-Object Text.UTF8Encoding $false))
}
$time = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$mtree = @('#mtree', "WtdMorePlayers type=dir mode=0755 uname=root gname=root time=$time.0")
foreach ($f in Get-ChildItem $unix -File) {
    $mode = if ($f.Extension -eq '.sh') { '0755' } else { '0644' }
    $mtree += "WtdMorePlayers/$($f.Name) type=file mode=$mode uname=root gname=root time=$time.0 contents=$($f.Name)"
}
Set-Content (Join-Path $unix 'files.mtree') $mtree -Encoding ascii
Push-Location $unix
try {
    tar -czf (Join-Path $dist 'WtdMorePlayers-mac-linux.tar.gz') '@files.mtree'
    if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
} finally { Pop-Location }

Copy-Item $dll $dist
Remove-Item $win, $unix -Recurse -Force
Write-Host "Release files in $dist"
Get-ChildItem $dist -File | Select-Object Name, Length
