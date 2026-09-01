using System;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// The "Target" Action is not a skill, but rather the result of a user left-clicking an enemy. This
    /// Action runs persistently, and automatically resets the NetworkCharacterState.Target property if the
    /// target becomes ineligible (dies or disappears). Note that while Actions in general can have multiple targets,
    /// you as a player can only have a single target selected at a time (the character that your target reticule appears under).
    /// </summary>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Target Action")]
    public partial class TargetAction : Action
    {
        public override bool OnStart(ServerCharacter serverCharacter)
        {
            //we must always clear the existing target, even if we don't run. This is how targets get cleared--running a TargetAction
            //with no target selected.
            serverCharacter.TargetId = 0;

            //there can only be one TargetAction at a time!
            serverCharacter.ActionPlayer.CancelRunningActionsByLogic(ActionLogic.Target, true, this);

            if (Data.TargetIds == null || Data.TargetIds.Length == 0) { return false; }

            serverCharacter.TargetId = TargetId;

            return true;
        }

        public override void Reset()
        {
            base.Reset();
            m_TargetReticule = null;
            m_CurrentTarget = 0;
            m_NewTarget = 0;
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            // Note this no longer swivels the character to face its target, which it used to do
            // whenever it was the only action running and we were standing still. Targeting is now
            // derived from where the player is aiming rather than chosen by a click, so turning the
            // body to the target meant the character quietly faced away from the aim — and since
            // the reticle updates several times a second, an idle player was spun around by every
            // imp that wandered into their cone. The body follows movement; an attack plants it on
            // the aim for the length of the swing (ServerCharacterMovement.LockFacing).
            return ActionUtils.IsValidTarget(TargetId);
        }

        public override void Cancel(ServerCharacter serverCharacter)
        {
            if (serverCharacter.TargetId == TargetId)
            {
                serverCharacter.TargetId = 0;
            }
        }

        private ulong TargetId { get { return Data.TargetIds[0]; } }
    }
}

