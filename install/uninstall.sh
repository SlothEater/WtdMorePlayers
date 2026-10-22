#!/usr/bin/env bash
# Removes WTD More Players and the BepInEx mod loader from What The Dub?! on macOS or Linux / Steam Deck.
# Usage: bash uninstall.sh [--game-dir "/path/to/WhatTheDub"] [--keep-bepinex]
# On Windows use uninstall.bat instead.

set -euo pipefail

GAME_DIR=""
KEEP_BEPINEX=""
while [ $# -gt 0 ]; do
    case "$1" in
        --game-dir) GAME_DIR="${2:-}"; shift 2 ;;
        --keep-bepinex) KEEP_BEPINEX=1; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

is_game_dir() {
    [ -n "$1" ] && { [ -f "$1/WhatTheDub.exe" ] || ls -d "$1"/*.app >/dev/null 2>&1; }
}

if [ -z "$GAME_DIR" ]; then
    case "$(uname -s)" in
        Darwin) roots=("$HOME/Library/Application Support/Steam") ;;
        *) roots=("$HOME/.steam/steam" "$HOME/.local/share/Steam"
                  "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/snap/steam/common/.local/share/Steam") ;;
    esac
    for root in "${roots[@]}"; do
        [ -d "$root" ] || continue
        libs=("$root")
        vdf="$root/steamapps/libraryfolders.vdf"
        if [ -f "$vdf" ]; then
            while IFS= read -r lib; do libs+=("$lib"); done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)".*/\1/p' "$vdf")
        fi
        for lib in "${libs[@]}"; do
            if is_game_dir "$lib/steamapps/common/WhatTheDub"; then GAME_DIR="$lib/steamapps/common/WhatTheDub"; break 2; fi
        done
    done
fi
is_game_dir "$GAME_DIR" || { echo 'Could not find What The Dub?!. Pass the folder with --game-dir.'; exit 1; }
if pgrep -x 'WhatTheDub|WhatTheDub.exe' >/dev/null 2>&1; then
    echo 'What The Dub?! is running. Close the game and run the uninstaller again.'; exit 1
fi

plugin="$GAME_DIR/BepInEx/plugins/WtdMorePlayers.dll"
[ -f "$plugin" ] && rm -f "$plugin" && echo 'Removed WTD More Players.'

others="$(find "$GAME_DIR/BepInEx/plugins" -name '*.dll' 2>/dev/null | head -n 1 || true)"
if [ -n "$KEEP_BEPINEX" ] || [ -n "$others" ]; then
    [ -n "$others" ] && echo 'Other BepInEx plugins found, leaving BepInEx installed.'
else
    for item in BepInEx winhttp.dll doorstop_config.ini .doorstop_version changelog.txt run_bepinex.sh libdoorstop.dylib libdoorstop.so; do
        rm -rf "${GAME_DIR:?}/$item"
    done
    echo 'Removed BepInEx.'
fi
echo 'Done. Also clear the launch option in Steam (right-click the game > Properties > Launch Options).'
