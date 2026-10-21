# Removes WTD More Players and the BepInEx mod loader from What The Dub?!, restoring the original game.
# Usage: uninstall.bat   (or: powershell -ExecutionPolicy Bypass -File uninstall.ps1 [-GameDir "D:\...\WhatTheDub"] [-KeepBepInEx])

param([string]$GameDir, [switch]$KeepBepInEx)

$ErrorActionPreference = 'Stop'

function Find-GameDir {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $libraries += $m.Groups[1].Value -replace '\\\\', '\'
        }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $dir = Join-Path $lib 'steamapps\common\WhatTheDub'
        if (Test-Path (Join-Path $dir 'WhatTheDub.exe')) { return $dir }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'WhatTheDub.exe'))) {
    Write-Host 'Could not find What The Dub?!. Pass the folder with -GameDir.' -ForegroundColor Red
    exit 1
}
if (Get-Process WhatTheDub -ErrorAction SilentlyContinue) {
    Write-Host 'What The Dub?! is running. Close the game and run the uninstaller again.' -ForegroundColor Red
    exit 1
}

$plugin = Join-Path $GameDir 'BepInEx\plugins\WtdMorePlayers.dll'
if (Test-Path $plugin) { Remove-Item $plugin -Force; Write-Host 'Removed WTD More Players.' }

$others = Get-ChildItem (Join-Path $GameDir 'BepInEx\plugins') -Filter *.dll -Recurse -ErrorAction SilentlyContinue
if ($KeepBepInEx -or $others) {
    if ($others) { Write-Host 'Other BepInEx plugins found, leaving BepInEx installed.' -ForegroundColor Yellow }
} else {
    foreach ($item in 'BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt') {
        $p = Join-Path $GameDir $item
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Write-Host 'Removed BepInEx.'
}
Write-Host 'Done. The game is back to normal.' -ForegroundColor Green
