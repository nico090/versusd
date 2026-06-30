using System;
using System.Collections;
using Unity.BossRoom.ApplicationLifecycle.Messages;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.GameState;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.MasterServer;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Unity.BossRoom.ApplicationLifecycle
{
    /// <summary>
    /// Entry point and root DI scope. Unity Services and session management removed
    /// during the Mirror + Master Server migration.
    /// </summary>
    public class ApplicationController : LifetimeScope
    {
        [SerializeField]
        UpdateRunner m_UpdateRunner;
        [SerializeField]
        ConnectionManager m_ConnectionManager;
        [SerializeField]
        MasterServerConfig m_MasterServerConfig;

        IDisposable m_Subscriptions;

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterComponent(m_UpdateRunner);
            builder.RegisterComponent(m_ConnectionManager);

            builder.Register<ProfileManager>(Lifetime.Singleton);
            builder.Register<PersistentGameState>(Lifetime.Singleton);

            if (m_MasterServerConfig != null)
            {
                builder.RegisterInstance(m_MasterServerConfig);
                builder.Register<MasterServerFacade>(Lifetime.Singleton);
            }

            builder.RegisterInstance(new MessageChannel<QuitApplicationMessage>()).AsImplementedInterfaces();
            builder.RegisterInstance(new MessageChannel<ConnectStatus>()).AsImplementedInterfaces();
            builder.RegisterInstance(new MessageChannel<DoorStateChangedEventMessage>()).AsImplementedInterfaces();
            builder.RegisterInstance(new MessageChannel<ReconnectMessage>()).AsImplementedInterfaces();

            builder.RegisterInstance(new NetworkedMessageChannel<LifeStateChangedEventMessage>()).AsImplementedInterfaces();
            builder.RegisterInstance(new NetworkedMessageChannel<ConnectionEventMessage>()).AsImplementedInterfaces();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            builder.RegisterInstance(new NetworkedMessageChannel<CheatUsedMessage>()).AsImplementedInterfaces();
#endif
        }

        void Start()
        {
            var quitApplicationSub = Container.Resolve<ISubscriber<QuitApplicationMessage>>();
            var subHandles = new DisposableGroup();
            subHandles.Add(quitApplicationSub.Subscribe(QuitGame));
            m_Subscriptions = subHandles;

            Application.wantsToQuit += OnWantToQuit;
            DontDestroyOnLoad(gameObject);
            DontDestroyOnLoad(m_UpdateRunner.gameObject);
            Application.targetFrameRate = 120;

            if (Application.isBatchMode)
            {
                // Headless dedicated server (container runs with -batchmode): skip the
                // client menu entirely and run the master-server registration /
                // allocation lifecycle. The NetworkManager lives in this Startup scene,
                // so StartServer() inside the bootstrapper has it available. The
                // bootstrapper reads MASTER_SERVER_URL / SERVER_IP / SERVER_PORT /
                // SERVER_SHARED_SECRET from the environment injected by spawn.py.
                gameObject.AddComponent<DedicatedServer.DedicatedServerBootstrapper>();
                return;
            }

            SceneManager.LoadScene("MainMenu");
        }

        protected override void OnDestroy()
        {
            m_Subscriptions?.Dispose();
            base.OnDestroy();
        }

        bool OnWantToQuit()
        {
            Application.wantsToQuit -= OnWantToQuit;
            return true;
        }

        void QuitGame(QuitApplicationMessage msg)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
