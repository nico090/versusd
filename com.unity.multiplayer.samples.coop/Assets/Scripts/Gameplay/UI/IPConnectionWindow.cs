using System;
using System.Collections;
using TMPro;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    public class IPConnectionWindow : MonoBehaviour
    {
        // KCP default: 10 connection attempts × 1s timeout = 10s display
        const int k_ConnectionTimeoutSeconds = 10;

        [SerializeField]
        CanvasGroup m_CanvasGroup;

        [SerializeField]
        TextMeshProUGUI m_TitleText;

        [Inject] IPUIMediator m_IPUIMediator;

        ISubscriber<ConnectStatus> m_ConnectStatusSubscriber;

        [Inject]
        void InjectDependencies(ISubscriber<ConnectStatus> connectStatusSubscriber)
        {
            m_ConnectStatusSubscriber = connectStatusSubscriber;
            m_ConnectStatusSubscriber.Subscribe(OnConnectStatusMessage);
        }

        void Awake()
        {
            Hide();
        }

        void OnDestroy()
        {
            m_ConnectStatusSubscriber?.Unsubscribe(OnConnectStatusMessage);
        }

        void OnConnectStatusMessage(ConnectStatus connectStatus)
        {
            // A status can land while the menu scene is being torn down: joining starts loading
            // the next scene, and the relay reporting the room gone arrives a frame or two later.
            // Destruction order within a scene is not guaranteed, so this handler can still run
            // after the window's own CanvasGroup has been destroyed.
            if (m_CanvasGroup == null)
            {
                return;
            }

            CancelConnectionWindow();

            if (m_IPUIMediator != null)
            {
                m_IPUIMediator.DisableSignInSpinner();
            }
        }

        void Show()
        {
            m_CanvasGroup.alpha = 1f;
            m_CanvasGroup.blocksRaycasts = true;
        }

        void Hide()
        {
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.blocksRaycasts = false;
        }

        public void ShowConnectingWindow()
        {
            StartCoroutine(DisplayConnectionDuration(k_ConnectionTimeoutSeconds, () =>
            {
                Hide();
                m_IPUIMediator.DisableSignInSpinner();
            }));
            Show();
        }

        public void CancelConnectionWindow()
        {
            Hide();
            StopAllCoroutines();
        }

        IEnumerator DisplayConnectionDuration(int seconds, Action endAction)
        {
            while (seconds > 0)
            {
                m_TitleText.text = $"Connecting...\n{seconds}";
                yield return new WaitForSeconds(1f);
                seconds--;
            }
            m_TitleText.text = "Conectando...";
            endAction();
        }

        public void OnCancelJoinButtonPressed()
        {
            CancelConnectionWindow();
            m_IPUIMediator.JoiningWindowCancelled();
        }
    }
}
