using Mirror;
using UnityEngine;

namespace Unity.Multiplayer.Samples.BossRoom.Client
{
    public class ClientPickUpPotEffects : NetworkBehaviour
    {
        [SerializeField]
        ParticleSystem m_PutDownParticleSystem;

        [SerializeField]
        AudioSource m_PickUpSound;

        [SerializeField]
        AudioSource m_PutDownSound;

        void Awake()
        {
            enabled = false;
        }

        public override void OnStartClient()
        {
            enabled = true;
        }

        // Mirror doesn't have OnNetworkObjectParentChanged; Unity fires OnTransformParentChanged natively.
        void OnTransformParentChanged()
        {
            if (!isClient)
                return;

            if (transform.parent == null)
            {
                m_PutDownParticleSystem.Play();
                m_PutDownSound.Play();
            }
            else
            {
                m_PickUpSound.Play();
            }
        }
    }
}
