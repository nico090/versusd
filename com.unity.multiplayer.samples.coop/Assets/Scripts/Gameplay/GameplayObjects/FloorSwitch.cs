using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects
{
    /// <summary>
    /// Server-side logic for a floor switch (a/k/a "pressure plate").
    /// This script should be attached to a physics trigger.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FloorSwitch : NetworkBehaviour
    {
        [SerializeField]
        Animator m_Animator;

        [SerializeField]
        Collider m_Collider;

        [SyncVar(hook = nameof(FloorSwitchStateChanged))]
        bool m_IsSwitchedOn;

        public bool IsSwitchedOn => m_IsSwitchedOn;

        List<Collider> m_RelevantCollidersInTrigger = new List<Collider>();

        const string k_AnimatorPressedDownBoolVarName = "IsPressed";

        [SerializeField, HideInInspector]
        int m_AnimatorPressedDownBoolVarID;

        void Awake()
        {
            m_Collider.isTrigger = true;
        }

        public override void OnStartServer()
        {
            // server only - nothing extra needed
        }

        public override void OnStartClient()
        {
            FloorSwitchStateChanged(false, m_IsSwitchedOn);
        }

        void OnTriggerEnter(Collider other)
        {
            // no need to check for layer; layer matrix has been configured to only allow FloorSwitch x PC interactions
            m_RelevantCollidersInTrigger.Add(other);
        }

        void OnTriggerExit(Collider other)
        {
            m_RelevantCollidersInTrigger.Remove(other);
        }

        void FixedUpdate()
        {
            if (!isServer) return;

            // it's possible that the Colliders in our trigger have been destroyed, while still inside our trigger.
            // In this case, OnTriggerExit() won't get called for them! We can tell if a Collider was destroyed
            // because its reference will become null. So here we remove any nulls and see if we're still active.
            m_RelevantCollidersInTrigger.RemoveAll(col => col == null);
            m_IsSwitchedOn = m_RelevantCollidersInTrigger.Count > 0;
        }

        void FloorSwitchStateChanged(bool previousValue, bool newValue)
        {
            m_Animator.SetBool(m_AnimatorPressedDownBoolVarID, newValue);
        }

        void OnValidate()
        {
            m_AnimatorPressedDownBoolVarID = Animator.StringToHash(k_AnimatorPressedDownBoolVarName);
        }
    }
}
