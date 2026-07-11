using System;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// Action that represents an always-hit raybeam-style ranged attack. A particle is shown from caster to target, and then the
    /// target takes damage. (It is not possible to escape the hit; the target ALWAYS takes damage.) This is intended for fairly
    /// swift particles; the time before it's applied is based on a simple distance-check at the attack's start.
    /// (If no target is provided (because the user clicked on an empty spot on the map) or if the caster doesn't have line of
    /// sight to the target (because it's behind a wall), we still perform an action, it just hits nothing.
    /// </summary>

    [CreateAssetMenu(menuName = "BossRoom/Actions/FX Projectile Targeted Action")]
    public partial class FXProjectileTargetedAction : Action
    {
        private bool m_ImpactedTarget;
        private float m_TimeUntilImpact;
        private IDamageable m_DamageableTarget;

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            m_DamageableTarget = GetDamageableTarget(serverCharacter);

            // figure out where the player wants us to aim at...
            Vector3 targetPos = m_DamageableTarget != null ? m_DamageableTarget.transform.position : m_Data.Position;

            // then make sure we can actually see that point!
            if (!ActionUtils.HasLineOfSight(serverCharacter.physicsWrapper.Transform.position, targetPos, out Vector3 collidePos))
            {
                // we do not have line of sight to the target point. So our target instead becomes the obstruction point
                m_DamageableTarget = null;
                targetPos = collidePos;

                // and update our action data so that when we send it to the clients, it will be up-to-date
                Data.TargetIds = new ulong[0];
                Data.Position = targetPos;
            }

            // turn to face our target!
            serverCharacter.physicsWrapper.Transform.LookAt(targetPos);

            // figure out how long the pretend-projectile will be flying to the target
            float distanceToTargetPos = Vector3.Distance(targetPos, serverCharacter.physicsWrapper.Transform.position);
            m_TimeUntilImpact = Config.ExecTimeSeconds + (distanceToTargetPos / Config.Projectiles[0].Speed_m_s);

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            // tell clients to visualize this action
            serverCharacter.clientCharacter.RpcPlayAction(Data);
            return true;
        }

        public override void Reset()
        {
            base.Reset();

            m_ImpactedTarget = false;
            m_TimeUntilImpact = 0;
            m_DamageableTarget = null;
            m_ImpactPlayed = false;
            m_ProjectileDuration = 0;
            m_Projectile = null;
            m_Target = null;
            m_TargetTransform = null;
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (!m_ImpactedTarget && m_TimeUntilImpact <= TimeRunning)
            {
                m_ImpactedTarget = true;
                if (m_DamageableTarget != null)
                {
                    m_DamageableTarget.ReceiveHitPoints(clientCharacter, -Config.Projectiles[0].Damage);

                    // Area damage on impact (when Config.Radius > 0): splash onto other nearby
                    // foes so the bolt isn't strictly single-target and combat feels punchier.
                    if (Config.Radius > 0f)
                    {
                        ApplySplashDamage(clientCharacter, m_DamageableTarget.transform.position, m_DamageableTarget.NetworkObjectId);
                    }
                }
            }
            return true;
        }

        // Scratch buffer for the splash overlap. Server runs Actions on the main thread, so a
        // shared static is safe and avoids per-hit allocations.
        static readonly Collider[] s_SplashHits = new Collider[16];

        /// <summary>
        /// Deals area-of-effect damage to foes within Config.Radius of the impact point,
        /// skipping the caster and the primary target (already hit). Uses SplashDamage when set,
        /// otherwise falls back to the projectile's main damage.
        /// </summary>
        void ApplySplashDamage(ServerCharacter caster, Vector3 center, ulong primaryTargetId)
        {
            int splash = Config.SplashDamage > 0 ? Config.SplashDamage : Config.Projectiles[0].Damage;
            int mask = LayerMask.GetMask("PCs", "NPCs");
            int num = Physics.OverlapSphereNonAlloc(center, Config.Radius, s_SplashHits, mask);

            for (int i = 0; i < num; i++)
            {
                var damageable = s_SplashHits[i].GetComponent<IDamageable>();
                if (damageable == null || !damageable.IsDamageable()) continue;
                if (damageable.NetworkObjectId == caster.NetworkObjectId) continue;   // never self
                if (damageable.NetworkObjectId == primaryTargetId) continue;          // already hit

                // Only splash actual foes (same faction rule the primary target uses).
                var sc = s_SplashHits[i].GetComponentInParent<ServerCharacter>();
                if (sc != null)
                {
                    bool isPvPPcVsPc = GameDataSource.IsPvPMode && !caster.IsNpc && !sc.IsNpc;
                    bool isInvalidFaction = sc.IsNpc == (Config.IsFriendly ^ caster.IsNpc);
                    if (!isPvPPcVsPc && isInvalidFaction) continue;
                }

                damageable.ReceiveHitPoints(caster, -splash);
            }
        }

        public override void Cancel(ServerCharacter serverCharacter)
        {
            if (!m_ImpactedTarget)
            {
                serverCharacter.clientCharacter.RpcCancelActionsByPrototypeID(ActionID);
            }
        }

        /// <summary>
        /// Returns our intended target, or null if not found/no target.
        /// </summary>
        private IDamageable GetDamageableTarget(ServerCharacter parent)
        {
            if (Data.TargetIds == null || Data.TargetIds.Length == 0)
            {
                return null;
            }

            var obj = NetworkIdentityUtils.FindByNetId((uint)Data.TargetIds[0]);
            if (obj != null)
            {
                var serverChar = obj.GetComponent<ServerCharacter>();
                if (serverChar)
                {
                    // In PvP mode a PC can target another PC (but never self).
                    bool isPvPPcVsPc = GameDataSource.IsPvPMode && !parent.IsNpc && !serverChar.IsNpc;
                    bool isSelf = serverChar.NetworkObjectId == parent.NetworkObjectId;
                    bool isInvalidFaction = serverChar.IsNpc == (Config.IsFriendly ^ parent.IsNpc);
                    if (isSelf || (!isPvPPcVsPc && isInvalidFaction))
                        return null;
                }

                if (PhysicsWrapper.TryGetPhysicsWrapper(Data.TargetIds[0], out var physicsWrapper))
                {
                    return physicsWrapper.DamageCollider.GetComponent<IDamageable>();
                }
                else
                {
                    return obj.GetComponent<IDamageable>();
                }
            }
            else
            {
                // target could have legitimately disappeared in the time it took to queue this action... but that's pretty unlikely, so we'll log about it to ease debugging
                Debug.Log($"FXProjectileTargetedAction was targeted at ID {Data.TargetIds[0]}, but that target can't be found in spawned object list! (May have just been deleted?)");
                return null;
            }
        }
    }
}
