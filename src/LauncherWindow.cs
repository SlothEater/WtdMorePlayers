using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WtdMorePlayers
{
    // Small settings window shown when the game starts (and on F8): player count and custom-clips-only mode.
    // Changes apply to the next game you host.
    internal static class LauncherWindow
    {
        internal static ConfigEntry<bool> ShowOnStart;
        internal static ConfigEntry<KeyboardShortcut> Hotkey;

        private const int WindowId = 0x57544450; // "WTDP"
        private const float RefHeight = 1080f;

        private static bool _open;
        private static Rect _rect = new Rect(0, 0, 640, 470);
        private static float _players;
        private static bool _customOnly;
        private static bool _showOnStart;
        private static EventSystem _pausedEventSystem;
        private static GUIStyle _title, _body, _small, _button, _toggle;

        internal static void Bind(ConfigFile config)
        {
            ShowOnStart = config.Bind("Launcher", "ShowOnStart", true, "Show the settings window when the game starts.");
            Hotkey = config.Bind("Launcher", "Hotkey", new KeyboardShortcut(KeyCode.F8), "Opens the settings window.");
        }

        private static bool _openWhenMenuReady;

        internal static void Init()
        {
            CustomClips.Scan();
            _openWhenMenuReady = ShowOnStart.Value;
        }

        internal static void Update()
        {
            // Wait for the menu's EventSystem so the window can block clicks behind it.
            if (_openWhenMenuReady && (EventSystem.current != null || Time.realtimeSinceStartup > 15f))
            {
                _openWhenMenuReady = false;
                Open();
            }
            if (Hotkey.Value.IsDown())
            {
                if (_open) Close(); else Open();
            }
        }

        private static void Open()
        {
            CustomClips.Scan();
            _players = Plugin.MaxPlayers.Value;
            _customOnly = CustomClips.CustomOnly.Value;
            _showOnStart = ShowOnStart.Value;
            _open = true;
            Plugin.Log.LogInfo($"Settings window opened (EventSystem: {EventSystem.current != null})");
            // Stop clicks reaching the game's menu behind the window.
            _pausedEventSystem = EventSystem.current;
            if (_pausedEventSystem != null)
                _pausedEventSystem.enabled = false;
        }

        private static void Close()
        {
            _open = false;
            if (_pausedEventSystem != null)
                _pausedEventSystem.enabled = true;
            _pausedEventSystem = null;
        }

        private static void Apply()
        {
            Plugin.MaxPlayers.Value = Mathf.RoundToInt(_players);
            CustomClips.CustomOnly.Value = _customOnly;
            ShowOnStart.Value = _showOnStart;
            Close();
        }

        internal static void OnGUI()
        {
            if (!_open)
                return;
            InitStyles();
            float scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _rect.x = (Screen.width / scale - _rect.width) / 2f;
            _rect.y = (RefHeight - _rect.height) / 2f;
            GUI.Box(new Rect(0, 0, Screen.width / scale, RefHeight), GUIContent.none); // dim the menu
            GUI.ModalWindow(WindowId, _rect, Draw, "WTD More Players");
        }

        private static void Draw(int id)
        {
            GUILayout.Space(16);

            int players = Mathf.RoundToInt(_players);
            GUILayout.Label($"Players: {players}", _title);
            _players = GUILayout.HorizontalSlider(_players, 3, 31);
            GUILayout.Space(6);
            // The online room holds 32: this PC, the players, then the audience.
            int audience = Mathf.Max(0, RoomCapPatch.PhotonRoomLimit - 1 - players);
            GUILayout.Label(players <= Plugin.VanillaMax
                ? "The normal game limit."
                : $"Up to {players} people write dubs. Room left for about {Mathf.Min(12, audience)} audience members.", _small);

            GUILayout.Space(20);
            int clips = CustomClips.Count;
            GUI.enabled = clips > 0;
            _customOnly = GUILayout.Toggle(_customOnly && clips > 0, "  Only play my custom clips", _toggle);
            GUI.enabled = true;
            GUILayout.Label(clips > 0
                ? $"{clips} custom clip(s) found in {CustomClips.RootDir}"
                : $"No custom clips found. Put clip packs in {CustomClips.RootDir}", _small);

            GUILayout.Space(20);
            GUILayout.Label(PhonePage.Address != null
                ? $"Phones join at:  {PhonePage.Address}   (same Wi-Fi)"
                : "Phones join at playwtd.com", _body);

            GUILayout.FlexibleSpace();
            _showOnStart = GUILayout.Toggle(_showOnStart, "  Show this window when the game starts (F8 opens it any time)", _small);
            GUILayout.Space(10);
            if (GUILayout.Button("Play", _button, GUILayout.Height(54)))
                Apply();
            GUILayout.Space(6);
        }

        private static void InitStyles()
        {
            if (_title != null)
                return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 24 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 28, fontStyle = FontStyle.Bold };
            GUI.skin.window.fontSize = 26;
            GUI.skin.horizontalSlider.fixedHeight = 22;
            GUI.skin.horizontalSliderThumb.fixedHeight = 22;
            GUI.skin.horizontalSliderThumb.fixedWidth = 22;
        }
    }
}
