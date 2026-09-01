using System;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// Causes the attacker to teleport near a target spot, then perform a melee attack. The client
    /// visualization moves the character locally beforehand, making the character appear to dash to the
    /// destination spot.
    ///
    /// After the ExecTime has elapsed, the character is immune to damage until the action ends.
    ///
    /// Since the "Range" field means "range when we can teleport to our target", we need another
    /// field to mean "range of our melee attack after dashing". We'll use the "Radius" field of the
    /// ActionDescription for that.
    /// </summary>
    /// <remarks>
    /// See MeleeAction for relevant discussion about targeting; we use the same concept here: preferring
    /// the chosen target, but using whatever is actually within striking distance at time of attack.
    /// </remarks>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Dash Attack Action")]
    public class DashAttackAction : Action
    {
        private Vector3 m_TargetSpot;

        private bool m_Dashed;

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            // remember the exact spot we'll stop.
            m_TargetSpot = ActionUtils.GetDashDestination(serverCharacter.physicsWrapper.Transform, Data.Position, true, Config.Range, Config.Range);

            // snap to face our destination. This ensures the client visualization faces the right way while "pretending" to dash
            serverCharacter.physicsWrapper.Transform.LookAt(m_TargetSpot);

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);

            // tell clients to visualize this action
            serverCharacter.clientCharacter.RpcPlayAction(Data);

            return ActionConclusion.Continue;
        }

        public override void Reset()
        {
            base.Reset();
            m_TargetSpot = default;
            m_Dashed = false;
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            return ActionConclusion.Continue;
        }

        public override void End(ServerCharacter serverCharacter)
        {
            // Anim2 contains the name of the end-loop-sequence trigger
            if (!string.IsNullOrEmpty(Config.Anim2))
            {
                serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim2);
            }

            // we're done, time to teleport!
            serverCharacter.Movement.Teleport(m_TargetSpot);

            // and then swing!
            PerformMeleeAttack(serverCharacter);
        }

        public override void Cancel(ServerCharacter serverCharacter)
        {
            // OtherAnimatorVariable contains the name of the cancellation trigger
            if (!string.IsNullOrEmpty(Config.OtherAnimatorVariable))
            {
                serverCharacter.serverAnimationHandler.SetTrigger(Config.OtherAnimatorVariable);
            }

            // because the client-side visualization of the action moves the character visualization around,
            // we need to explicitly end the client-side visuals when we abort
            serverCharacter.clientCharacter.RpcCancelActionsByPrototypeID(ActionID);

        }

        public override void BuffValue(BuffableValue buffType, ref float buffedValue)
        {
            if (TimeRunning >= Config.ExecTimeSeconds && buffType == BuffableValue.PercentDamageReceived)
            {
                // we suffer no damage during the "dash" (client-side pretend movement)
                buffedValue = 0;
            }
        }

        private void PerformMeleeAttack(ServerCharacter parent)
        {
            IDamageable foe = MeleeAction.GetIdealMeleeFoe(Config.IsFriendly ^ parent.IsNpc,
                parent.physicsWrapper.DamageCollider,
                Config.Radius, 0.0f,
                (Data.TargetIds != null && Data.TargetIds.Length > 0 ? Data.TargetIds[0] : 0),
                parent.NetworkObjectId);

            if (foe != null)
            {
                // PvP balance pass: the Rogue's dash hits slightly harder. It's the class's only
                // currency — if it doesn't kill fast, the Rogue has nothing.
                foe.ReceiveHitPoints(parent, -HeroBalance.ScaleDamage(parent, Config.Logic, Config.Amount));
            }
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            if (m_Dashed) { return ActionConclusion.Stop; } // we're done!

            // Nothing ever set m_Dashed. The partial that did — DashAttackAction.Client.cs in the
            // original sample — did not survive the Mirror port, so the flag was declared, reset
            // and read but never raised, and this method answered Continue for ever. The dash's
            // client visualisation therefore never concluded and the run loop played until
            // something else interrupted it.
            //
            // Ending on the action's own duration is what the server already does, so both halves
            // now finish on the same clock instead of the client waiting for a signal that was
            // never coming.
            if (TimeRunning >= Config.DurationSeconds)
            {
                m_Dashed = true;
                return ActionConclusion.Stop;
            }

            return ActionConclusion.Continue;
        }

        /// <summary>
        /// Puts the run animation away on the client's own authority.
        /// </summary>
        /// <remarks>
        /// The server fires Anim2 from <see cref="End"/> through the animation handler, and on a
        /// dedicated server that trigger does not reach clients — the same gap this project
        /// already had to work around for attack swings and life-state changes. So the one trigger
        /// that stops the dash loop was exactly the one that could go missing. Setting it here as
        /// well costs nothing when the server's copy does arrive: re-triggering a transition the
        /// animator has already taken is a no-op.
        /// </remarks>
        public override void EndClient(ClientCharacter clientCharacter)
        {
            base.EndClient(clientCharacter);

            if (!string.IsNullOrEmpty(Config.Anim2) && clientCharacter != null
                && clientCharacter.OurAnimator != null)
            {
                clientCharacter.OurAnimator.SetTrigger(Config.Anim2);
            }
        }
    }
}
