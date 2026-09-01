using System;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Subscribes to connection status messages to display them through the popup panel.
    /// </summary>
    public class ConnectionStatusMessageUIManager : MonoBehaviour
    {
        DisposableGroup m_Subscriptions;

        PopupPanel m_CurrentReconnectPopup;

        [Inject]
        void InjectDependencies(ISubscriber<ConnectStatus> connectStatusSub, ISubscriber<ReconnectMessage> reconnectMessageSub)
        {
            m_Subscriptions = new DisposableGroup();
            m_Subscriptions.Add(connectStatusSub.Subscribe(OnConnectStatus));
            m_Subscriptions.Add(reconnectMessageSub.Subscribe(OnReconnectMessage));
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (m_Subscriptions != null)
            {
                m_Subscriptions.Dispose();
            }
        }

        void OnConnectStatus(ConnectStatus status)
        {
            switch (status)
            {
                case ConnectStatus.Undefined:
                case ConnectStatus.UserRequestedDisconnect:
                    break;
                case ConnectStatus.ServerFull:
                    PopupManager.ShowPopupPanel("No se pudo conectar", "La sala está llena y no acepta más jugadores.");
                    break;
                case ConnectStatus.Success:
                    break;
                case ConnectStatus.LoggedInAgain:
                    PopupManager.ShowPopupPanel("No se pudo conectar", "Iniciaste sesión en otro lado con la misma cuenta. Si querés conectarte igual, elegí otro perfil con el botón 'Cambiar perfil'.");
                    break;
                case ConnectStatus.IncompatibleBuildType:
                    PopupManager.ShowPopupPanel("No se pudo conectar", "Las versiones del servidor y del cliente no son compatibles. Una build de release no se puede conectar a una de desarrollo ni al Editor.");
                    break;
                case ConnectStatus.GenericDisconnect:
                    PopupManager.ShowPopupPanel("Se cortó la conexión", "Se perdió la conexión con la partida.");
                    break;
                case ConnectStatus.HostEndedSession:
                    PopupManager.ShowPopupPanel("Se terminó la partida", "El anfitrión salió y la sala se cerró. Podés crear una nueva o unirte a otra desde el menú.");
                    break;
                case ConnectStatus.Reconnecting:
                    break;
                case ConnectStatus.StartHostFailed:
                    PopupManager.ShowPopupPanel("No se pudo crear la sala", "No se pudo iniciar la partida como anfitrión.");
                    break;
                case ConnectStatus.StartClientFailed:
                    PopupManager.ShowPopupPanel("No se pudo conectar", "No se pudo iniciar la conexión. Revisá la dirección o probá de nuevo.");
                    break;
                default:
                    Debug.LogWarning($"New ConnectStatus {status} has been added, but no connect message defined for it.");
                    break;
            }
        }

        void OnReconnectMessage(ReconnectMessage message)
        {
            if (message.CurrentAttempt == message.MaxAttempt)
            {
                CloseReconnectPopup();
            }
            else if (m_CurrentReconnectPopup != null)
            {
                m_CurrentReconnectPopup.SetupPopupPanel("Connection lost", $"Attempting to reconnect...\nAttempt {message.CurrentAttempt + 1}/{message.MaxAttempt}", closeableByUser: false);
            }
            else
            {
                m_CurrentReconnectPopup = PopupManager.ShowPopupPanel("Connection lost", $"Attempting to reconnect...\nAttempt {message.CurrentAttempt + 1}/{message.MaxAttempt}", closeableByUser: false);
            }
        }

        void CloseReconnectPopup()
        {
            if (m_CurrentReconnectPopup != null)
            {
                m_CurrentReconnectPopup.Hide();
                m_CurrentReconnectPopup = null;
            }
        }
    }
}
