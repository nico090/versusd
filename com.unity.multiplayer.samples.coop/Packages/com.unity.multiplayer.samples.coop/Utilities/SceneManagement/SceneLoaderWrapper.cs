using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Multiplayer.Samples.Utilities
{
    /// <summary>
    /// Wraps scene loading APIs.
    /// When useNetworkSceneManager is true and the server is active, delegates to
    /// Mirror's NetworkManager.ServerChangeScene so that scene objects receive netIds
    /// and clients are notified. Falls back to SceneManager for non-networked loads.
    /// </summary>
    public class SceneLoaderWrapper : MonoBehaviour
    {
        [SerializeField]
        ClientLoadingScreen m_ClientLoadingScreen;

        [SerializeField]
        LoadingProgressManager m_LoadingProgressManager;

        public static SceneLoaderWrapper Instance { get; protected set; }

        public virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
            }
            DontDestroyOnLoad(this);
        }

        public virtual void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public virtual void LoadScene(string sceneName, bool useNetworkSceneManager, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
        {
            if (useNetworkSceneManager && NetworkServer.active)
            {
                // Mirror's ServerChangeScene spawns scene NetworkIdentity objects (assigning netIds)
                // and notifies all clients to change scenes.
                NetworkManager.singleton.ServerChangeScene(sceneName);
                return;
            }

            var loadOperation = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);
            if (loadSceneMode == LoadSceneMode.Single)
            {
                m_ClientLoadingScreen.StartLoadingScreen(sceneName);
                m_LoadingProgressManager.LocalLoadOperation = loadOperation;
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
        {
            m_ClientLoadingScreen.StopLoadingScreen();
        }
    }
}
