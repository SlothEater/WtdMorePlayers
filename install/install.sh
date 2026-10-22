#!/usr/bin/env bash
# Installs WTD More Players into What The Dub?! (Steam) on macOS or Linux / Steam Deck.
#  - finds the game through Steam
#  - picks the mode: macOS (native Mac game) or Linux (the Windows game running through Proton)
#  - installs BepInEx 5 (the mod loader) for that mode if it is not there yet
#  - copies WtdMorePlayers.dll into BepInEx/plugins
#  - prints the Steam launch option that turns the mod on
# Usage: ./install.sh [--game-dir "/path/to/WhatTheDub"]
# On Windows use install.bat instead.

set -euo pipefail

BEPINEX_VERSION=5.4.23.5
BEPINEX_BASE="https://github.com/BepInEx/BepInEx/releases/download/v$BEPINEX_VERSION"
PLUGIN_URL=https://github.com/SlothEater/WtdMorePlayers/releases/latest/download/WtdMorePlayers.dll
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GAME_DIR=""
while [ $# -gt 0 ]; do
    case "$1" in
        --game-dir) GAME_DIR="${2:-}"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

say()  { printf '%s\n' "$*"; }
warn() { printf '\033[33m%s\033[0m\n' "$*"; }
fail() { printf '\033[31m%s\033[0m\n' "$*"; exit 1; }

steam_roots() {
    case "$(uname -s)" in
        Darwin) echo "$HOME/Library/Application Support/Steam" ;;
        *)
            echo "$HOME/.steam/steam"
            echo "$HOME/.local/share/Steam"
            echo "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"   # Flatpak
            echo "$HOME/snap/steam/common/.local/share/Steam"                  # Snap
            ;;
    esac
}

steam_libraries() {
    steam_roots | while IFS= read -r root; do
        [ -d "$root" ] || continue
        echo "$root"
        vdf="$root/steamapps/libraryfolders.vdf"
        [ -f "$vdf" ] && sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)".*/\1/p' "$vdf"
    done
}

is_game_dir() {
    [ -n "$1" ] && { [ -f "$1/WhatTheDub.exe" ] || ls -d "$1"/*.app >/dev/null 2>&1; }
}

find_game_dir() {
    steam_libraries | while IFS= read -r lib; do
        if is_game_dir "$lib/steamapps/common/WhatTheDub"; then
            echo "$lib/steamapps/common/WhatTheDub"
            return
        fi
    done
}

download() {
    if command -v curl >/dev/null; then curl -fsSL -o "$2" "$1"
    else wget -q -O "$2" "$1"; fi
}

say ''
say '=== WTD More Players installer ==='

[ -n "$GAME_DIR" ] || GAME_DIR="$(find_game_dir | head -n 1 || true)"
while ! is_game_dir "$GAME_DIR"; do
    warn 'Could not find What The Dub?! automatically.'
    read -r -p 'Game folder (Steam > right-click the game > Manage > Browse local files): ' GAME_DIR
done
GAME_DIR="$(cd "$GAME_DIR" && pwd)"
say "Game folder: $GAME_DIR"

# Mode: which build of the game is installed decides which BepInEx it needs.
APP_NAME="$(cd "$GAME_DIR" && ls -d *.app 2>/dev/null | head -n 1 || true)"
if [ -n "$APP_NAME" ]; then
    MODE=macos
    BEPINEX_ZIP="BepInEx_macos_universal_$BEPINEX_VERSION.zip"
    say "Mode: macOS (native Mac game, $APP_NAME)"
else
    MODE=proton
    BEPINEX_ZIP="BepInEx_win_x64_$BEPINEX_VERSION.zip"
    say 'Mode: Windows game through Proton/Wine (Linux, Steam Deck)'
fi

if pgrep -x 'WhatTheDub|WhatTheDub.exe' >/dev/null 2>&1; then
    fail 'What The Dub?! is running. Close the game and run the installer again.'
fi
command -v unzip >/dev/null || fail 'unzip is needed (for example: sudo apt install unzip).'

# 1. BepInEx
if [ -f "$GAME_DIR/BepInEx/core/BepInEx.dll" ]; then
    say 'BepInEx already installed.'
else
    say "Downloading BepInEx $BEPINEX_VERSION..."
    tmp="$(mktemp -d)"
    download "$BEPINEX_BASE/$BEPINEX_ZIP" "$tmp/bepinex.zip"
    unzip -o -q "$tmp/bepinex.zip" -d "$GAME_DIR"
    rm -rf "$tmp"
    say 'BepInEx installed.'
fi
if [ "$MODE" = macos ]; then
    # Lets the script also be started by hand; under Steam it finds the game itself.
    sed -i.bak "s|^executable_name=\"\"|executable_name=\"$APP_NAME\"|" "$GAME_DIR/run_bepinex.sh" && rm -f "$GAME_DIR/run_bepinex.sh.bak"
    chmod +x "$GAME_DIR/run_bepinex.sh"
fi

# 2. The mod
mkdir -p "$GAME_DIR/BepInEx/plugins"
installed=""
for f in "$SCRIPT_DIR/WtdMorePlayers.dll" "$SCRIPT_DIR/../src/bin/Release/WtdMorePlayers.dll"; do
    if [ -f "$f" ]; then
        cp "$f" "$GAME_DIR/BepInEx/plugins/"
        say "Installed mod from $f"
        installed=1
        break
    fi
done
if [ -z "$installed" ]; then
    say 'Downloading the latest mod release...'
    download "$PLUGIN_URL" "$GAME_DIR/BepInEx/plugins/WtdMorePlayers.dll"
    say 'Mod installed.'
fi

# 3. Firewall hints (the phone page is served from this computer on port 8080).
if [ "$MODE" = proton ] && command -v ufw >/dev/null && ufw status 2>/dev/null | grep -q 'Status: active'; then
    warn 'The ufw firewall is on. Let phones reach the game with:  sudo ufw allow 8080/tcp'
fi

say ''
printf '\033[32m%s\033[0m\n' 'Almost done! One step in Steam turns the mod on:'
say '  Steam > right-click What The Dub?! > Properties > General > Launch Options, and paste:'
say ''
if [ "$MODE" = macos ]; then
    say "  \"$GAME_DIR/run_bepinex.sh\" %command%"
else
    say '  WINEDLLOVERRIDES="winhttp=n,b" %command%'
fi
say ''
say 'Then start the game from Steam. The TV shows an address like 192.168.1.20:8080 instead of'
say 'playwtd.com. Players on your Wi-Fi open that on their phones to join.'
say 'The game needs internet the first time so the mod can fetch the phone page from playwtd.com.'
[ "$MODE" = macos ] && say 'If macOS asks whether the game may accept incoming network connections, click Allow.'
say ''
say "Settings: $GAME_DIR/BepInEx/config/com.wtd.moreplayers.cfg (created after the first launch)"
