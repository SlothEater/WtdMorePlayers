using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WtdMorePlayers
{
    [BepInPlugin("com.wtd.moreplayers", "WTD More Players", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal const int VanillaMax = 6;

        internal static ConfigEntry<int> MaxPlayers;
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            MaxPlayers = Config.Bind("General", "MaxPlayers", 12,
                new ConfigDescription("Max active (dubbing) players. Vanilla is 6. Extra joiners still go to the audience. " +
                    "The online room holds 32 connections in total (host + players + audience), so audience space shrinks as this grows.",
                    new AcceptableValueRange<int>(3, 31)));
            PhonePage.Bind(Config);
            CustomClips.Bind(Config);
            LauncherWindow.Bind(Config);

            ExtendPlayerColors();
            new Harmony("com.wtd.moreplayers").PatchAll();
            Log.LogInfo($"WTD More Players loaded on {GamePlatform.Name}, MaxPlayers = {MaxPlayers.Value}");

            CustomClips.EnsureFolder();
            LauncherWindow.Init();

            // The game destroys BepInEx's manager object on scene loads, so run per-frame work on our own object.
            var host = new GameObject("WtdMorePlayers");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            var runner = host.AddComponent<Runner>();
            if (PhonePage.Enabled.Value)
                runner.StartCoroutine(PhonePage.Run());
        }

        private class Runner : MonoBehaviour
        {
            private bool _logged;

            private void Update()
            {
                if (!_logged)
                {
                    _logged = true;
                    Log.LogInfo("Runner active");
                }
                PhonePage.UpdateLabels();
                LauncherWindow.Update();
            }

            private void OnGUI() => LauncherWindow.OnGUI();

            private void OnDestroy() => PhonePage.Stop();
        }

        // Called from transpiled code in place of the literal 6.
        public static int GetMaxPlayers() => MaxPlayers.Value;

        // Vanilla room size starts at 7 (6 players + host); grow it by the extra player slots.
        public static int GetBaseRoomSize() => 7 + Math.Max(0, MaxPlayers.Value - VanillaMax);

        private static readonly Color[] ExtraColors =
        {
            new Color32(155, 89, 182, 255),  // purple
            new Color32(241, 196, 15, 255),  // yellow
            new Color32(230, 126, 34, 255),  // orange
            new Color32(26, 188, 156, 255),  // teal
            new Color32(192, 57, 43, 255),   // red
            new Color32(46, 204, 113, 255),  // green
            new Color32(52, 152, 219, 255),  // light blue
            new Color32(233, 30, 99, 255),   // magenta
            new Color32(121, 85, 72, 255),   // brown
            new Color32(158, 158, 158, 255), // grey
        };

        private static void ExtendPlayerColors()
        {
            var colors = GameDataHelper.playerColorsOptions.ToList();
            int i = 0;
            // MaxPlayers can change from the launcher window mid-session, so cover the whole range up front.
            while (colors.Count < ((AcceptableValueRange<int>)MaxPlayers.Description.AcceptableValues).MaxValue)
            {
                colors.Add(i < ExtraColors.Length
                    ? ExtraColors[i]
                    : Color.HSVToRGB((i * 0.618f) % 1f, 0.6f, 0.85f));
                i++;
            }
            GameDataHelper.playerColorsOptions = colors.ToArray();
        }

        // Replaces every `ldc.i4.6` in a method with a call to GetMaxPlayers().
        internal static IEnumerable<CodeInstruction> ReplaceSix(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var getMax = AccessTools.Method(typeof(Plugin), nameof(GetMaxPlayers));
            int count = 0;
            foreach (var ci in instructions)
            {
                if (ci.opcode == OpCodes.Ldc_I4_6)
                {
                    count++;
                    yield return new CodeInstruction(OpCodes.Call, getMax).MoveLabelsFrom(ci);
                }
                else
                {
                    yield return ci;
                }
            }
            Log.LogInfo($"Patched {count} player cap check(s) in {original.DeclaringType?.Name}.{original.Name}");
        }
    }

    // --- Lobby: let more than 6 people join as players ---

    [HarmonyPatch]
    internal static class LobbyCapPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(LocalPlayWebWaitForPlayersToJoinProjectorUi), typeof(LocalPlayWebWaitForPlayersToJoinUi) })
            {
                foreach (var name in new[] { "PlayerEnteredRoom", "UpdateTitle" })
                {
                    foreach (var m in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name))
                        yield return m;
                }
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => Plugin.ReplaceSix(instructions, original);
    }

    // --- Photon room size: make room for the extra players ---

    [HarmonyPatch]
    internal static class RoomSizePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LocalPlayWebWaitForPlayersToJoinProjectorUi), "CreateRoom");
            yield return AccessTools.Method(typeof(LocalPlayWebCreateGame), "CreateRoom");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var getBase = AccessTools.Method(typeof(Plugin), nameof(Plugin.GetBaseRoomSize));
            bool done = false;
            foreach (var ci in instructions)
            {
                if (!done && ci.opcode == OpCodes.Ldc_I4_7)
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call, getBase).MoveLabelsFrom(ci);
                }
                else
                {
                    yield return ci;
                }
            }
            Plugin.Log.LogInfo($"Room size patch {(done ? "applied" : "NOT applied")} in {original.DeclaringType?.Name}.{original.Name}");
        }
    }

    // The developer's Photon app refuses rooms above 32 actors, so trim the audience slots instead of failing.
    [HarmonyPatch]
    internal static class RoomCapPatch
    {
        internal const int PhotonRoomLimit = 32;

        private static IEnumerable<MethodBase> TargetMethods()
            => AccessTools.GetDeclaredMethods(typeof(PhotonNetwork))
                .Where(m => m.Name == nameof(PhotonNetwork.CreateRoom) && m.GetParameters().Any(p => p.ParameterType == typeof(RoomOptions)));

        private static void Prefix(RoomOptions roomOptions)
        {
            if (roomOptions != null && roomOptions.MaxPlayers > PhotonRoomLimit)
            {
                Plugin.Log.LogInfo($"Room size {roomOptions.MaxPlayers} capped to Photon limit {PhotonRoomLimit}");
                roomOptions.MaxPlayers = PhotonRoomLimit;
            }
        }
    }

    // --- Scoring: vanilla table drops back to 1350 above 11 votes ---

    [HarmonyPatch(typeof(GameDataHelper), "GetScoreForVotes")]
    internal static class ScoreForVotesPatch
    {
        private static void Postfix(int votes, ref int __result)
        {
            if (votes > 11)
                __result = 4000 + (votes - 11) * 700;
        }
    }

    // --- Result cards: voter-name layout only exists for up to 6 names ---

    [HarmonyPatch(typeof(RoundResultsCard), nameof(RoundResultsCard.SetupNames))]
    internal static class CardNamesPatch
    {
        private static void Prefix(ref List<PlayerVoteInfo> players)
        {
            if (players == null || players.Count <= 6)
                return;
            var trimmed = players.Take(5).ToList();
            trimmed.Add(new PlayerVoteInfo("+" + (players.Count - 5) + " more", GameDataHelper.audienceColor));
            players = trimmed;
        }
    }

    // --- Round results screen: clone extra cards and lay them out in a bigger grid ---

    internal static class ResultsLayout
    {
        private const float CardSpacingX = 523f;
        private const float CardSpacingY = 275f;

        internal static readonly AccessTools.FieldRef<RoundResultsUi, RoundResultsCard[]> Cards =
            AccessTools.FieldRefAccess<RoundResultsUi, RoundResultsCard[]>("_cards");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, RectTransform> CardHolder =
            AccessTools.FieldRefAccess<RoundResultsUi, RectTransform>("_cardHolder");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, Dictionary<int, Vector2[]>> CardPositions =
            AccessTools.FieldRefAccess<RoundResultsUi, Dictionary<int, Vector2[]>>("_cardPositions");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, AnimationCurve> EnterCurve =
            AccessTools.FieldRefAccess<RoundResultsUi, AnimationCurve>("_cardEnterAnimationCurve");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, AnimationCurve> FeaturedCurve =
            AccessTools.FieldRefAccess<RoundResultsUi, AnimationCurve>("_featuredCardAnimationCurve");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, AnimationCurve> BounceCurve =
            AccessTools.FieldRefAccess<RoundResultsUi, AnimationCurve>("_bounceAnimationCurve");
        internal static readonly AccessTools.FieldRef<RoundResultsUi, Image> Overlay =
            AccessTools.FieldRefAccess<RoundResultsUi, Image>("_overlay");

        internal static float CardScale = 1f;

        internal static void EnsureCards(RoundResultsUi ui, int count)
        {
            var cards = Cards(ui);
            if (cards.Length >= count)
                return;

            var list = cards.ToList();
            var source = cards[0].gameObject;
            bool wasActive = source.activeSelf;
            // Clone while inactive so the card's Awake runs on first enable, not during Instantiate.
            source.SetActive(false);
            while (list.Count < count)
            {
                var clone = UnityEngine.Object.Instantiate(source, source.transform.parent, false);
                // Awake adds a runtime "New Game Object" Text child; drop the copied one so Awake makes a fresh one.
                foreach (Transform child in clone.transform.Cast<Transform>().ToList())
                {
                    if (child.GetComponent<Text>() != null && child.name == "New Game Object")
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                clone.name = source.name + " (extra " + list.Count + ")";
                // Init() deactivates the card before it is ever shown, so force Awake to run now.
                clone.SetActive(true);
                clone.SetActive(false);
                list.Add(clone.GetComponent<RoundResultsCard>());
            }
            source.SetActive(wasActive);
            Cards(ui) = list.ToArray();
            Plugin.Log.LogInfo($"Results screen now has {list.Count} cards");
        }

        // Grid for 7+ cards; vanilla layouts are kept for 6 or fewer.
        internal static void EnsurePositions(RoundResultsUi ui, int n)
        {
            if (n <= Plugin.VanillaMax)
            {
                CardScale = 1f;
                return;
            }
            int cols = n <= 12 ? 3 : n <= 20 ? 4 : 5;
            int rows = Mathf.CeilToInt(n / (float)cols);
            CardScale = Mathf.Min(2f / cols, 3f / rows);
            float spacingX = CardSpacingX * 2f / cols;
            float spacingY = CardSpacingY * Mathf.Min(1f, 3f / rows);

            var positions = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int row = i / cols;
                int col = i % cols;
                int inRow = row == rows - 1 ? n - cols * (rows - 1) : cols;
                float x = (col - (inRow - 1) / 2f) * spacingX;
                float y = ((rows - 1) / 2f - row) * spacingY;
                positions[i] = new Vector2(x, y);
            }
            CardPositions(ui)[n] = positions;
        }
    }

    [HarmonyPatch(typeof(RoundResultsUi), "RaiseCards")]
    internal static class RaiseCardsPatch
    {
        private static bool Prefix(RoundResultsUi __instance, List<LeaderboardEntry> cardInfo, ref IEnumerator __result)
        {
            ResultsLayout.EnsureCards(__instance, cardInfo.Count);
            ResultsLayout.EnsurePositions(__instance, cardInfo.Count);
            __result = RaiseCards(__instance, cardInfo);
            return false;
        }

        // Same as vanilla RaiseCards, but applies the grid scale.
        private static IEnumerator RaiseCards(RoundResultsUi ui, List<LeaderboardEntry> cardInfo)
        {
            var cards = ResultsLayout.Cards(ui);
            var holder = ResultsLayout.CardHolder(ui);
            var curve = ResultsLayout.EnterCurve(ui);
            float scale = ResultsLayout.CardScale;

            holder.gameObject.SetActive(true);
            Vector2[] cardPositions = ResultsLayout.CardPositions(ui)[cardInfo.Count];
            for (int i = 0; i < cards.Length; i++)
            {
                if (cardPositions.Length <= i)
                {
                    cards[i].gameObject.SetActive(false);
                    continue;
                }
                cards[i].Init(cardInfo[i]);
                cards[i].transform.SetParent(holder);
                cards[i].transform.SetAsLastSibling();
                cards[i].GetRect().localScale = Vector3.one * scale;
            }
            yield return null;
            yield return null;
            for (int x = 0; x < cards.Length; x++)
            {
                if (cardPositions.Length > x)
                {
                    ui.StartCoroutine(cards[x].RaiseUp(cardPositions[x], 1f, curve));
                    yield return Yielders.Get(0.15f);
                }
            }
        }
    }

    [HarmonyPatch(typeof(RoundResultsUi), "FeatureCard")]
    internal static class FeatureCardPatch
    {
        private static bool Prefix(RoundResultsUi __instance, RoundResultsCard card, RoundResultsUi.FeaturedCardType cardType, ref IEnumerator __result)
        {
            __result = FeatureCard(__instance, card, cardType);
            return false;
        }

        // Same as vanilla FeatureCard, but returns the card to its grid scale instead of 1.
        private static IEnumerator FeatureCard(RoundResultsUi ui, RoundResultsCard card, RoundResultsUi.FeaturedCardType cardType)
        {
            var overlay = ResultsLayout.Overlay(ui);
            var featuredCurve = ResultsLayout.FeaturedCurve(ui);
            var bounceCurve = ResultsLayout.BounceCurve(ui);
            var holder = ResultsLayout.CardHolder(ui);

            overlay.SetAlpha(0f);
            overlay.enabled = true;
            overlay.gameObject.SetActive(true);
            RectTransform cardRect = card.GetRect();
            card.transform.SetParent(overlay.transform);
            float originalRotation = cardRect.GetRotation();
            if (originalRotation > 180f)
                originalRotation -= 360f;
            else if (originalRotation < -180f)
                originalRotation += 360f;
            Vector2 initialPosition = cardRect.anchoredPosition;
            Vector3 initialScale = Vector3.one * ResultsLayout.CardScale;
            Vector3 featuredScale = new Vector3(2f, 2f, 2f);
            float elapsed = 0f;
            AudioManager.Instance.PlayUiSfx(AudioManager.UiSfx.Swipe);
            while (elapsed <= 0.3f)
            {
                elapsed += Time.deltaTime;
                overlay.SetAlpha(Mathf.Lerp(0f, 85f / 128f, elapsed / 0.3f));
                cardRect.anchoredPosition = featuredCurve.Lerp(initialPosition, Vector2.zero, elapsed / 0.3f);
                cardRect.SetRotation(Mathf.Lerp(originalRotation, 0f, elapsed / 0.3f));
                cardRect.localScale = Vector3.Lerp(initialScale, featuredScale, elapsed / 0.3f);
                yield return null;
            }
            cardRect.SetRotation(0f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.localScale = featuredScale;
            yield return Yielders.Get(0.4f);
            yield return card.DisplayCardInfo(bounceCurve, cardType);
            yield return Yielders.Get(0.4f);
            elapsed = 0.3f;
            AudioManager.Instance.PlayUiSfx(AudioManager.UiSfx.Swipe);
            while (elapsed >= 0f)
            {
                elapsed -= Time.deltaTime;
                overlay.SetAlpha(Mathf.Lerp(0f, 85f / 128f, elapsed / 0.3f));
                cardRect.anchoredPosition = featuredCurve.Lerp(initialPosition, Vector2.zero, elapsed / 0.3f);
                cardRect.localScale = Vector3.Lerp(initialScale, featuredScale, elapsed / 0.3f);
                cardRect.SetRotation(Mathf.Lerp(originalRotation, 0f, elapsed / 0.3f));
                yield return null;
            }
            cardRect.SetRotation(0f);
            cardRect.anchoredPosition = initialPosition;
            cardRect.localScale = initialScale;
            card.transform.SetParent(holder);
        }
    }

    // --- Phone voting: playwtd.com only has 6 vote buttons, so give each phone its own list of at most 6 dubs ---

    internal static class PhoneVoteLimit
    {
        internal const int MaxVoteButtons = 6;
        private const string CurrentRoundDubsProperty = "current_round_dubs";

        private static readonly System.Random Rng = new System.Random();

        private static bool IsOwnDub(Dictionary<string, object> dub, string playerId)
            => dub.TryGetValue("playerIDs", out var ids) && ids is string[] arr && arr.Contains(playerId);

        // Moderators and phones on the mod's local page can show any number of dubs.
        private static bool CanShowEveryDub(Player player)
            => player.IsModerator() || player.CustomProperties.ContainsKey(PhonePage.FullVotesProperty);

        internal static bool EveryPhoneCanShowEveryDub()
            => PhotonNetwork.CurrentRoom.Players.Values.Where(p => !p.IsLocal).All(CanShowEveryDub);

        // Live voting: replace the broadcast VoteTime event with one targeted event per phone.
        internal static bool SplitVoteEvent(Dictionary<string, Dictionary<string, object>> dubs)
        {
            var shownCount = dubs.Keys.ToDictionary(k => k, k => 0);
            int sent = 0;
            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player.IsLocal)
                    continue;

                Dictionary<string, Dictionary<string, object>> toSend;
                if (CanShowEveryDub(player))
                {
                    toSend = dubs;
                }
                else
                {
                    string id = player.GetInternalPlayerID();
                    var picks = dubs.Where(kv => !IsOwnDub(kv.Value, id))
                        .OrderBy(kv => shownCount[kv.Key])
                        .ThenBy(_ => Rng.Next())
                        .Take(MaxVoteButtons)
                        .OrderBy(_ => Rng.Next())
                        .ToList();
                    foreach (var kv in picks)
                        shownCount[kv.Key]++;
                    toSend = picks.ToDictionary(kv => kv.Key, kv => kv.Value);
                }

                PhotonNetwork.RaiseEvent((byte)LocalPlayWebGameManager.LocalPlayEventType.VoteTime, toSend,
                    new RaiseEventOptions { TargetActors = new[] { player.ActorNumber } }, SendOptions.SendReliable);
                sent++;
            }
            Plugin.Log.LogInfo($"Split {dubs.Count} dubs into {sent} per-phone vote lists (max {MaxVoteButtons} each)");
            return false;
        }

        // Phones that (re)join mid-vote read this room property instead of the event; cap it too.
        internal static object CapRoomDubs(object value)
        {
            if (!(value is Dictionary<string, Dictionary<string, object>> dubs) || dubs.Count <= MaxVoteButtons)
                return value;
            return dubs.OrderBy(_ => Rng.Next()).Take(MaxVoteButtons).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    [HarmonyPatch(typeof(LocalPlayWebGameManager), nameof(LocalPlayWebGameManager.RaiseEvent))]
    internal static class VoteEventPatch
    {
        private static bool Prefix(LocalPlayWebGameManager.LocalPlayEventType eventType, object eventContent)
        {
            if (eventType != LocalPlayWebGameManager.LocalPlayEventType.VoteTime)
                return true;
            if (!(eventContent is Dictionary<string, Dictionary<string, object>> dubs) || dubs.Count <= PhoneVoteLimit.MaxVoteButtons)
                return true;
            try
            {
                return PhoneVoteLimit.EveryPhoneCanShowEveryDub() || PhoneVoteLimit.SplitVoteEvent(dubs);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Vote split failed, falling back to broadcast: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(LocalPlayWebGameManager), nameof(LocalPlayWebGameManager.SetRoomProperty))]
    internal static class RoomDubsPatch
    {
        private static void Prefix(string name, ref object value)
        {
            if (name == "current_round_dubs" && !PhoneVoteLimit.EveryPhoneCanShowEveryDub())
                value = PhoneVoteLimit.CapRoomDubs(value);
        }
    }

    // --- Leaderboard: clone extra score rows and rank labels ---

    [HarmonyPatch(typeof(RoundResultsScoreHolder), nameof(RoundResultsScoreHolder.UpdateLastRoundScores))]
    internal static class ScoreRowsPatch
    {
        private static readonly AccessTools.FieldRef<RoundResultsScoreHolder, RoundResultsScore[]> Scores =
            AccessTools.FieldRefAccess<RoundResultsScoreHolder, RoundResultsScore[]>("_scores");
        private static readonly AccessTools.FieldRef<RoundResultsScoreHolder, TMP_Text[]> RankLabels =
            AccessTools.FieldRefAccess<RoundResultsScoreHolder, TMP_Text[]>("_rankLabels");
        private static readonly AccessTools.FieldRef<RoundResultsScoreHolder, RectTransform[]> RankLabelRects =
            AccessTools.FieldRefAccess<RoundResultsScoreHolder, RectTransform[]>("_rankLabelRects");

        private static void Prefix(RoundResultsScoreHolder __instance, List<LeaderboardEntry> cardInfo)
        {
            var scores = Scores(__instance).ToList();
            var labels = RankLabels(__instance).ToList();
            var rects = RankLabelRects(__instance).ToList();
            int need = cardInfo.Count;
            if (scores.Count >= need && labels.Count >= need && rects.Count >= need)
                return;

            int last = scores.Count - 1;
            float step = rects[last].anchoredPosition.y - rects[last - 1].anchoredPosition.y;
            bool labelIsRect = labels[last].rectTransform == rects[last];

            while (scores.Count < need)
            {
                int index = scores.Count;

                var scoreGo = UnityEngine.Object.Instantiate(scores[last].gameObject, scores[last].transform.parent, false);
                scoreGo.name = scores[last].gameObject.name + " (extra " + index + ")";
                scores.Add(scoreGo.GetComponent<RoundResultsScore>());

                var labelGo = UnityEngine.Object.Instantiate(labels[last].gameObject, labels[last].transform.parent, false);
                labelGo.name = labels[last].gameObject.name + " (extra " + index + ")";
                var label = labelGo.GetComponent<TMP_Text>();
                label.text = RankText(labels[last].text, index + 1);
                labels.Add(label);

                RectTransform rect;
                if (labelIsRect)
                {
                    rect = label.rectTransform;
                }
                else
                {
                    var rectGo = UnityEngine.Object.Instantiate(rects[last].gameObject, rects[last].transform.parent, false);
                    rect = rectGo.GetComponent<RectTransform>();
                }
                var pos = rects[last].anchoredPosition;
                pos.y += step * (index - last);
                rect.anchoredPosition = pos;
                rects.Add(rect);
            }

            Scores(__instance) = scores.ToArray();
            RankLabels(__instance) = labels.ToArray();
            RankLabelRects(__instance) = rects.ToArray();
            Plugin.Log.LogInfo($"Leaderboard now has {scores.Count} rows");
        }

        // Rows that fit on screen at full size; longer leaderboards get squeezed.
        private const int MaxFullSizeRows = 14;
        // Vanilla raises the leaderboard by 70 + (rows - 2) * 25 to centre it.
        private const float RaisePerRow = 25f;
        private const float TwoDigitRankShift = 22f;

        private static readonly Dictionary<RoundResultsScoreHolder, (Vector2 first, float step)> Originals =
            new Dictionary<RoundResultsScoreHolder, (Vector2, float)>();

        private static void LayoutRows(RoundResultsScoreHolder holder, int count)
        {
            var scores = Scores(holder);
            var labels = RankLabels(holder);
            var rects = RankLabelRects(holder);
            if (!Originals.TryGetValue(holder, out var orig))
            {
                orig = (rects[0].anchoredPosition, rects[1].anchoredPosition.y - rects[0].anchoredPosition.y);
                Originals[holder] = orig;
            }

            float f = count > MaxFullSizeRows ? MaxFullSizeRows / (float)count : 1f;
            // Vanilla's raise assumes full-size rows; push the squeezed list down by the excess so it stays centred.
            float excessRaise = RaisePerRow * count * (1f - f);
            for (int i = 0; i < rects.Length; i++)
            {
                var pos = new Vector2(orig.first.x, orig.first.y - excessRaise + i * orig.step * f);
                if (i >= 9)
                    pos.x -= TwoDigitRankShift * f;
                rects[i].anchoredPosition = pos;
                rects[i].localScale = Vector3.one * f;
                if (labels[i].rectTransform != rects[i])
                    labels[i].rectTransform.localScale = Vector3.one * f;
                scores[i].GetRectTransform().localScale = Vector3.one * f;
            }
        }

        private static void Postfix(RoundResultsScoreHolder __instance, List<LeaderboardEntry> cardInfo)
        {
            LayoutRows(__instance, cardInfo.Count);
            // UpdateLastRoundScores already placed the score rows; re-snap them to the new label heights.
            var scores = Scores(__instance);
            var rects = RankLabelRects(__instance);
            for (int i = 0; i < scores.Length; i++)
            {
                var rt = scores[i].GetRectTransform();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rects[i].anchoredPosition.y);
            }
        }

        // Turns the template label (e.g. "6th" or "6.") into the label for rank n.
        private static string RankText(string template, int n)
        {
            var match = Regex.Match(template ?? "", @"\d+");
            if (!match.Success)
                return n.ToString();
            string rest = template.Substring(match.Index + match.Length);
            string suffixPattern = @"^(st|nd|rd|th)";
            if (Regex.IsMatch(rest, suffixPattern, RegexOptions.IgnoreCase))
                rest = Regex.Replace(rest, suffixPattern, Ordinal(n), RegexOptions.IgnoreCase);
            return template.Substring(0, match.Index) + n + rest;
        }

        private static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13)
                return "th";
            switch (n % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }
    }
}
