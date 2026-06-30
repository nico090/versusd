using System;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    public class ServerAnimationHandler : NetworkBehaviour
    {
        [SerializeField]
        NetworkAnimator m_NetworkAnimator;

        [SerializeField]
        VisualizationConfiguration m_VisualizationConfiguration;

        [SerializeField]
        NetworkLifeState m_NetworkLifeState;

        public NetworkAnimator NetworkAnimator => m_NetworkAnimator;

        public void SetTrigger(string triggerName) => m_NetworkAnimator?.SetTrigger(triggerName);
        public void SetTrigger(int triggerID) => m_NetworkAnimator?.SetTrigger(triggerID);
        public void ResetTrigger(string triggerName) => m_NetworkAnimator?.ResetTrigger(triggerName);
        public void ResetTrigger(int triggerID) => m_NetworkAnimator?.ResetTrigger(triggerID);
        public void SetAnimatorInt(string paramName, int value) => m_NetworkAnimator?.animator?.SetInteger(paramName, value);
        public int GetAnimatorInt(string paramName) => m_NetworkAnimator?.animator?.GetInteger(paramName) ?? 0;

        public override void OnStartServer()
        {
            m_NetworkLifeState.LifeStateChanged += OnLifeStateChanged;
        }

        void OnLifeStateChanged(LifeState previousValue, LifeState newValue)
        {
            if (m_NetworkAnimator == null || m_VisualizationConfiguration == null) return;

            switch (newValue)
            {
                case LifeState.Alive:
                    NetworkAnimator.SetTrigger(m_VisualizationConfiguration.AliveStateTriggerID);
                    break;
                case LifeState.Fainted:
                    NetworkAnimator.SetTrigger(m_VisualizationConfiguration.FaintedStateTriggerID);
                    break;
                case LifeState.Dead:
                    NetworkAnimator.SetTrigger(m_VisualizationConfiguration.DeadStateTriggerID);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(newValue), newValue, null);
            }
        }

        public override void OnStopServer()
        {
            if (m_NetworkLifeState != null)
            {
                m_NetworkLifeState.LifeStateChanged -= OnLifeStateChanged;
            }
        }
    }
}
