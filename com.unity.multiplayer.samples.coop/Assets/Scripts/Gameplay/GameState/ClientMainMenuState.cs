using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Unity.BossRoom.Gameplay.GameState
{
    /// <summary>
    /// Main menu state. Shows LoginUI first; main menu buttons are gated until authenticated.
    /// </summary>
    public class ClientMainMenuState : GameStateBehaviour
    {
        public override GameState ActiveState => GameState.MainMenu;

        [SerializeField] NameGenerationData m_NameGenerationData;
        [SerializeField] SessionUIMediator m_SessionUIMediator;
        [SerializeField] IPUIMediator m_IPUIMediator;
        [SerializeField] Button m_SessionButton;
        [SerializeField] GameObject m_SignInSpinner;
        [SerializeField] UIProfileSelector m_UIProfileSelector;
        [SerializeField] UITooltipDetector m_UGSSetupTooltipDetector;
        [SerializeField] LoginUI m_LoginUI;
        [SerializeField] Text m_UsernameLabel;

        [Inject] ProfileManager m_ProfileManager;

        public IObjectResolver Container { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            m_SessionUIMediator?.Hide();
            m_IPUIMediator?.Hide();
            if (m_SessionButton) m_SessionButton.interactable = false;
            if (m_SignInSpinner) m_SignInSpinner.SetActive(false);
            if (m_UGSSetupTooltipDetector) m_UGSSetupTooltipDetector.enabled = false;
            if (m_UsernameLabel) m_UsernameLabel.text = string.Empty;
        }

        void Start()
        {
            if (m_LoginUI != null)
            {
                m_LoginUI.OnAuthSuccess += OnAuthSuccess;
                m_LoginUI.Show();
            }
            else
            {
                // No LoginUI in scene — skip auth gate (editor / direct-IP-only builds)
                UnlockMainMenu(string.Empty);
            }
        }

        void OnDestroy()
        {
            if (m_LoginUI != null)
                m_LoginUI.OnAuthSuccess -= OnAuthSuccess;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterComponent(m_NameGenerationData);
            builder.RegisterComponent(m_SessionUIMediator);
            builder.RegisterComponent(m_IPUIMediator);
            if (m_LoginUI != null)
                builder.RegisterComponent(m_LoginUI);
        }

        void OnAuthSuccess(string username)
        {
            m_LoginUI?.Hide();
            UnlockMainMenu(username);
        }

        void UnlockMainMenu(string username)
        {
            if (m_UsernameLabel) m_UsernameLabel.text = username;
            if (m_SessionButton) m_SessionButton.interactable = true;
        }

        public void OnStartClicked()
        {
            // "Start Session" opens the master-server lobby UI: create a room
            // (P2P or dedicated/VPS via the injected toggle) or browse & join
            // public rooms. This is the path that can spawn a dedicated container.
            m_IPUIMediator?.Hide();
            m_SessionUIMediator?.Show();
        }

        public void OnDirectIPClicked()
        {
            // "Start with IP" is the direct peer-to-peer path (host/join by IP),
            // bypassing the master server entirely.
            m_SessionUIMediator?.Hide();
            m_IPUIMediator?.Show();
        }

        public void OnChangeProfileClicked()
        {
            m_UIProfileSelector?.Show();
        }
    }
}
