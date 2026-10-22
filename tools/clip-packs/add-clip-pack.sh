#!/usr/bin/env bash
# Adds a clip pack (zip or folder) to the game's CustomClips folder on macOS or Linux / Steam Deck.
# Needs python3 (and ffmpeg to convert videos); offers to install them if missing.
# Usage: ./add-clip-pack.sh MyPack.zip
# On Windows drag the pack onto "Add Clip Pack.bat" instead.

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[ $# -ge 1 ] || { echo 'Usage: ./add-clip-pack.sh <pack.zip or folder> [--game-dir DIR]'; exit 1; }

# Prints the command that installs the given packages with this system's package manager.
install_cmd() {
    if command -v brew >/dev/null; then echo "brew install $*"
    elif command -v apt-get >/dev/null; then echo "sudo apt-get install -y $*"
    elif command -v dnf >/dev/null; then echo "sudo dnf install -y $*"
    elif command -v pacman >/dev/null; then echo "sudo pacman -S --needed $*"
    elif command -v zypper >/dev/null; then echo "sudo zypper install -y $*"
    fi
}

offer_install() {  # <what> <package>
    local cmd
    cmd="$(install_cmd "$2")"
    if [ -z "$cmd" ]; then
        echo "Please install $1 with your package manager and try again."
        return 1
    fi
    read -r -p "Install $1 now with: $cmd ? [Y/n] " ans || ans=n
    case "$ans" in [nN]*) return 1 ;; esac
    $cmd
}

if ! command -v python3 >/dev/null; then
    echo 'Python 3 is needed to add clip packs.'
    offer_install 'Python 3' python3 || { echo "Or unzip the pack into the game's CustomClips folder yourself."; exit 1; }
fi
if ! command -v ffmpeg >/dev/null; then
    echo "ffmpeg converts videos the game can't play. Without it, videos are copied as-is."
    offer_install ffmpeg ffmpeg || true
fi

python3 -I "$SCRIPT_DIR/wtd_pack.py" add "$@" --force
