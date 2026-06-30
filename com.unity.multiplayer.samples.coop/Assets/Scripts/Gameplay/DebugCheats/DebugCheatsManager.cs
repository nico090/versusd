using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using Unity.Multiplayer.Samples.BossRoom;
using Unity.Multiplayer.Samples.Utilities;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Unity.BossRoom.DebugCheats
{
    public class DebugCheatsManager : NetworkBehaviour
    {
        [SerializeField]
        GameObject m_DebugCheatsPanel;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField]
        [Tooltip("Enemy to spawn. Make sure this prefab is registered in NetworkManager's spawnPrefabs list!")]
        GameObject m_EnemyPrefab;

        [SerializeField]
        [Tooltip("Boss to spawn. Make sure this prefab is registered in NetworkManager's spawnPrefabs list!")]
        GameObject m_BossPrefab;

        [SerializeField]
        InputActionReference m_ToggleCheatsAction;

        SwitchedDoor m_SwitchedDoor;
        SwitchedDoor SwitchedDoor => m_SwitchedDoor ??= FindAnyObjectByType<SwitchedDoor>();

        bool m_DestroyPortalsOnNextToggle = true;

        [Inject]
        IPublisher<CheatUsedMessage> m_CheatUsedMessagePublisher;

        void Start()
        {
            m_ToggleCheatsAction.action.performed += OnToggleCheatsActionPerformed;
        }

        void OnDestroy()
        {
            m_ToggleCheatsAction.action.performed -= OnToggleCheatsActionPerformed;
        }

        void OnToggleCheatsActionPerformed(InputAction.CallbackContext obj)
        {
            m_DebugCheatsPanel.SetActive(!m_DebugCheatsPanel.activeSelf);
        }

        public void SpawnEnemy() => CmdSpawnEnemy();
        public void SpawnBoss() => CmdSpawnBoss();
        public void KillTarget() => CmdKillTarget();
        public void KillAllEnemies() => CmdKillAllEnemies();
        public void ToggleGodMode() => CmdToggleGodMode();
        public void HealPlayer() => CmdHealPlayer();
        public void ToggleSuperSpeed() => CmdToggleSuperSpeed();
        public void ToggleTeleportMode() => CmdToggleTeleportMode();
        public void ToggleDoor() => CmdToggleDoor();
        public void TogglePortals() => CmdTogglePortals();
        public void GoToPostGame() => CmdGoToPostGame();

        [Command(requiresAuthority = false)]
        void CmdSpawnEnemy(NetworkConnectionToClient sender = null)
        {
            var newEnemy = Instantiate(m_EnemyPrefab);
            NetworkServer.Spawn(newEnemy, sender);
            PublishCheatUsedMessage(sender, "SpawnEnemy");
        }

        [Command(requiresAuthority = false)]
        void CmdSpawnBoss(NetworkConnectionToClient sender = null)
        {
            var newBoss = Instantiate(m_BossPrefab);
            NetworkServer.Spawn(newBoss, sender);
            PublishCheatUsedMessage(sender, "SpawnBoss");
        }

        [Command(requiresAuthority = false)]
        void CmdKillTarget(NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            var playerServerCharacter = PlayerServerCharacter.GetPlayerServerCharacter(clientId);
            if (playerServerCharacter != null)
            {
                var targetId = playerServerCharacter.TargetId;
                var targetNetId = NetworkIdentityUtils.FindByNetId((uint)targetId);
                if (targetNetId != null)
                {
                    var damageable = targetNetId.GetComponent<IDamageable>();
                    if (damageable != null && damageable.IsDamageable())
                    {
                        damageable.ReceiveHitPoints(playerServerCharacter, int.MinValue);
                        PublishCheatUsedMessage(sender, "KillTarget");
                    }
                }
            }
        }

        [Command(requiresAuthority = false)]
        void CmdKillAllEnemies(NetworkConnectionToClient sender = null)
        {
            foreach (var sc in FindObjectsByType<ServerCharacter>(FindObjectsSortMode.None))
            {
                if (sc.IsNpc && sc.LifeState == LifeState.Alive && sc.TryGetComponent(out IDamageable damageable))
                    damageable.ReceiveHitPoints(null, -sc.HitPoints);
            }
            PublishCheatUsedMessage(sender, "KillAllEnemies");
        }

        [Command(requiresAuthority = false)]
        void CmdToggleGodMode(NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            var sc = PlayerServerCharacter.GetPlayerServerCharacter(clientId);
            if (sc != null)
            {
                sc.NetLifeState.IsGodMode = !sc.NetLifeState.IsGodMode;
                PublishCheatUsedMessage(sender, "ToggleGodMode");
            }
        }

        [Command(requiresAuthority = false)]
        void CmdHealPlayer(NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            var sc = PlayerServerCharacter.GetPlayerServerCharacter(clientId);
            if (sc != null)
            {
                var baseHp = sc.CharacterClass.BaseHP.Value;
                if (sc.LifeState == LifeState.Fainted)
                    sc.Revive(null, baseHp);
                else if (sc.TryGetComponent(out IDamageable damageable))
                    damageable.ReceiveHitPoints(null, baseHp);
                PublishCheatUsedMessage(sender, "HealPlayer");
            }
        }

        [Command(requiresAuthority = false)]
        void CmdToggleSuperSpeed(NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            foreach (var sc in PlayerServerCharacter.GetPlayerServerCharacters())
            {
                if (sc.OwnerClientId == clientId)
                {
                    sc.Movement.SpeedCheatActivated = !sc.Movement.SpeedCheatActivated;
                    break;
                }
            }
            PublishCheatUsedMessage(sender, "ToggleSuperSpeed");
        }

        [Command(requiresAuthority = false)]
        void CmdToggleTeleportMode(NetworkConnectionToClient sender = null)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            foreach (var sc in PlayerServerCharacter.GetPlayerServerCharacters())
            {
                if (sc.OwnerClientId == clientId)
                {
                    sc.Movement.TeleportModeActivated = !sc.Movement.TeleportModeActivated;
                    break;
                }
            }
            PublishCheatUsedMessage(sender, "ToggleTeleportMode");
        }

        [Command(requiresAuthority = false)]
        void CmdToggleDoor(NetworkConnectionToClient sender = null)
        {
            if (SwitchedDoor != null)
            {
                SwitchedDoor.ForceOpen = !SwitchedDoor.ForceOpen;
                PublishCheatUsedMessage(sender, "ToggleDoor");
            }
        }

        [Command(requiresAuthority = false)]
        void CmdTogglePortals(NetworkConnectionToClient sender = null)
        {
            foreach (var portal in FindObjectsByType<EnemyPortal>(FindObjectsSortMode.None))
            {
                if (m_DestroyPortalsOnNextToggle)
                    portal.ForceDestroy();
                else
                    portal.ForceRestart();
            }
            m_DestroyPortalsOnNextToggle = !m_DestroyPortalsOnNextToggle;
            PublishCheatUsedMessage(sender, "TogglePortals");
        }

        [Command(requiresAuthority = false)]
        void CmdGoToPostGame(NetworkConnectionToClient sender = null)
        {
            SceneLoaderWrapper.Instance.LoadScene("PostGame", useNetworkSceneManager: true);
            PublishCheatUsedMessage(sender, "GoToPostGame");
        }

        void PublishCheatUsedMessage(NetworkConnectionToClient sender, string cheatUsed)
        {
            ulong clientId = (ulong)(uint)(sender?.connectionId ?? 0);
            var playerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData(clientId);
            if (playerData.HasValue)
            {
                m_CheatUsedMessagePublisher.Publish(new CheatUsedMessage
                {
                    CheatUsed = cheatUsed,
                    CheaterName = playerData.Value.PlayerName
                });
            }
        }

#else
        void Awake()
        {
            m_DebugCheatsPanel.SetActive(false);
        }
#endif
    }
}
