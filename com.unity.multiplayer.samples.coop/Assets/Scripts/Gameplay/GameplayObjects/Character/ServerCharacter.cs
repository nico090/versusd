using System;
using System.Collections;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects.Character.AI;
using Unity.Multiplayer.Samples.BossRoom;
using UnityEngine;
using UnityEngine.Serialization;
using Action = Unity.BossRoom.Gameplay.Actions.Action;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// Contains all NetworkVariables, RPCs and server-side logic of a character.
    /// This class was separated in two to keep client and server context self contained. This way you don't have to continuously ask yourself if code is running client or server side.
    /// </summary>
    [RequireComponent(typeof(NetworkHealthState),
        typeof(NetworkLifeState),
        typeof(NetworkAvatarGuidState))]
    public class ServerCharacter : NetworkBehaviour, ITargetable
    {
        [FormerlySerializedAs("m_ClientVisualization")]
        [SerializeField]
        ClientCharacter m_ClientCharacter;

        public ClientCharacter clientCharacter => m_ClientCharacter;

        // Mirror uses netId (uint); expose as ulong to match IDamageable and legacy TargetId patterns
        public ulong NetworkObjectId => (ulong)(uint)netId;

        [SerializeField]
        CharacterClass m_CharacterClass;

        public CharacterClass CharacterClass
        {
            get
            {
                if (m_CharacterClass == null)
                {
                    m_CharacterClass = m_State.RegisteredAvatar.CharacterClass;
                }

                return m_CharacterClass;
            }

            set => m_CharacterClass = value;
        }

        /// Indicates how the character's movement should be depicted.
        [SyncVar(hook = nameof(OnMovementStatusChanged))]
        MovementStatus m_MovementStatus;
        public MovementStatus MovementStatus
        {
            get => m_MovementStatus;
            set => m_MovementStatus = value;
        }

        public event Action<MovementStatus, MovementStatus> MovementStatusChanged;

        void OnMovementStatusChanged(MovementStatus oldValue, MovementStatus newValue)
        {
            MovementStatusChanged?.Invoke(oldValue, newValue);
        }

        [SyncVar(hook = nameof(OnHeldNetworkObjectChanged))]
        ulong m_HeldNetworkObject;
        public ulong HeldNetworkObject
        {
            get => m_HeldNetworkObject;
            set => m_HeldNetworkObject = value;
        }

        public event Action<ulong, ulong> HeldNetworkObjectChanged;

        void OnHeldNetworkObjectChanged(ulong oldValue, ulong newValue)
        {
            HeldNetworkObjectChanged?.Invoke(oldValue, newValue);
        }

        /// <summary>
        /// Indicates whether this character is in "stealth mode" (invisible to monsters and other players).
        /// </summary>
        [SyncVar(hook = nameof(OnIsStealthyChanged))]
        bool m_IsStealthy;
        public bool IsStealthy
        {
            get => m_IsStealthy;
            set => m_IsStealthy = value;
        }

        public event Action<bool, bool> IsStealthyChanged;

        void OnIsStealthyChanged(bool oldValue, bool newValue)
        {
            IsStealthyChanged?.Invoke(oldValue, newValue);
        }

        public NetworkHealthState NetHealthState { get; private set; }

        /// <summary>
        /// The active target of this character.
        /// </summary>
        [SyncVar(hook = nameof(OnTargetIdChanged))]
        ulong m_TargetId;
        public ulong TargetId
        {
            get => m_TargetId;
            set => m_TargetId = value;
        }

        public event Action<ulong, ulong> TargetIdChanged;

        void OnTargetIdChanged(ulong oldValue, ulong newValue)
        {
            TargetIdChanged?.Invoke(oldValue, newValue);
        }

        /// <summary>
        /// Current HP. This value is populated at startup time from CharacterClass data.
        /// </summary>
        public int HitPoints
        {
            get => NetHealthState.HitPoints;
            private set => NetHealthState.HitPoints = value;
        }

        public NetworkLifeState NetLifeState { get; private set; }

        /// <summary>
        /// The server-assigned id of the connection that owns this character (0 for the host / non-owned objects).
        /// </summary>
        public ulong OwnerClientId => (ulong)(uint)(connectionToClient?.connectionId ?? 0);

        /// <summary>
        /// Current LifeState. Only Players should enter the FAINTED state.
        /// </summary>
        public LifeState LifeState
        {
            get => NetLifeState.LifeState;
            private set => NetLifeState.LifeState = value;
        }

        /// <summary>
        /// Returns true if this Character is an NPC.
        /// </summary>
        public bool IsNpc => CharacterClass.IsNpc;

        public bool IsValidTarget => LifeState != LifeState.Dead;

        /// <summary>
        /// Returns true if the Character is currently in a state where it can play actions, false otherwise.
        /// </summary>
        public bool CanPerformActions => LifeState == LifeState.Alive;

        /// <summary>
        /// Character Type. This value is populated during character selection.
        /// </summary>
        public CharacterTypeEnum CharacterType => CharacterClass.CharacterType;

        private ServerActionPlayer m_ServerActionPlayer;

        /// <summary>
        /// The Character's ActionPlayer. This is mainly exposed for use by other Actions. In particular, users are discouraged from
        /// calling 'PlayAction' directly on this, as the ServerCharacter has certain game-level checks it performs in its own wrapper.
        /// </summary>
        public ServerActionPlayer ActionPlayer => m_ServerActionPlayer;

        [SerializeField]
        [Tooltip("If set to false, an NPC character will be denied its brain (won't attack or chase players)")]
        private bool m_BrainEnabled = true;

        [SerializeField]
        [Tooltip("Setting negative value disables destroying object after it is killed.")]
        private float m_KilledDestroyDelaySeconds = 3.0f;

        [SerializeField]
        [Tooltip("If set, the ServerCharacter will automatically play the StartingAction when it is created. ")]
        private Action m_StartingAction;


        [SerializeField]
        DamageReceiver m_DamageReceiver;

        [SerializeField]
        ServerCharacterMovement m_Movement;

        public ServerCharacterMovement Movement => m_Movement;

        [SerializeField]
        PhysicsWrapper m_PhysicsWrapper;

        public PhysicsWrapper physicsWrapper => m_PhysicsWrapper;

        [SerializeField]
        ServerAnimationHandler m_ServerAnimationHandler;

        public ServerAnimationHandler serverAnimationHandler => m_ServerAnimationHandler;

        private AIBrain m_AIBrain;
        NetworkAvatarGuidState m_State;

        ServerCharacter m_LastLethalInflicter;

        /// <summary>
        /// The last ServerCharacter whose attack reduced this character's HP to 0.
        /// Null if death had no attributable attacker.
        /// </summary>
        public ServerCharacter LastLethalInflicter => m_LastLethalInflicter;

        // Server-only spawn-protection deadline (Time.time). While Time.time is below this,
        // incoming damage is ignored. Plain field (not a SyncVar): damage resolution is
        // server-authoritative, so clients don't need it. Crucially this is NOT the
        // #if-gated god-mode path — that check is compiled out of the release DS build.
        float m_InvulnerableUntilTime;

        /// <summary>Server-only: grant brief damage immunity (e.g. spawn protection after a respawn).</summary>
        public void SetInvulnerable(float seconds)
        {
            m_InvulnerableUntilTime = Time.time + seconds;
        }

        void Awake()
        {
            m_ServerActionPlayer = new ServerActionPlayer(this);
            NetLifeState = GetComponent<NetworkLifeState>();
            NetHealthState = GetComponent<NetworkHealthState>();
            m_State = GetComponent<NetworkAvatarGuidState>();
        }

        public override void OnStartServer()
        {
            NetLifeState.LifeStateChanged += OnLifeStateChanged;
            m_DamageReceiver.DamageReceived += ReceiveHP;
            m_DamageReceiver.CollisionEntered += CollisionEntered;
            m_DamageReceiver.GetTotalDamageFunc += GetTotalDamage;

            if (IsNpc)
            {
                m_AIBrain = new AIBrain(this, m_ServerActionPlayer);
            }

            if (m_StartingAction != null)
            {
                var startingAction = new ActionRequestData() { ActionID = m_StartingAction.ActionID };
                PlayAction(ref startingAction);
            }
            InitializeHitPoints();
        }

        public override void OnStopServer()
        {
            NetLifeState.LifeStateChanged -= OnLifeStateChanged;

            if (m_DamageReceiver)
            {
                m_DamageReceiver.DamageReceived -= ReceiveHP;
                m_DamageReceiver.CollisionEntered -= CollisionEntered;
                m_DamageReceiver.GetTotalDamageFunc -= GetTotalDamage;
            }
        }


        /// <summary>
        /// RPC to send inputs for this character from a client to a server.
        /// </summary>
        /// <param name="movementTarget">The position which this character should move towards.</param>
        [Command]
        public void CmdSendCharacterInput(Vector3 movementTarget)
        {
            // SECURITY: reject non-finite positions sent by a malformed/malicious client
            // before they reach the NavMesh/physics layer.
            if (!IsFiniteVector(movementTarget))
            {
                return;
            }

            if (LifeState == LifeState.Alive && !m_Movement.IsPerformingForcedMovement())
            {
                // if we're currently playing an interruptible action, interrupt it!
                if (m_ServerActionPlayer.GetActiveActionInfo(out ActionRequestData data))
                {
                    if (GameDataSource.Instance.TryGetActionPrototypeByID(data.ActionID, out var proto)
                        && proto.Config.ActionInterruptible)
                    {
                        m_ServerActionPlayer.ClearActions(false);
                    }
                }

                m_ServerActionPlayer.CancelRunningActionsByLogic(ActionLogic.Target, true); //clear target on move.
                m_Movement.SetMovementTarget(movementTarget);
            }
        }

        /// <summary>True if every component of <paramref name="v"/> is finite (no NaN/Infinity).</summary>
        static bool IsFiniteVector(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsInfinity(v.x) ||
                     float.IsNaN(v.y) || float.IsInfinity(v.y) ||
                     float.IsNaN(v.z) || float.IsInfinity(v.z));
        }

        /// <summary>
        /// RPC for continuous directional movement (WASD / mobile joystick). Unlike
        /// click-to-move this does NOT clear the current target, so the continuous
        /// auto-lock survives while the player walks around. Pass Vector3.zero to stop.
        /// </summary>
        /// <param name="worldDirection">Desired movement direction in world space.</param>
        [Command]
        public void CmdSetMovementDirection(Vector3 worldDirection)
        {
            // SECURITY: reject non-finite directions from a malformed/malicious client.
            if (!IsFiniteVector(worldDirection))
            {
                return;
            }

            if (LifeState == LifeState.Alive && !m_Movement.IsPerformingForcedMovement())
            {
                // moving interrupts an interruptible action (same rule as click-move)
                if (m_ServerActionPlayer.GetActiveActionInfo(out ActionRequestData data))
                {
                    if (GameDataSource.Instance.TryGetActionPrototypeByID(data.ActionID, out var proto)
                        && proto.Config.ActionInterruptible)
                    {
                        m_ServerActionPlayer.ClearActions(false);
                    }
                }

                m_Movement.SetMovementDirection(worldDirection);
            }
        }

        // ACTION SYSTEM

        /// <summary>
        /// Client->Server RPC that sends a request to play an action.
        /// </summary>
        /// <param name="data">Data about which action to play and its associated details. </param>
        [Command]
        public void CmdPlayAction(ActionRequestData data)
        {
            // SECURITY: data.ActionID comes straight from the client. Look it up via the
            // bounds-checked Try* helper — GetActionPrototypeByID indexes a List directly,
            // so a malformed/out-of-range ActionID would throw on the server thread and
            // crash the headless dedicated server (DoS for the whole match).
            if (!GameDataSource.Instance.TryGetActionPrototypeByID(data.ActionID, out var actionPrototype))
            {
                return;
            }

            ActionRequestData data1 = data;
            if (!actionPrototype.Config.IsFriendly)
            {
                // notify running actions that we're using a new attack. (e.g. so Stealth can cancel itself)
                ActionPlayer.OnGameplayActivity(Action.GameplayActivity.UsingAttackAction);
            }

            PlayAction(ref data1);
        }

        // UTILITY AND SPECIAL-PURPOSE RPCs

        /// <summary>
        /// Called on server when the character's client decides they have stopped "charging up" an attack.
        /// </summary>
        [Command]
        public void CmdStopChargingUp()
        {
            m_ServerActionPlayer.OnGameplayActivity(Action.GameplayActivity.StoppedChargingUp);
        }

        void InitializeHitPoints()
        {
            HitPoints = CharacterClass.BaseHP.Value;

            if (!IsNpc)
            {
                SessionPlayerData? sessionPlayerData = SessionManager<SessionPlayerData>.Instance.GetPlayerData((ulong)(uint)(connectionToClient?.connectionId ?? 0));
                if (sessionPlayerData is { HasCharacterSpawned: true })
                {
                    HitPoints = sessionPlayerData.Value.CurrentHitPoints;
                    if (HitPoints <= 0)
                    {
                        LifeState = LifeState.Fainted;
                    }
                }
            }
        }

        /// <summary>
        /// Play a sequence of actions!
        /// </summary>
        public void PlayAction(ref ActionRequestData action)
        {
            //the character needs to be alive in order to be able to play actions
            if (LifeState == LifeState.Alive && !m_Movement.IsPerformingForcedMovement())
            {
                if (action.CancelMovement)
                {
                    m_Movement.CancelMove();
                }

                m_ServerActionPlayer.PlayAction(ref action);
            }
        }

        void OnLifeStateChanged(LifeState prevLifeState, LifeState lifeState)
        {
            if (lifeState != LifeState.Alive)
            {
                m_ServerActionPlayer.ClearActions(true);
                m_Movement.CancelMove();
            }
        }

        IEnumerator KilledDestroyProcess()
        {
            yield return new WaitForSeconds(m_KilledDestroyDelaySeconds);

            if (gameObject != null)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        /// <summary>
        /// Directly apply an HP change to this character, routed through the normal
        /// healing/damage pipeline (respects the alive-check, healing buffs, clamping, etc.).
        /// Used by self-targeted actions such as the Mage's self-heal, which don't go through
        /// the usual foe-detection path. Positive = heal, negative = damage.
        /// </summary>
        public void ApplyHealthChange(ServerCharacter inflicter, int hitPoints)
        {
            m_DamageReceiver.ReceiveHitPoints(inflicter, hitPoints);
        }

        /// <summary>
        /// Receive an HP change from somewhere. Could be healing or damage.
        /// </summary>
        /// <param name="inflicter">Person dishing out this damage/healing. Can be null. </param>
        /// <param name="HP">The HP to receive. Positive value is healing. Negative is damage.  </param>
        void ReceiveHP(ServerCharacter inflicter, int HP)
        {
            //to our own effects, and modify the damage or healing as appropriate. But in this game, we just take it straight.
            if (HP > 0)
            {
                m_ServerActionPlayer.OnGameplayActivity(Action.GameplayActivity.Healed);
                float healingMod = m_ServerActionPlayer.GetBuffedValue(Action.BuffableValue.PercentHealingReceived);
                HP = (int)(HP * healingMod);
            }
            else
            {
                // Spawn protection / brief immunity. Unconditional (unlike the god-mode
                // check below, which the release dedicated-server build compiles out), so
                // post-respawn invulnerability actually works on the headless server.
                if (Time.time < m_InvulnerableUntilTime)
                {
                    return;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Don't apply damage if god mode is on
                if (NetLifeState.IsGodMode)
                {
                    return;
                }
#endif

                m_ServerActionPlayer.OnGameplayActivity(Action.GameplayActivity.AttackedByEnemy);
                float damageMod = m_ServerActionPlayer.GetBuffedValue(Action.BuffableValue.PercentDamageReceived);
                HP = (int)(HP * damageMod);

                serverAnimationHandler.SetTrigger("HitReact1");
            }

            HitPoints = Mathf.Clamp(HitPoints + HP, 0, CharacterClass.BaseHP.Value);

            if (m_AIBrain != null)
            {
                //let the brain know about the modified amount of damage we received.
                m_AIBrain.ReceiveHP(inflicter, HP);
            }

            //we can't currently heal a dead character back to Alive state.
            //that's handled by a separate function.
            if (HitPoints <= 0)
            {
                m_LastLethalInflicter = inflicter;

                if (IsNpc)
                {
                    if (m_KilledDestroyDelaySeconds >= 0.0f && LifeState != LifeState.Dead)
                    {
                        StartCoroutine(KilledDestroyProcess());
                    }

                    LifeState = LifeState.Dead;
                }
                else
                {
                    LifeState = LifeState.Fainted;
                }

                m_ServerActionPlayer.ClearActions(false);
            }
        }

        /// <summary>
        /// Determines a gameplay variable for this character. The value is determined
        /// by the character's active Actions.
        /// </summary>
        /// <param name="buffType"></param>
        /// <returns></returns>
        public float GetBuffedValue(Action.BuffableValue buffType)
        {
            return m_ServerActionPlayer.GetBuffedValue(buffType);
        }

        /// <summary>
        /// Receive a Life State change that brings Fainted characters back to Alive state.
        /// </summary>
        /// <param name="inflicter">Person reviving the character.</param>
        /// <param name="HP">The HP to set to a newly revived character.</param>
        public void Revive(ServerCharacter inflicter, int HP)
        {
            if (LifeState == LifeState.Fainted)
            {
                HitPoints = Mathf.Clamp(HP, 0, CharacterClass.BaseHP.Value);
                NetLifeState.LifeState = LifeState.Alive;
            }
        }

        void Update()
        {
            m_ServerActionPlayer.OnUpdate();
            if (m_AIBrain != null && LifeState == LifeState.Alive && m_BrainEnabled)
            {
                m_AIBrain.Update();
            }
        }

        void CollisionEntered(Collision collision)
        {
            if (m_ServerActionPlayer != null)
            {
                m_ServerActionPlayer.CollisionEntered(collision);
            }
        }

        int GetTotalDamage()
        {
            return Math.Max(0, CharacterClass.BaseHP.Value - HitPoints);
        }

        /// <summary>
        /// This character's AIBrain. Will be null if this is not an NPC.
        /// </summary>
        public AIBrain AIBrain { get { return m_AIBrain; } }

    }
}
