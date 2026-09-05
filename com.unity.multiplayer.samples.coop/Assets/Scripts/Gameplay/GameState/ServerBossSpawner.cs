using System.Collections;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.Multiplayer.Samples.Utilities;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server-only: puts the boss in the middle of the map once, when the match clock reaches
    /// <see cref="DeathmatchRules.BossSpawnTimeRemaining"/> (the final stretch) — not at the
    /// start of the match. One boss per match — it never respawns, so the 20 points for the final
    /// blow can only ever be claimed by one player.
    /// </summary>
    /// <remarks>
    /// A plain MonoBehaviour, created at runtime by <see cref="ServerBossRoomState"/>. It can't be
    /// a NetworkBehaviour: Mirror bakes the component layout of a NetworkIdentity at build time, so
    /// nothing networked can be attached to the already-spawned game-state object at runtime. It
    /// doesn't need to be networked anyway — it only ever runs server-side and the boss it spawns
    /// is replicated by NetworkServer.Spawn.
    /// </remarks>
    public class ServerBossSpawner : MonoBehaviour
    {
        const string k_BossPrefabName = "ImpBoss";

        Transform[] m_PlayerSpawnPoints;
        NetworkGameState m_GameState;
        bool m_Spawned;

        /// <summary>Creates the spawner, which waits for the clock before spawning the boss.</summary>
        public static ServerBossSpawner Create(Transform[] playerSpawnPoints, NetworkGameState gameState)
        {
            // Tell the scene-placed NetworkObjectSpawner(ImpBoss) to stand down — we own the boss.
            NetworkObjectSpawner.SuppressBossSpawn = true;

            var go = new GameObject(nameof(ServerBossSpawner));
            var spawner = go.AddComponent<ServerBossSpawner>();
            spawner.m_PlayerSpawnPoints = playerSpawnPoints;
            spawner.m_GameState = gameState;
            spawner.StartCoroutine(spawner.CoroSpawnBoss());
            return spawner;
        }

        void OnDestroy()
        {
            NetworkObjectSpawner.SuppressBossSpawn = false;
        }

        IEnumerator CoroSpawnBoss()
        {
            if (m_GameState == null)
            {
                // Without the clock we can't tell when the boss window opens. Spawning at the start
                // of the match is the old (wrong) behaviour, so make the misconfiguration loud
                // instead of silently going back to it.
                Debug.LogError($"[{nameof(ServerBossSpawner)}] No {nameof(NetworkGameState)} — " +
                               "can't time the boss spawn, so no boss will appear this match.");
                yield break;
            }

            // Hold until the match clock hits the boss window (the final stretch). The timer is
            // only ticked while the match is actually running, so a pre-game/frozen clock simply
            // keeps us waiting here.
            while (m_GameState.Phase != MatchPhase.Ended &&
                   m_GameState.TimeRemaining > DeathmatchRules.BossSpawnTimeRemaining)
            {
                yield return null;
            }

            // The match ran out before the boss window (a very short match or an early forfeit
            // would do it), or the game state went away under us — either way, no boss.
            if (m_GameState == null || m_GameState.Phase == MatchPhase.Ended)
            {
                yield break;
            }

            SpawnBoss();
        }

        void SpawnBoss()
        {
            if (m_Spawned || !NetworkServer.active) return;

            var prefab = ResolveBossPrefab();
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(ServerBossSpawner)}] '{k_BossPrefabName}' is not in " +
                               "NetworkManager.spawnPrefabs — no boss will appear this match.");
                return;
            }

            if (!TryResolveSpawnPosition(out var position, out var rotation))
            {
                Debug.LogError($"[{nameof(ServerBossSpawner)}] Could not work out where the centre of " +
                               "the map is (no BossSpawnPoint and no player spawn points).");
                return;
            }

            m_Spawned = true;

            var boss = Instantiate(prefab, position, rotation);
            NetworkServer.Spawn(boss);
        }

        static GameObject ResolveBossPrefab()
        {
            var prefabs = NetworkManager.singleton != null ? NetworkManager.singleton.spawnPrefabs : null;
            return prefabs?.Find(p => p != null && p.name == k_BossPrefabName);
        }

        /// <summary>
        /// Where the boss goes: a designer-placed marker if there is one, otherwise the middle of
        /// the player spawn points, which is the centre of the play area by construction.
        /// </summary>
        bool TryResolveSpawnPosition(out Vector3 position, out Quaternion rotation)
        {
            var marker = GameObject.Find(DeathmatchRules.BossSpawnPointName);
            if (marker != null)
            {
                position = marker.transform.position;
                rotation = marker.transform.rotation;
                return true;
            }

            rotation = Quaternion.identity;
            position = Vector3.zero;

            if (m_PlayerSpawnPoints == null || m_PlayerSpawnPoints.Length == 0)
            {
                return false;
            }

            int count = 0;
            var sum = Vector3.zero;
            foreach (var point in m_PlayerSpawnPoints)
            {
                if (point == null) continue;
                sum += point.position;
                count++;
            }

            if (count == 0) return false;

            position = sum / count;

            // The centroid of a ring of spawn points can easily land inside a pillar or off the
            // walkable floor. Snapping to the NavMesh keeps the boss's AI able to move from the
            // first frame instead of standing stuck wherever the average happened to fall.
            if (NavMesh.SamplePosition(position, out var hit, 12f, NavMesh.AllAreas))
            {
                position = hit.position;
            }

            return true;
        }
    }
}
