using System;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// Action that represents a swing of a melee weapon. It is not explicitly targeted, but rather detects the foe that was hit with a physics check.
    /// </summary>
    /// <remarks>
    /// Q: Why do we DetectFoe twice, once in Start, once when we actually connect?
    /// A: The weapon swing doesn't happen instantaneously. We want to broadcast the action to other clients as fast as possible to minimize latency,
    ///    but this poses a conundrum. At the moment the swing starts, you don't know for sure if you've hit anybody yet. There are a few possible resolutions to this:
    ///      1. Do the DetectFoe operation once--in Start.
    ///         Pros: Simple! Only one physics cast per swing--saves on perf.
    ///         Cons: Is unfair. You can step out of the swing of an attack, but no matter how far you go, you'll still be hit. The reverse is also true--you can
    ///               "step into an attack", and it won't affect you. This will feel terrible to the attacker.
    ///      2. Do the DetectFoe operation once--in Update. Send a separate RPC to the targeted entity telling it to play its hit react.
    ///         Pros: Always shows the correct behavior. The entity that gets hit plays its hit react (if any).
    ///         Cons: You need another RPC. Adds code complexity and bandwidth. You also don't have enough information when you start visualizing the swing on
    ///               the client to do any intelligent animation handshaking. If your server->client latency is even a little uneven, your "attack" animation
    ///               won't line up correctly with the hit react, making combat look floaty and disjointed.
    ///      3. Do the DetectFoe operation twice, once in Start and once in Update.
    ///         Pros: Is fair--you do the hit-detect at the moment of the swing striking home. And will generally play the hit react on the right target.
    ///         Cons: Requires more complicated visualization logic. The initial broadcast foe can only ever be treated as a "hint". The graphics logic
    ///               needs to do its own range checking to pick the best candidate to play the hit react on.
    ///
    /// As so often happens in networked games (and games in general), there's no perfect solution--just sets of tradeoffs. For our example, we're showing option "3".
    /// </remarks>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Melee Action")]
    public partial class MeleeAction : Action
    {
        private bool m_ExecutionFired;
        private ulong m_ProvisionalTarget;

        /// <summary>
        /// Hard ceiling on a single self-heal (the Mage's Healing Touch), as a fraction of the
        /// caster's maximum HP. Keeps the heal a comeback tool instead of a full reset.
        /// </summary>
        const float k_SelfHealMaxFractionOfMaxHp = 0.25f;

        /// <summary>
        /// Extra time the facing stays planted after the swing connects, so the snap doesn't
        /// visibly unwind while the follow-through is still playing.
        /// </summary>
        const float k_FacingLockTailSeconds = 0.1f;

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            if (Config.IsFriendly)
            {
                // Self-only support action (e.g. the Mage's Healing Touch): no targeting at
                // all — it always affects the caster. The actual heal is applied at exec time
                // in OnUpdate. We still force the target to be ourselves so the client-side
                // heal FX spawns on the caster.
                Data.TargetIds = new ulong[] { serverCharacter.NetworkObjectId };
                serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
                serverCharacter.clientCharacter.RpcPlayAction(Data);
                return true;
            }

            ulong target = (Data.TargetIds != null && Data.TargetIds.Length > 0) ? Data.TargetIds[0] : serverCharacter.TargetId;
            IDamageable foe = DetectFoe(serverCharacter, target);
            Debug.Log($"[AttackDebug] MeleeAction.OnStart on {serverCharacter.name}: anim='{Config.Anim}', requestedTarget={target}, DetectFoe={(foe != null ? foe.NetworkObjectId.ToString() : "NULL")}");
            if (foe != null)
            {
                m_ProvisionalTarget = foe.NetworkObjectId;
                Data.TargetIds = new ulong[] { foe.NetworkObjectId };
            }

            // Snap to face the swing — and hold it. Melee re-runs DetectFoe at ExecTimeSeconds
            // (see the remarks above), and that hit-test is a box/sphere cast projected from the
            // character's facing, so a player who kept walking after starting the swing used to
            // have the hitbox drift off to wherever they were heading. Locking the facing across
            // the exec window is what makes the swing land where it was aimed.
            serverCharacter.Movement.LockFacing(
                serverCharacter.ResolveAimDirection(Data.Direction),
                Config.ExecTimeSeconds + k_FacingLockTailSeconds);

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            serverCharacter.clientCharacter.RpcPlayAction(Data);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            m_ExecutionFired = false;
            m_ProvisionalTarget = 0;
            m_ImpactPlayed = false;
            m_SpawnedGraphics = null;
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (!m_ExecutionFired && (Time.time - TimeStarted) >= Config.ExecTimeSeconds)
            {
                m_ExecutionFired = true;

                if (Config.IsFriendly)
                {
                    // Self-heal: Amount is stored negative (like damage), so -Amount is the
                    // positive HP restored to the caster. It is capped here rather than in the
                    // .asset both because a flat 250 HP was a full heal for every hero (making
                    // the Mage unkillable) and because a code-side cap can't be silently
                    // reverted by Unity's asset cache at build time.
                    int maxHitPoints = clientCharacter.CharacterClass != null && clientCharacter.CharacterClass.BaseHP != null
                        ? clientCharacter.CharacterClass.BaseHP.Value
                        : 0;
                    int healAmount = -Config.Amount;
                    if (maxHitPoints > 0)
                    {
                        healAmount = Mathf.Min(healAmount, Mathf.RoundToInt(maxHitPoints * k_SelfHealMaxFractionOfMaxHp));
                    }

                    if (healAmount > 0)
                    {
                        clientCharacter.ApplyHealthChange(clientCharacter, healAmount);
                    }
                    return true;
                }

                var foe = DetectFoe(clientCharacter, m_ProvisionalTarget);
                Debug.Log($"[AttackDebug] MeleeAction exec on {clientCharacter.name}: DetectFoe={(foe != null ? foe.NetworkObjectId.ToString() : "NULL")}, dealing {Config.Amount} dmg");
                if (foe != null)
                {
                    foe.ReceiveHitPoints(clientCharacter, -Config.Amount);
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the ServerCharacter of the foe we hit, or null if none found.
        /// </summary>
        /// <returns></returns>
        private IDamageable DetectFoe(ServerCharacter parent, ulong foeHint = 0)
        {
            return GetIdealMeleeFoe(Config.IsFriendly ^ parent.IsNpc, parent.physicsWrapper.DamageCollider, Config.Range, Config.Radius, foeHint, parent.NetworkObjectId);
        }

        /// <summary>
        /// Utility used by Actions to perform Melee attacks. Performs a melee hit-test
        /// and then looks through the results to find an alive target, preferring the provided
        /// enemy.
        /// </summary>
        /// <param name="isNPC">true if the attacker is an NPC (and therefore should hit PCs). False for the reverse.</param>
        /// <param name="ourCollider">The collider of the attacking GameObject.</param>
        /// <param name="meleeRange">The range in meters to check for foes.</param>
        /// <param name="meleeRadius">The radius in meters to check for foes.</param>
        /// <param name="preferredTargetNetworkId">The NetworkObjectId of our preferred foe, or 0 if no preference</param>
        /// <returns>ideal target's IDamageable, or null if no valid target found</returns>
        /// <remarks>
        /// If a Radius value is set (greater than 0), collision checking will be done with a Sphere the size of the Radius, not the size of the Box.
        /// Also, if multiple targets collide as a result, the target with the highest total damage is prioritized.
        /// </remarks>
        /// <summary>
        /// Half-angle of the melee arc, in degrees. 90 gives the full 180-degree cone in front.
        /// </summary>
        const float k_MeleeConeHalfAngle = 90f;

        public static IDamageable GetIdealMeleeFoe(bool isNPC, Collider ourCollider, float meleeRange, float meleeRadius, ulong preferredTargetNetworkId, ulong attackerNetId = 0)
        {
            // In PvP mode a PC attacker also hits other PCs (never self).
            bool wantPcs = isNPC || (!isNPC && GameDataSource.IsPvPMode);
            bool wantNpcs = !isNPC;

            // A 180-degree cone in front, rather than a cast straight ahead. Every melee power
            // goes through here — the basic attacks of all four classes, the Rogue's dash and the
            // Mage's heal, which is a friendly melee — so widening it here widens all of them at
            // once and keeps them agreeing with each other.
            //
            // The radius on the asset still widens the search when it is set: it is added to the
            // range, so a weapon authored as chunky stays chunky.
            float reach = meleeRange + Mathf.Max(0f, meleeRadius);
            int numResults = ActionUtils.DetectFoesInCone(wantPcs, wantNpcs, ourCollider, reach,
                k_MeleeConeHalfAngle, out var results);

            IDamageable foundFoe = null;
            int maxDamage = int.MinValue;

            for (int i = 0; i < numResults; i++)
            {
                var damageable = results[i].GetComponent<IDamageable>();
                if (damageable == null || !damageable.IsDamageable())
                    continue;

                // never hit self
                if (attackerNetId != 0 && damageable.NetworkObjectId == attackerNetId)
                    continue;

                if (damageable.NetworkObjectId == preferredTargetNetworkId)
                {
                    foundFoe = damageable;
                    maxDamage = int.MaxValue;
                    continue;
                }

                var totalDamage = damageable.GetTotalDamage();
                if (foundFoe == null || maxDamage < totalDamage)
                {
                    foundFoe = damageable;
                    maxDamage = totalDamage;
                }
            }

            return foundFoe;
        }
    }
}
