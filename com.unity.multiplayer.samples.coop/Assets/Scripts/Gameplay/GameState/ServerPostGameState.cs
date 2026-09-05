using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.DedicatedServer;
using Unity.BossRoom.Gameplay.Actions;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameState
{
    [RequireComponent(typeof(NetcodeHooks))]
    public class ServerPostGameState : GameStateBehaviour
    {
        [SerializeField]
        NetcodeHooks m_NetcodeHooks;

        [FormerlySerializedAs("synchronizedStateData")]
        [SerializeField]
        NetworkPostGame networkPostGame;
        public NetworkPostGame NetworkPostGame => networkPostGame;

        public override GameState ActiveState { get { return GameState.PostGame; } }

        [Inject]
        ConnectionManager m_ConnectionManager;

        [Inject]
        PersistentGameState m_PersistentGameState;

        /// <summary>
        /// Whether the server side of this state has already run.
        /// </summary>
        /// <remarks>
        /// <see cref="NetcodeHooks"/> raises OnNetworkSpawnHook from BOTH OnStartServer and
        /// OnStartClient, so on a host every hook lands twice. Without this the final table was
        /// filled twice — every player listed twice, in the host's screen and, because the list is
        /// replicated, in everyone else's — the session was ended twice, and the ranked result was
        /// reported to the master server twice. <see cref="ServerBossRoomState"/> and
        /// <see cref="ServerCharSelectState"/> already carry the same flag for the same reason.
        /// </remarks>
        bool m_ServerInitialized;

        protected override void Awake()
        {
            base.Awake();

            m_NetcodeHooks.OnNetworkSpawnHook += OnNetworkSpawn;
        }

        void OnNetworkSpawn()
        {
            if (!NetworkServer.active)
            {
                enabled = false;
                return;
            }

            if (m_ServerInitialized)
            {
                return;
            }

            m_ServerInitialized = true;

            SessionManager<SessionPlayerData>.Instance.OnSessionEnded();
            networkPostGame.WinState = m_PersistentGameState.WinState;

            var scoreboard = m_PersistentGameState.FinalScoreboard;
            networkPostGame.FinalScoreboard.Clear();
            foreach (var entry in scoreboard)
                networkPostGame.FinalScoreboard.Add(entry);

            _ = ReportMatchResultAsync(scoreboard);
        }

        async Task ReportMatchResultAsync(IReadOnlyList<ScoreEntry> scoreboard)
        {
            if (scoreboard.Count == 0) return;

            // Only dedicated servers report ranked stats; P2P sessions are unranked.
            var facade = DedicatedServerBootstrapper.Current?.Facade;
            if (facade == null) return;

            var playerIds = new List<string>(scoreboard.Count);
            string winnerId = null;

            for (int i = 0; i < scoreboard.Count; i++)
            {
                // Bots are excluded from the ranked report entirely. They hold real session data
                // and therefore a real player id, so without this check they were submitted as
                // players — padding everyone's match history with opponents who do not exist and,
                // when a bot finished first, recording a bot as the winner.
                if (scoreboard[i].IsBot) continue;

                var pid = SessionManager<SessionPlayerData>.Instance.GetPlayerId(scoreboard[i].ClientId);
                if (string.IsNullOrEmpty(pid)) continue;

                // The winner is the first entry that survives the filter, not entry zero: the
                // table is already sorted, so the best-placed real player is the one to credit
                // whether or not a bot outscored them.
                winnerId ??= pid;
                playerIds.Add(pid);
            }

            if (playerIds.Count > 0)
                await facade.SubmitMatchResultAsync(playerIds.ToArray(), winnerId ?? "");
        }

        protected override void OnDestroy()
        {
            //clear actions pool
            ActionFactory.PurgePooledActions();
            m_PersistentGameState.Reset();

            base.OnDestroy();

            m_NetcodeHooks.OnNetworkSpawnHook -= OnNetworkSpawn;
        }

        public void PlayAgain()
        {
            SceneLoaderWrapper.Instance.LoadScene("CharSelect", useNetworkSceneManager: true);
        }

        public void GoToMainMenu()
        {
            m_ConnectionManager.RequestShutdown();
        }
    }
}
