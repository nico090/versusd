using System;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>
    /// MonoBehaviour containing only one SyncVar which represents this object's health.
    /// </summary>
    public class NetworkHealthState : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnHitPointsChanged))]
        int m_HitPoints;

        public int HitPoints
        {
            get => m_HitPoints;
            set => m_HitPoints = value;
        }

        // public subscribable event to be invoked when HP has been fully depleted
        public event Action HitPointsDepleted;

        // public subscribable event to be invoked when HP has been replenished
        public event Action HitPointsReplenished;

        // public subscribable event fired whenever HP changes; signature: (oldValue, newValue)
        public event Action<int, int> HitPointsChanged;

        void OnHitPointsChanged(int oldVal, int newVal)
        {
            HitPointsChanged?.Invoke(oldVal, newVal);

            if (oldVal > 0 && newVal <= 0)
            {
                // newly reached 0 HP
                HitPointsDepleted?.Invoke();
            }
            else if (oldVal <= 0 && newVal > 0)
            {
                // newly revived
                HitPointsReplenished?.Invoke();
            }
        }
    }
}
