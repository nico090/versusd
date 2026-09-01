using System;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// Action responsible for creating a projectile object.
    /// </summary>
    [CreateAssetMenu(menuName = "BossRoom/Actions/Launch Projectile Action")]
    public class LaunchProjectileAction : Action
    {
        private bool m_Launched = false;

        /// <summary>
        /// Extra time the facing stays planted after the projectile leaves, so the snap doesn't
        /// visibly unwind on the same frame the arrow appears.
        /// </summary>
        const float k_FacingLockTailSeconds = 0.1f;

        public override bool OnStart(ServerCharacter serverCharacter)
        {
            // Snap to face the direction we're firing — and *hold* it. The projectile doesn't
            // leave until Config.ExecTimeSeconds later, and ServerCharacterMovement turns the
            // character to face its movement on every physics tick in between, so a player who
            // fires while walking used to have the shot leave along their walk direction. The
            // lock is what makes the shot honour the aim. See ServerCharacterMovement.LockFacing.
            if (Data.Direction.sqrMagnitude > 0.0001f)
            {
                serverCharacter.Movement.LockFacing(Data.Direction, Config.ExecTimeSeconds + k_FacingLockTailSeconds);
            }

            serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim);
            serverCharacter.clientCharacter.RpcPlayAction(Data);
            return true;
        }

        public override void Reset()
        {
            m_Launched = false;
            base.Reset();
        }

        public override bool OnUpdate(ServerCharacter clientCharacter)
        {
            if (TimeRunning >= Config.ExecTimeSeconds && !m_Launched)
            {
                LaunchProjectile(clientCharacter);
            }

            return true;
        }

        /// <summary>
        /// Looks through the ProjectileInfo list and finds the appropriate one to instantiate.
        /// For the base class, this is always just the first entry with a valid prefab in it!
        /// </summary>
        /// <exception cref="System.Exception">thrown if no Projectiles are valid</exception>
        protected virtual ProjectileInfo GetProjectileInfo()
        {
            foreach (var projectileInfo in Config.Projectiles)
            {
                if (projectileInfo.ProjectilePrefab && projectileInfo.ProjectilePrefab.GetComponent<PhysicsProjectile>())
                    return projectileInfo;
            }
            throw new System.Exception($"Action {name} has no usable Projectiles!");
        }

        /// <summary>
        /// Instantiates and configures the arrow. Repeatedly calling this does nothing.
        /// </summary>
        /// <remarks>
        /// This calls GetProjectilePrefab() to find the prefab it should instantiate.
        /// </remarks>
        protected void LaunchProjectile(ServerCharacter parent)
        {
            if (!m_Launched)
            {
                m_Launched = true;

                var projectileInfo = GetProjectileInfo();

                // PvP balance pass: the Archer's charged shot hits for less than its asset says.
                // Scaled on the local copy of the struct, right before it is handed to the
                // projectile, so the shared ActionConfig asset is never mutated.
                projectileInfo.Damage = HeroBalance.ScaleDamage(parent, Config.Logic, projectileInfo.Damage);

                var go = NetworkObjectPool.Singleton.GetNetworkObject(projectileInfo.ProjectilePrefab, projectileInfo.ProjectilePrefab.transform.position, projectileInfo.ProjectilePrefab.transform.rotation);

                // Fire along the direction the player aimed, not along whatever the character
                // transform happens to be pointing at right now. The facing lock in OnStart should
                // already have kept the two identical, but reading the aim directly means a shot
                // can never silently inherit a walk direction even if something else (a knockback,
                // a charge) moved us in between. For a charged shot Data.Direction is empty — it
                // was requested before the player had aimed — so this picks up the live aim
                // instead, which is the whole reason ServerCharacter streams it.
                Vector3 launchDirection = parent.ResolveAimDirection(Data.Direction);

                var launchRotation = Quaternion.LookRotation(launchDirection);
                go.transform.forward = launchDirection;

                //this way, you just need to "place" the arrow by moving it in the prefab, and that will control
                //where it appears next to the player. Built from the launch direction rather than the
                //character's localToWorldMatrix so the muzzle offset stays glued to the shot.
                go.transform.position = parent.physicsWrapper.Transform.position + launchRotation * go.transform.position;

                go.GetComponent<PhysicsProjectile>().Initialize((ulong)(uint)parent.GetComponent<NetworkIdentity>().netId, projectileInfo, projectileInfo.ProjectilePrefab);

                NetworkServer.Spawn(go);
            }
        }

        public override void End(ServerCharacter serverCharacter)
        {
            //make sure this happens.
            LaunchProjectile(serverCharacter);
        }

        public override void Cancel(ServerCharacter serverCharacter)
        {
            if (!string.IsNullOrEmpty(Config.Anim2))
            {
                serverCharacter.serverAnimationHandler.SetTrigger(Config.Anim2);
            }
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            return ActionConclusion.Continue;
        }

    }
}
