using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// A burst of ice around the caster that damages and freezes whoever it catches — the Tank's
    /// Frost Nova. Frozen characters cannot move or act until it wears off.
    /// </summary>
    /// <remarks>
    /// <para>This gives the Tank the one thing the PvP balance pass says it lacked: the class has
    /// no stun, so its only lever was permanent damage reduction, which is exactly what got put on
    /// a long cooldown (see <see cref="HeroBalance"/>). A short, close-range freeze is control the
    /// Tank has to walk into melee to use, which suits a class that wants to be stood next to.</para>
    ///
    /// <para>The freeze is implemented with the movement system's forced-movement state at zero
    /// speed, rather than as a new "frozen" flag. That state already means "you are not in charge
    /// of this character right now": <c>ServerCharacter</c> refuses movement input and refuses to
    /// start actions while it is set, and clients already animate it as
    /// <c>MovementStatus.Uncontrolled</c>. A separate flag would have needed all three of those
    /// taught about it, and would have been one more thing to keep in sync.</para>
    ///
    /// <para>Deliberately NOT built on <see cref="StunnedAction"/>: that action's length comes from
    /// its own asset, so the freeze would have run for a duration this power has no control over —
    /// two timers for one effect, drifting apart the moment either asset is retuned.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Frost Nova Action")]
    public class FrostNovaAction : Action
    {
        /// <summary>Used when the asset leaves Radius at 0, so the nova is never a no-op.</summary>
        const float k_DefaultRadius = 5f;

        /// <summary>
        /// Used when the asset leaves EffectDuration at 0. Kept short on purpose: being unable to
        /// act is the least fun thing that can happen to a player, so the freeze is long enough to
        /// land a follow-up and no longer.
        /// </summary>
        const float k_DefaultFreezeSeconds = 1.5f;

        /// <summary>
        /// Hard ceiling on the freeze regardless of what the asset says. A control effect is the
        /// one number that must never be tunable into "you don't get to play" territory.
        /// </summary>
        const float k_MaxFreezeSeconds = 2.5f;

        bool m_DidNova;

        readonly List<ServerCharacter> m_Victims = new List<ServerCharacter>();

        float NovaRadius => Config.Radius > 0f ? Config.Radius : k_DefaultRadius;

        float FreezeSeconds => Mathf.Min(
            Config.EffectDurationSeconds > 0f ? Config.EffectDurationSeconds : k_DefaultFreezeSeconds,
            k_MaxFreezeSeconds);

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            // Centred on the caster, so there is nothing to aim and nobody known in advance.
            Data.TargetIds = new ulong[0];

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            serverCharacter.clientCharacter.RpcPlayAction(Data);
            return ActionConclusion.Continue;
        }

        public override void Reset()
        {
            base.Reset();
            m_DidNova = false;
            m_Victims.Clear();
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (!m_DidNova && TimeRunning >= Config.ExecTimeSeconds)
            {
                m_DidNova = true;
                PerformNova(clientCharacter);
            }

            return ActionConclusion.Continue;
        }

        void PerformNova(ServerCharacter parent)
        {
            Vector3 centre = parent.physicsWrapper.Transform.position;

            string[] layers = GameDataSource.IsPvPMode && !parent.IsNpc
                ? new[] { "NPCs", "PCs" }
                : new[] { "NPCs" };

            var colliders = Physics.OverlapSphere(centre, NovaRadius, LayerMask.GetMask(layers));

            m_Victims.Clear();
            for (int i = 0; i < colliders.Length; i++)
            {
                var victim = colliders[i].GetComponentInParent<ServerCharacter>();
                if (victim == null || victim == parent)
                {
                    continue; // the caster is immune to its own ice
                }

                if (victim.LifeState != LifeState.Alive)
                {
                    continue;
                }

                if (m_Victims.Contains(victim))
                {
                    continue; // one character, several colliders
                }

                m_Victims.Add(victim);
            }

            int damage = HeroBalance.ScaleDamage(parent, Config.Logic, Config.Amount);
            float freeze = FreezeSeconds;

            for (int i = 0; i < m_Victims.Count; i++)
            {
                var victim = m_Victims[i];

                if (damage > 0)
                {
                    victim.ApplyHealthChange(parent, -damage);
                }

                // Damage can be lethal — freezing a corpse would leave it locked in the forced
                // movement state for the rest of its death animation.
                if (victim.LifeState != LifeState.Alive)
                {
                    continue;
                }

                // Whatever they were in the middle of is over.
                victim.ActionPlayer.ClearActions(false);

                // Zero-speed forced movement: they hold position, can't steer, can't act.
                victim.Movement.StartKnockback(centre, 0f, freeze);
            }
        }

        public override bool OnStartClient(ClientCharacter clientCharacter)
        {
            base.OnStartClient(clientCharacter);
            // The ice burst, parented to the caster so it sits where the nova actually went off.
            InstantiateSpecialFXGraphics(clientCharacter.transform, false);
            return ActionConclusion.Stop;
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            return ActionConclusion.Stop;
        }
    }
}
