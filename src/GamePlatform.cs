using System;
using Microsoft.Win32;
using UnityEngine;

namespace WtdMorePlayers
{
    // Which kind of machine the game is running on. The mod works the same everywhere; this only changes
    // hints and the log. The Mac build runs natively, Linux and Steam Deck run the Windows build through Proton.
    internal static class GamePlatform
    {
        internal static readonly bool IsWine = DetectWine();
        internal static readonly string Name = Describe();

        // Real Windows, where the .bat tools work.
        internal static bool IsWindows => Application.platform == RuntimePlatform.WindowsPlayer && !IsWine;

        private static string Describe()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer: return IsWine ? "Windows game under Proton/Wine (Linux, Steam Deck or Mac)" : "Windows";
                case RuntimePlatform.OSXPlayer: return "macOS";
                case RuntimePlatform.LinuxPlayer: return "Linux";
                default: return Application.platform.ToString();
            }
        }

        private static bool DetectWine()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer)
                return false;
            if (Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH") != null || Environment.GetEnvironmentVariable("WINEPREFIX") != null)
                return true;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"Software\Wine"))
                    return key != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
