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

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            ulong target = (Data.TargetIds != null && Data.TargetIds.Length > 0) ? Data.TargetIds[0] : serverCharacter.TargetId;
            IDamageable foe = DetectFoe(serverCharacter, target);
            Debug.Log($"[AttackDebug] MeleeAction.OnStart on {serverCharacter.name}: anim='{Config.Anim}', requestedTarget={target}, DetectFoe={(foe != null ? foe.NetworkObjectId.ToString() : "NULL")}");
            if (foe != null)
            {
                m_ProvisionalTarget = foe.NetworkObjectId;
                Data.TargetIds = new ulong[] { foe.NetworkObjectId };
            }

            // snap to face the right direction
            if (Data.Direction != Vector3.zero)
            {
                serverCharacter.physicsWrapper.Transform.forward = Data.Direction;
            }

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
        public static IDamageable GetIdealMeleeFoe(bool isNPC, Collider ourCollider, float meleeRange, float meleeRadius, ulong preferredTargetNetworkId, ulong attackerNetId = 0)
        {
            // In PvP mode a PC attacker also hits other PCs (never self).
            bool wantPcs = isNPC || (!isNPC && GameDataSource.IsPvPMode);
            bool wantNpcs = !isNPC;

            RaycastHit[] results;
            int numResults = 0.0f < meleeRadius
                ? ActionUtils.DetectNearbyEntitiesUseSphere(wantPcs, wantNpcs, ourCollider, meleeRange, meleeRadius, out results)
                : ActionUtils.DetectNearbyEntities(wantPcs, wantNpcs, ourCollider, meleeRange, out results);

            IDamageable foundFoe = null;
            int maxDamage = int.MinValue;

            for (int i = 0; i < numResults; i++)
            {
                var damageable = results[i].collider.GetComponent<IDamageable>();
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
