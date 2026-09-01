using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// A spinning melee attack centred on the caster — the Rogue's Twisting Slash. The character
    /// whirls on the spot with the blade out, hitting everything around it several times over the
    /// action's duration.
    /// </summary>
    /// <remarks>
    /// <para>Unlike <see cref="MeleeAction"/> this is not a single hit-test in a cone: it ticks. A
    /// spin that dealt its damage once would be a worse basic attack with a longer animation; the
    /// point of the move is that standing next to the Rogue while it runs is a mistake, and that
    /// only reads if the damage keeps arriving. It is also why the per-tick <c>Amount</c> is small:
    /// the move's total output is Amount × ticks, and the ticks are the interesting part.</para>
    ///
    /// <para><b>The asset must stay non-interruptible.</b> In this codebase "interruptible" means
    /// "movement cancels it", enforced in two places: <c>ServerActionPlayer</c> stops the character
    /// dead when an interruptible action starts, and <c>ServerCharacter</c> clears the action again
    /// on the next movement input. Turning the flag on therefore roots the Rogue for the whole
    /// spin and kills the move the instant they touch the stick — which is the opposite of what
    /// this action is for. Walking out is the escape hatch here, not cancelling.</para>
    ///
    /// <para>The spin itself is driven through <see cref="ServerCharacterMovement.LockFacing"/>
    /// rather than by writing <c>transform.rotation</c> directly. Movement rewrites the character's
    /// rotation every physics tick to face where it is walking, so a directly-written rotation is
    /// stomped the instant the player moves — the spin would only work while standing still. The
    /// facing lock is the one channel movement already agrees to honour.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Spin Attack Action")]
    public class SpinAttackAction : Action
    {
        /// <summary>Full rotations per second while spinning.</summary>
        const float k_SpinsPerSecond = 2.5f;

        /// <summary>Seconds between damage ticks.</summary>
        const float k_TickIntervalSeconds = 0.3f;

        /// <summary>Used when the asset leaves Radius at 0, so a mis-serialised asset still spins
        /// and still hits rather than becoming a no-op animation.</summary>
        const float k_DefaultRadius = 3.5f;

        /// <summary>Likewise for a missing duration.</summary>
        const float k_DefaultDurationSeconds = 2f;

        /// <summary>How long each facing lock is held. Comfortably longer than a frame so the spin
        /// stays smooth, short enough that the lock lapses promptly when the action ends.</summary>
        const float k_FacingLockSeconds = 0.25f;

        float m_NextTickTime;
        bool m_Started;

        // Reused across ticks so a two-second spin doesn't allocate a fresh list every 0.3s.
        readonly List<IDamageable> m_TickVictims = new List<IDamageable>();

        float SpinRadius => Config.Radius > 0f ? Config.Radius : k_DefaultRadius;

        float SpinDuration => Config.DurationSeconds > 0f ? Config.DurationSeconds : k_DefaultDurationSeconds;

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            // The victims are found per tick, so there is no meaningful target list to broadcast.
            // Clearing it (rather than leaving whatever the client aimed at) stops the client-side
            // visualisation from playing a hit react on someone we may never touch.
            Data.TargetIds = new ulong[0];

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            serverCharacter.clientCharacter.RpcPlayAction(Data);

            m_Started = true;
            m_NextTickTime = Config.ExecTimeSeconds;
            return ActionConclusion.Continue;
        }

        public override void Reset()
        {
            base.Reset();
            m_Started = false;
            m_NextTickTime = 0f;
            m_TickVictims.Clear();
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (!m_Started)
            {
                return ActionConclusion.Stop;
            }

            float elapsed = TimeRunning;
            if (elapsed >= SpinDuration)
            {
                return ActionConclusion.Stop;
            }

            // Keep the body whirling. Re-asserted every frame because the lock is deliberately
            // short-lived — if the action is cancelled mid-spin the character stops turning within
            // a quarter second instead of being stuck pirouetting.
            float angle = elapsed * k_SpinsPerSecond * 360f;
            Vector3 spinDirection = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            clientCharacter.Movement.LockFacing(spinDirection, k_FacingLockSeconds);

            if (elapsed >= m_NextTickTime)
            {
                m_NextTickTime = elapsed + k_TickIntervalSeconds;
                PerformTick(clientCharacter);
            }

            return ActionConclusion.Continue;
        }

        void PerformTick(ServerCharacter parent)
        {
            Vector3 centre = parent.physicsWrapper.Transform.position;

            // Same friend/foe rule the other area attacks use: a hero only catches other heroes
            // when PvP is on, and a monster only ever catches heroes.
            string[] layers = GameDataSource.IsPvPMode && !parent.IsNpc
                ? new[] { "NPCs", "PCs" }
                : new[] { "NPCs" };

            var colliders = Physics.OverlapSphere(centre, SpinRadius, LayerMask.GetMask(layers));

            // A character contributes several colliders, so the same victim can appear more than
            // once in one sweep. Without this they would take the tick two or three times over,
            // which turns a tuned move into a blender.
            m_TickVictims.Clear();
            for (int i = 0; i < colliders.Length; i++)
            {
                var victim = colliders[i].GetComponent<IDamageable>();
                if (victim == null || !victim.IsDamageable())
                {
                    continue;
                }

                if (victim.NetworkObjectId == parent.NetworkObjectId)
                {
                    continue; // never hit yourself
                }

                if (m_TickVictims.Contains(victim))
                {
                    continue;
                }

                m_TickVictims.Add(victim);
            }

            int damage = HeroBalance.ScaleDamage(parent, Config.Logic, Config.Amount);
            for (int i = 0; i < m_TickVictims.Count; i++)
            {
                m_TickVictims[i].ReceiveHitPoints(parent, -damage);
            }
        }

        public override bool OnStartClient(ClientCharacter clientCharacter)
        {
            base.OnStartClient(clientCharacter);
            // The blade trail / dust ring, if the asset has one. Parented to the character so it
            // travels with a Rogue who keeps walking while spinning.
            InstantiateSpecialFXGraphics(clientCharacter.transform, true);
            return ActionConclusion.Continue;
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            return TimeRunning < SpinDuration ? ActionConclusion.Continue : ActionConclusion.Stop;
        }
    }
}
