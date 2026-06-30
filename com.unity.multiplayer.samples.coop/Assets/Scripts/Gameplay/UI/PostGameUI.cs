using System.Collections.Generic;
using System.Text;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameState;
using TMPro;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    public class PostGameUI : MonoBehaviour
    {
        [SerializeField]
        private Light m_SceneLight;

        [SerializeField]
        private TextMeshProUGUI m_WinEndMessage;

        [SerializeField]
        private TextMeshProUGUI m_LoseGameMessage;

        [SerializeField]
        private GameObject m_ReplayButton;

        [SerializeField]
        private GameObject m_WaitOnHostMsg;

        [SerializeField]
        private Color m_WinLightColor;

        [SerializeField]
        private Color m_LoseLightColor;

        /// <summary>Wire in editor, or left null to auto-build at runtime.</summary>
        [SerializeField]
        TextMeshProUGUI m_RankingText;

        ServerPostGameState m_PostGameState;

        [Inject]
        void Inject(ServerPostGameState postGameState)
        {
            m_PostGameState = postGameState;

            bool isHost = NetworkServer.active && NetworkClient.active;
            m_ReplayButton.SetActive(isHost);
            m_WaitOnHostMsg.SetActive(!isHost);
        }

        void Start()
        {
            EnsureRankingText();

            var postGame = m_PostGameState.NetworkPostGame;
            postGame.FinalScoreboard.Callback += OnScoreboardChanged;

            RefreshAll();
        }

        void OnDestroy()
        {
            if (m_PostGameState?.NetworkPostGame == null) return;
            m_PostGameState.NetworkPostGame.FinalScoreboard.Callback -= OnScoreboardChanged;
        }

        void OnScoreboardChanged(SyncList<ScoreEntry>.Operation op, int index, ScoreEntry old, ScoreEntry @new)
            => RefreshAll();

        void RefreshAll()
        {
            RefreshRanking();
            SetOutcomeUI();
        }

        // Determine Win/Loss locally from the scoreboard so each client sees the right result.
        // WinState SyncVar is a single value (same for all), so we can't rely on it for PvP.
        void SetOutcomeUI()
        {
            var scoreboard = m_PostGameState.NetworkPostGame.FinalScoreboard;
            if (scoreboard.Count == 0) return;

            // Match our own row by the stable master-server PlayerId. ScoreEntry.ClientId is
            // the server-assigned connectionId, which a remote client never learns
            // (NetworkConnection.LocalConnectionId is always 0), so comparing on it would
            // make every non-host client think connId 0 won. See ScoreEntry / port notes.
            string localPlayerId = ClientAuthPayload.Current?.PlayerId;

            var sorted = new List<ScoreEntry>(scoreboard.Count);
            for (int i = 0; i < scoreboard.Count; i++)
                sorted.Add(scoreboard[i]);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            bool localWon = !string.IsNullOrEmpty(localPlayerId) && sorted[0].PlayerId == localPlayerId;

            m_SceneLight.color = localWon ? m_WinLightColor : m_LoseLightColor;
            m_WinEndMessage.gameObject.SetActive(localWon);
            m_LoseGameMessage.gameObject.SetActive(!localWon);
        }

        void RefreshRanking()
        {
            if (m_RankingText == null) return;

            var scoreboard = m_PostGameState.NetworkPostGame.FinalScoreboard;
            if (scoreboard.Count == 0)
            {
                m_RankingText.text = string.Empty;
                return;
            }

            var sorted = new List<ScoreEntry>(scoreboard.Count);
            for (int i = 0; i < scoreboard.Count; i++)
                sorted.Add(scoreboard[i]);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

            var sb = new StringBuilder();
            sb.AppendLine("<b>— Clasificación final —</b>\n");
            for (int i = 0; i < sorted.Count; i++)
                sb.AppendLine($"{i + 1}.  {sorted[i].PlayerName}   <b>{sorted[i].Score} pts</b>");

            m_RankingText.text = sb.ToString();
        }

        /// <summary>Creates a centered ranking Text widget when not wired in the prefab.</summary>
        void EnsureRankingText()
        {
            if (m_RankingText != null) return;

            var canvasGO = new GameObject("PostGame_RankingCanvas");
            canvasGO.transform.SetParent(transform);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var go = new GameObject("RankingText");
            go.transform.SetParent(canvasGO.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -60f);
            rt.sizeDelta = new Vector2(420f, 480f);

            m_RankingText = go.AddComponent<TextMeshProUGUI>();
            m_RankingText.alignment = TextAlignmentOptions.Center;
            m_RankingText.fontSize = 22;
            m_RankingText.color = Color.white;
        }

        public void OnPlayAgainClicked()
        {
            m_PostGameState.PlayAgain();
        }

        public void OnMainMenuClicked()
        {
            m_PostGameState.GoToMainMenu();
        }
    }
}
