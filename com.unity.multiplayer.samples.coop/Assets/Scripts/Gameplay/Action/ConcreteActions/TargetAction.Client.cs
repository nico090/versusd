using System;
using Unity.BossRoom.Gameplay.UserInput;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.BossRoom.Gameplay.Actions
{
    public partial class TargetAction
    {
        private GameObject m_TargetReticule;
        private ulong m_CurrentTarget;
        private ulong m_NewTarget;

        private const float k_ReticuleGroundHeight = 0.2f;

        public override bool OnStartClient(ClientCharacter clientCharacter)
        {
            base.OnStartClient(clientCharacter);
            clientCharacter.serverCharacter.TargetIdChanged += OnTargetChanged;
            clientCharacter.serverCharacter.GetComponent<ClientInputSender>().ActionInputEvent += OnActionInput;

            return true;
        }

        private void OnTargetChanged(ulong oldTarget, ulong newTarget)
        {
            m_NewTarget = newTarget;
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            if (m_CurrentTarget != m_NewTarget)
            {
                m_CurrentTarget = m_NewTarget;

                var targetObject = NetworkIdentityUtils.FindByNetId((uint)m_CurrentTarget);
                if (targetObject != null)
                {
                    var targetEntity = targetObject.GetComponent<ITargetable>();
                    if (targetEntity != null)
                    {
                        ValidateReticule(clientCharacter, targetObject);
                        m_TargetReticule.SetActive(true);

                        var parentTransform = targetObject.transform;
                        if (targetObject.TryGetComponent(out ServerCharacter serverCharacter) && serverCharacter.clientCharacter)
                        {
                            //for characters, attach the reticule to the child graphics object.
                            parentTransform = serverCharacter.clientCharacter.transform;
                        }

                        m_TargetReticule.transform.parent = parentTransform;
                        m_TargetReticule.transform.localPosition = new Vector3(0, k_ReticuleGroundHeight, 0);
                    }
                }
                else
                {
                    // null check here in case the target was destroyed along with the target reticule
                    if (m_TargetReticule != null)
                    {
                        m_TargetReticule.transform.parent = null;
                        m_TargetReticule.SetActive(false);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Ensures that the TargetReticule GameObject exists. This must be done prior to enabling it because it can be destroyed
        /// "accidentally" if its parent is destroyed while it is detached.
        /// </summary>
        void ValidateReticule(ClientCharacter parent, NetworkIdentity targetObject)
        {
            if (m_TargetReticule == null)
            {
                m_TargetReticule = Object.Instantiate(parent.TargetReticulePrefab);
            }

            bool target_isnpc = targetObject.GetComponent<ITargetable>().IsNpc;
            bool myself_isnpc = parent.serverCharacter.CharacterClass.IsNpc;
            bool hostile = target_isnpc != myself_isnpc;

            m_TargetReticule.GetComponent<MeshRenderer>().material = hostile ? parent.ReticuleHostileMat : parent.ReticuleFriendlyMat;
        }

        public override void CancelClient(ClientCharacter clientCharacter)
        {
            GameObject.Destroy(m_TargetReticule);

            clientCharacter.serverCharacter.TargetIdChanged -= OnTargetChanged;
            if (clientCharacter.TryGetComponent(out ClientInputSender inputSender))
            {
                inputSender.ActionInputEvent -= OnActionInput;
            }
        }

        private void OnActionInput(ActionRequestData data)
        {
            //this method runs on the owning client, and allows us to anticipate our new target for purposes of FX visualization.
            if (GameDataSource.Instance.GetActionPrototypeByID(data.ActionID).IsGeneralTargetAction)
            {
                // A Target action with no target ids is a "clear target" request (see
                // TargetAction.OnStart), e.g. the continuous auto-lock releasing when
                // nothing is in front. Treat that as target 0 instead of indexing null.
                m_NewTarget = (data.TargetIds != null && data.TargetIds.Length > 0) ? data.TargetIds[0] : 0;
            }
        }
    }
}
