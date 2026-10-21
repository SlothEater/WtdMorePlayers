using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace WtdMorePlayers
{
    // Plays clips from <game>\CustomClips without uploading them to the Workshop or touching the game's files.
    //
    // A pack is any folder containing VideoClips\*.mp4 with matching Subtitles\*.srt (the Dub Editor export layout):
    //   CustomClips\MyPack\VideoClips\clip1.mp4
    //   CustomClips\MyPack\Subtitles\clip1.srt
    // VideoClips\ and Subtitles\ directly inside CustomClips also work.
    //
    // Clips are given ids like "local:MyPack:clip1.mp4", which the game passes back to the Workshop path lookups
    // below, the same way it handles "workshop:<id>:<file>" clips.
    internal static class CustomClips
    {
        private const string Prefix = "local:";
        private const string RootPackName = "Custom Clips";

        internal static ConfigEntry<bool> CustomOnly;
        internal static ConfigEntry<string> Folder;

        // clip id -> (pack folder, file name, pack display name)
        private static readonly Dictionary<string, (string dir, string file, string pack)> Clips =
            new Dictionary<string, (string, string, string)>();

        internal static string RootDir
        {
            get
            {
                string f = Folder.Value;
                return Path.IsPathRooted(f) ? f : Path.Combine(Paths.GameRootPath, f);
            }
        }

        internal static void Bind(ConfigFile config)
        {
            CustomOnly = config.Bind("Clips", "CustomOnly", false,
                "Only play clips from the custom clips folder (stock and Workshop clips are skipped). " +
                "Ignored when no custom clips are found.");
            Folder = config.Bind("Clips", "Folder", "CustomClips",
                "Folder holding your own clip packs. Relative paths are inside the game folder.");
        }

        internal static void EnsureFolder()
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                string readme = Path.Combine(RootDir, "README.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "Put your own clip packs here, one folder per pack:\r\n\r\n" +
                        "  CustomClips\\MyPack\\VideoClips\\clip1.mp4\r\n" +
                        "  CustomClips\\MyPack\\Subtitles\\clip1.srt\r\n\r\n" +
                        "An unzipped Dub Editor export already has this layout.\r\n" +
                        "Every .mp4 needs a .srt with the same name, containing exactly one [male_dub] or [female_dub] line.\r\n" +
                        "Videos must be H.264 MP4. To convert other videos, drag the pack onto 'Add Clip Pack.bat' from the mod download.\r\n");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not create custom clips folder {RootDir}: {e.Message}");
            }
        }

        // Rescans the folder; cheap enough to run every time a game starts.
        internal static List<string> Scan()
        {
            Clips.Clear();
            string root = RootDir;
            if (!Directory.Exists(root))
                return new List<string>();

            var packDirs = new List<(string dir, string name)> { (root, RootPackName) };
            packDirs.AddRange(Directory.GetDirectories(root).Select(d => (d, Path.GetFileName(d))));

            foreach (var (dir, name) in packDirs)
            {
                string videos = Path.Combine(dir, "VideoClips");
                string subs = Path.Combine(dir, "Subtitles");
                if (!Directory.Exists(videos) || !Directory.Exists(subs))
                    continue;
                foreach (string mp4 in Directory.GetFiles(videos, "*.mp4"))
                {
                    string file = Path.GetFileName(mp4);
                    string srt = Path.Combine(subs, Path.ChangeExtension(file, ".srt"));
                    if (!File.Exists(srt))
                    {
                        Plugin.Log.LogWarning($"Custom clip {name}/{file} skipped: no matching .srt");
                        continue;
                    }
                    if (!HasDubLine(srt))
                    {
                        Plugin.Log.LogWarning($"Custom clip {name}/{file} skipped: subtitles need one [male_dub] or [female_dub] line");
                        continue;
                    }
                    string folderKey = dir == root ? "." : name;
                    Clips[Prefix + folderKey + ":" + file] = (dir, file, name);
                }
            }
            return Clips.Keys.ToList();
        }

        private static bool HasDubLine(string srt)
        {
            try
            {
                string text = File.ReadAllText(srt).ToLowerInvariant();
                return text.Contains("[male_dub]") || text.Contains("[female_dub]");
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static int Count => Clips.Count;

        internal static bool TryGet(string clip, out (string dir, string file, string pack) info)
        {
            info = default;
            return clip != null && clip.StartsWith(Prefix) && Clips.TryGetValue(clip, out info);
        }
    }

    [HarmonyPatch(typeof(VideoClipFinder), MethodType.Constructor)]
    internal static class ClipListPatch
    {
        private static readonly AccessTools.FieldRef<VideoClipFinder, List<string>> AllClips =
            AccessTools.FieldRefAccess<VideoClipFinder, List<string>>("_allClips");

        private static void Postfix(VideoClipFinder __instance)
        {
            var custom = CustomClips.Scan();
            if (custom.Count == 0)
                return;
            var all = AllClips(__instance);
            if (CustomClips.CustomOnly.Value)
            {
                all.Clear();
                Plugin.Log.LogInfo($"Custom-only mode: playing {custom.Count} custom clip(s)");
            }
            else
            {
                Plugin.Log.LogInfo($"Added {custom.Count} custom clip(s) to {all.Count} other clips");
            }
            all.AddRange(custom);
        }
    }

    [HarmonyPatch(typeof(SteamWorkshopManager), nameof(SteamWorkshopManager.GetVideoClipFullPath))]
    internal static class ClipVideoPathPatch
    {
        private static bool Prefix(string clip, ref string __result)
        {
            if (!CustomClips.TryGet(clip, out var info))
                return true;
            __result = Path.Combine(Path.Combine(info.dir, "VideoClips"), info.file);
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamWorkshopManager), nameof(SteamWorkshopManager.GetSrtFullPath))]
    internal static class ClipSrtPathPatch
    {
        private static bool Prefix(string clip, ref string __result)
        {
            if (!CustomClips.TryGet(clip, out var info))
                return true;
            __result = Path.Combine(Path.Combine(info.dir, "Subtitles"), Path.ChangeExtension(info.file, ".srt"));
            return false;
        }
    }

    // Shows the pack name on screen during the clip, like Workshop packs do.
    [HarmonyPatch(typeof(SteamWorkshopManager), nameof(SteamWorkshopManager.GetVideoClipPackName))]
    internal static class ClipPackNamePatch
    {
        private static bool Prefix(string clip, ref string __result)
        {
            if (!CustomClips.TryGet(clip, out var info))
                return true;
            __result = info.pack;
            return false;
        }
    }
}
