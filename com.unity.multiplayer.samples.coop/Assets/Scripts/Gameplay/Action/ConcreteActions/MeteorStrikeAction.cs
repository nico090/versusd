using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// A delayed area strike that falls on a chosen spot from above — the Mage's Meteor. The
    /// caster marks the ground, and a moment later everything standing there is hit hard and
    /// thrown outwards.
    /// </summary>
    /// <remarks>
    /// <para>The delay is the whole design. An instant ground-targeted nuke is just a bigger
    /// fireball and there is nothing to play against; a telegraphed one turns the Mage's power into
    /// a question the other player gets to answer — move, or take it. That is why
    /// <c>ExecTimeSeconds</c> is long here (three quarters of a second) where the Mage's bolt is a
    /// third of that, and why the impact point is fixed at cast time rather than following the
    /// target.</para>
    ///
    /// <para>It is a window, though, not a stroll: the delay and the radius are tuned against each
    /// other, because a telegraph long enough to walk out of at a normal pace is not counterplay,
    /// it is an animation nobody has to think about.</para>
    ///
    /// <para>Everything about the strike is resolved on the server at impact, so a player who
    /// steps out in time genuinely escapes it — the damage is never "locked in" when the action is
    /// requested.</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Meteor Strike Action")]
    public class MeteorStrikeAction : Action
    {
        /// <summary>
        /// Cheat prevention: the impact point comes from the client, so it is re-checked against
        /// the caster's real position on the server, with a small allowance for the movement that
        /// happened during the round trip. Same guard, and same reasoning, as
        /// <see cref="AOEAction"/>.
        /// </summary>
        const float k_MaxDistanceDivergence = 1f;

        /// <summary>Used when the asset leaves Radius at 0, so the strike is never a no-op.</summary>
        const float k_DefaultRadius = 4.5f;

        /// <summary>Used when the asset leaves ExecTime at 0 — without a delay this would not be
        /// a meteor, it would be an instant nuke with no counterplay.</summary>
        const float k_DefaultImpactDelaySeconds = 1f;

        /// <summary>
        /// Indices into <c>Config.Spawns</c>: the ground telegraph, the falling body, the impact
        /// burst, in the order <c>NewPowersInstaller</c> fills them. Read by index rather than
        /// instantiated as a set, because the three play at different times and in different
        /// places — which is exactly what <see cref="Action.InstantiateSpecialFXGraphics"/>, the
        /// usual helper, cannot express.
        /// </summary>
        const int k_TelegraphSpawnIndex = 0;
        const int k_FallingSpawnIndex = 1;
        const int k_ImpactSpawnIndex = 2;

        /// <summary>
        /// The blast radius the borrowed toss-attack VFX were authored for
        /// (<c>TossedItem.m_HitRadius</c>). Everything taken from that set is scaled by
        /// <see cref="StrikeRadius"/> over this, so the art tracks the tuning instead of the
        /// tuning having to match the art.
        /// </summary>
        const float k_AuthoredFxRadius = 5f;

        /// <summary>How far above the impact point the meteor starts its fall.</summary>
        /// <remarks>
        /// Lowered from 24. With a top-down camera, height is mostly wasted: the meteor spends it
        /// small, far from the plane the player is watching, and often outside the frame entirely.
        /// Starting lower keeps the whole descent on screen and makes the thing read bigger for
        /// the same amount of travel.
        /// </remarks>
        const float k_FallHeight = 15f;

        /// <summary>
        /// Floor on the fall time, whatever the asset's ExecTimeSeconds says.
        /// </summary>
        /// <remarks>
        /// The asset ships 0.75s, which is not enough to watch — and the point of this power is
        /// that the telegraph gives people time to move, so a longer fall is better play as well
        /// as a better picture. In code because an asset edited on disk can be replaced by the
        /// Editor's cached copy when the dedicated-server build is made, and this value has to be
        /// identical on the server (which times the damage by it) and the client (which times the
        /// fall by it) or the rock lands somewhere the damage is not.
        /// </remarks>
        const float k_MinImpactDelaySeconds = 1.5f;

        /// <summary>How much bigger the falling body is drawn than the borrowed VFX authors it.</summary>
        /// <remarks>
        /// The glow was authored to sit on an imp's thrown rock at arm's length, not to be seen
        /// falling out of the sky. Scaling it up is what turns a speck into something that reads
        /// as a meteor from the game camera.
        /// </remarks>
        const float k_FallingBodyBoost = 2.4f;

        /// <summary>
        /// How far back along the cast direction it starts, so it arcs in over the caster's
        /// shoulder rather than descending vertically like a lift.
        /// </summary>
        const float k_FallSetback = 9f;

        /// <summary>Seconds the impact burst is left alive to finish playing.</summary>
        const float k_ImpactFxSeconds = 2.5f;

        /// <summary>
        /// How long the caster is planted facing the spot. Short on purpose: the meteor is already
        /// committed to a fixed point, so holding the Mage's facing for the whole telegraph only
        /// took away the ability to keep fighting while it fell.
        /// </summary>
        const float k_CastFacingSeconds = 0.35f;

        bool m_DidStrike;

        // Reused between the sweep and the damage pass; a meteor can catch a crowd.
        readonly List<IDamageable> m_Victims = new List<IDamageable>();

        // Client-only visuals: the ring on the ground and the thing falling towards it.
        GameObject m_Telegraph;
        GameObject m_FallingBody;
        Vector3 m_FallStart;
        bool m_PlayedImpactFx;

        float StrikeRadius => Config.Radius > 0f ? Config.Radius : k_DefaultRadius;

        float ImpactDelay => Mathf.Max(
            Config.ExecTimeSeconds > 0f ? Config.ExecTimeSeconds : k_DefaultImpactDelaySeconds,
            k_MinImpactDelaySeconds);

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            float distanceAway = Vector3.Distance(serverCharacter.physicsWrapper.Transform.position, Data.Position);
            if (distanceAway > Config.Range + k_MaxDistanceDivergence)
            {
                return ActionConclusion.Stop;
            }

            // Victims are decided at impact, not now — see the class remarks. Clearing the list
            // keeps the client from playing a hit react on somebody who may well walk away.
            Data.TargetIds = new ulong[0];

            // Plant the caster facing the spot they are calling the meteor down on, so the cast
            // animation reads as aimed rather than as the Mage staring off somewhere else.
            Vector3 toImpact = Data.Position - serverCharacter.physicsWrapper.Transform.position;
            toImpact.y = 0f;
            if (toImpact.sqrMagnitude > 0.0001f)
            {
                serverCharacter.Movement.LockFacing(toImpact.normalized, k_CastFacingSeconds);
            }

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            serverCharacter.clientCharacter.RpcPlayAction(Data);
            return ActionConclusion.Continue;
        }

        public override void Reset()
        {
            base.Reset();
            m_DidStrike = false;
            m_Victims.Clear();

            // Actions are pooled and handed back out, so client state has to be cleared as
            // thoroughly as server state or the next meteor inherits this one's leftovers.
            CleanUpVisuals();
            m_PlayedImpactFx = false;
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (!m_DidStrike && TimeRunning >= ImpactDelay)
            {
                m_DidStrike = true;
                PerformStrike(clientCharacter);
            }

            // Stay alive briefly past the impact so the action's blocking window covers the hit,
            // then end. Config.DurationSeconds still has the final say via the action player.
            return ActionConclusion.Continue;
        }

        void PerformStrike(ServerCharacter parent)
        {
            string[] layers = GameDataSource.IsPvPMode && !parent.IsNpc
                ? new[] { "NPCs", "PCs" }
                : new[] { "NPCs" };

            var colliders = Physics.OverlapSphere(Data.Position, StrikeRadius, LayerMask.GetMask(layers));

            m_Victims.Clear();
            for (int i = 0; i < colliders.Length; i++)
            {
                var victim = colliders[i].GetComponent<IDamageable>();
                if (victim == null || !victim.IsDamageable())
                {
                    continue;
                }

                // A meteor does not spare the Mage who called it. Standing in your own impact is
                // a mistake the game is allowed to punish — and it is what stops the move from
                // being a free panic button at melee range.
                if (m_Victims.Contains(victim))
                {
                    continue; // one character, several colliders
                }

                m_Victims.Add(victim);
            }

            int damage = HeroBalance.ScaleDamage(parent, Config.Logic, Config.Amount);

            for (int i = 0; i < m_Victims.Count; i++)
            {
                m_Victims[i].ReceiveHitPoints(parent, -damage);

                // Throw survivors clear of the crater.
                if (Config.KnockbackSpeed > 0f &&
                    m_Victims[i] is Component victimComponent &&
                    victimComponent.TryGetComponent(out ServerCharacter victimCharacter))
                {
                    victimCharacter.Movement.StartKnockback(Data.Position, Config.KnockbackSpeed, Config.KnockbackDuration);
                }
            }
        }

        public override bool OnStartClient(ClientCharacter clientCharacter)
        {
            base.OnStartClient(clientCharacter);

            m_PlayedImpactFx = false;
            m_Telegraph = SpawnTelegraph();

            // Comes in over the caster's shoulder: Data.Direction points from the Mage at the
            // spot, so stepping back along it puts the meteor's start on the near side rather
            // than straight overhead, where a top-down camera would barely see it move.
            Vector3 setback = Data.Direction.sqrMagnitude > 0.0001f
                ? Data.Direction.normalized * k_FallSetback
                : Vector3.zero;
            m_FallStart = Data.Position - setback + Vector3.up * k_FallHeight;
            m_FallingBody = SpawnFallingBody();

            return ActionConclusion.Continue;
        }

        /// <summary>
        /// Drives the fall and fires the burst. Kept on the client's own clock rather than waiting
        /// for a "it landed" message: the impact time is <see cref="ImpactDelay"/> after the cast
        /// on every machine, so the picture stays in step with the server's damage without another
        /// round trip.
        /// </summary>
        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            float delay = ImpactDelay;

            if (!m_PlayedImpactFx && TimeRunning >= delay)
            {
                m_PlayedImpactFx = true;
                PlayImpactVisual();
                return ActionConclusion.Continue; // the tail on Config.DurationSeconds ends it
            }

            if (m_FallingBody != null && delay > 0f)
            {
                // Accelerating, because a linear descent reads as a lift — but t squared spent
                // most of the fall hanging near the top and then dropped in the last instant,
                // which is the other way to be invisible. t^1.6 still gains speed the whole way
                // down while actually being somewhere the eye can follow.
                float t = Mathf.Clamp01(TimeRunning / delay);
                m_FallingBody.transform.position =
                    Vector3.Lerp(m_FallStart, Data.Position, Mathf.Pow(t, 1.6f));
            }

            return ActionConclusion.Continue;
        }

        public override void CancelClient(ClientCharacter clientCharacter)
        {
            base.CancelClient(clientCharacter);
            CleanUpVisuals();
        }

        /// <summary>
        /// The ring on the ground, which goes up at cast time and is drawn at the true blast
        /// radius — it IS the counterplay, so it has to be honest about where the damage will be.
        /// </summary>
        /// <remarks>
        /// Mounted exactly the way <c>ImpTossedItem</c> mounts the same effect: laid flat and
        /// scaled to the blast diameter (that prefab uses scale 10 for its 5-unit blast). Wrapping
        /// it in a holder rather than scaling the instance keeps that arrangement in one place and
        /// gives the whole thing a single object to destroy.
        /// </remarks>
        GameObject SpawnTelegraph()
        {
            var prefab = SpawnPrefab(k_TelegraphSpawnIndex);
            if (prefab == null)
            {
                return null;
            }

            var holder = new GameObject("MeteorTelegraph");
            holder.transform.SetPositionAndRotation(Data.Position, Quaternion.Euler(90f, 0f, 0f));
            holder.transform.localScale = new Vector3(StrikeRadius * 2f, StrikeRadius * 2f, 1f);
            GameObject.Instantiate(prefab, holder.transform);
            return holder;
        }

        /// <summary>
        /// The meteor itself, put at the top of its arc and left for
        /// <see cref="OnUpdateClient"/> to fly down.
        /// </summary>
        /// <remarks>
        /// <para>The borrowed effect is authored to sit still. <c>FX_IMP_TossAttack_Glow</c> emits
        /// a single particle from one burst at time zero and simulates in <b>world</b> space,
        /// because on the imp it rides an object that carries its own motion. Instantiated here
        /// and then moved, it did nothing at all: the one particle was released 24 units up and
        /// stayed there for its whole 5.5-second life while this script dutifully flew an empty
        /// transform to the ground. The meteor was never seen to fall because it never fell —
        /// only its holder did.</para>
        ///
        /// <para>Switching the systems to local space makes the particles ride the transform,
        /// which is what every other user of this prefab gets for free by parenting it to
        /// something that moves. Done at runtime rather than on the asset: the prefab is shared
        /// with the imp's toss attack, which wants world space, and an on-disk edit is exactly
        /// what Unity's cache reverts on a dedicated-server build.</para>
        /// </remarks>
        GameObject SpawnFallingBody()
        {
            var prefab = SpawnPrefab(k_FallingSpawnIndex);
            if (prefab == null)
            {
                return null;
            }

            var body = GameObject.Instantiate(prefab, m_FallStart, Quaternion.identity);
            body.transform.localScale *= FxScale();

            foreach (var system in body.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startSizeMultiplier *= k_FallingBodyBoost;
            }

            return body;
        }

        void PlayImpactVisual()
        {
            var prefab = SpawnPrefab(k_ImpactSpawnIndex);
            if (prefab != null)
            {
                var impact = GameObject.Instantiate(prefab, Data.Position, Quaternion.identity);
                impact.transform.localScale *= FxScale();

                // Left to finish on its own rather than parented to anything: the action is over
                // in a fraction of a second and the burst outlives it.
                GameObject.Destroy(impact, k_ImpactFxSeconds);
            }

            CleanUpVisuals();
        }

        GameObject SpawnPrefab(int index)
        {
            var spawns = Config.Spawns;
            return spawns != null && index < spawns.Length ? spawns[index] : null;
        }

        float FxScale()
        {
            return k_AuthoredFxRadius > 0f ? StrikeRadius / k_AuthoredFxRadius : 1f;
        }

        void CleanUpVisuals()
        {
            DestroyVisual(ref m_FallingBody);
            DestroyVisual(ref m_Telegraph);
        }

        static void DestroyVisual(ref GameObject visual)
        {
            if (visual != null)
            {
                GameObject.Destroy(visual);
            }

            visual = null;
        }
    }
}
