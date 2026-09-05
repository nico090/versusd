using System.Collections.Generic;
using System.Text;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameState;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Client-side in-game HUD for the PvPvE deathmatch: countdown timer (with the x2 marker during
    /// the final phase), live scoreboard, phase announcements and kill feed.
    /// Self-builds its Canvas UI when serialized refs are not wired.
    /// Discovers NetworkGameState at runtime and subscribes to its SyncVar/SyncList callbacks.
    /// </summary>
    public class DeathmatchHUD : MonoBehaviour
    {
        public static void EnsureInstance()
        {
            if (FindAnyObjectByType<DeathmatchHUD>() == null)
                new GameObject("DeathmatchHUD").AddComponent<DeathmatchHUD>();
        }

        [SerializeField] Text m_TimerText;

        /// <summary>
        /// The word under the clock while the warm-up runs. The countdown up there is standing in
        /// for the match clock, and a green number on its own does not say which of the two it is;
        /// this does, for anyone who joined after the banner had already come and gone.
        /// </summary>
        [SerializeField] Text m_TimerLabel;
        [SerializeField] Text m_ScoreboardText;
        [SerializeField] Text m_AnnouncementText;
        [SerializeField] Text m_KillFeedText;

        NetworkGameState m_NetworkGameState;

        /// <summary>
        /// Locally ticked copy of the countdown. The server only publishes the timer once a second
        /// (see NetworkGameState.Update), so without this the display would visibly stutter; each
        /// sync snaps it back to the authoritative value.
        /// </summary>
        float m_DisplayedTime;

        /// <summary>Locally ticked copy of the warm-up clock, for the same reason as
        /// <see cref="m_DisplayedTime"/> — the server only publishes it once a second.</summary>
        float m_DisplayedWarmup;

        /// <summary>
        /// Clearance kept at the top right for the pause button. Everything this HUD draws in that
        /// corner starts below it, which is what stops the scoreboard and the corner icon from
        /// sharing pixels the way the scoreboard and the sample's gear used to.
        /// </summary>
        const float k_TopRightGutter = 92f;

        const float k_AnnouncementSeconds = 3.5f;
        const float k_KillFeedSeconds = 4f;
        const int k_KillFeedMaxLines = 4;

        float m_AnnouncementHideAt;

        readonly List<(string text, float expiresAt)> m_KillFeed = new List<(string, float)>();

        static readonly Color k_DoubleKillsColor = HudSkin.Gold; // the x2 marker is a reward

        /// <summary>
        /// Warm-up countdown: violet, so it can't be mistaken for the match clock (blue) or for
        /// its last thirty seconds (red). Was green, which was the only green left in the game.
        /// </summary>
        static readonly Color k_WarmupColor = HudSkin.AccentViolet;

        /// <summary>
        /// Rank colours for the first three places: gold, silver, bronze. Standings are the one
        /// thing a player reads mid-fight, and a colour is read faster than a number.
        /// </summary>
        static readonly string[] k_RankColors = { "FFD34D", "D8E0EA", "E39A5A" };

        /// <summary>The row belonging to this client, in the HUD accent.</summary>
        const string k_LocalRowColor = "7FE0FF";

        void Start()
        {
            TryFindAndSubscribe();
        }

        void Update()
        {
            if (m_NetworkGameState == null)
            {
                TryFindAndSubscribe();
                return;
            }

            TickLocalTimer();
            ExpireAnnouncement();
            ExpireKillFeed();
        }

        void TryFindAndSubscribe()
        {
            m_NetworkGameState = Object.FindAnyObjectByType<NetworkGameState>();
            if (m_NetworkGameState == null) return;

            EnsureUI();
            m_NetworkGameState.OnTimeRemainingChangedEvent += OnTimerChanged;
            m_NetworkGameState.OnWarmupRemainingChangedEvent += OnWarmupChanged;
            m_NetworkGameState.OnPhaseChangedEvent += OnPhaseChanged;
            m_NetworkGameState.OnPhaseAnnouncedEvent += OnPhaseAnnounced;
            m_NetworkGameState.OnKillFeedEvent += OnKill;
            m_NetworkGameState.Scores.Callback += OnScoresChanged;

            m_DisplayedTime = m_NetworkGameState.TimeRemaining;
            m_DisplayedWarmup = m_NetworkGameState.WarmupRemaining;
            RefreshTimer();
            RefreshScoreboard();
        }

        void OnDestroy()
        {
            if (m_NetworkGameState == null) return;
            m_NetworkGameState.OnTimeRemainingChangedEvent -= OnTimerChanged;
            m_NetworkGameState.OnWarmupRemainingChangedEvent -= OnWarmupChanged;
            m_NetworkGameState.OnPhaseChangedEvent -= OnPhaseChanged;
            m_NetworkGameState.OnPhaseAnnouncedEvent -= OnPhaseAnnounced;
            m_NetworkGameState.OnKillFeedEvent -= OnKill;
            m_NetworkGameState.Scores.Callback -= OnScoresChanged;
        }

        void OnTimerChanged(float _, float newVal)
        {
            m_DisplayedTime = newVal;
            RefreshTimer();
        }

        void OnWarmupChanged(float _, float newVal)
        {
            m_DisplayedWarmup = newVal;
            RefreshTimer();
        }

        void OnPhaseChanged(MatchPhase _, MatchPhase __) => RefreshTimer();

        void OnScoresChanged(SyncList<ScoreEntry>.Operation op, int index, ScoreEntry old, ScoreEntry @new)
            => RefreshScoreboard();

        void TickLocalTimer()
        {
            if (m_NetworkGameState.Phase == MatchPhase.Warmup)
            {
                if (m_DisplayedWarmup > 0f)
                {
                    m_DisplayedWarmup = Mathf.Max(0f, m_DisplayedWarmup - Time.deltaTime);
                    RefreshTimer();
                }

                return;
            }

            if (m_NetworkGameState.Phase != MatchPhase.Normal && m_NetworkGameState.Phase != MatchPhase.DoubleKills)
            {
                return;
            }

            if (m_DisplayedTime <= 0f) return;

            m_DisplayedTime = Mathf.Max(0f, m_DisplayedTime - Time.deltaTime);
            RefreshTimer();
        }

        void RefreshTimer()
        {
            if (m_TimerText == null || m_NetworkGameState == null) return;

            // The match clock stands still at its full length through the warm-up, so showing it
            // would be a number that never moves. The warm-up's own countdown goes in its place,
            // which is also what tells a player who missed the banner why nothing is happening yet.
            if (m_NetworkGameState.Phase == MatchPhase.Warmup)
            {
                int warmupSecs = Mathf.CeilToInt(m_DisplayedWarmup);
                // Same mm:ss shape as the match clock it stands in for, so it reads as a clock
                // rather than as a stray number.
                m_TimerText.text = $"{warmupSecs / 60}:{warmupSecs % 60:D2}";
                m_TimerText.color = k_WarmupColor;
                ShowTimerLabel(true);
                return;
            }

            ShowTimerLabel(false);

            float seconds = m_NetworkGameState.Phase == MatchPhase.Ended ? 0f : m_DisplayedTime;
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);

            bool doubleKills = m_NetworkGameState.Phase == MatchPhase.DoubleKills;
            m_TimerText.text = doubleKills ? $"{mins}:{secs:D2}  x2" : $"{mins}:{secs:D2}";

            m_TimerText.color = doubleKills
                ? k_DoubleKillsColor
                : (seconds <= 30f ? Color.red : HudSkin.AccentBlue);
        }

        /// <summary>
        /// Turns the warm-up caption under the clock on or off. Toggled by object rather than by
        /// blanking the string so it takes no layout space once the match is running.
        /// </summary>
        void ShowTimerLabel(bool show)
        {
            if (m_TimerLabel != null && m_TimerLabel.gameObject.activeSelf != show)
            {
                m_TimerLabel.gameObject.SetActive(show);
            }
        }

        // ---------------------------------------------------------------- announcements

        void OnPhaseAnnounced(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Warmup:
                    ShowAnnouncement("CALENTAMIENTO", k_WarmupColor);
                    break;
                case MatchPhase.Normal:
                    // Only ever announced coming out of the warm-up: Normal is the phase the match
                    // starts in, and SetPhase is the only thing that fires this.
                    ShowAnnouncement("¡EMPIEZA LA PARTIDA!", Color.white);
                    break;
                case MatchPhase.DoubleKills:
                    ShowAnnouncement("¡KILLS DOBLES!", k_DoubleKillsColor);
                    break;
                case MatchPhase.Ended:
                    ShowAnnouncement("¡SE ACABÓ EL TIEMPO!", Color.white);
                    break;
            }
        }

        void ShowAnnouncement(string text, Color color)
        {
            if (m_AnnouncementText == null) return;
            m_AnnouncementText.text = text;
            m_AnnouncementText.color = color;
            m_AnnouncementHideAt = Time.time + k_AnnouncementSeconds;
        }

        void ExpireAnnouncement()
        {
            if (m_AnnouncementText == null) return;
            if (m_AnnouncementHideAt > 0f && Time.time >= m_AnnouncementHideAt)
            {
                m_AnnouncementText.text = string.Empty;
                m_AnnouncementHideAt = 0f;
            }
        }

        // ------------------------------------------------------------------- kill feed

        void OnKill(string killerName, string victimName, int points)
        {
            m_KillFeed.Add(($"{killerName} mató a {victimName}  (+{points})", Time.time + k_KillFeedSeconds));
            while (m_KillFeed.Count > k_KillFeedMaxLines)
            {
                m_KillFeed.RemoveAt(0);
            }
            RefreshKillFeed();
        }

        void ExpireKillFeed()
        {
            bool removed = false;
            for (int i = m_KillFeed.Count - 1; i >= 0; i--)
            {
                if (Time.time >= m_KillFeed[i].expiresAt)
                {
                    m_KillFeed.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed) RefreshKillFeed();
        }

        void RefreshKillFeed()
        {
            if (m_KillFeedText == null) return;

            var sb = new StringBuilder();
            foreach (var line in m_KillFeed)
            {
                sb.AppendLine(line.text);
            }
            m_KillFeedText.text = sb.ToString();
        }

        // ------------------------------------------------------------------ scoreboard

        void RefreshScoreboard()
        {
            if (m_ScoreboardText == null || m_NetworkGameState == null) return;

            var sorted = new List<ScoreEntry>(m_NetworkGameState.Scores.Count);
            for (int i = 0; i < m_NetworkGameState.Scores.Count; i++)
                sorted.Add(m_NetworkGameState.Scores[i]);
            sorted.Sort(ScoreEntry.CompareForRanking);

            // Highlight our own row. Match on the stable master-server PlayerId — ScoreEntry.ClientId
            // is the server-side connectionId, which a remote client never learns (its own
            // LocalConnectionId is always 0), so it can't be used to find "me".
            string localPlayerId = ClientAuthPayload.Current?.PlayerId;

            var sb = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                bool isLocal = !string.IsNullOrEmpty(localPlayerId) && sorted[i].PlayerId == localPlayerId;
                string crown = sorted[i].KilledBoss ? " ☠" : string.Empty;
                if (isLocal)
                    sb.AppendLine($"<b>> {sorted[i].PlayerName}  {sorted[i].Score}pts{crown}</b>");
                else
                    sb.AppendLine($"{i + 1}. {sorted[i].PlayerName}  {sorted[i].Score}pts{crown}");
            }
            m_ScoreboardText.text = sb.ToString();
        }

        // Builds a minimal Screen Space Overlay Canvas with the HUD's Text widgets.
        void EnsureUI()
        {
            if (m_TimerText != null && m_TimerLabel != null && m_ScoreboardText != null
                && m_AnnouncementText != null && m_KillFeedText != null) return;

            var canvasGO = new GameObject("DeathmatchHUD_Canvas");
            canvasGO.transform.SetParent(transform);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Timer — top-center
            if (m_TimerText == null)
            {
                m_TimerText = CreateText(canvasGO, "Timer",
                    anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                    position: new Vector2(0f, -16f), size: new Vector2(260f, 60f),
                    alignment: TextAnchor.UpperCenter, fontSize: 42, bold: true);
                HudSkin.WrapInPanel(m_TimerText.rectTransform, new Vector2(0f, 4f));
                HudSkin.StyleText(m_TimerText);
            }

            // Warm-up caption — directly under the clock's panel, which ends at -84.
            if (m_TimerLabel == null)
            {
                m_TimerLabel = CreateText(canvasGO, "TimerLabel",
                    anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                    position: new Vector2(0f, -86f), size: new Vector2(260f, 22f),
                    alignment: TextAnchor.UpperCenter, fontSize: 18, bold: true);
                m_TimerLabel.text = "CALENTAMIENTO";
                HudSkin.StyleText(m_TimerLabel);
                // StyleText owns the colour it applies, so the warm-up green goes back on after it.
                m_TimerLabel.color = k_WarmupColor;
                m_TimerLabel.gameObject.SetActive(false);
            }

            // Scoreboard — top-right, under the pause button PauseMenuUI parks in the corner.
            // The sample's own gear used to sit on top of these rows; k_TopRightGutter is the
            // clearance both now share.
            if (m_ScoreboardText == null)
            {
                var header = CreateText(canvasGO, "ScoreboardHeader",
                    anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                    position: new Vector2(-16f, -k_TopRightGutter), size: new Vector2(230f, 26f),
                    alignment: TextAnchor.UpperRight, fontSize: 16, bold: true);
                header.text = "MARCADOR";
                HudSkin.StyleText(header, dim: true);
                AddIcon(header.rectTransform, UIIcons.Icon.Trophy, HudSkin.AccentBlue, 22f);

                m_ScoreboardText = CreateText(canvasGO, "Scoreboard",
                    anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                    position: new Vector2(-16f, -(k_TopRightGutter + 30f)), size: new Vector2(260f, 320f),
                    alignment: TextAnchor.UpperRight, fontSize: 18, bold: false);
                // The scoreboard's rect is tall on purpose (it grows with the roster), so the
                // panel is not wrapped around the whole rect — a mostly-empty 320px slab reads
                // worse than no panel. The text carries an outline instead and stays readable
                // over anything.
                HudSkin.StyleText(m_ScoreboardText);
            }

            // Phase announcement — upper third, centred, big
            if (m_AnnouncementText == null)
            {
                m_AnnouncementText = CreateText(canvasGO, "Announcement",
                    anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                    position: new Vector2(0f, -110f), size: new Vector2(640f, 80f),
                    alignment: TextAnchor.UpperCenter, fontSize: 34, bold: true);
                m_AnnouncementText.text = string.Empty;
                // No panel: announcements are momentary and centre-screen, and a box that pops
                // in and out there reads as a dialog. The outline alone carries readability, and
                // ShowAnnouncement keeps ownership of the colour.
                HudSkin.StyleText(m_AnnouncementText);
            }

            // Kill feed — under the scoreboard, right-aligned
            if (m_KillFeedText == null)
            {
                m_KillFeedText = CreateText(canvasGO, "KillFeed",
                    anchor: new Vector2(1f, 1f), pivot: new Vector2(1f, 1f),
                    position: new Vector2(-16f, -(k_TopRightGutter + 360f)), size: new Vector2(360f, 120f),
                    alignment: TextAnchor.UpperRight, fontSize: 18, bold: false);
                m_KillFeedText.text = string.Empty;
                HudSkin.StyleText(m_KillFeedText, dim: true);
            }
        }

        /// <summary>
        /// Parks an icon immediately left of a right-aligned label. Sized off the label so the two
        /// keep their relationship whatever the HUD is scaled to.
        /// </summary>
        static void AddIcon(RectTransform label, UIIcons.Icon icon, Color color, float size)
        {
            var host = new GameObject("Icon " + icon, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(label, false);

            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            // Past the right edge of the label's own rect, which is where a right-aligned string
            // starts from.
            rect.anchoredPosition = new Vector2(-label.rect.width * 0.55f, 0f);
            rect.sizeDelta = new Vector2(size, size);

            var image = host.GetComponent<Image>();
            image.sprite = UIIcons.Get(icon);
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        static Text CreateText(GameObject parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, TextAnchor alignment, int fontSize, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = alignment;
            text.fontSize = fontSize;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
