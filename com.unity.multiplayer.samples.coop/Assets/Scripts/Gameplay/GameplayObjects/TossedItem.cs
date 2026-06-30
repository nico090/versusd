using System;
using Mirror;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    public class TossedItem : NetworkBehaviour
    {
        [Header("Server")]

        [SerializeField]
        int m_DamagePoints;

        [SerializeField]
        float m_HitRadius = 5f;

        [SerializeField]
        float m_KnockbackSpeed;

        [SerializeField]
        float m_KnockbackDuration;

        [SerializeField]
        LayerMask m_LayerMask;

        bool m_Started;

        const int k_MaxCollisions = 16;

        Collider[] m_CollisionCache = new Collider[k_MaxCollisions];

        [SerializeField]
        float m_DetonateAfterSeconds = 5f;

        float m_DetonateTimer;

        [SerializeField]
        float m_DestroyAfterSeconds = 6f;

        float m_DestroyTimer;

        bool m_Detonated;

        public UnityEvent detonatedCallback;

        [Header("Client")]

        [SerializeField]
        Transform m_TossedItemVisualTransform;

        const float k_DisplayHeight = 0.1f;

        readonly Quaternion k_TossAttackRadiusDisplayRotation = Quaternion.Euler(90f, 0f, 0f);

        [SerializeField]
        GameObject m_TossedObjectGraphics;

        [SerializeField]
        AudioSource m_FallingSound;

        public override void OnStartServer()
        {
            m_Started = true;
            m_Detonated = false;

            m_DetonateTimer = Time.fixedTime + m_DetonateAfterSeconds;
            m_DestroyTimer = Time.fixedTime + m_DestroyAfterSeconds;
        }

        public override void OnStartClient()
        {
            m_TossedItemVisualTransform.gameObject.SetActive(true);
            m_TossedObjectGraphics.SetActive(true);
            m_FallingSound.Play();
        }

        public override void OnStopServer()
        {
            m_Started = false;
            m_Detonated = false;
        }

        public override void OnStopClient()
        {
            m_TossedItemVisualTransform.gameObject.SetActive(false);
        }

        void Detonate()
        {
            var hits = Physics.OverlapSphereNonAlloc(transform.position, m_HitRadius, m_CollisionCache, m_LayerMask);

            for (int i = 0; i < hits; i++)
            {
                if (m_CollisionCache[i].gameObject.TryGetComponent(out IDamageable damageReceiver))
                {
                    damageReceiver.ReceiveHitPoints(null, -m_DamagePoints);

                    var serverCharacter = m_CollisionCache[i].gameObject.GetComponentInParent<ServerCharacter>();
                    if (serverCharacter)
                    {
                        serverCharacter.Movement.StartKnockback(transform.position, m_KnockbackSpeed, m_KnockbackDuration);
                    }
                }
            }

            // send client RPC to detonate on clients
            RpcDetonate();

            m_Detonated = true;
        }

        [ClientRpc]
        void RpcDetonate()
        {
            detonatedCallback?.Invoke();
        }

        void FixedUpdate()
        {
            if (isServer)
            {
                if (!m_Started)
                {
                    return; //don't do anything before OnStartServer has run.
                }

                if (!m_Detonated && m_DetonateTimer < Time.fixedTime)
                {
                    Detonate();
                }

                if (m_Detonated && m_DestroyTimer < Time.fixedTime)
                {
                    // destroy after sending detonate RPC
                    NetworkServer.Destroy(gameObject);
                }
            }
        }

        void LateUpdate()
        {
            if (isClient)
            {
                var tossedItemPosition = transform.position;
                m_TossedItemVisualTransform.SetPositionAndRotation(
                    new Vector3(tossedItemPosition.x, k_DisplayHeight, tossedItemPosition.z),
                    k_TossAttackRadiusDisplayRotation);
            }
        }
    }
}
