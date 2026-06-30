using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.Animations;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    public class ServerDisplacerOnParentChange : NetworkBehaviour
    {
        [SerializeField]
        NetworkTransformBase m_NetworkTransform;

        [SerializeField]
        PositionConstraint m_PositionConstraint;

        const float k_DropAnimationLength = 0.1f;

        void Awake()
        {
            m_PositionConstraint.enabled = false;
            enabled = false;
        }

        public override void OnStartServer()
        {
            m_PositionConstraint.enabled = true;
            enabled = true;
        }

        // Mirror has no OnNetworkObjectParentChanged; use Unity's built-in MonoBehaviour callback.
        void OnTransformParentChanged()
        {
            if (!isServer)
                return;

            RemoveParentConstraintSources();

            if (transform.parent == null)
            {
                StopAllCoroutines();
                m_NetworkTransform.enabled = true;
                m_PositionConstraint.enabled = true;
                StartCoroutine(SmoothPositionLerpY(k_DropAnimationLength, 0));
            }
        }

        void RemoveParentConstraintSources()
        {
            if (m_PositionConstraint)
            {
                for (int i = m_PositionConstraint.sourceCount - 1; i >= 0; i--)
                    m_PositionConstraint.RemoveSource(i);
            }
        }

        IEnumerator SmoothPositionLerpY(float length, float targetHeight)
        {
            var start = transform.position.y;
            var progress = 0f;
            var duration = 0f;

            while (progress < 1f)
            {
                duration += Time.deltaTime;
                progress = Mathf.Clamp(duration / length, 0f, 1f);
                var progressY = Mathf.Lerp(start, targetHeight, progress);
                transform.position = new Vector3(transform.position.x, progressY, transform.position.z);
                yield return null;
            }
        }
    }
}
