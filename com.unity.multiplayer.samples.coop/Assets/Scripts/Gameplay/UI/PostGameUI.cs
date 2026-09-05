using System.Collections.Generic;
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

        /// <summary>The column the ranking rows are rebuilt into. Built at runtime.</summary>
        RectTransform m_RankingRows;

        /// <summary>
        /// Medal colours for the podium places. Fourth onward gets the ordinary row colour —
        /// which is the point: a medal that everyone gets is not a medal.
        /// </summary>
        static readonly Color[] k_MedalColors =
        {
            new Color(0.86f, 0.74f, 0.42f, 1f), // gold leaf — first place, and nothing else
            new Color(0.78f, 0.80f, 0.90f, 1f), // cold silver
            new Color(0.60f, 0.50f, 0.86f, 1f), // amethyst, standing in for bronze
        };

        ServerPostGameState m_PostGameState;

        /// <summary>
        /// The replicated results object. Resolved on its own rather than only through
        /// <see cref="ServerPostGameState"/>, because that one is deliberately not there to be
        /// found on a client: it disables itself when NetworkServer is not active, and a disabled
        /// component is not returned by FindAnyObjectByType. Whenever injection had not supplied
        /// it — which is the ordinary case for someone who did not host — the lookup came back
        /// null, the subscription never happened, and the results card stayed empty for the whole
        /// screen. NetworkPostGame, by contrast, is an ordinary NetworkBehaviour that spawns on
        /// every client, and it is where the scoreboard actually lives.
        /// </summary>
        NetworkPostGame m_NetworkPostGame;

        /// <summary>Headline built at runtime when the prefab win/lose texts are not wired.</summary>
        TextMeshProUGUI m_RuntimeHeadline;

        Canvas m_RuntimeCanvas;

        [Inject]
        void Inject(ServerPostGameState postGameState)
        {
            m_PostGameState = postGameState;

            bool isHost = NetworkServer.active && NetworkClient.active;
            m_ReplayButton.SetActive(isHost);
            m_WaitOnHostMsg.SetActive(!isHost);
        }

        /// <summary>True once the scoreboard callback is attached, so we stop looking.</summary>
        bool m_Subscribed;

        void Start()
        {
            EnsureRankingUI();
            EnsureHeadlineText();
            TrySubscribe();
        }

        void Update()
        {
            if (!m_Subscribed)
            {
                TrySubscribe();
            }
        }

        /// <summary>
        /// Attaches to the replicated scoreboard as soon as there is one, and keeps trying until
        /// there is.
        /// </summary>
        /// <remarks>
        /// <para>This used to be a single attempt in Start with an early return, and the early
        /// return sat <i>after</i> the card was built. So when the state object had not arrived
        /// yet — VContainer had not injected, or NetworkPostGame had simply not spawned on this
        /// client in the frame the scene came up — the screen drew its title and its empty table
        /// and then gave up for good. The rows arrived a moment later with nobody listening, which
        /// is exactly "the heading is there and the table never fills in".</para>
        ///
        /// <para><see cref="RefreshAll"/> runs immediately after subscribing, not only from the
        /// callback: a SyncList does not replay what it already holds to a new subscriber, and by
        /// the time this succeeds the entries are usually already in.</para>
        /// </remarks>
        void TrySubscribe()
        {
            if (m_PostGameState == null)
            {
                // Injection is the normal route; this is the net for when it has not run.
                // Include disabled components: on a client this one has switched itself off.
                m_PostGameState = FindAnyObjectByType<ServerPostGameState>(FindObjectsInactive.Include);
            }

            if (m_NetworkPostGame == null)
            {
                m_NetworkPostGame = m_PostGameState != null && m_PostGameState.NetworkPostGame != null
                    ? m_PostGameState.NetworkPostGame
                    : FindAnyObjectByType<NetworkPostGame>();
            }

            if (m_NetworkPostGame == null)
            {
                return;
            }

            m_NetworkPostGame.FinalScoreboard.Callback += OnScoreboardChanged;
            m_Subscribed = true;

            RefreshAll();
        }

        void OnDestroy()
        {
            // The runtime canvas is a root object (see EnsureRuntimeCanvas), so nothing else takes
            // it down with this screen. Done before the early return below, which only concerns
            // the scoreboard subscription.
            if (m_RuntimeCanvas != null)
            {
                Destroy(m_RuntimeCanvas.gameObject);
                m_RuntimeCanvas = null;
            }

            if (!m_Subscribed || m_NetworkPostGame == null) return;
            m_NetworkPostGame.FinalScoreboard.Callback -= OnScoreboardChanged;
            m_Subscribed = false;
        }

        void OnScoreboardChanged(SyncList<ScoreEntry>.Operation op, int index, ScoreEntry old, ScoreEntry @new)
            => RefreshAll();

        void RefreshAll()
        {
            var sorted = GetSortedScoreboard();
            int localIndex = FindLocalIndex(sorted);

            SetOutcomeUI(sorted, localIndex);
            RefreshRanking(sorted, localIndex);
        }

        List<ScoreEntry> GetSortedScoreboard()
        {
            var scoreboard = m_NetworkPostGame != null ? m_NetworkPostGame.FinalScoreboard : null;
            if (scoreboard == null) return new List<ScoreEntry>();

            var sorted = new List<ScoreEntry>(scoreboard.Count);
            for (int i = 0; i < scoreboard.Count; i++)
                sorted.Add(scoreboard[i]);

            // Every peer sorts identically, so they all agree on who sits at index 0.
            sorted.Sort(ScoreEntry.CompareForRanking);
            return sorted;
        }

        /// <summary>
        /// Index of this client's own row in the sorted scoreboard, or -1 if we can't
        /// identify ourselves (spectator, or a session with no master-server PlayerId).
        /// Match on the stable master-server PlayerId — ScoreEntry.ClientId is the
        /// server-assigned connectionId, which a remote client never learns (its own
        /// NetworkConnection.LocalConnectionId is always 0), so comparing on it would make
        /// every non-host client think connId 0 was them. PlayerName is only a fallback for
        /// unauthenticated sessions where PlayerId comes back empty.
        /// </summary>
        int FindLocalIndex(List<ScoreEntry> sorted)
        {
            var auth = ClientAuthPayload.Current;
            if (auth == null) return -1;

            if (!string.IsNullOrEmpty(auth.PlayerId))
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].PlayerId == auth.PlayerId) return i;
                }
            }

            if (!string.IsNullOrEmpty(auth.PlayerName))
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i].PlayerName == auth.PlayerName) return i;
                }
            }

            return -1;
        }

        // Determine Win/Loss locally from the scoreboard so each client sees the right result.
        // WinState SyncVar is a single value (same for all), so we can't rely on it for PvP.
        void SetOutcomeUI(List<ScoreEntry> sorted, int localIndex)
        {
            bool hasResults = sorted.Count > 0;
            bool localWon = hasResults && localIndex == 0;

            if (m_SceneLight != null)
                m_SceneLight.color = localWon ? m_WinLightColor : m_LoseLightColor;

            // Text is set from code (not from the prefab) so the wording is guaranteed to
            // match what the ranking below says, regardless of the authored asset.
            string loserHeadline = !hasResults
                ? string.Empty
                : localIndex > 0
                    ? "PERDISTE"
                    : $"GANÓ {sorted[0].PlayerName}"; // we're not on the board (spectator)

            if (m_WinEndMessage != null)
            {
                m_WinEndMessage.text = "¡GANASTE!";
                m_WinEndMessage.gameObject.SetActive(localWon);
            }

            if (m_LoseGameMessage != null)
            {
                m_LoseGameMessage.text = loserHeadline;
                m_LoseGameMessage.gameObject.SetActive(hasResults && !localWon);
            }

            if (m_RuntimeHeadline != null)
            {
                m_RuntimeHeadline.text = !hasResults ? string.Empty : localWon ? "¡GANASTE!" : loserHeadline;
                m_RuntimeHeadline.color = localWon ? UIKit.Gold : HudSkin.TextPrimary;
            }
        }

        /// <summary>
        /// Rebuilds the ranking as one row per player. Rows rather than a block of rich text
        /// because this is the screen everyone stares at for ten seconds: a player should find
        /// their own line, and the gap between them and the winner, without reading.
        /// </summary>
        void RefreshRanking(List<ScoreEntry> sorted, int localIndex)
        {
            if (m_RankingRows == null) return;

            for (int i = m_RankingRows.childCount - 1; i >= 0; i--)
            {
                Destroy(m_RankingRows.GetChild(i).gameObject);
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                BuildRankingRow(sorted[i], i, i == localIndex);
            }
        }

        void BuildRankingRow(ScoreEntry entry, int rank, bool isLocal)
        {
            bool podium = rank < k_MedalColors.Length;
            Color accent = podium ? k_MedalColors[rank] : HudSkin.TextDim;

            var row = UIKit.Strip(m_RankingRows, "Rank " + (rank + 1),
                new Color(0.105f, 0.098f, 0.165f, isLocal ? 0.95f : 0.6f), 64f);

            if (isLocal)
            {
                UIKit.Outline(row.gameObject, ToonMenuSkin.Accent);
            }

            // The winner gets a crown instead of a "1", which is the one row worth reading as a
            // picture rather than as a number.
            if (rank == 0)
            {
                UIKit.Icon(row, UIIcons.Icon.Crown, accent, 30f);
            }
            else
            {
                var place = UIKit.Text(row, (rank + 1).ToString(), UIKit.TextStyle.Heading,
                    TextAlignmentOptions.Center, accent);
                place.enableWordWrapping = false;
                var placeElement = place.GetComponent<LayoutElement>();
                placeElement.preferredWidth = 34f;
                placeElement.flexibleWidth = 0f;
            }

            var names = UIKit.Column(row, "Names", 0f, 0f, TextAnchor.MiddleLeft);
            UIKit.Flexible(names, 52f, expandWidth: true);

            var name = UIKit.Text(names, entry.PlayerName, UIKit.TextStyle.Body, TextAlignmentOptions.Left,
                isLocal ? ToonMenuSkin.Accent : HudSkin.TextPrimary);
            name.enableWordWrapping = false;
            name.fontStyle = podium || isLocal ? FontStyles.Bold : FontStyles.Normal;

            // Said plainly on the row. A bot plays under an ordinary name, so without this the
            // table quietly invites the player to compare themselves against opponents that were
            // never people.
            if (entry.IsBot)
            {
                UIKit.Badge(row, "BOT", HudSkin.TextDim);
            }

            // The breakdown is what stops a tie broken on player kills from looking arbitrary.
            string detail = $"{entry.PlayerKills} jug · {entry.NpcKills} imps";
            UIKit.Text(names, detail, UIKit.TextStyle.Caption, TextAlignmentOptions.Left);

            if (entry.KilledBoss)
            {
                UIKit.Icon(row, UIIcons.Icon.Skull, UIKit.Gold, 26f);
            }

            var score = UIKit.Text(row, entry.Score.ToString(), UIKit.TextStyle.Heading,
                TextAlignmentOptions.Right, accent);
            score.enableWordWrapping = false;
            var scoreElement = score.GetComponent<LayoutElement>();
            scoreElement.preferredWidth = 90f;
            scoreElement.flexibleWidth = 0f;

            var unit = UIKit.Text(row, "pts", UIKit.TextStyle.Caption, TextAlignmentOptions.Left);
            unit.enableWordWrapping = false;
            var unitElement = unit.GetComponent<LayoutElement>();
            unitElement.preferredWidth = 34f;
            unitElement.flexibleWidth = 0f;
        }

        /// <summary>
        /// Builds the results card. It hangs below the outcome headline the prefab draws at the
        /// centre of the screen, which is why it is pinned by its top edge rather than centred.
        /// </summary>
        void EnsureRankingUI()
        {
            if (m_RankingRows != null) return;

            // A real height, and a non-zero one on purpose: UIKit.Card fits itself to its content
            // when given a height of 0, which is what let this table grow past the bottom of the
            // screen and straight over the buttons once a lobby was full. It is then stretched
            // between two fixed insets instead of being centred, so the space it may occupy is
            // decided by the screen rather than by how many people played.
            var card = UIKit.Card(EnsureRuntimeCanvas().transform, "ResultsCard", new Vector2(760f, 400f),
                UIKit.Unit * 3f, UIKit.Unit);
            card.anchorMin = new Vector2(0.5f, 0f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.offsetMin = new Vector2(-380f, k_CardBottomInset);
            card.offsetMax = new Vector2(380f, -k_CardTopInset);

            MoveButtonsToBottom();

            var header = UIKit.Row(card, "Header", UIKit.Unit, 0f, TextAnchor.MiddleCenter);
            UIKit.Flexible(header, 40f, expandWidth: true);
            UIKit.Icon(header, UIIcons.Icon.Trophy, UIKit.Gold, 28f);
            UIKit.Text(header, "Clasificación final", UIKit.TextStyle.Heading);

            UIKit.Divider(card);

            // Scrolled rather than merely clipped. Eight players do not fit a phone in landscape
            // however the card is sized, and a table that silently cuts the last two places off is
            // worse than one you have to drag: the people missing are the ones who came last, who
            // are exactly the ones checking.
            UIKit.List(card, "Rows", out m_RankingRows, UIKit.Unit * 0.75f);
        }

        /// <summary>Gap above the card, leaving the outcome headline visible.</summary>
        const float k_CardTopInset = 210f;

        /// <summary>
        /// Gap below the card. Clears the two buttons, which sit at 170 and 70 from the bottom and
        /// are 90 tall — so the lowest the card may reach is a little above 215.
        /// </summary>
        const float k_CardBottomInset = 250f;

        /// <summary>Buttons the prefab pins near the middle of the screen, top one first.</summary>
        static readonly string[] k_BottomButtons = { "PlayAgainBtn", "WaitOnHost", "MenuBtn" };

        /// <summary>
        /// Re-anchors the end-of-match buttons to the bottom of the screen.
        /// </summary>
        /// <remarks>
        /// <para>The prefab pins them to the centre at y -180 and y -314, from a layout that had no
        /// results table in it. The table is anchored by its top edge and grows downwards with one
        /// row per player, so as soon as there were more than a couple of players it covered both
        /// buttons — and a player who could see the final scores had no way to leave the match.</para>
        ///
        /// <para>Done here rather than in the prefab because the two have to agree: the card's top
        /// offset and these positions are one layout, and splitting it across an asset and a script
        /// is how it drifted apart in the first place. Bottom-anchored so the gap the table can
        /// grow into scales with the window instead of being fixed at the design resolution.</para>
        ///
        /// <para>WaitOnHost sits exactly where PlayAgainBtn does (it is the message shown to a
        /// non-host in its place), so it has to move with it or it reappears under the table.</para>
        /// </remarks>
        void MoveButtonsToBottom()
        {
            var canvas = GetComponentInChildren<Canvas>(true);
            var root = canvas != null ? canvas.transform : transform;

            for (int i = 0; i < k_BottomButtons.Length; i++)
            {
                var button = FindDeep(root, k_BottomButtons[i]) as RectTransform;
                if (button == null)
                {
                    continue;
                }

                // WaitOnHost shares the top slot with PlayAgainBtn rather than taking its own.
                float slot = k_BottomButtons[i] == "MenuBtn" ? k_MenuButtonFromBottom : k_PlayButtonFromBottom;

                button.anchorMin = new Vector2(0.5f, 0f);
                button.anchorMax = new Vector2(0.5f, 0f);
                button.pivot = new Vector2(0.5f, 0.5f);
                button.anchoredPosition = new Vector2(0f, slot);
            }
        }

        const float k_PlayButtonFromBottom = 170f;
        const float k_MenuButtonFromBottom = 70f;

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Fallback headline for when the prefab's win/lose texts are missing — without it a
        /// client whose PostGameUICanvas lost those refs would see the ranking but no result.
        /// </summary>
        void EnsureHeadlineText()
        {
            if (m_WinEndMessage != null && m_LoseGameMessage != null) return;

            var go = new GameObject("OutcomeHeadline");
            go.transform.SetParent(EnsureRuntimeCanvas().transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -40f);
            rt.sizeDelta = new Vector2(720f, 90f);

            m_RuntimeHeadline = go.AddComponent<TextMeshProUGUI>();
            m_RuntimeHeadline.alignment = TextAlignmentOptions.Center;
            m_RuntimeHeadline.enableAutoSizing = true;
            m_RuntimeHeadline.fontSizeMin = 24;
            m_RuntimeHeadline.fontSizeMax = 56;
            m_RuntimeHeadline.fontStyle = FontStyles.Bold;
            m_RuntimeHeadline.color = Color.white;
        }

        Canvas EnsureRuntimeCanvas()
        {
            if (m_RuntimeCanvas != null) return m_RuntimeCanvas;

            var canvasGO = new GameObject("PostGame_RankingCanvas");

            // Deliberately NOT parented. A ScreenSpaceOverlay canvas has its rect driven by the
            // canvas system, but it still inherits its ancestors' transform — so hanging it under
            // whatever GameObject this component happens to sit on hands it that object's offset
            // and scale, and the results card ends up drawn correctly but somewhere off screen.
            // As a root object there is nothing to inherit. OnDestroy below owns its lifetime,
            // which is what the parenting was buying.
            canvasGO.transform.SetParent(null);
            canvasGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            canvasGO.transform.localScale = Vector3.one;

            // Through the kit, so the results card is measured against the same 1920×1080 design
            // resolution as every other screen instead of a default constant-pixel scaler.
            m_RuntimeCanvas = UIKit.Root(canvasGO, "PostGame_RankingCanvas", 20);

            return m_RuntimeCanvas;
        }

        public void OnPlayAgainClicked()
        {
            // Host-only button, but the state object is looked up the same forgiving way as the
            // scoreboard rather than assumed: a null here would have been an exception thrown out
            // of a UI click, which leaves the player on a screen whose buttons no longer respond.
            var state = ResolvePostGameState();
            if (state != null)
            {
                state.PlayAgain();
            }
        }

        public void OnMainMenuClicked()
        {
            var state = ResolvePostGameState();
            if (state != null)
            {
                state.GoToMainMenu();
            }
        }

        ServerPostGameState ResolvePostGameState()
        {
            if (m_PostGameState == null)
            {
                m_PostGameState = FindAnyObjectByType<ServerPostGameState>(FindObjectsInactive.Include);
            }

            return m_PostGameState;
        }
    }
}
