using System;
using TMPro;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    public class QualityButton : MonoBehaviour
    {
        [SerializeField]
        TMP_Text m_QualityBtnText;

        private void Start()
        {
            var index = QualitySettings.GetQualityLevel();
            m_QualityBtnText.text = QualitySettings.names[index];
        }

        public void SetQualitySettings()
        {
            var qualityLevels = QualitySettings.names.Length - 1;
            var currentLevel = QualitySettings.GetQualityLevel();

            if (currentLevel < qualityLevels)
            {
                QualitySettings.IncreaseLevel();
            }
            else
            {
                QualitySettings.SetQualityLevel(0);
            }

            m_QualityBtnText.text = QualitySettings.names[QualitySettings.GetQualityLevel()];

            // Kept, so the choice survives the session it was made in. Without this the button
            // moved the level and the next launch put it straight back.
            GraphicsQuality.Remember();
        }
    }
}


