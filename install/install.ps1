# Installs WTD More Players into What The Dub?! (Steam).
#  - finds the game through Steam
#  - installs BepInEx 5 (the mod loader) if it is not there yet
#  - copies WtdMorePlayers.dll into BepInEx\plugins
# Usage: install.bat            (or: powershell -ExecutionPolicy Bypass -File install.ps1 [-GameDir "D:\...\WhatTheDub"])

param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$BepInExVersion = '5.4.23.5'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$PluginUrl = 'https://github.com/SlothEater/WtdMorePlayers/releases/latest/download/WtdMorePlayers.dll'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

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

Write-Host ''
Write-Host '=== WTD More Players installer ===' -ForegroundColor Cyan

if (-not $GameDir) { $GameDir = Find-GameDir }
while (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'WhatTheDub.exe'))) {
    Write-Host "Could not find What The Dub?! automatically." -ForegroundColor Yellow
    $GameDir = Read-Host 'Paste the game folder (Steam > right-click the game > Manage > Browse local files)'
    $GameDir = $GameDir.Trim('"', ' ')
}
Write-Host "Game folder: $GameDir"

if (Get-Process WhatTheDub -ErrorAction SilentlyContinue) {
    Write-Host 'What The Dub?! is running. Close the game and run the installer again.' -ForegroundColor Red
    exit 1
}

# 1. BepInEx
if (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
    Write-Host 'BepInEx already installed.'
} else {
    Write-Host "Downloading BepInEx $BepInExVersion..."
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "wtd-bepinex-$([guid]::NewGuid())"
    New-Item -ItemType Directory $tmp | Out-Null
    $zip = Join-Path $tmp 'bepinex.zip'
    Invoke-WebRequest $BepInExUrl -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $GameDir -Force
    Remove-Item $tmp -Recurse -Force
    Write-Host 'BepInEx installed.'
}

# 2. The mod
$plugins = Join-Path $GameDir 'BepInEx\plugins'
New-Item -ItemType Directory -Force $plugins | Out-Null
$candidates = @(
    (Join-Path $PSScriptRoot 'WtdMorePlayers.dll'),
    (Join-Path $PSScriptRoot '..\src\bin\Release\WtdMorePlayers.dll')
)
$local = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($local) {
    Copy-Item $local $plugins -Force
    Write-Host "Installed mod from $local"
} else {
    Write-Host 'Downloading the latest mod release...'
    Invoke-WebRequest $PluginUrl -OutFile (Join-Path $plugins 'WtdMorePlayers.dll') -UseBasicParsing
    Write-Host 'Mod installed.'
}

Write-Host ''
Write-Host 'Done!' -ForegroundColor Green
Write-Host 'Start What The Dub?! from Steam and host a game as usual.'
Write-Host 'The TV will show an address like 192.168.1.20:8080 instead of playwtd.com -'
Write-Host 'players on your Wi-Fi open that on their phones to join.'
Write-Host 'If Windows asks whether What The Dub?! may use the network, click Allow (Private networks).'
Write-Host ''
Write-Host "Settings: $GameDir\BepInEx\config\com.wtd.moreplayers.cfg (created after the first launch)"
