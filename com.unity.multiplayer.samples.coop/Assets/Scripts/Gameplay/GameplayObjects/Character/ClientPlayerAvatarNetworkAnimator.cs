using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// Instantiates the player avatar graphics on the client when the avatar GUID is known.
    /// In Mirror, NetworkAnimator is a separate component on the prefab; this MonoBehaviour
    /// handles avatar graphics spawning via OnStartClient lifecycle.
    /// </summary>
    [RequireComponent(typeof(NetworkAnimator))]
    public class ClientPlayerAvatarNetworkAnimator : NetworkBehaviour
    {
        [SerializeField]
        NetworkAvatarGuidState m_NetworkAvatarGuidState;

        NetworkAnimator m_NetworkAnimator;
        bool m_AvatarInstantiated;

        void Awake()
        {
            m_NetworkAnimator = GetComponent<NetworkAnimator>();
        }

        public Animator Animator => m_NetworkAnimator != null ? m_NetworkAnimator.animator : null;

        public override void OnStartClient()
        {
            if (m_AvatarInstantiated)
                return;

            InstantiateAvatar();
        }

        public override void OnStopClient()
        {
            m_AvatarInstantiated = false;
            if (Animator != null && Animator.transform.childCount > 0)
                Destroy(Animator.transform.GetChild(0).gameObject);
        }

        void InstantiateAvatar()
        {
            if (Animator == null || Animator.transform.childCount > 0)
                return;

            Instantiate(m_NetworkAvatarGuidState.RegisteredAvatar.Graphics, Animator.transform);
            Animator.Rebind();
            m_AvatarInstantiated = true;
        }
    }
}
