"""Prepare clip packs for WTD More Players' custom clips folder.

The mod plays every pack in <game>\\CustomClips. A pack is a folder with VideoClips\\*.mp4
and matching Subtitles\\*.srt (the Dub Editor export layout). This tool copies a pack there
and fixes the usual problems on the way:
  - re-encodes videos the game can't play to H.264/AAC MP4 (needs ffmpeg)
  - sorts subtitles, trims overlaps and lines running past the end of the clip
  - skips clips without exactly one [male_dub] / [female_dub] line

Usage:
  python wtd_pack.py add <pack.zip|folder> [--name NAME] [--force]
  python wtd_pack.py remove <NAME>
  python wtd_pack.py list
"""

import argparse
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

DUB_TAGS = ("[male_dub]", "[female_dub]")


# ---------- locating the game ----------

def steam_libraries():
    libs = []
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as k:
            libs.append(Path(winreg.QueryValueEx(k, "SteamPath")[0]))
    except OSError:
        pass
    libs.append(Path(r"C:\Program Files (x86)\Steam"))
    for steam in list(libs):
        vdf = steam / "steamapps" / "libraryfolders.vdf"
        if vdf.is_file():
            for m in re.finditer(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="replace")):
                libs.append(Path(m.group(1).replace("\\\\", "\\")))
    return libs


def find_game_dir():
    for lib in steam_libraries():
        d = lib / "steamapps" / "common" / "WhatTheDub"
        if (d / "WhatTheDub.exe").is_file():
            return d
    return None


def clips_root(args) -> Path:
    game = args.game_dir or find_game_dir()
    if not game or not (Path(game) / "WhatTheDub.exe").is_file():
        sys.exit("Could not find What The Dub?!. Pass --game-dir \"<folder with WhatTheDub.exe>\".")
    root = Path(game) / "CustomClips"
    root.mkdir(exist_ok=True)
    return root


def find_ffmpeg():
    for exe in (shutil.which("ffmpeg"), r"C:\ffmpeg\bin\ffmpeg.exe"):
        if exe and Path(exe).exists():
            return exe
    return None


# ---------- SRT handling ----------

TIME_RE = re.compile(r"(\d+):(\d\d):(\d\d)[,.](\d{1,3})")


def parse_time(s: str) -> float:
    m = TIME_RE.search(s)
    if not m:
        raise ValueError(f"bad timestamp: {s!r}")
    h, mi, se, ms = m.groups()
    return int(h) * 3600 + int(mi) * 60 + int(se) + int(ms.ljust(3, "0")) / 1000


def fmt_time(t: float) -> str:
    ms_total = int(round(t * 1000))
    h, rem = divmod(ms_total, 3_600_000)
    m, rem = divmod(rem, 60_000)
    s, ms = divmod(rem, 1000)
    return f"{h:02}:{m:02}:{s:02},{ms:03}"


def parse_srt(text: str):
    blocks = []
    text = text.lstrip("\ufeff").replace("\r\n", "\n").replace("\r", "\n")
    for chunk in re.split(r"\n\s*\n", text.strip()):
        lines = chunk.split("\n")
        ti = next((i for i, l in enumerate(lines) if "-->" in l), None)
        if ti is None:
            continue
        a, b = lines[ti].split("-->", 1)
        body = "\n".join(lines[ti + 1:]).strip()
        if body:
            blocks.append([parse_time(a), parse_time(b), body])
    return blocks


def fix_srt(text: str, clip_len):
    """Sort, drop empties, clamp overlaps and clip-end overruns. Returns (srt, warnings)."""
    warnings = []
    blocks = parse_srt(text)
    if not blocks:
        raise ValueError("no subtitles")
    if blocks != sorted(blocks, key=lambda b: b[0]):
        warnings.append("subtitles were out of order; sorted")
    blocks.sort(key=lambda b: b[0])
    for i, blk in enumerate(blocks):
        if i + 1 < len(blocks) and blk[1] > blocks[i + 1][0]:
            warnings.append(f"line {i + 1} overlapped the next; trimmed")
            blk[1] = blocks[i + 1][0]
        if clip_len and blk[1] > clip_len:
            warnings.append(f"line {i + 1} ran past the end of the clip; trimmed")
            blk[1] = clip_len
    blocks = [b for b in blocks if b[1] > b[0]]
    dubs = sum(1 for b in blocks if b[2].strip().lower() in DUB_TAGS)
    if dubs != 1:
        raise ValueError(f"needs exactly one [male_dub]/[female_dub] line, found {dubs}")
    out = "\n\n".join(f"{i}\n{fmt_time(a)} --> {fmt_time(b)}\n{body}" for i, (a, b, body) in enumerate(blocks, 1))
    return out + "\n", warnings


# ---------- video handling ----------

def probe(ffmpeg, path: Path):
    if not ffmpeg:
        return {"duration": None, "vcodec": None, "pix_fmt": None, "acodec": None}
    r = subprocess.run([ffmpeg, "-hide_banner", "-i", str(path)], capture_output=True, text=True, errors="replace")
    info = r.stderr
    dur = re.search(r"Duration: (\d+:\d\d:\d\d\.\d+)", info)
    v = re.search(r"Stream #\S+.*?: Video: (\w+).*?, (\w+)[(,]", info)
    a = re.search(r"Stream #\S+.*?: Audio: (\w+)", info)
    return {
        "duration": parse_time(dur.group(1)) if dur else None,
        "vcodec": v.group(1) if v else None,
        "pix_fmt": v.group(2) if v else None,
        "acodec": a.group(1) if a else None,
    }


def needs_reencode(p) -> bool:
    return not (p["vcodec"] == "h264" and p["pix_fmt"] == "yuv420p" and p["acodec"] in ("aac", None))


def reencode(ffmpeg, src: Path, dst: Path):
    cmd = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(src),
           "-c:v", "libx264", "-profile:v", "high", "-pix_fmt", "yuv420p", "-crf", "20", "-preset", "medium",
           "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", str(dst)]
    subprocess.run(cmd, check=True)


# ---------- pack source ----------

def open_source(src: Path, tmp: Path) -> Path:
    if src.is_file() and src.suffix.lower() == ".zip":
        with zipfile.ZipFile(src) as z:
            z.extractall(tmp)
        root = tmp
    elif src.is_dir():
        root = src
    else:
        sys.exit(f"Not a zip or folder: {src}")
    for d in [root, *[p for p in root.rglob("*") if p.is_dir()]]:
        kids = {c.name.lower() for c in d.iterdir() if c.is_dir()}
        if "videoclips" in kids and "subtitles" in kids:
            return d
    sys.exit("The pack has no VideoClips and Subtitles folders.")


def subdir(d: Path, name: str) -> Path:
    return next(c for c in d.iterdir() if c.is_dir() and c.name.lower() == name.lower())


def safe_name(s: str) -> str:
    return re.sub(r"[^A-Za-z0-9 _-]+", "_", s).strip(" _") or "pack"


# ---------- commands ----------

def cmd_add(args):
    root = clips_root(args)
    ffmpeg = find_ffmpeg()
    if not ffmpeg:
        print("ffmpeg not found: videos will be copied as-is (they must already be H.264 MP4).")
        print("Get ffmpeg from https://www.gyan.dev/ffmpeg/builds/ to convert other videos.\n")
    src = Path(args.source).resolve()
    pack = safe_name(args.name or src.stem)
    dest = root / pack
    if dest.exists():
        if not args.force:
            sys.exit(f"A pack called '{pack}' already exists. Use --force to replace it, or --name to pick another name.")
        shutil.rmtree(dest)

    with tempfile.TemporaryDirectory() as t:
        tmp = Path(t)
        pack_src = open_source(src, tmp / "src")
        vdir, sdir = subdir(pack_src, "VideoClips"), subdir(pack_src, "Subtitles")
        srts = {p.stem.lower(): p for p in sdir.glob("*.srt")}
        out_v, out_s = tmp / "out" / "VideoClips", tmp / "out" / "Subtitles"
        out_v.mkdir(parents=True)
        out_s.mkdir(parents=True)
        ready, failed = 0, []

        for mp4 in sorted(vdir.glob("*.mp4")):
            srt = srts.get(mp4.stem.lower())
            if not srt:
                failed.append((mp4.name, "no matching .srt"))
                continue
            info = probe(ffmpeg, mp4)
            try:
                fixed, warns = fix_srt(srt.read_text(encoding="utf-8-sig", errors="replace"), info["duration"])
            except ValueError as e:
                failed.append((mp4.name, f"subtitles: {e}"))
                continue
            dst = out_v / mp4.name
            if ffmpeg and needs_reencode(info):
                print(f"  converting {mp4.name} ({info['vcodec']}/{info['pix_fmt']}/{info['acodec']} -> h264/yuv420p/aac)")
                try:
                    reencode(ffmpeg, mp4, dst)
                except subprocess.CalledProcessError:
                    failed.append((mp4.name, "ffmpeg could not convert it"))
                    continue
            else:
                shutil.copy2(mp4, dst)
            (out_s / f"{mp4.stem}.srt").write_text(fixed, encoding="utf-8")
            for w in warns:
                print(f"  {mp4.name}: {w}")
            ready += 1

        for name, why in failed:
            print(f"  SKIPPED {name}: {why}")
        if not ready:
            sys.exit("Nothing to add.")
        shutil.copytree(tmp / "out", dest)

    print(f"\nAdded pack '{pack}' with {ready} clip(s) to {dest}")
    print("It plays next time you host a game (press F8 in the game to turn on 'only my custom clips').")


def cmd_remove(args):
    root = clips_root(args)
    dest = root / args.name
    if not dest.is_dir():
        sys.exit(f"No pack called '{args.name}'. Packs: {', '.join(p.name for p in root.iterdir() if p.is_dir()) or 'none'}")
    shutil.rmtree(dest)
    print(f"Removed pack '{args.name}'")


def cmd_list(args):
    root = clips_root(args)
    packs = [p for p in root.iterdir() if p.is_dir() and (p / "VideoClips").is_dir()]
    if not packs:
        print(f"No packs in {root}")
    for p in packs:
        print(f"{p.name}: {len(list((p / 'VideoClips').glob('*.mp4')))} clip(s)")


def main():
    ap = argparse.ArgumentParser(description="Custom clip packs for WTD More Players")
    ap.add_argument("--game-dir", type=Path)
    sp = ap.add_subparsers(dest="cmd", required=True)
    p = sp.add_parser("add", help="add a pack (zip or folder)")
    p.add_argument("source")
    p.add_argument("--name")
    p.add_argument("--force", action="store_true")
    p.set_defaults(fn=cmd_add)
    p = sp.add_parser("remove", help="remove a pack")
    p.add_argument("name")
    p.set_defaults(fn=cmd_remove)
    p = sp.add_parser("list", help="list packs")
    p.set_defaults(fn=cmd_list)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
