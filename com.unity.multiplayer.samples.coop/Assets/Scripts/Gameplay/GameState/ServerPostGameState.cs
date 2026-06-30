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

            SessionManager<SessionPlayerData>.Instance.OnSessionEnded();
            networkPostGame.WinState = m_PersistentGameState.WinState;

            var scoreboard = m_PersistentGameState.FinalScoreboard;
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
                var pid = SessionManager<SessionPlayerData>.Instance.GetPlayerId(scoreboard[i].ClientId);
                if (string.IsNullOrEmpty(pid)) continue;
                playerIds.Add(pid);
                if (i == 0) winnerId = pid;
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
