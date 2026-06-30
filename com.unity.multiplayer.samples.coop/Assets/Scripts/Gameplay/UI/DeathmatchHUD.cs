using System.Collections.Generic;
using System.Text;
using Mirror;
using Unity.BossRoom.Gameplay.GameState;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Client-side in-game HUD showing the match countdown timer and live scoreboard.
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
        [SerializeField] Text m_ScoreboardText;

        NetworkGameState m_NetworkGameState;

        void Start()
        {
            TryFindAndSubscribe();
        }

        void Update()
        {
            if (m_NetworkGameState != null) return;
            TryFindAndSubscribe();
        }

        void TryFindAndSubscribe()
        {
            m_NetworkGameState = Object.FindAnyObjectByType<NetworkGameState>();
            if (m_NetworkGameState == null) return;

            EnsureUI();
            m_NetworkGameState.OnTimeRemainingChangedEvent += OnTimerChanged;
            m_NetworkGameState.Scores.Callback += OnScoresChanged;
            RefreshTimer(m_NetworkGameState.TimeRemaining);
            RefreshScoreboard();
        }

        void OnDestroy()
        {
            if (m_NetworkGameState == null) return;
            m_NetworkGameState.OnTimeRemainingChangedEvent -= OnTimerChanged;
            m_NetworkGameState.Scores.Callback -= OnScoresChanged;
        }

        void OnTimerChanged(float _, float newVal) => RefreshTimer(newVal);

        void OnScoresChanged(SyncList<ScoreEntry>.Operation op, int index, ScoreEntry old, ScoreEntry @new)
            => RefreshScoreboard();

        void RefreshTimer(float seconds)
        {
            if (m_TimerText == null) return;
            int mins = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            m_TimerText.text = $"{mins}:{secs:D2}";

            // Turn red when under 30s
            m_TimerText.color = seconds <= 30f ? Color.red : Color.white;
        }

        void RefreshScoreboard()
        {
            if (m_ScoreboardText == null || m_NetworkGameState == null) return;

            var sorted = new List<ScoreEntry>(m_NetworkGameState.Scores.Count);
            for (int i = 0; i < m_NetworkGameState.Scores.Count; i++)
                sorted.Add(m_NetworkGameState.Scores[i]);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            var sb = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
                sb.AppendLine($"{i + 1}. {sorted[i].PlayerName}  {sorted[i].Score}pts");
            m_ScoreboardText.text = sb.ToString();
        }

        // Builds a minimal Screen Space Overlay Canvas with timer + scoreboard Text widgets.
        void EnsureUI()
        {
            if (m_TimerText != null && m_ScoreboardText != null) return;

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
                var go = new GameObject("Timer");
                go.transform.SetParent(canvasGO.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -16f);
                rt.sizeDelta = new Vector2(200f, 60f);
                m_TimerText = go.AddComponent<Text>();
                m_TimerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                m_TimerText.alignment = TextAnchor.UpperCenter;
                m_TimerText.fontSize = 42;
                m_TimerText.fontStyle = FontStyle.Bold;
                m_TimerText.color = Color.white;
            }

            // Scoreboard — top-right
            if (m_ScoreboardText == null)
            {
                var go = new GameObject("Scoreboard");
                go.transform.SetParent(canvasGO.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-16f, -16f);
                rt.sizeDelta = new Vector2(260f, 320f);
                m_ScoreboardText = go.AddComponent<Text>();
                m_ScoreboardText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                m_ScoreboardText.alignment = TextAnchor.UpperRight;
                m_ScoreboardText.fontSize = 18;
                m_ScoreboardText.color = Color.white;
            }
        }
    }
}
