using System.Collections;
using System.Collections.Generic;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Server-only. Puts effect zones on the ground and applies what they do.
    /// </summary>
    /// <remarks>
    /// <para>Built the same way as <see cref="ServerBossSpawner"/> — a bare GameObject made at
    /// runtime and owned by <see cref="ServerBossRoomState"/> — because there is no prefab to
    /// register and nothing here needs its own network identity. The zones replicate as a
    /// <c>SyncList</c> on <see cref="NetworkGameState"/>, which is already a NetworkBehaviour on
    /// the state object, so the whole feature is code and touches no asset.</para>
    ///
    /// <para><b>Why the server owns the effect entirely.</b> Healing, damage and the boons are
    /// decided here and only here; the client is told the circle exists and draws it. A client that
    /// disagrees about where a zone is cannot heal itself, and a laggy one is not punished for
    /// standing somewhere its own picture said was safe any more than it already is for any other
    /// server-authoritative damage.</para>
    ///
    /// <para><b>The two boons outlive their zone on purpose.</b> Speed and double damage last
    /// <see cref="ZoneRules.BoonSeconds"/> counted from the last tick you were inside, so the zone
    /// is somewhere you go to pick something up and then leave, not somewhere you stand. Heal and
    /// hazard are the opposite — they only act while you are in them — which is what makes the
    /// green one contested and the red one an obstacle rather than a trap you die to once.</para>
    /// </remarks>
    public class ServerZoneSpawner : MonoBehaviour
    {
        NetworkGameState m_GameState;
        Vector3 m_Centre;
        int m_NextId = 1;

        readonly List<ServerCharacter> m_Scratch = new List<ServerCharacter>();

        public static ServerZoneSpawner Create(Transform[] playerSpawnPoints, NetworkGameState gameState)
        {
            var go = new GameObject(nameof(ServerZoneSpawner));
            var spawner = go.AddComponent<ServerZoneSpawner>();
            spawner.m_GameState = gameState;
            spawner.m_Centre = Centroid(playerSpawnPoints);

            ZoneBoons.Clear();
            gameState.Zones.Clear();

            Debug.Log($"[Zones] Spawner up. Centre {spawner.m_Centre}, first zone in " +
                      $"{ZoneRules.SpawnIntervalSeconds * 0.5f:0.#}s.");

            spawner.StartCoroutine(spawner.CoroSpawnZones());
            spawner.StartCoroutine(spawner.CoroApplyEffects());
            return spawner;
        }

        void OnDestroy()
        {
            // The boons are static, so leaving them behind would carry a player's double damage
            // into the next match.
            ZoneBoons.Clear();

            if (m_GameState != null)
            {
                m_GameState.Zones.Clear();
            }
        }

        static Vector3 Centroid(Transform[] points)
        {
            if (points == null || points.Length == 0)
            {
                return Vector3.zero;
            }

            var sum = Vector3.zero;
            int counted = 0;
            foreach (var point in points)
            {
                if (point != null)
                {
                    sum += point.position;
                    counted++;
                }
            }

            return counted == 0 ? Vector3.zero : sum / counted;
        }

        // ── Spawning ──────────────────────────────────────────────────────────────────────────

        IEnumerator CoroSpawnZones()
        {
            // Nothing lands until the warm-up is over. The first zone would otherwise arrive about
            // nine seconds in, and the speed and damage boons outlive their zone by
            // ZoneRules.BoonSeconds — so whoever happened to be standing on it would start the
            // match buffed for something they did while nobody could be hurt.
            while (m_GameState != null && m_GameState.Phase == MatchPhase.Warmup)
            {
                yield return null;
            }

            // A short head start, so the first zone is something that happens during the match
            // rather than part of the opening picture.
            yield return new WaitForSeconds(ZoneRules.SpawnIntervalSeconds * 0.5f);

            while (true)
            {
                RetireExpired();

                if (m_GameState.Zones.Count < ZoneRules.MaxConcurrent)
                {
                    SpawnOne();
                }

                float wait = ZoneRules.SpawnIntervalSeconds
                             + Random.Range(-ZoneRules.SpawnJitterSeconds, ZoneRules.SpawnJitterSeconds);
                yield return new WaitForSeconds(Mathf.Max(3f, wait));
            }
        }

        void SpawnOne()
        {
            if (!TryFindSpot(out var position))
            {
                return;
            }

            var zone = new ZoneState
            {
                Id = m_NextId++,
                Kind = PickKind(),
                Position = position,
                Radius = ZoneRules.Radius,
                ExpiresAt = NetworkTime.time + ZoneRules.LifetimeSeconds,
            };

            m_GameState.Zones.Add(zone);
            Debug.Log($"[Zones] Spawned {zone.Kind} zone {zone.Id} at {zone.Position} " +
                      $"({m_GameState.Zones.Count} live).");
        }

        /// <summary>
        /// Which zone appears. The three boons are equally likely and the hazard is rarer.
        /// </summary>
        /// <remarks>
        /// A hazard is the only one that is purely bad to walk into, so at even odds a quarter of
        /// everything the map offers would be a reason not to move — which is the opposite of what
        /// these are for.
        /// </remarks>
        static ZoneKind PickKind()
        {
            float roll = Random.value;
            if (roll < 0.30f) return ZoneKind.Heal;
            if (roll < 0.55f) return ZoneKind.Speed;
            if (roll < 0.80f) return ZoneKind.Damage;
            return ZoneKind.Hazard;
        }

        /// <summary>
        /// A spot inside the arena that is not on top of a zone that is already there.
        /// </summary>
        bool TryFindSpot(out Vector3 position)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var offset = Random.insideUnitCircle * ZoneRules.SpawnSpread;
                var candidate = m_Centre + new Vector3(offset.x, 0f, offset.y);

                bool tooClose = false;
                foreach (var existing in m_GameState.Zones)
                {
                    if (Vector3.Distance(existing.Position, candidate) < ZoneRules.MinSeparation)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                {
                    continue;
                }

                candidate.y = GroundHeightAt(candidate);

                position = candidate;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>Ground layers only, resolved once — GetMask is a string lookup.</summary>
        /// <remarks>
        /// <para>Masking is the whole point. The first version raycast against everything, and
        /// "everything" from twenty units up includes a player standing on the spot, an imp walking
        /// through it, a projectile in flight and any prop in the way — so the zone was placed on
        /// top of whatever happened to be there and hung in the air, sometimes several metres up.
        /// A zone belongs on the floor, and the floor is the only thing worth asking.</para>
        ///
        /// <para>Environment is included alongside Ground so a zone can legitimately land on a
        /// raised platform rather than falling through it to the floor below.</para>
        /// </remarks>
        static int s_GroundMask = -1;

        static int GroundMask
        {
            get
            {
                if (s_GroundMask < 0)
                {
                    s_GroundMask = LayerMask.GetMask("Ground", "Environment", "Default");
                }

                return s_GroundMask;
            }
        }

        /// <summary>
        /// The height of the floor under <paramref name="point"/>, or the arena's own height when
        /// there is nothing there to stand on.
        /// </summary>
        float GroundHeightAt(Vector3 point)
        {
            // Triggers ignored: several of them are room-sized volumes that a downward ray would
            // stop on, which would put the zone at the top of the box rather than on its floor.
            if (Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out var hit, 80f,
                    GroundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point.y + 0.05f;
            }

            // No floor found. Falling back to the spawn points' own height keeps the zone at play
            // level instead of at whatever y the random offset inherited.
            return m_Centre.y + 0.05f;
        }

        void RetireExpired()
        {
            for (int i = m_GameState.Zones.Count - 1; i >= 0; i--)
            {
                if (NetworkTime.time >= m_GameState.Zones[i].ExpiresAt)
                {
                    m_GameState.Zones.RemoveAt(i);
                }
            }
        }

        // ── Effects ───────────────────────────────────────────────────────────────────────────

        IEnumerator CoroApplyEffects()
        {
            var wait = new WaitForSeconds(ZoneRules.TickSeconds);

            while (true)
            {
                yield return wait;

                RetireExpired();

                if (m_GameState.Zones.Count == 0)
                {
                    continue;
                }

                CollectLivingCharacters();

                foreach (var zone in m_GameState.Zones)
                {
                    ApplyZone(zone);
                }
            }
        }

        void CollectLivingCharacters()
        {
            m_Scratch.Clear();

            foreach (var identity in NetworkServer.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out ServerCharacter character))
                {
                    continue;
                }

                if (character.LifeState == LifeState.Alive && character.physicsWrapper != null)
                {
                    m_Scratch.Add(character);
                }
            }
        }

        /// <summary>
        /// Hands hit points to a character the same way an attack does.
        /// </summary>
        /// <remarks>
        /// <para>Through its DamageReceiver rather than by calling ServerCharacter directly: that
        /// is the public route every weapon in the game already takes, and its own IsDamageable
        /// guard is what keeps a zone from healing a corpse or catching somebody mid-respawn.</para>
        ///
        /// <para><b>The inflicter is deliberately null.</b> A zone is the environment, and
        /// PublishMessageOnLifeChange reads a death with no attacker as scoring nobody — dying to
        /// the floor should not be a point for whoever chased you onto it.</para>
        /// </remarks>
        static void Deliver(ServerCharacter character, int hitPoints)
        {
            if (character.TryGetComponent(out DamageReceiver receiver))
            {
                receiver.ReceiveHitPoints(null, hitPoints);
            }
        }

        void ApplyZone(ZoneState zone)
        {
            // Per tick, not per second: the rates in ZoneRules are written per second because that
            // is how they are reasoned about, and converted here so changing the tick rate is a
            // performance decision rather than a balance one.
            int heal = Mathf.Max(1, Mathf.RoundToInt(ZoneRules.HealPerSecond * ZoneRules.TickSeconds));
            int hurt = Mathf.Max(1, Mathf.RoundToInt(ZoneRules.HazardPerSecond * ZoneRules.TickSeconds));

            float radiusSquared = zone.Radius * zone.Radius;

            foreach (var character in m_Scratch)
            {
                var toCharacter = character.physicsWrapper.Transform.position - zone.Position;
                toCharacter.y = 0f;
                if (toCharacter.sqrMagnitude > radiusSquared)
                {
                    continue;
                }

                switch (zone.Kind)
                {
                    case ZoneKind.Heal:
                        // Only up to the ceiling, which the character clamps to anyway.
                        if (character.HitPoints < character.MaxHitPoints)
                        {
                            Deliver(character, heal);
                        }
                        break;

                    case ZoneKind.Hazard:
                        Deliver(character, -hurt);
                        break;

                    case ZoneKind.Speed:
                        ZoneBoons.GrantSpeed((uint)character.netId);
                        break;

                    case ZoneKind.Damage:
                        ZoneBoons.GrantDamage((uint)character.netId);
                        break;
                }
            }
        }
    }
}
