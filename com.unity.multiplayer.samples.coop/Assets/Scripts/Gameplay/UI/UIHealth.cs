using System;
using Unity.BossRoom.Gameplay.GameplayObjects;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// UI object that visually represents an object's health. Visuals are updated when the NetworkHealthState reports a change.
    /// </summary>
    public class UIHealth : MonoBehaviour
    {
        [SerializeField]
        Slider m_HitPointsSlider;

        NetworkHealthState m_NetworkedHealth;

        public void Initialize(NetworkHealthState networkedHealth, int maxValue)
        {
            m_NetworkedHealth = networkedHealth;

            m_HitPointsSlider.minValue = 0;
            m_HitPointsSlider.maxValue = maxValue;
            HealthChanged(maxValue, maxValue);

            m_NetworkedHealth.HitPointsChanged += HealthChanged;
        }

        void HealthChanged(int previousValue, int newValue)
        {
            m_HitPointsSlider.value = newValue;
            // disable slider when we're at full health!
            m_HitPointsSlider.gameObject.SetActive(m_HitPointsSlider.value != m_HitPointsSlider.maxValue);
        }

        void OnDestroy()
        {
            if (m_NetworkedHealth != null)
            {
                m_NetworkedHealth.HitPointsChanged -= HealthChanged;
            }
        }
    }
}
