using System;
using Mirror;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Assertions;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    public enum MovementState
    {
        Idle = 0,
        PathFollowing = 1,
        Charging = 2,
        Knockback = 3,
        // Continuous directional movement driven by WASD / a mobile joystick. The
        // character moves along a world-space direction until told to stop, rather
        // than pathing to a fixed point.
        DirectMoving = 4,
    }

    /// <summary>
    /// Component responsible for moving a character on the server side based on inputs.
    /// </summary>
    /*[RequireComponent(typeof(NetworkCharacterState), typeof(NavMeshAgent), typeof(ServerCharacter)), RequireComponent(typeof(Rigidbody))]*/
    public class ServerCharacterMovement : NetworkBehaviour
    {
        [SerializeField]
        NavMeshAgent m_NavMeshAgent;

        [SerializeField]
        Rigidbody m_Rigidbody;

        private NavigationSystem m_NavigationSystem;

        private DynamicNavPath m_NavPath;

        private MovementState m_MovementState;

        MovementStatus m_PreviousState;

        [SerializeField]
        private ServerCharacter m_CharLogic;

        // when we are in charging and knockback mode, we use these additional variables
        private float m_ForcedSpeed;
        private float m_SpecialModeDurationRemaining;

        // this one is specific to knockback mode
        private Vector3 m_KnockbackVector;

        // normalized world-space direction used while in DirectMoving state
        private Vector3 m_DirectMoveDirection;

        // While a facing lock is held, PerformMovement stops turning the character to face
        // its movement. See LockFacing.
        private Vector3 m_FacingLockDirection;
        private float m_FacingLockUntil;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool TeleportModeActivated { get; set; }

        const float k_CheatSpeed = 20;

        public bool SpeedCheatActivated { get; set; }
#endif

        void Awake()
        {
            // disable this NetworkBehavior until it is spawned
            enabled = false;
        }

        public override void OnStartServer()
        {
            // Only enable server component on servers
            enabled = true;

            // On the server enable navMeshAgent and initialize
            m_NavMeshAgent.enabled = true;
            m_NavigationSystem = GameObject.FindGameObjectWithTag(NavigationSystem.NavigationSystemTag).GetComponent<NavigationSystem>();
            m_NavPath = new DynamicNavPath(m_NavMeshAgent, m_NavigationSystem);
        }

        /// <summary>
        /// Sets a movement target. We will path to this position, avoiding static obstacles.
        /// </summary>
        /// <param name="position">Position in world space to path to. </param>
        public void SetMovementTarget(Vector3 position)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TeleportModeActivated)
            {
                Teleport(position);
                return;
            }
#endif
            Debug.Log($"[MoveDebug] SetMovementTarget({position}) agentEnabled={m_NavMeshAgent.enabled} isOnNavMesh={(m_NavMeshAgent.enabled ? m_NavMeshAgent.isOnNavMesh.ToString() : "n/a")} agentPos={transform.position} navPathNull={(m_NavPath == null)}");
            m_MovementState = MovementState.PathFollowing;
            m_NavPath.SetTargetPosition(position);
        }

        public void StartForwardCharge(float speed, float duration)
        {
            m_NavPath.Clear();
            m_MovementState = MovementState.Charging;
            m_ForcedSpeed = speed;
            m_SpecialModeDurationRemaining = duration;
        }

        public void StartKnockback(Vector3 knocker, float speed, float duration)
        {
            m_NavPath.Clear();
            // Being hit outranks an attack's facing lock: staying planted on your own aim while
            // being flung backwards reads as the character being stuck.
            m_FacingLockUntil = 0f;
            m_MovementState = MovementState.Knockback;
            m_KnockbackVector = transform.position - knocker;
            m_ForcedSpeed = speed;
            m_SpecialModeDurationRemaining = duration;
        }

        /// <summary>
        /// Follow the given transform until it is reached.
        /// </summary>
        /// <param name="followTransform">The transform to follow</param>
        public void FollowTransform(Transform followTransform)
        {
            m_MovementState = MovementState.PathFollowing;
            m_NavPath.FollowTransform(followTransform);
        }

        /// <summary>
        /// Continuously move along a world-space direction (WASD / mobile joystick).
        /// Pass a zero-length vector to stop. The direction is projected onto the
        /// horizontal plane and normalized.
        /// </summary>
        /// <param name="worldDirection">Desired movement direction in world space.</param>
        public void SetMovementDirection(Vector3 worldDirection)
        {
            worldDirection.y = 0;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                // Releasing the stick/keys stops directional movement, but must not
                // stomp a path-follow or forced move that something else started.
                if (m_MovementState == MovementState.DirectMoving)
                {
                    CancelMove();
                }
                return;
            }

            m_NavPath.Clear();
            m_MovementState = MovementState.DirectMoving;
            m_DirectMoveDirection = worldDirection.normalized;
        }

        /// <summary>
        /// Plants the character facing a world direction for <paramref name="seconds"/>, overriding
        /// the usual "turn to face where you're walking".
        ///
        /// <para>This exists because an attack's aim and its projectile are separated in time. An
        /// action snaps the character to face <c>Data.Direction</c> in OnStart, but the projectile
        /// isn't spawned until <c>Config.ExecTimeSeconds</c> later — 0.15s for the Archer, 0.25s for
        /// the Mage, a full second for the charged shot. <see cref="PerformMovement"/> runs at 50Hz
        /// in between and used to overwrite that rotation on every tick, so a player who was walking
        /// while they fired had their shot leave along the walk direction instead of the one they
        /// aimed. Locking the facing across the exec window is what makes "the shot goes where you
        /// aimed" actually true.</para>
        ///
        /// <para>Movement itself is not blocked — you still walk, you just don't pivot.</para>
        /// </summary>
        /// <param name="worldDirection">Direction to face. Flattened; ignored if degenerate.</param>
        /// <param name="seconds">How long to hold it.</param>
        public void LockFacing(Vector3 worldDirection, float seconds)
        {
            worldDirection.y = 0;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            m_FacingLockDirection = worldDirection.normalized;
            m_FacingLockUntil = Time.time + seconds;

            // Apply at once: the caller is mid-OnStart and the shot may be resolved before the
            // next FixedUpdate ever runs.
            ApplyFacing(m_FacingLockDirection);
        }

        void ApplyFacing(Vector3 direction)
        {
            transform.rotation = Quaternion.LookRotation(direction);
            m_Rigidbody.rotation = transform.rotation;
        }

        /// <summary>
        /// Returns true if the current movement-mode is unabortable (e.g. a knockback effect)
        /// </summary>
        /// <returns></returns>
        public bool IsPerformingForcedMovement()
        {
            return m_MovementState == MovementState.Knockback || m_MovementState == MovementState.Charging;
        }

        /// <summary>
        /// Returns true if the character is actively moving, false otherwise.
        /// </summary>
        /// <returns></returns>
        public bool IsMoving()
        {
            return m_MovementState != MovementState.Idle;
        }

        /// <summary>
        /// Cancels any moves that are currently in progress.
        /// </summary>
        public void CancelMove()
        {
            if (m_NavPath != null)
            {
                m_NavPath.Clear();
            }
            m_MovementState = MovementState.Idle;
        }

        /// <summary>
        /// Instantly moves the character to a new position. NOTE: this cancels any active movement operation!
        /// This does not notify the client that the movement occurred due to teleportation, so that needs to
        /// happen in some other way, such as with the custom action visualization in DashAttackActionFX. (Without
        /// this, the clients will animate the character moving to the new destination spot, rather than instantly
        /// appearing in the new spot.)
        /// </summary>
        /// <param name="newPosition">new coordinates the character should be at</param>
        public void Teleport(Vector3 newPosition)
        {
            CancelMove();
            if (!m_NavMeshAgent.Warp(newPosition))
            {
                // warping failed! We're off the navmesh somehow. Weird... but we can still teleport
                Debug.LogWarning($"NavMeshAgent.Warp({newPosition}) failed!", gameObject);
                transform.position = newPosition;
            }

            m_Rigidbody.position = transform.position;
            m_Rigidbody.rotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            PerformMovement();

            var currentState = GetMovementStatus(m_MovementState);
            if (m_PreviousState != currentState)
            {
                m_CharLogic.MovementStatus = currentState;
                m_PreviousState = currentState;
            }
        }

        public override void OnStopServer()
        {
            if (m_NavPath != null)
            {
                m_NavPath.Dispose();
            }
            // Disable server components when despawning
            enabled = false;
            m_NavMeshAgent.enabled = false;
        }

        private void PerformMovement()
        {
            if (m_MovementState == MovementState.Idle)
                return;

            Vector3 movementVector;

            if (m_MovementState == MovementState.Charging)
            {
                // if we're done charging, stop moving
                m_SpecialModeDurationRemaining -= Time.fixedDeltaTime;
                if (m_SpecialModeDurationRemaining <= 0)
                {
                    m_MovementState = MovementState.Idle;
                    return;
                }

                var desiredMovementAmount = m_ForcedSpeed * Time.fixedDeltaTime;
                movementVector = transform.forward * desiredMovementAmount;
            }
            else if (m_MovementState == MovementState.Knockback)
            {
                m_SpecialModeDurationRemaining -= Time.fixedDeltaTime;
                if (m_SpecialModeDurationRemaining <= 0)
                {
                    m_MovementState = MovementState.Idle;
                    return;
                }

                var desiredMovementAmount = m_ForcedSpeed * Time.fixedDeltaTime;
                movementVector = m_KnockbackVector * desiredMovementAmount;
            }
            else if (m_MovementState == MovementState.DirectMoving)
            {
                // Move straight along the requested direction; the NavMeshAgent.Move
                // below keeps us on the mesh and resolves collisions/avoidance.
                var desiredMovementAmount = GetBaseMovementSpeed() * Time.fixedDeltaTime;
                movementVector = m_DirectMoveDirection * desiredMovementAmount;
            }
            else
            {
                var desiredMovementAmount = GetBaseMovementSpeed() * Time.fixedDeltaTime;
                movementVector = m_NavPath.MoveAlongPath(desiredMovementAmount);

                // If we didn't move stop moving.
                if (movementVector == Vector3.zero)
                {
                    m_MovementState = MovementState.Idle;
                    return;
                }
            }

            m_NavMeshAgent.Move(movementVector);

            // Turn to face the way we're going — unless an attack has planted us facing its aim
            // (see LockFacing). Without that exception a player who walks while firing has their
            // shot leave along the walk direction, because the projectile spawns several physics
            // ticks after the action aimed us.
            if (Time.time < m_FacingLockUntil)
            {
                transform.rotation = Quaternion.LookRotation(m_FacingLockDirection);
            }
            else if (movementVector.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(movementVector);
            }

            // After moving adjust the position of the dynamic rigidbody.
            m_Rigidbody.position = transform.position;
            m_Rigidbody.rotation = transform.rotation;
        }

        /// <summary>
        /// Retrieves the speed for this character's class.
        /// </summary>
        private float GetBaseMovementSpeed()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (SpeedCheatActivated)
            {
                return k_CheatSpeed;
            }
#endif
            CharacterClass characterClass = GameDataSource.Instance.CharacterDataByType[m_CharLogic.CharacterType];
            Assert.IsNotNull(characterClass, $"No CharacterClass data for character type {m_CharLogic.CharacterType}");

            // Monsters are buffed on HP and damage, so they get a single fixed (slow) speed to
            // keep them kiteable: heroes run at 4-7 m/s and must always be able to disengage.
            if (characterClass.IsNpc)
            {
                return NpcBalance.MoveSpeed;
            }

            float speed = characterClass.Speed * GetClassSpeedMultiplier(m_CharLogic.CharacterType);

            // The blue zone. Applied at the very end so it multiplies the class tuning rather than
            // replacing it — a buffed Archer is still slower than a buffed Rogue.
            if (ZoneBoons.HasSpeed((uint)m_CharLogic.netId))
            {
                speed *= ZoneRules.SpeedMultiplier;
            }

            return speed;
        }

        /// <summary>
        /// Per-class movement tuning applied on top of the CharacterClass asset's Speed.
        /// Lives in code rather than in the .asset because a value edited on disk can be
        /// silently replaced by Unity's cached copy when the build is made.
        /// </summary>
        private static float GetClassSpeedMultiplier(CharacterTypeEnum characterType)
        {
            switch (characterType)
            {
                // The Archer out-ranges everyone; being just as fast as the melee classes on
                // top of that left them with no way to close the distance.
                case CharacterTypeEnum.Archer:
                    return k_ArcherSpeedMultiplier;
                case CharacterTypeEnum.Rogue:
                    return k_RogueSpeedMultiplier;
                default:
                    return 1f;
            }
        }

        private const float k_ArcherSpeedMultiplier = 0.75f;

        /// <summary>
        /// -15% on the Rogue, whose asset ships the highest Speed in the game (7) and which had
        /// never been given a multiplier here.
        /// </summary>
        /// <remarks>
        /// <para>Untouched, the effective speeds were Rogue 7.0, Mage 6.0, Archer 4.5, Tank 4.0 —
        /// the Rogue ran 75% faster than the Tank and 56% faster than the Archer, on top of owning
        /// both a dash and stealth. Nothing could disengage from it and nothing could catch it, so
        /// it chose every fight in the match.</para>
        ///
        /// <para>At 0.85 the Rogue lands on 5.95, level with the Mage. That is deliberate: the
        /// class keeps its mobility, but the mobility now lives in the <i>dash</i> — a resource on
        /// a cooldown that an opponent can watch for and play around — instead of in a permanent
        /// movement-speed lead there was no counter to.</para>
        /// </remarks>
        private const float k_RogueSpeedMultiplier = 0.85f;

        /// <summary>
        /// Determines the appropriate MovementStatus for the character. The
        /// MovementStatus is used by the client code when animating the character.
        /// </summary>
        private MovementStatus GetMovementStatus(MovementState movementState)
        {
            switch (movementState)
            {
                case MovementState.Idle:
                    return MovementStatus.Idle;
                case MovementState.Knockback:
                    return MovementStatus.Uncontrolled;
                default:
                    return MovementStatus.Normal;
            }
        }
    }
}
