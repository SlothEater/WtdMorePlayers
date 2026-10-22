# WTD More Players

A homebrew mod for [What The Dub?!](https://store.steampowered.com/app/1495860/What_The_Dub/) on Steam.

- **More players:** raises the limit from 6 to as many as 31. Everyone writes dubs and everyone votes on every dub.
- **Your own clips:** play clip packs you made yourself (for example with the Dub Editor), straight from a
  folder. Nothing gets uploaded to the Workshop. You can also play *only* your own clips.
- **A settings window when the game starts:** pick the number of players and turn custom-clips-only on or off.

## Install

You need What The Dub?! on Steam. Works on:

| Your computer | How the game runs | Installer |
|---|---|---|
| Windows | Windows game | `install.bat` |
| Mac | Native Mac game | `install.sh` |
| Linux, Steam Deck | Windows game through Proton | `install.sh` |

The installer works out which one you have by itself.

### Windows

**Option A: download (easiest)**

1. Go to [Releases](https://github.com/SlothEater/WtdMorePlayers/releases/latest) and download `WtdMorePlayers.zip`.
2. Extract it anywhere.
3. Double-click **`install.bat`**.

**Option B: clone the repo**

```
git clone https://github.com/SlothEater/WtdMorePlayers.git
WtdMorePlayers\install\install.bat
```

The installer finds the game through Steam, installs the [BepInEx](https://github.com/BepInEx/BepInEx) mod
loader, and adds the mod. When you clone the repo you don't need to build anything: the installer downloads
the latest released mod file. If the game is installed somewhere unusual, it asks you for the folder.

It also adds a Windows Firewall rule so phones on your network can reach the game. Windows asks for
permission for this. If you say no, click **Allow** when Windows asks the first time the game starts.

### Mac, Linux and Steam Deck

1. Download `WtdMorePlayers.zip` from [Releases](https://github.com/SlothEater/WtdMorePlayers/releases/latest)
   and extract it. (Steam Deck: do this in Desktop Mode.)
2. Open a terminal in the extracted folder and run:
   ```
   bash install.sh
   ```
3. The installer ends by printing one line to paste into Steam: right-click What The Dub?! > **Properties** >
   **General** > **Launch Options**. It's one of these:
   - **Mac:** `"/path/to/WhatTheDub/run_bepinex.sh" %command%` (the installer prints your exact path)
   - **Linux / Steam Deck:** `WINEDLLOVERRIDES="winhttp=n,b" %command%`

   This tells Steam to start the mod loader with the game. Without it the game starts without the mod.

On Linux, if the `ufw` firewall is on, the installer tells you the command that lets phones connect. On a Mac,
click **Allow** if macOS asks whether the game may accept incoming connections. To add clip packs, run
`bash add-clip-pack.sh MyPack.zip` (it offers to install Python and ffmpeg if they're missing).

> The Mac and Proton modes use the official BepInEx builds for those systems and were tested up to the install
> step. They haven't been played on a real Mac or Steam Deck yet. If something goes wrong, please open an
> [issue](https://github.com/SlothEater/WtdMorePlayers/issues) with `BepInEx/LogOutput.log`.

### What you need, and what's automatic

| Needed | How you get it |
|---|---|
| What The Dub?! on Steam | Your own copy. Nothing from the game is included here. |
| BepInEx mod loader | Downloaded by the installer. |
| The mod | In the zip, or downloaded by the installer. |
| The phone page players join on | **Automatic.** Each time the game starts, the mod downloads the official page from playwtd.com, adds the extra vote buttons and serves it from your PC. It's never stored in this repo. The last copy is kept in `BepInEx\cache`, so it still works if you're offline later. The very first launch needs internet. |
| Firewall permission | Windows: added by the installer (asks for permission). Mac/Linux: see above. |
| Network set to **Private** | Windows: the installer warns you if it isn't. Windows blocks phones on *Public* networks. |
| Steam launch option | Mac/Linux only: the installer prints the line to paste. |
| Python and ffmpeg | **Only** for adding clip packs. `Add Clip Pack.bat` / `add-clip-pack.sh` offer to install them. |

## Playing

1. Start What The Dub?! from Steam. A **WTD More Players** window opens. Pick the number of players, tick
   *Only play my custom clips* if you want that, and press **Play**. Press **F8** at any time to open the window again.
2. Host a game as normal.
3. Where the TV normally says **playwtd.com**, it now shows an address like **`192.168.1.20:8080`**.
4. Players on the **same Wi-Fi** open that address on their phones and join with the room code as usual.

The first time, Windows may ask whether What The Dub?! can use the network. Click **Allow** (private networks).

Players who aren't on your Wi-Fi (for example, joining through a stream) can still use playwtd.com. It works,
but the playwtd.com page only has room for 6 choices on the vote screen, so the mod gives each of those
phones a fair, shuffled selection of 6 dubs to vote from. Phones using the address on the TV see every dub.

## Your own clips

The mod plays every clip pack in the **`CustomClips`** folder inside the game folder. The mod creates that
folder the first time the game runs. Each pack is a folder with the videos and their subtitles:

```
WhatTheDub\CustomClips\
    MyPack\
        VideoClips\clip1.mp4
        Subtitles\clip1.srt
```

An unzipped **Dub Editor** export already has this layout, so you can unzip it straight into `CustomClips`.
Each `.srt` needs exactly one `[male_dub]` or `[female_dub]` line, marking where the players' dubs go.

Custom clips are mixed in with the normal clips. To play **only** your own clips, tick *Only play my custom clips*
in the settings window (F8). Your clips show their pack name on screen while they play.

### Converting videos

The game only plays **H.264 MP4** videos. If a pack has other formats, or its subtitles overlap or run past the
end of the clip, **drag the pack (zip or folder) onto `Add Clip Pack.bat`** (in the download, or
`tools\clip-packs` in the repo). It converts the videos, fixes the subtitles, and copies the pack into
`CustomClips`. It needs [Python](https://www.python.org/downloads/) and [ffmpeg](https://www.gyan.dev/ffmpeg/builds/),
and offers to install both with winget if they're missing.

```
python tools\clip-packs\wtd_pack.py add MyPack.zip     # add or convert a pack
python tools\clip-packs\wtd_pack.py list               # show packs
python tools\clip-packs\wtd_pack.py remove MyPack      # delete a pack
```

## Settings

The settings window (F8) covers the usual options. Everything else is in the settings file, which is created
after you launch the game once with the mod:

`<game folder>\BepInEx\config\com.wtd.moreplayers.cfg`

| Setting | Default | What it does |
|---|---|---|
| `MaxPlayers` | `12` | How many people play (write dubs). Anyone after that joins the audience. Can go up to `31`. |
| `PhonePage.Enabled` | `true` | Serve the phone page from your PC so every phone can vote on every dub. |
| `PhonePage.Port` | `8080` | Change this if something else on your PC already uses port 8080. |
| `Clips.CustomOnly` | `false` | Only play clips from the custom clips folder. |
| `Clips.Folder` | `CustomClips` | Where your clip packs live. A relative path is inside the game folder. |
| `Launcher.ShowOnStart` | `true` | Show the settings window when the game starts. |
| `Launcher.Hotkey` | `F8` | The key that opens the settings window. |

Settings changed in the window take effect for the next game you host. If you edit the file instead,
restart the game afterwards.

**About the 31 limit:** the online service the game uses allows 32 connections per room. That covers your
PC, the players and the audience together. With `MaxPlayers = 31` there's no room left for an audience. With
`MaxPlayers = 20` there's room for about 10 audience members.

**About big games:** the clip replays once for every dub, so each round with 20 players takes several minutes
of playback. Around 12 to 16 players feels best.

## Uninstall

Double-click **`uninstall.bat`**. It removes the mod, BepInEx and the firewall rule, and the game goes back to normal.
On Mac/Linux run `bash uninstall.sh`, then clear the Launch Options box in Steam.
Your `CustomClips` folder is kept. Delete it yourself if you don't want your clips any more.

## Troubleshooting

**The TV is stuck on "Connecting..."**
`MaxPlayers` is probably set higher than 31. Lower it and restart the game.

**Phones can't open the address on the TV**
- The phone must be on the same Wi-Fi as the PC, not mobile data or a guest network. Many guest networks
  block devices from seeing each other.
- Run `install.bat` again and accept the permission prompt: it adds the firewall rule. Or check Windows
  Security > Firewall > Allow an app, and allow What The Dub?! on Private networks.
- Your PC's network must be set to Private (Settings > Network & internet > your connection).
- Type the address exactly as shown, including `:8080`. If the browser complains, put `http://` in front.

**The TV still says playwtd.com**
The mod couldn't download or patch the phone page (no internet at startup, or playwtd.com changed). Players can
still join at playwtd.com and get the 6-choice vote screen.

**Mac/Linux: no settings window, the game looks normal**
The mod loader didn't start. Check the Launch Options line in Steam (see install). If
`BepInEx/LogOutput.log` doesn't exist in the game folder after starting the game, the loader never ran.

**Something else broke**
Send the log from `<game folder>\BepInEx\LogOutput.log` in an
[issue](https://github.com/SlothEater/WtdMorePlayers/issues).

## Known limitations

- The cartoon theatre on the "waiting for players" screen only has 6 seats, so it only shows the first 6 players.
  Everyone still plays normally.
- Twitch integration (Twitch chat picking clips or voting) isn't supported with more than 6 players.
- The "Full House" achievement only unlocks with exactly 6 players.

## How it works

The game sets its limit of 6 on the PC that hosts, so the mod raises it there:

- **Lobby:** the "first 6 are players, the rest are audience" check now uses `MaxPlayers`, and the online
  room is made bigger (up to the 32 limit).
- **Results screen:** extra result cards and leaderboard rows are added, and the layout shrinks to fit:
  a 3, 4 or 5-wide grid of cards and a smaller leaderboard.
- **Scoring and colours:** more player colours, and a points table that goes past 11 votes.
- **Vote screen:** the playwtd.com phone page has exactly 6 vote buttons. When the game starts, the mod
  downloads that page from playwtd.com, adds code that creates as many buttons as needed, and serves it on your
  local network. The page itself isn't included in this repo. Phones on the patched page get every dub;
  phones still on playwtd.com get a balanced selection of 6.

All of this happens in memory using [Harmony](https://github.com/pardeike/Harmony) patches. The game's own
files aren't modified.

## Building from source

Needs the [.NET SDK](https://dotnet.microsoft.com/download) (any recent version) and the game, with BepInEx
installed (run `install\install.bat` once).

```
cd src
dotnet build -c Release
```

The built file is copied into the game's `BepInEx\plugins` folder automatically. If the game isn't in the
default Steam folder:

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\WhatTheDub"
```

`tools/test-bots` has a script that joins up to 30 fake phone players to a room for testing. See its README.

## Disclaimer

This is an unofficial fan mod. It isn't affiliated with or endorsed by Wide Right Interactive. You need your
own copy of the game. No game files are included. Please support the developers and
[buy the game](https://store.steampowered.com/app/1495860/What_The_Dub/).
