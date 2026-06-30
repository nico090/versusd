using System;
using Mirror;
using Unity.BossRoom.Gameplay.Messages;
using Unity.BossRoom.Infrastructure;
using UnityEngine;
using VContainer;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>
    /// This class contains both client and server logic for a door that is opened when a player stands on a floor switch.
    /// (Assign the floor switches for this door in the editor.)
    /// Represents a door in the client. The visuals of the door animate as
    /// "opening" and "closing", but for physics purposes this is an illusion:
    /// whenever the door is open on the server, the door's physics are disabled,
    /// and vice versa.
    /// </summary>
    public class SwitchedDoor : NetworkBehaviour
    {
        [SerializeField]
        FloorSwitch[] m_SwitchesThatOpenThisDoor;

        [SerializeField]
        Animator m_Animator;

        [SyncVar(hook = nameof(OnDoorStateChanged))]
        bool m_IsOpen;

        public bool IsOpen => m_IsOpen;

        const string k_AnimatorDoorOpenBoolVarName = "IsOpen";

        [SerializeField, HideInInspector]
        int m_AnimatorDoorOpenBoolID;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool ForceOpen;
#endif

        [SerializeField]
        [Tooltip("This physics and navmesh obstacle is enabled when the door is closed.")]
        GameObject m_PhysicsObject;

        [Inject]
        IPublisher<DoorStateChangedEventMessage> m_Publisher;

        void Awake()
        {
            if (m_SwitchesThatOpenThisDoor.Length == 0)
                Debug.LogError("Door has no switches and can never be opened!", gameObject);
        }

        public override void OnStartClient()
        {
            // initialize visuals based on current server state (or else we default to "closed")
            m_PhysicsObject.SetActive(!m_IsOpen);
        }

        public override void OnStartServer()
        {
            OnDoorStateChanged(false, m_IsOpen);
        }

        public override void OnStopServer()
        {
            // cleanup if needed
        }

        void Update()
        {
            if (isServer)
            {
                var isAnySwitchOn = false;
                foreach (var floorSwitch in m_SwitchesThatOpenThisDoor)
                {
                    if (floorSwitch && floorSwitch.IsSwitchedOn)
                    {
                        isAnySwitchOn = true;
                        break;
                    }
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                isAnySwitchOn |= ForceOpen;
#endif

                m_IsOpen = isAnySwitchOn;
            }
        }

        void OnDoorStateChanged(bool wasDoorOpen, bool isDoorOpen)
        {
            if (isServer)
            {
                m_Animator.SetBool(m_AnimatorDoorOpenBoolID, isDoorOpen);
            }

            if (isClient)
            {
                m_PhysicsObject.SetActive(!isDoorOpen);
                if (m_Publisher != null)
                {
                    m_Publisher.Publish(new DoorStateChangedEventMessage() { IsDoorOpen = isDoorOpen });
                }
            }
        }

        void OnValidate()
        {
            m_AnimatorDoorOpenBoolID = Animator.StringToHash(k_AnimatorDoorOpenBoolVarName);
        }
    }
}
