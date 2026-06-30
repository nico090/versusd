using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.Utils;
using Unity.BossRoom.VisualEffects;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    public class PhysicsProjectile : NetworkBehaviour
    {
        bool m_Started;

        [SerializeField]
        SphereCollider m_OurCollider;

        ulong m_SpawnerId;
        ProjectileInfo m_ProjectileInfo;
        GameObject m_SourcePrefab; // tracked for pool return

        const int k_MaxCollisions = 4;
        const float k_WallLingerSec = 2f;
        const float k_EnemyLingerSec = 0.2f;
        Collider[] m_CollisionCache = new Collider[k_MaxCollisions];

        float m_DestroyAtSec;
        int m_CollisionMask;
        int m_BlockerMask;
        int m_NpcLayer;
        int m_PcLayer;

        List<GameObject> m_HitTargets = new List<GameObject>();
        bool m_IsDead;

        [SerializeField]
        [Tooltip("Explosion prefab used when projectile hits enemy.")]
        SpecialFXGraphic m_OnHitParticlePrefab;

        [SerializeField]
        TrailRenderer m_TrailRenderer;

        [SerializeField]
        Transform m_Visualization;

        const float k_LerpTime = 0.1f;
        PositionLerper m_PositionLerper;

        /// <summary>Call before NetworkServer.Spawn() — sets up projectile config.</summary>
        public void Initialize(ulong creatorsNetId, in ProjectileInfo projectileInfo, GameObject sourcePrefab = null)
        {
            m_SpawnerId = creatorsNetId;
            m_ProjectileInfo = projectileInfo;
            m_SourcePrefab = sourcePrefab;
        }

        public override void OnStartServer()
        {
            m_Started = true;
            m_HitTargets = new List<GameObject>();
            m_IsDead = false;
            m_DestroyAtSec = Time.fixedTime + (m_ProjectileInfo.Range / m_ProjectileInfo.Speed_m_s);
            m_NpcLayer = LayerMask.NameToLayer("NPCs");
            m_PcLayer  = LayerMask.NameToLayer("PCs");
            m_BlockerMask = LayerMask.GetMask("Default", "Environment");
            m_CollisionMask = GameDataSource.IsPvPMode
                ? LayerMask.GetMask("NPCs", "PCs", "Default", "Environment")
                : LayerMask.GetMask("NPCs", "Default", "Environment");
        }

        public override void OnStartClient()
        {
            m_TrailRenderer.Clear();
            m_Visualization.parent = null;
            m_PositionLerper = new PositionLerper(transform.position, k_LerpTime);
            m_Visualization.transform.rotation = transform.rotation;
        }

        public override void OnStopServer()
        {
            m_Started = false;
        }

        public override void OnStopClient()
        {
            m_TrailRenderer.Clear();
            m_Visualization.parent = transform;
        }

        void FixedUpdate()
        {
            if (!m_Started || !isServer)
                return;

            if (m_DestroyAtSec < Time.fixedTime)
            {
                ReturnToPool();
                return;
            }

            transform.position += transform.forward * (m_ProjectileInfo.Speed_m_s * Time.fixedDeltaTime);

            if (!m_IsDead)
                DetectCollisions();
        }

        void Update()
        {
            if (!isClient)
                return;

            bool isHost = isServer && isClient;
            if (isHost)
            {
                m_Visualization.position = m_PositionLerper.LerpPosition(m_Visualization.position, transform.position);
            }
            else
            {
                m_Visualization.position = transform.position;
            }
        }

        void ReturnToPool()
        {
            if (m_SourcePrefab != null && NetworkObjectPool.Singleton != null)
            {
                NetworkServer.UnSpawn(gameObject);
                NetworkObjectPool.Singleton.ReturnNetworkObject(gameObject, m_SourcePrefab);
            }
            else
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        void DetectCollisions()
        {
            var position = transform.localToWorldMatrix.MultiplyPoint(m_OurCollider.center);
            var numCollisions = Physics.OverlapSphereNonAlloc(position, m_OurCollider.radius, m_CollisionCache, m_CollisionMask);

            for (int i = 0; i < numCollisions; i++)
            {
                int layerTest = 1 << m_CollisionCache[i].gameObject.layer;
                if ((layerTest & m_BlockerMask) != 0)
                {
                    m_ProjectileInfo.Speed_m_s = 0;
                    m_IsDead = true;
                    m_DestroyAtSec = Time.fixedTime + k_WallLingerSec;
                    return;
                }

                int layer = m_CollisionCache[i].gameObject.layer;
                bool isHittable = layer == m_NpcLayer || (GameDataSource.IsPvPMode && layer == m_PcLayer);
                if (isHittable && !m_HitTargets.Contains(m_CollisionCache[i].gameObject))
                {
                    m_HitTargets.Add(m_CollisionCache[i].gameObject);

                    if (m_HitTargets.Count >= m_ProjectileInfo.MaxVictims)
                    {
                        m_DestroyAtSec = Time.fixedTime + k_EnemyLingerSec;
                        m_IsDead = true;
                    }

                    var targetNetId = m_CollisionCache[i].GetComponentInParent<NetworkIdentity>();
                    if (targetNetId && (ulong)(uint)targetNetId.netId != m_SpawnerId)
                    {
                        RpcHitEnemy((uint)targetNetId.netId);

                        var spawnerNet = NetworkIdentityUtils.FindByNetId((uint)m_SpawnerId);
                        var spawnerObj = spawnerNet != null ? spawnerNet.GetComponent<ServerCharacter>() : null;

                        if (m_CollisionCache[i].TryGetComponent(out IDamageable damageable))
                            damageable.ReceiveHitPoints(spawnerObj, -m_ProjectileInfo.Damage);
                    }

                    if (m_IsDead)
                        return;
                }
            }
        }

        [ClientRpc]
        void RpcHitEnemy(uint enemyNetId)
        {
            if (m_OnHitParticlePrefab)
                Instantiate(m_OnHitParticlePrefab.gameObject, transform.position, transform.rotation);
        }
    }
}
