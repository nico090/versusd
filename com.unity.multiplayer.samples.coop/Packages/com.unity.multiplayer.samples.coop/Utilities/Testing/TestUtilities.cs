using System;
using System.Collections;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Samples.Utilities
{
    public abstract class TestUtilities
    {
        const float k_MaxSceneLoadDuration = 10f;

        /// <summary>
        /// Custom IEnumerator class to validate the loading of a Scene by name.
        /// </summary>
        public class WaitForSceneLoad : CustomYieldInstruction
        {
            string m_SceneName;
            float m_LoadSceneStart;
            float m_MaxLoadDuration;

            public override bool keepWaiting
            {
                get
                {
                    var scene = SceneManager.GetSceneByName(m_SceneName);
                    var isSceneLoaded = scene.IsValid() && scene.isLoaded;

                    if (Time.time - m_LoadSceneStart >= m_MaxLoadDuration)
                    {
                        throw new Exception($"Timeout for scene load for scene name {m_SceneName}");
                    }

                    return !isSceneLoaded;
                }
            }

            public WaitForSceneLoad(string sceneName, float maxLoadDuration = k_MaxSceneLoadDuration)
            {
                m_LoadSceneStart = Time.time;
                m_SceneName = sceneName;
                m_MaxLoadDuration = maxLoadDuration;
            }
        }
    }
}
