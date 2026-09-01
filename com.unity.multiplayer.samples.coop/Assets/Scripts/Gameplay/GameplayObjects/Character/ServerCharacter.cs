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

        /// <summary>
        /// Max HP for this character: the PvP-adjusted pool from <see cref="HeroBalance"/> for
        /// heroes, the buffed pool from <see cref="NpcBalance"/> for monsters. Use this everywhere
        /// instead of BaseHP.Value so spawn HP, damage clamping and health bars all agree on the
        /// same ceiling.
        /// </summary>
        public int MaxHitPoints => CharacterClass.IsNpc
            ? NpcBalance.GetMaxHitPoints(CharacterClass, CharacterClass.BaseHP.Value)
            : HeroBalance.GetMaxHitPoints(CharacterClass.CharacterType, CharacterClass.BaseHP.Value);

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

        /// <summary>
        /// Server-only: drop any spawn protection immediately. Called the moment the character uses
        /// an offensive action — the shield is there to stop spawn-camping, not to let you open a
        /// fight for free while untouchable.
        /// </summary>
        public void CancelInvulnerability()
        {
            m_InvulnerableUntilTime = 0f;
        }

        /// <summary>
        /// Server-only: set while the match is over (final table on screen). Player commands are
        /// dropped so nobody can sneak in a kill after the buzzer. Static because it is a property
        /// of the match, not of any one character, and a server process runs exactly one match.
        /// </summary>
        public static bool MatchInputFrozen { get; set; }

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

            if (MatchInputFrozen && !IsNpc)
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
            ServerSetMovementDirection(worldDirection);
        }

        /// <summary>
        /// The body of <see cref="CmdSetMovementDirection"/>, callable directly on the server.
        /// A bot has no client to send the Command from, so it steers through here — which means
        /// bots and players go through byte-for-byte the same movement rules (freeze on match end,
        /// forced-movement guard, interrupting an interruptible action). See ServerBotManager.
        /// </summary>
        public void ServerSetMovementDirection(Vector3 worldDirection)
        {
            // SECURITY: reject non-finite directions from a malformed/malicious client.
            if (!IsFiniteVector(worldDirection))
            {
                return;
            }

            if (LifeState != LifeState.Alive)
            {
                return;
            }

            if (MatchInputFrozen && !IsNpc)
            {
                m_Movement.SetMovementDirection(Vector3.zero);
                return;
            }

            // A zero direction is a "stop" and must ALWAYS get through. It used to be dropped
            // along with everything else while a forced move (knockback / charge) was running,
            // and since the client only sends the stop once, the character kept running for
            // ever afterwards. SetMovementDirection(zero) only cancels directional movement,
            // so it can't stomp the knockback or charge that's in progress.
            bool isStopRequest = worldDirection.sqrMagnitude < 0.0001f;

            if (!isStopRequest && m_Movement.IsPerformingForcedMovement())
            {
                return;
            }

            if (!isStopRequest)
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
            }

            m_Movement.SetMovementDirection(worldDirection);
        }

        /// <summary>
        /// Where the owning client is currently aiming, in world space, flattened and normalized.
        /// Zero until the client has sent one.
        ///
        /// <para>Most attacks carry their own <c>Data.Direction</c>, captured when the action was
        /// requested, and that is what they should use. This is the fallback for the ones that
        /// can't: a charged shot is aimed when the button is *released*, up to a second after the
        /// action started, and <c>ChargedActionInput</c> has no way to amend the request it already
        /// sent. Reading the live aim at launch time is what stops the charged shot from flying
        /// wherever the player happened to be walking.</para>
        /// </summary>
        public Vector3 AimDirection { get; private set; }

        /// <summary>
        /// Streamed by <c>ClientInputSender</c> while the player aims. Cheap and idempotent: it
        /// only records the direction, it never moves or turns the character. Turning is still
        /// driven by the actions themselves (see <c>ServerCharacterMovement.LockFacing</c>).
        /// </summary>
        [Command]
        public void CmdSetAimDirection(Vector3 worldDirection)
        {
            ServerSetAimDirection(worldDirection);
        }

        /// <summary>
        /// The body of <see cref="CmdSetAimDirection"/>, callable directly on the server so a bot
        /// can aim the same way a player does. See ServerBotManager.
        /// </summary>
        public void ServerSetAimDirection(Vector3 worldDirection)
        {
            // SECURITY: reject non-finite directions from a malformed/malicious client.
            if (!IsFiniteVector(worldDirection))
            {
                return;
            }

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            AimDirection = worldDirection.normalized;
        }

        /// <summary>
        /// The direction an action should fire along: what the request carried, else the live aim,
        /// else wherever we're facing. Keeps the fallback order in one place so every action agrees.
        /// </summary>
        public Vector3 ResolveAimDirection(Vector3 requestedDirection)
        {
            requestedDirection.y = 0f;
            if (requestedDirection.sqrMagnitude > 0.0001f)
            {
                return requestedDirection.normalized;
            }

            if (AimDirection.sqrMagnitude > 0.0001f)
            {
                return AimDirection;
            }

            Vector3 forward = physicsWrapper.Transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        // ACTION SYSTEM

        /// <summary>
        /// Client->Server RPC that sends a request to play an action.
        /// </summary>
        /// <summary>
        /// Fired client-side (owning client only) when one of this character's actions goes on
        /// cooldown, so UI (the action bar) can show it. See <see cref="NotifyActionCooldownStarted"/>.
        /// </summary>
        public event Action<ActionID, float> ActionCooldownStarted;

        /// <summary>
        /// Server-only: tells the owning client that the given action just started its cooldown,
        /// so its UI can animate the remaining time. Called from <see cref="ServerActionPlayer"/>.
        /// </summary>
        public void NotifyActionCooldownStarted(ActionID actionID, float duration)
        {
            TargetActionCooldownStarted(actionID, duration);
        }

        [TargetRpc]
        void TargetActionCooldownStarted(ActionID actionID, float duration)
        {
            ActionCooldownStarted?.Invoke(actionID, duration);
        }

        /// <param name="data">Data about which action to play and its associated details. </param>
        [Command]
        public void CmdPlayAction(ActionRequestData data)
        {
            ServerPlayAction(data);
        }

        /// <summary>
        /// The body of <see cref="CmdPlayAction"/>, callable directly on the server. Bots request
        /// their attacks through here rather than through <see cref="PlayAction"/> so that they are
        /// bound by the same rules a player is: the match-end freeze, the "attacking drops spawn
        /// protection" rule, and the stealth-cancelling gameplay activity. See ServerBotManager.
        /// </summary>
        public void ServerPlayAction(ActionRequestData data)
        {
            // SECURITY: data.ActionID comes straight from the client. Look it up via the
            // bounds-checked Try* helper — GetActionPrototypeByID indexes a List directly,
            // so a malformed/out-of-range ActionID would throw on the server thread and
            // crash the headless dedicated server (DoS for the whole match).
            if (!GameDataSource.Instance.TryGetActionPrototypeByID(data.ActionID, out var actionPrototype))
            {
                return;
            }

            if (MatchInputFrozen && !IsNpc)
            {
                return;
            }

            ActionRequestData data1 = data;
            if (!actionPrototype.Config.IsFriendly)
            {
                // notify running actions that we're using a new attack. (e.g. so Stealth can cancel itself)
                ActionPlayer.OnGameplayActivity(Action.GameplayActivity.UsingAttackAction);

                // Attacking gives up spawn protection. Otherwise the 2s post-respawn shield turns
                // into 2s of free damage, which is exactly the abuse it was meant to prevent.
                CancelInvulnerability();
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
            ServerStopChargingUp();
        }

        /// <summary>
        /// The body of <see cref="CmdStopChargingUp"/>, callable directly on the server. Lets a bot
        /// release a charged shot early (for a weaker projectile) instead of always holding it to
        /// full charge, which is what a human actually does under pressure.
        /// </summary>
        public void ServerStopChargingUp()
        {
            m_ServerActionPlayer.OnGameplayActivity(Action.GameplayActivity.StoppedChargingUp);
        }

        void InitializeHitPoints()
        {
            HitPoints = MaxHitPoints;

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

        IEnumerator KilledDestroyProcess(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

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

                // Monsters hit harder than their Action .assets say. Scaling here (the single
                // choke point every melee/projectile/AOE/trample hit passes through) buffs every
                // NPC attack at once, instead of having to touch each action asset.
                // int.MinValue is used by the debug "kill" cheat; leave those alone so negating
                // it can't overflow.
                if (inflicter != null && inflicter.IsNpc && HP > int.MinValue / 4)
                {
                    HP = -NpcBalance.ScaleDamage(-HP, inflicter.CharacterType);
                }

                m_ServerActionPlayer.OnGameplayActivity(Action.GameplayActivity.AttackedByEnemy);
                float damageMod = m_ServerActionPlayer.GetBuffedValue(Action.BuffableValue.PercentDamageReceived);
                HP = (int)(HP * damageMod);

                serverAnimationHandler.SetTrigger("HitReact1");
            }

            // The violet zone, applied to the ATTACKER's buff and only to damage. Done here, at
            // the single point every source of damage passes through, rather than in the actions:
            // there are a dozen of those and any new one would silently miss the buff. Healing is
            // left alone — a damage boon that also doubled the Mage's heal would be a different
            // power entirely.
            if (HP < 0 && inflicter != null && ZoneBoons.HasDamage((uint)inflicter.netId))
            {
                HP = Mathf.RoundToInt(HP * ZoneRules.DamageMultiplier);
            }

            // Clamp against MaxHitPoints, not the raw BaseHP asset value: both NpcBalance (monster
            // HP pools) and HeroBalance (the PvP hero pass) move the real ceiling away from BaseHP,
            // and healing past it would hand back HP a class is not supposed to have.
            HitPoints = Mathf.Clamp(HitPoints + HP, 0, MaxHitPoints);

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
                    // Through NpcBalance, which overrides the boss's prefab value of -1 — the
                    // sentinel that was stopping its body from ever being despawned.
                    float destroyDelay = NpcBalance.GetKilledDestroyDelay(CharacterType, m_KilledDestroyDelaySeconds);
                    if (destroyDelay >= 0.0f && LifeState != LifeState.Dead)
                    {
                        StartCoroutine(KilledDestroyProcess(destroyDelay));
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
                HitPoints = Mathf.Clamp(HP, 0, MaxHitPoints);
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
            return Math.Max(0, MaxHitPoints - HitPoints);
        }

        /// <summary>
        /// This character's AIBrain. Will be null if this is not an NPC.
        /// </summary>
        public AIBrain AIBrain { get { return m_AIBrain; } }

    }
}
