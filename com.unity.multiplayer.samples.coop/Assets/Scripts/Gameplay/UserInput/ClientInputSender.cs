using System;
using System.Collections.Generic;
using Unity.BossRoom.CameraUtils;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.Gameplay.UserInput
{
    [RequireComponent(typeof(ServerCharacter))]
    public class ClientInputSender : NetworkBehaviour
    {
        const float k_MouseInputRaycastDistance = 100f;
        const float k_MoveSendRateSeconds = 0.04f;
        const float k_TargetMoveTimeout = 0.45f;

        float m_LastSentMove;

        LayerMask m_GroundLayerMask;

        [SerializeField] ServerCharacter m_ServerCharacter;
        [SerializeField] InputActionReference m_TargetAction;
        [SerializeField] InputActionReference m_Skill1Action;
        [SerializeField] InputActionReference m_PointAction;
        // Vector2 movement (WASD composite / gamepad stick / mobile joystick).
        [SerializeField] InputActionReference m_MoveAction;

        // The actual InputAction read each frame for directional movement. Resolved in
        // OnStartClient: uses m_MoveAction if it was wired in the prefab, otherwise falls
        // back to looking up "Move" in the same asset the other actions belong to (the
        // serialized reference was missing on PlayerAvatar.prefab, which silently disabled WASD).
        InputAction m_MoveActionResolved;
        [SerializeField] InputActionReference m_Action1;
        [SerializeField] InputActionReference m_Action2;
        [SerializeField] InputActionReference m_Action3;
        [SerializeField] InputActionReference m_Action5;
        [SerializeField] InputActionReference m_Action6;
        [SerializeField] InputActionReference m_Action7;
        [SerializeField] InputActionReference m_Action8;

        public event Action<ActionRequestData> ActionInputEvent;

        public enum SkillTriggerStyle
        {
            None,
            MouseClick,
            Keyboard,
            KeyboardRelease,
            UI,
            UIRelease,
        }

        bool IsReleaseStyle(SkillTriggerStyle style) =>
            style == SkillTriggerStyle.KeyboardRelease || style == SkillTriggerStyle.UIRelease;

        struct ActionRequest
        {
            public SkillTriggerStyle TriggerStyle;
            public ActionID RequestedActionID;
            public ulong TargetId;
            // The mouse button that asked for this, when a mouse button did. Carried through so a
            // charge-up skill started by a click knows which button has to come back up to end it.
            public InputAction SourceButton;
        }

        readonly ActionRequest[] m_ActionRequests = new ActionRequest[5];
        int m_ActionRequestCount;

        BaseActionInput m_CurrentSkillInput;
        // When the current charge-up style skill input was created, and how long we let it live.
        // A BaseActionInput blocks all clicking (see Update) until its key/button is released, so
        // a swallowed release event — e.g. the action-bar button turning non-interactable while
        // held — used to leave the player able to run around but unable to attack ever again.
        // This timeout guarantees it always ends.
        float m_SkillInputStartTime;
        const float k_MaxSkillInputHoldSeconds = 8f;
        // The mouse button that started m_CurrentSkillInput, if a mouse button started it. The
        // keyboard has always ended a charge through its KeyboardRelease request; a click had no
        // equivalent, so a charged skill fired with the right button (the Archer's charged shot,
        // the Tank's shield) never got its release and sat there until the timeout above — and
        // for those seconds every left click was swallowed by the m_CurrentSkillInput guard in
        // Update, which is why attacking with the mouse would intermittently do nothing while the
        // "1" key kept working.
        InputAction m_CurrentSkillInputButton;
        // true while the last frame sent a directional (WASD/stick) move, so we know
        // to send a single "stop" when the stick is released.
        bool m_WasDirectMoving;
        // The server drops movement commands it can't apply right now (e.g. while a knockback
        // or charge is in progress), so a single "stop" packet sent at that moment was lost and
        // the character kept running forever. Resend the stop a few times to cover that window.
        int m_PendingStopSends;
        const int k_StopResendCount = 4;
        Camera m_MainCamera;

        // ── Aiming ───────────────────────────────────────────────────────────────────────────
        // The aim is explicit and it is the single source of truth: the mouse cursor on desktop,
        // a drag out of the skill button on touch, the right stick on a gamepad.
        //
        // Two things used to sit here and are deliberately gone. First, a mode that latched to
        // whichever device the player had touched most recently, so walking with WASD quietly
        // switched aiming from "at the cursor" to "where the body faces" and back. Second, an
        // autonomous 80° soft-lock that picked a foe, swung the character round to face it and
        // painted the reticle on it — while a right-click skillshot ignored all of that and flew
        // at the cursor anyway. The reticle said one thing and the shot did another, which is
        // most of why aiming read as unpredictable.
        //
        // What is left is one narrow aim assist, resolved from the aim direction. The same call
        // draws the reticle and nudges the shot at fire time, so what you are shown is what you
        // get.

        // How far the assist will reach for a foe. Raised from 14 to match the distances the
        // ranged weapons actually fight at (bolts and the meteor reach ~18-20): an assist shorter
        // than the weapon meant the two classes that most need the help fought outside it.
        const float k_AimAssistRange = 20f;
        // Half-angle of the assist cone. A shot only snaps onto a foe this close to the aim, so
        // you hit essentially where you point and merely get the last couple of degrees for free.
        // Tightened from 20: at that width the cone reached well past what reads as "pointing at
        // him", and the assist was picking targets the player had not aimed at.
        const float k_AimAssistMaxAngle = 12f;

        // ── Mouse-only refinements ────────────────────────────────────────────────────────────
        // A cursor is a POINT, not a bearing, and the pure-angle score wasted that information:
        // a foe 2m away but 15 degrees off beat the foe your cursor was PARKED ON at 12m, because
        // near things subtend huge angles. These three constants make the cursor mean what a PC
        // player thinks it means. None of them apply to touch or gamepad aim, which really are
        // bearings and keep the pure-angle behaviour.

        // Hovering the cursor within this many metres of a foe picks that foe, full stop.
        // Tightened from 2.5, which is wider than a character and so fired on a foe merely
        // standing next to where the cursor actually was.
        const float k_CursorSnapRadius = 1.5f;
        // Beyond the snap radius, every metre between cursor and foe costs this many "virtual
        // degrees", so among foes in the cone the one nearest the cursor wins. Raised from 2 to
        // buy back some of the certainty the smaller snap radius gave up: the cursor now decides
        // more of the ordering, and does it by a smooth ramp rather than a hard radius.
        const float k_CursorDistanceWeightDegreesPerMetre = 3f;
        // Hysteresis: a challenger must beat the current target's score by this margin to steal
        // the lock. Without it two similar candidates trade the reticle several times a second,
        // and every trade re-aims the next shot — the flicker IS mis-aim. Eased from 6 alongside
        // the narrower cone: with fewer candidates qualifying at all there is less to flicker
        // between, and 6 degrees of hysteresis inside a 12-degree cone made deliberately switching
        // targets feel like the game was arguing with you.
        const float k_StickyTargetBonusDegrees = 4f;
        // How often the reticle's assist target is recomputed. Fire-time assist is always computed
        // fresh, so this rate only affects what's drawn.
        const float k_AimTargetInterval = 0.1f;
        float m_LastAimTargetUpdate;
        // The assist target this client last worked out, and the one it last told the server about.
        // The first is what the aim line is drawn from; the second stops the reticle update being
        // resent on every tick while the server's confirmation is still in flight.
        ServerCharacter m_LocalAssistTarget;
        ulong m_LastSentTargetId;
        // In PvP other players are the point of the fight, but the map is full of imps that would
        // otherwise win the "closest to my aim" contest. This many "virtual degrees" of bonus go to
        // a player candidate, so a foe player beats a nearby NPC unless the NPC is much better aligned.
        // Cut from 40, which was larger than the whole cone: any player anywhere inside it beat
        // every NPC unconditionally, so aiming point-blank at an imp still fired at a player off to
        // the side. At 15 the preference survives for genuinely comparable candidates and loses to
        // an NPC the player is clearly pointing at.
        const float k_PvPPlayerPriorityBonus = 15f;
        // Every metre between you and a candidate costs this many "virtual degrees", so among
        // otherwise comparable foes the near one wins. This is what makes the assist read as
        // "hit what's in front of me" rather than "hit whatever is best aligned", which at range
        // meant a distant foe two degrees off beat the one actually swinging at you.
        const float k_SelfDistanceWeightDegreesPerMetre = 1.5f;
        // Inside this radius an NPC is close enough that ignoring it is never what the player
        // meant — it is the imp already hitting them.
        const float k_PointBlankRange = 3.5f;
        // ...so it gets a bonus big enough to outweigh k_PvPPlayerPriorityBonus. The player
        // preference is the rule; a foe this close is the exception to it. Anything the cursor is
        // actually on still beats both, because that is an explicit choice rather than a guess.
        const float k_PointBlankBonus = 25f;
        // Eye height for the line-of-sight check, so we test against waist/chest-high geometry
        // instead of the floor under the characters' feet.
        const float k_AimEyeHeight = 1f;
        // Every character contributes several colliders, so 16 overflowed easily in a crowd (and an
        // overflow silently drops candidates — including the player you're aiming at).
        readonly Collider[] m_AimCandidateHits = new Collider[32];
        readonly RaycastHit[] m_LineOfFireHits = new RaycastHit[8];
        LayerMask m_AimCandidateMask;
        // Geometry that blocks line of fire. Mirrors PhysicsProjectile's blocker mask so
        // "if a projectile would hit a wall, the assist won't snap through it".
        LayerMask m_LineOfFireMask;

        // Rate-limited stream of the aim direction to the server. Actions that are aimed when they
        // are *released* rather than when they are requested — the Archer's charged shot, held for
        // up to a second — carry no direction in their request and read this instead.
        // See ServerCharacter.AimDirection.
        const float k_AimSendRateSeconds = 0.1f;
        // Don't spend a packet on the sub-degree jitter of a resting mouse.
        const float k_AimSendMinDegrees = 2f;
        float m_LastSentAimTime;
        Vector3 m_LastSentAimDirection;

        public event Action<Vector3> ClientMoveEvent;

        // Relays ServerCharacter.ActionCooldownStarted so UI (HeroActionBar) doesn't need to
        // know about ServerCharacter directly — same pattern as action1ModifiedCallback.
        public event Action<ActionID, float> ActionCooldownStarted;

        CharacterClass CharacterClass => m_ServerCharacter.CharacterClass;

        [SerializeField] PhysicsWrapper m_PhysicsWrapper;

        public ActionState actionState1 { get; private set; }
        public ActionState actionState2 { get; private set; }
        public ActionState actionState3 { get; private set; }
        public System.Action action1ModifiedCallback;

        ServerCharacter m_TargetServerCharacter;

        void Awake()
        {
            m_MainCamera = Camera.main;
        }

        /// <summary>
        /// The local player's input sender, for UI that needs to draw the aim. Null before the
        /// local player spawns and after it despawns.
        /// </summary>
        public static ClientInputSender LocalInstance { get; private set; }

        /// <summary>Where the local player is aiming — the same value the skills resolve from.</summary>
        public Vector3 CurrentAimDirection => GetAimDirection();

        /// <summary>The ground point the local player is aiming at.</summary>
        public Vector3 CurrentAimPoint => GetAimPoint();

        /// <summary>
        /// The character the aim assist would correct a shot onto, or null. This is the client's own
        /// latest answer rather than the server-confirmed TargetId, so the drawn aim doesn't trail
        /// the cursor by a round-trip.
        /// </summary>
        public ServerCharacter CurrentAssistTarget => m_LocalAssistTarget;

        /// <summary>Where the shot leaves from, for drawing the aim line.</summary>
        public Vector3 AimOrigin => m_PhysicsWrapper.Transform.position;

        public override void OnStartClient()
        {
            if (!isOwned)
            {
                enabled = false;
                return;
            }

            LocalInstance = this;

            m_ServerCharacter.TargetIdChanged += OnTargetChanged;
            m_ServerCharacter.HeldNetworkObjectChanged += OnHeldNetworkObjectChanged;
            m_ServerCharacter.ActionCooldownStarted += OnActionCooldownStarted;

            if (CharacterClass.Skill1 &&
                GameDataSource.Instance.TryGetActionPrototypeByID(CharacterClass.Skill1.ActionID, out var action1))
                actionState1 = new ActionState { actionID = action1.ActionID, selectable = true };

            if (CharacterClass.Skill2 &&
                GameDataSource.Instance.TryGetActionPrototypeByID(CharacterClass.Skill2.ActionID, out var action2))
                actionState2 = new ActionState { actionID = action2.ActionID, selectable = true };

            if (CharacterClass.Skill3 &&
                GameDataSource.Instance.TryGetActionPrototypeByID(CharacterClass.Skill3.ActionID, out var action3))
                actionState3 = new ActionState { actionID = action3.ActionID, selectable = true };

            m_Action1.action.started += OnAction1Started;
            m_Action1.action.canceled += OnAction1Canceled;
            m_Action2.action.started += OnAction2Started;
            m_Action2.action.canceled += OnAction2Canceled;
            m_Action3.action.started += OnAction3Started;
            m_Action3.action.canceled += OnAction3Canceled;
            m_Action5.action.performed += OnAction5Performed;
            m_Action6.action.performed += OnAction6Performed;
            m_Action7.action.performed += OnAction7Performed;
            m_Action8.action.performed += OnAction8Performed;

            m_GroundLayerMask = LayerMask.GetMask("Ground");
            m_AimCandidateMask = LayerMask.GetMask("PCs", "NPCs");
            m_LineOfFireMask = LayerMask.GetMask("Default", "Environment");

            // Resolve the directional-movement action. Prefer the serialized reference, but
            // fall back to finding "Move" in the same asset as the other (wired) actions, so
            // WASD works even when m_MoveAction was left unassigned on the prefab. Enable it
            // explicitly so it reads regardless of how the rest of the map gets enabled.
            m_MoveActionResolved = m_MoveAction != null ? m_MoveAction.action : null;
            if (m_MoveActionResolved == null)
            {
                var asset = m_TargetAction != null ? m_TargetAction.asset : null;
                if (asset != null)
                {
                    m_MoveActionResolved = asset.FindAction("Move");
                }
            }
            m_MoveActionResolved?.Enable();
        }

        public override void OnStopClient()
        {
            if (LocalInstance == this)
            {
                LocalInstance = null;
            }

            if (m_ServerCharacter)
            {
                m_ServerCharacter.TargetIdChanged -= OnTargetChanged;
                m_ServerCharacter.HeldNetworkObjectChanged -= OnHeldNetworkObjectChanged;
                m_ServerCharacter.ActionCooldownStarted -= OnActionCooldownStarted;
            }

            if (m_TargetServerCharacter)
                m_TargetServerCharacter.NetLifeState.LifeStateChanged -= OnTargetLifeStateChanged;

            m_Action1.action.started -= OnAction1Started;
            m_Action1.action.canceled -= OnAction1Canceled;
            m_Action2.action.started -= OnAction2Started;
            m_Action2.action.canceled -= OnAction2Canceled;
            m_Action3.action.started -= OnAction3Started;
            m_Action3.action.canceled -= OnAction3Canceled;
            m_Action5.action.performed -= OnAction5Performed;
            m_Action6.action.performed -= OnAction6Performed;
            m_Action7.action.performed -= OnAction7Performed;
            m_Action8.action.performed -= OnAction8Performed;
        }

        void OnTargetChanged(ulong previousValue, ulong newValue)
        {
            if (m_TargetServerCharacter)
                m_TargetServerCharacter.NetLifeState.LifeStateChanged -= OnTargetLifeStateChanged;

            m_TargetServerCharacter = null;

            var selection = NetworkIdentityUtils.FindByNetId((uint)newValue);
            if (selection != null && selection.TryGetComponent(out m_TargetServerCharacter))
                m_TargetServerCharacter.NetLifeState.LifeStateChanged += OnTargetLifeStateChanged;

            UpdateAction1();
        }

        void OnHeldNetworkObjectChanged(ulong previousValue, ulong newValue) => UpdateAction1();

        void OnActionCooldownStarted(ActionID actionID, float duration) => ActionCooldownStarted?.Invoke(actionID, duration);

        void OnTargetLifeStateChanged(LifeState previousValue, LifeState newValue) => UpdateAction1();

        /// <summary>
        /// The frame on which a targeting input last let go, so the click that dismissed it cannot
        /// also be read as a world click.
        /// </summary>
        /// <remarks>
        /// A ground-targeted power (the Mage's meteor, and anything else carrying an ActionInput)
        /// puts a reticle up and waits for a confirming click. That click is consumed by the
        /// reticle's own Update, which sends the action and tears itself down — clearing
        /// <see cref="m_CurrentSkillInput"/>. If the reticle's Update happens to run before this
        /// component's in the same frame, the guard below is already open by the time we look, and
        /// <c>WasPressedThisFrame</c> is still true for the very same press: the meteor goes out
        /// AND the basic attack fires off one click. Which of the two runs first is script
        /// execution order, so the bug comes and goes rather than failing honestly.
        /// </remarks>
        int m_SkillInputEndedFrame = -1;

        void FinishSkill()
        {
            m_CurrentSkillInput = null;
            m_CurrentSkillInputButton = null;
            m_SkillInputEndedFrame = Time.frameCount;
        }

        /// <summary>
        /// Ends the running charge-up input. Clears our reference first: OnReleaseKey destroys the
        /// input object, which calls back into FinishSkill, and we must not be left pointing at a
        /// dead one either way.
        /// </summary>
        void ReleaseCurrentSkillInput()
        {
            var input = m_CurrentSkillInput;
            m_CurrentSkillInput = null;
            m_CurrentSkillInputButton = null;
            m_SkillInputEndedFrame = Time.frameCount;
            input.OnReleaseKey();
        }

        void SendInput(ActionRequestData action)
        {
            ActionInputEvent?.Invoke(action);
            m_ServerCharacter.CmdPlayAction(action);
        }

        void FixedUpdate()
        {
            for (int i = 0; i < m_ActionRequestCount; ++i)
            {
                if (m_CurrentSkillInput != null)
                {
                    // Cleared here rather than left to the input object's OnDestroy: Destroy is
                    // deferred to the end of the frame, so anything else pressed in this same
                    // batch would still see a skill input in progress and be dropped.
                    if (IsReleaseStyle(m_ActionRequests[i].TriggerStyle))
                        ReleaseCurrentSkillInput();
                }
                else if (!IsReleaseStyle(m_ActionRequests[i].TriggerStyle))
                {
                    var actionPrototype = GameDataSource.Instance.GetActionPrototypeByID(m_ActionRequests[i].RequestedActionID);
                    if (actionPrototype.Config.ActionInput != null)
                    {
                        var skillPlayer = Instantiate(actionPrototype.Config.ActionInput);
                        skillPlayer.Initiate(m_ServerCharacter, m_PhysicsWrapper.Transform.position, actionPrototype.ActionID, SendInput, FinishSkill);
                        m_CurrentSkillInput = skillPlayer;
                        m_CurrentSkillInputButton = m_ActionRequests[i].SourceButton;
                        m_SkillInputStartTime = Time.time;
                    }
                    else
                    {
                        PerformSkill(actionPrototype.ActionID, m_ActionRequests[i].TargetId);
                    }
                }
            }

            m_ActionRequestCount = 0;

            // Movement is directional only (WASD / gamepad / on-screen joystick). Click-to-move
            // was removed on purpose so that aiming/firing with the mouse no longer competes
            // with a walk-to-cursor command (left click just selects a target now).
            Vector2 moveInput = m_MoveActionResolved != null ? m_MoveActionResolved.ReadValue<Vector2>() : Vector2.zero;
            // The mobile on-screen joystick feeds movement through here too (it builds itself at
            // runtime; see MobileMovementJoystick).
            moveInput = Vector2.ClampMagnitude(moveInput + MobileMovementJoystick.MovementInput, 1f);
            bool moving = moveInput.sqrMagnitude > 0.01f;

            Vector3 moveDir = moving ? CameraRelativeMove(moveInput) : Vector3.zero;

            if (moving)
            {
                m_PendingStopSends = 0;
                if ((Time.time - m_LastSentMove) > k_MoveSendRateSeconds)
                {
                    m_LastSentMove = Time.time;
                    m_ServerCharacter.CmdSetMovementDirection(moveDir);
                    m_WasDirectMoving = true;
                }
            }
            else if (m_WasDirectMoving)
            {
                // stick/keys released — tell the server to stop, and queue a few repeats in
                // case the server was in a state where it had to ignore this one.
                m_WasDirectMoving = false;
                m_PendingStopSends = k_StopResendCount;
                m_ServerCharacter.CmdSetMovementDirection(Vector3.zero);
            }
            else if (m_PendingStopSends > 0 && (Time.time - m_LastSentMove) > k_MoveSendRateSeconds)
            {
                m_LastSentMove = Time.time;
                m_PendingStopSends--;
                m_ServerCharacter.CmdSetMovementDirection(Vector3.zero);
            }
        }

        // Deliberately takes no SkillTriggerStyle. It used to, and used it to pick a different
        // targeting rule per trigger — that is exactly the inconsistency this rework removes.
        void PerformSkill(ActionID actionID, ulong targetId = 0)
        {
            var actionProto = GameDataSource.Instance.GetActionPrototypeByID(actionID);

            // Self-targeted support skills (e.g. the Mage's Healing Touch): always cast on
            // ourselves, with no targeting and no movement, no matter what's selected or under
            // the cursor. Gated to friendly Melee so only the self-heal takes this path
            // (Revive/Emote are friendly too but use their own logics).
            if (actionProto.Config.IsFriendly && actionProto.Config.Logic == ActionLogic.Melee)
            {
                var selfData = new ActionRequestData
                {
                    ActionID = actionID,
                    ShouldClose = false,
                    CancelMovement = true,
                };
                SendInput(selfData);
                return;
            }

            // One aim, whatever pressed the button. A skill fired with the left mouse button, the
            // "1" key or the action-bar button now resolves identically — the three used to pick
            // their target three different ways, so the same power aimed differently depending on
            // how you asked for it, which is the kind of thing a player feels but can't name.
            Vector3 aimPoint = GetAimPoint();
            var target = ResolveTarget(actionProto.Config.Logic, targetId, GetAimDirection());

            var data = new ActionRequestData { ActionID = actionID };

            if (target != null)
            {
                ulong targetNetObjId = (ulong)(uint)target.netId;

                // Co-op only: the basic attack on a downed ally becomes a Revive. In PvP other
                // players are enemies, so Skill1 stays an attack.
                if (!GameDataSource.IsPvPMode
                    && actionID == CharacterClass.Skill1.ActionID
                    && target.TryGetComponent<ServerCharacter>(out var targetCharacter)
                    && !targetCharacter.IsNpc
                    && targetCharacter.LifeState == LifeState.Fainted)
                {
                    actionID = GameDataSource.Instance.ReviveActionPrototype.ActionID;
                }

                data.ActionID = actionID;
                data.TargetIds = new[] { targetNetObjId };

                // Aim at the foe the assist chose rather than the raw aim point. That correction
                // is the entire job of the assist, and it keeps Direction agreeing with the target
                // we just declared.
                aimPoint = PhysicsWrapper.TryGetPhysicsWrapper(targetNetObjId, out var movementContainer)
                    ? movementContainer.Transform.position
                    : target.transform.position;

                m_LastSentMove = Time.time + k_TargetMoveTimeout;
            }
            else if (actionProto.IsGeneralTargetAction)
            {
                // A Target action with nothing to target only clears the reticle — there is no
                // meaningful positional version of it to send.
                SendInput(new ActionRequestData { ActionID = actionID, ShouldQueue = false });
                return;
            }

            PopulateSkillRequest(aimPoint, actionID, ref data);
            SendInput(data);
        }

        /// <summary>
        /// Who this skill is aimed at: an explicitly named target (the action bar can supply one),
        /// otherwise — for offensive skills — whatever the aim assist picks from the current aim.
        /// Skills cast on a thing rather than in a direction (Revive, Pick Up) fall back to the
        /// reticle's current target.
        ///
        /// <para>Deliberately takes no trigger style. Which button was pressed used to change the
        /// answer, and that inconsistency is what this rework exists to remove.</para>
        /// </summary>
        NetworkIdentity ResolveTarget(ActionLogic logic, ulong requestedTargetId, Vector3 aimDirection)
        {
            NetworkIdentity target;

            if (requestedTargetId != 0)
            {
                target = NetworkIdentityUtils.FindByNetId((uint)requestedTargetId);
            }
            else if (IsOffensive(logic))
            {
                TryGetAimAssistTarget(aimDirection, out target);
            }
            else
            {
                target = NetworkIdentityUtils.FindByNetId((uint)m_ServerCharacter.TargetId);
            }

            if (target == null) return null;
            return ActionUtils.IsValidTarget((ulong)(uint)target.netId) ? target : null;
        }

        static bool IsOffensive(ActionLogic logic) =>
            logic == ActionLogic.Melee || logic == ActionLogic.LaunchProjectile
            || logic == ActionLogic.RangedFXTargeted || logic == ActionLogic.DashAttack
            || logic == ActionLogic.MeteorStrike;
        // SpinAttack and FrostNova are deliberately absent: they are centred on the caster, so
        // there is no foe for the aim assist to resolve and asking it for one would only move the
        // reticle for no reason.

        void PopulateSkillRequest(Vector3 hitPoint, ActionID actionID, ref ActionRequestData resultData)
        {
            resultData.ActionID = actionID;
            var actionConfig = GameDataSource.Instance.GetActionPrototypeByID(actionID).Config;
            resultData.ShouldClose = true;

            Vector3 offset = hitPoint - m_PhysicsWrapper.Transform.position;
            offset.y = 0;
            float directionLength = offset.magnitude;
            Vector3 direction = 1.0f <= directionLength ? (offset / directionLength) : m_PhysicsWrapper.Transform.forward;

            switch (actionConfig.Logic)
            {
                case ActionLogic.LaunchProjectile:
                    resultData.Direction = direction;
                    resultData.ShouldClose = false;
                    return;
                case ActionLogic.Melee:
                    resultData.Direction = direction;
                    return;
                case ActionLogic.Target:
                    resultData.ShouldClose = false;
                    return;
                case ActionLogic.Emote:
                    resultData.CancelMovement = true;
                    return;
                case ActionLogic.RangedFXTargeted:
                    resultData.Position = hitPoint;
                    return;
                case ActionLogic.DashAttack:
                    resultData.Position = hitPoint;
                    // ShouldClose off. Left on — it defaults to true at the top of this method —
                    // the server synthesises a Chase action in front of the dash and hands it the
                    // movement, so pressing dash while running stopped the player and walked them
                    // to the target instead. Which is absurd for the one ability whose entire job
                    // is closing distance: it was queueing a slower version of itself first.
                    resultData.ShouldClose = false;
                    return;
                case ActionLogic.PickUp:
                    resultData.CancelMovement = true;
                    resultData.ShouldQueue = false;
                    return;
                case ActionLogic.SpinAttack:
                case ActionLogic.FrostNova:
                    // Centred on the caster: there is nothing to aim and nobody to close on.
                    // Direction is still filled in so the client visualisation has something
                    // sensible to orient the effect by.
                    resultData.Direction = direction;
                    resultData.ShouldClose = false;
                    return;
                case ActionLogic.MeteorStrike:
                    // Lands on a spot, like the other ground-targeted powers. ShouldClose stays
                    // off — the whole point is that the Mage calls it down from where they are.
                    resultData.Position = hitPoint;
                    resultData.Direction = direction;
                    resultData.ShouldClose = false;
                    return;
            }
        }

        public void RequestAction(ActionID actionID, SkillTriggerStyle triggerStyle, ulong targetId = 0,
            InputAction sourceButton = null)
        {
            Assert.IsNotNull(GameDataSource.Instance.GetActionPrototypeByID(actionID),
                $"Action {actionID} must be in GameDataSource prototypes!");

            if (m_ActionRequestCount < m_ActionRequests.Length)
            {
                m_ActionRequests[m_ActionRequestCount].RequestedActionID = actionID;
                m_ActionRequests[m_ActionRequestCount].TriggerStyle = triggerStyle;
                m_ActionRequests[m_ActionRequestCount].TargetId = targetId;
                // Always assigned: the array is reused, so a stale button from an earlier request
                // would otherwise be inherited by a keyboard or UI one.
                m_ActionRequests[m_ActionRequestCount].SourceButton = sourceButton;
                m_ActionRequestCount++;
            }
        }

        void OnAction1Started(InputAction.CallbackContext obj) => RequestAction(actionState1.actionID, SkillTriggerStyle.Keyboard);
        void OnAction1Canceled(InputAction.CallbackContext obj) => RequestAction(actionState1.actionID, SkillTriggerStyle.KeyboardRelease);
        void OnAction2Started(InputAction.CallbackContext obj) => RequestAction(actionState2.actionID, SkillTriggerStyle.Keyboard);
        void OnAction2Canceled(InputAction.CallbackContext obj) => RequestAction(actionState2.actionID, SkillTriggerStyle.KeyboardRelease);
        void OnAction3Started(InputAction.CallbackContext obj) => RequestAction(actionState3.actionID, SkillTriggerStyle.Keyboard);
        void OnAction3Canceled(InputAction.CallbackContext obj) => RequestAction(actionState3.actionID, SkillTriggerStyle.KeyboardRelease);
        void OnAction5Performed(InputAction.CallbackContext obj) => RequestAction(GameDataSource.Instance.Emote1ActionPrototype.ActionID, SkillTriggerStyle.Keyboard);
        void OnAction6Performed(InputAction.CallbackContext obj) => RequestAction(GameDataSource.Instance.Emote2ActionPrototype.ActionID, SkillTriggerStyle.Keyboard);
        void OnAction7Performed(InputAction.CallbackContext obj) => RequestAction(GameDataSource.Instance.Emote3ActionPrototype.ActionID, SkillTriggerStyle.Keyboard);
        void OnAction8Performed(InputAction.CallbackContext obj) => RequestAction(GameDataSource.Instance.Emote4ActionPrototype.ActionID, SkillTriggerStyle.Keyboard);

        void Update()
        {
            UpdateAimTarget();
            SendAimDirection();

            // A charge started with a mouse button ends when that button comes back up. This has to
            // sit outside the click block below, because that block is skipped for as long as a
            // skill input is alive — which is precisely when the release matters. Tested by button
            // state rather than by a release event so a release that landed before the input
            // existed (a very fast click) still ends it on the next frame.
            if (m_CurrentSkillInput != null && m_CurrentSkillInputButton != null
                && !m_CurrentSkillInputButton.IsPressed())
            {
                ReleaseCurrentSkillInput();
            }

            // Safety net: never let a charge-up input (Tank's Shield Aura, Archer's charged shot)
            // hold the input system hostage. If its release never arrived, end it ourselves —
            // otherwise the hero can still run but every attack click is swallowed.
            if (m_CurrentSkillInput != null && (Time.time - m_SkillInputStartTime) > k_MaxSkillInputHoldSeconds)
            {
                ReleaseCurrentSkillInput();
            }

            // Not merely "no reticle up" but "no reticle up, and none went down this frame":
            // see m_SkillInputEndedFrame.
            if (m_CurrentSkillInput == null && Time.frameCount != m_SkillInputEndedFrame)
            {
                // Left click: the basic attack (Skill1). It used to select a target instead, which
                // no longer has a job — the aim decides who gets hit, and the reticle follows the
                // aim rather than a click. Putting the attack on the left button is where a player
                // picking the game up reaches for it.
                //
                // Still routed through the input actions rather than read off Mouse.current, so the
                // bindings stay rebindable and a gamepad's face buttons keep working — but a press
                // that came from the touchscreen is dropped. A world tap used to be harmless (it
                // only selected a target); firing on it would mean every stray tap let off an
                // attack, and touch already has a better way in — the action-bar button, which can
                // be dragged to aim first.
                bool fromTouch = Touchscreen.current != null &&
                                 Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
                bool blocked = fromTouch || IsTouchGestureActive();

                bool leftPressed = actionState1 != null && m_TargetAction.action.WasPressedThisFrame();
                // Right click: the class power (Skill2). Not every class defines one, hence the guard.
                bool rightPressed = actionState2 != null && m_Skill1Action.action.WasPressedThisFrame();

                // The UI test runs only on the frame a button actually went down: it costs a
                // raycast, and asking once per click is both cheaper and more accurate than asking
                // every frame off a pointer state that lags by one.
                if ((leftPressed || rightPressed) && !blocked && !IsPointerOverClickableUI())
                {
                    if (leftPressed)
                        RequestAction(actionState1.actionID, SkillTriggerStyle.MouseClick,
                            sourceButton: m_TargetAction.action);

                    if (rightPressed)
                        RequestAction(actionState2.actionID, SkillTriggerStyle.MouseClick,
                            sourceButton: m_Skill1Action.action);
                }
            }
        }

        // Scratch for the click-time UI test. Reused so a click doesn't allocate.
        PointerEventData m_UiPointerData;
        readonly List<RaycastResult> m_UiRaycastResults = new List<RaycastResult>();

        /// <summary>
        /// Whether the cursor is over UI that would actually do something with a click.
        /// </summary>
        /// <remarks>
        /// <para>This used to be <c>EventSystem.IsPointerOverGameObject()</c>, which answers "is
        /// there any graphic under the pointer" — and the HUD is full of graphics that are not
        /// buttons. The action bar's own backdrop is a 575×128 image at 2% opacity sitting behind
        /// the skill buttons, the emote bar has a twin, and the party HUD contributes name labels
        /// and portraits. All of them are invisible or decorative, all of them answered "yes", and
        /// every attack click that landed on one was silently thrown away. That is why attacking
        /// with the mouse dropped hits in some corners of the screen and never anywhere else, with
        /// every class, while the "1" key — which does not pass through here — always worked.</para>
        ///
        /// <para>So the question asked is the narrower one that was always meant: would this click
        /// be delivered to something? A backdrop takes no click and no longer eats one; a skill
        /// button still does, which is what stops a press on the action bar from also swinging.</para>
        /// </remarks>
        bool IsPointerOverClickableUI()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            m_UiPointerData ??= new PointerEventData(eventSystem);
            m_UiPointerData.position = m_PointAction.action.ReadValue<Vector2>();

            m_UiRaycastResults.Clear();
            eventSystem.RaycastAll(m_UiPointerData, m_UiRaycastResults);

            for (int i = 0; i < m_UiRaycastResults.Count; i++)
            {
                var hit = m_UiRaycastResults[i].gameObject;
                if (hit == null)
                {
                    continue;
                }

                // GetEventHandler walks up the hierarchy, so hitting a button's icon still finds
                // the button. Down is checked as well as click: the action bar's charge-up buttons
                // act on press, not on release.
                if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) != null
                    || ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit) != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True while a touch widget owns the current gesture, so a press meant for the joystick,
        /// the zoom bar or a camera swing doesn't also fire a skill.
        /// </summary>
        static bool IsTouchGestureActive() =>
            MobileMovementJoystick.IsActive || MobileZoomBar.IsActive || TouchCameraOrbit.IsActive;

        // Converts a 2D move input (WASD/stick) into a world-space direction on the ground plane,
        // relative to the camera so "up" always means "away from camera". Built from the camera's
        // orbit yaw rather than its transform — the orbital follow damps its position, so a basis
        // taken from the transform lags the orbit while the camera is being swung and slides around
        // under the player's thumb. See CameraOrbitYaw.
        Vector3 CameraRelativeMove(Vector2 input) => CameraOrbitYaw.ToWorldDirection(input);

        // How far out a purely directional aim (touch drag, gamepad stick) is treated as pointing,
        // in metres. Only matters for skills that land at a point rather than fly along a
        // direction — an AoE's ground zero, a dash's destination.
        const float k_AimProjectionDistance = 12f;

        // Dedicated buffer for the cursor ground-raycast. The aim is read several times a frame
        // (reticle, aim stream, firing), so it gets its own scratch rather than sharing one with
        // whatever the caller is in the middle of using.
        readonly RaycastHit[] m_AimRayHits = new RaycastHit[4];

        /// <summary>
        /// The world point the player is aiming at, on the ground plane. Together with
        /// <see cref="GetAimDirection"/> this is the only place the aim is decided; every skill,
        /// however it was triggered, resolves from here.
        ///
        /// <para>Order of authority, and it is deliberately the same list, in the same order, as
        /// <see cref="GetAimDirection"/>: a touch aim (a drag out of a skill button) wins, since it
        /// only exists while the player is deliberately aiming; then a gamepad's right stick; then
        /// the mouse cursor, but only while it is still fresh; then where the character is
        /// running; and finally straight ahead.</para>
        ///
        /// <para><b>The two must not disagree.</b> They did: the direction was taught to fall back
        /// on the run direction and this was left on the old list, so a skill would be aimed
        /// forwards and land behind. Point-targeted powers are where that shows worst — the dash
        /// took its destination from here, so it would compute a spot at the player's feet and
        /// appear not to fire at all.</para>
        /// </summary>
        Vector3 GetAimPoint()
        {
            Vector3 pos = m_PhysicsWrapper.Transform.position;

            if (TryGetTouchAimDirection(out var touchDir))
                return pos + touchDir * k_AimProjectionDistance;

            if (TryGetGamepadAimDirection(out var padDir))
                return pos + padDir * k_AimProjectionDistance;

            // The cursor gives a real distance, not just a bearing, so use the point itself —
            // but only while the player has been moving it. A cursor left behind during a chase
            // points backwards, and for a dash that means dashing away from what you are chasing.
            if (CursorIsFresh() && TryGetCursorGroundPoint(out var cursorPoint))
                return cursorPoint;

            if (TryGetMoveDirection(out var moveDir))
                return pos + moveDir * k_AimProjectionDistance;

            return pos + FlatForward() * k_AimProjectionDistance;
        }

        /// <summary>
        /// The direction the player is aiming, flattened and normalized. Never zero.
        /// </summary>
        Vector3 GetAimDirection()
        {
            if (TryGetTouchAimDirection(out var touchDir)) return touchDir;
            if (TryGetGamepadAimDirection(out var padDir)) return padDir;

            // A cursor the player has not touched in a while is not an aim, it is a leftover.
            // Chasing somebody with WASD, the character runs PAST the place the mouse is still
            // pointing at, so the direction to the cursor flips round and the attack comes out
            // backwards — and it reads as the aim assist picking an enemy behind you, when the
            // assist is faithfully searching a cone that was already facing the wrong way.
            if (CursorIsFresh() && TryGetCursorGroundPoint(out var cursorPoint))
            {
                Vector3 toCursor = cursorPoint - m_PhysicsWrapper.Transform.position;
                toCursor.y = 0f;
                if (toCursor.sqrMagnitude > 0.001f) return toCursor.normalized;
            }

            // Where the player is running, ahead of where the character happens to be pointing.
            //
            // This is what phones fall through to, and it is the same bug wearing different
            // clothes. There is no cursor on a phone, so the aim used to end at FlatForward — the
            // character's facing — which is owned by the SERVER and therefore arrives late. Turn
            // hard while chasing someone and the client is still holding the facing from before
            // the turn, so the swing goes out along it. The joystick, unlike the facing, is read
            // locally and is never stale: it is what the player is asking for right now.
            if (TryGetMoveDirection(out var moveDir))
            {
                return moveDir;
            }

            return FlatForward();
        }

        /// <summary>Screen position of the pointer last frame, to notice that it moved.</summary>
        Vector2 m_LastPointerPosition;
        float m_LastPointerMoveTime = -999f;

        /// <summary>
        /// How long a still mouse keeps owning the aim.
        /// </summary>
        /// <remarks>
        /// Long enough that aiming somewhere and holding it is respected — that is a deliberate
        /// act and must never be overridden — and short enough that it has expired by the time a
        /// chase has carried the player past their own cursor.
        /// </remarks>
        const float k_CursorFreshSeconds = 1.2f;

        /// <summary>Pixels of movement that count as the player re-aiming.</summary>
        const float k_PointerMoveThreshold = 6f;

        bool CursorIsFresh()
        {
            if (m_PointAction == null || m_PointAction.action == null)
            {
                return false;
            }

            var position = m_PointAction.action.ReadValue<Vector2>();
            if ((position - m_LastPointerPosition).sqrMagnitude
                > k_PointerMoveThreshold * k_PointerMoveThreshold)
            {
                m_LastPointerPosition = position;
                m_LastPointerMoveTime = Time.time;
            }

            return Time.time - m_LastPointerMoveTime < k_CursorFreshSeconds;
        }

        /// <summary>
        /// Where the player is running, in world space, or false if they are standing still.
        /// </summary>
        /// <remarks>
        /// Taken through the same camera-relative basis the movement itself uses, so "the way I am
        /// running" means the same thing to the aim as it does to the legs.
        /// </remarks>
        bool TryGetMoveDirection(out Vector3 direction)
        {
            direction = Vector3.zero;

            // BOTH sources, combined exactly as the movement code itself does. The on-screen
            // joystick does not feed the Move action — it publishes through its own static and is
            // added in at the point movement is applied — so reading only the action returns zero
            // on a phone. That is the whole reason this had no effect on mobile: the aim fell
            // through to the character's facing, which is the server's and arrives late, so a hard
            // turn mid-chase still swung at where the player used to be looking.
            Vector2 move = m_MoveActionResolved != null ? m_MoveActionResolved.ReadValue<Vector2>() : Vector2.zero;
            move = Vector2.ClampMagnitude(move + MobileMovementJoystick.MovementInput, 1f);

            if (move.sqrMagnitude < 0.04f)
            {
                return false;
            }

            direction = CameraRelativeMove(move);
            return direction.sqrMagnitude > 0.001f;
        }

        Vector3 FlatForward()
        {
            Vector3 forward = m_PhysicsWrapper.Transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>Where the mouse cursor meets the ground, if there is a mouse and it hit.</summary>
        bool TryGetCursorGroundPoint(out Vector3 point)
        {
            point = default;
            if (m_MainCamera == null || Mouse.current == null) return false;

            var ray = m_MainCamera.ScreenPointToRay(m_PointAction.action.ReadValue<Vector2>());
            if (Physics.RaycastNonAlloc(ray, m_AimRayHits, k_MouseInputRaycastDistance, m_GroundLayerMask) <= 0)
                return false;

            point = m_AimRayHits[0].point;
            return true;
        }

        /// <summary>
        /// The touch aim, set while the player drags out of a skill button. This is the only way to
        /// aim on a phone — before it existed, touch players aimed with their body and simply could
        /// not re-aim while standing still.
        /// </summary>
        bool TryGetTouchAimDirection(out Vector3 direction)
        {
            if (TouchSkillAim.IsAiming)
            {
                direction = TouchSkillAim.WorldDirection;
                if (direction.sqrMagnitude > 0.001f) return true;
            }

            direction = default;
            return false;
        }

        /// <summary>Right stick, in the camera's basis so "up" is away from the camera.</summary>
        bool TryGetGamepadAimDirection(out Vector3 direction)
        {
            direction = default;

            var gamepad = Gamepad.current;
            if (gamepad == null) return false;

            Vector2 stick = gamepad.rightStick.ReadValue();
            if (stick.sqrMagnitude < k_GamepadAimDeadzoneSqr) return false;

            direction = CameraRelativeMove(stick);
            return direction.sqrMagnitude > 0.001f;
        }

        const float k_GamepadAimDeadzoneSqr = 0.08f;

        /// <summary>
        /// Keeps the reticle on whoever the aim assist would actually correct a shot onto, so the
        /// mark the player sees and the foe the game picks can never disagree. That they used to
        /// disagree — an 80° soft-lock painting one foe while a skillshot flew at the cursor — is
        /// the single biggest reason aiming read as unpredictable.
        ///
        /// <para>Recomputed on a timer only to save physics queries; firing always resolves the
        /// assist fresh, so a stale frame here can never cost a hit.</para>
        /// </summary>
        void UpdateAimTarget()
        {
            if (Time.time - m_LastAimTargetUpdate < k_AimTargetInterval) return;
            m_LastAimTargetUpdate = Time.time;

            bool found = TryGetAimAssistTarget(GetAimDirection(), out var foe);
            ulong bestNetId = found ? (ulong)(uint)foe.netId : 0;

            // Kept locally so the aim line can be drawn from this frame's answer. TargetId is a
            // server SyncVar and only catches up a round-trip later, which on a real server is long
            // enough for the drawn line to visibly trail the cursor.
            m_LocalAssistTarget = found ? foe.GetComponent<ServerCharacter>() : null;

            // Compare against what we last *sent*, not only against the SyncVar: the SyncVar takes
            // a round-trip to reflect the change, so against a remote server this would otherwise
            // resend the same pick every tick until the confirmation arrived.
            if (bestNetId == m_LastSentTargetId || bestNetId == m_ServerCharacter.TargetId) return;
            m_LastSentTargetId = bestNetId;

            // Drives the existing Target action, which is what puts the reticle under a foe. It no
            // longer turns the character to face them: the body follows movement, and an attack
            // plants it on the aim for the length of the swing (ServerCharacterMovement.LockFacing).
            SendInput(new ActionRequestData
            {
                ActionID = GameDataSource.Instance.GeneralTargetActionPrototype.ActionID,
                TargetIds = bestNetId != 0 ? new[] { bestNetId } : null,
                ShouldQueue = false,
            });
        }

        /// <summary>
        /// Streams the aim direction to the server, rate-limited and skipped while it barely moves.
        /// Only actions that are aimed at *release* rather than at request need it — the Archer's
        /// charged shot, which is held for up to a second after its request was sent and so has no
        /// usable direction of its own. See <c>ServerCharacter.AimDirection</c>.
        /// </summary>
        void SendAimDirection()
        {
            if (Time.time - m_LastSentAimTime < k_AimSendRateSeconds) return;

            Vector3 aim = GetAimDirection();
            if (m_LastSentAimDirection != Vector3.zero &&
                Vector3.Angle(m_LastSentAimDirection, aim) < k_AimSendMinDegrees)
            {
                return;
            }

            m_LastSentAimTime = Time.time;
            m_LastSentAimDirection = aim;
            m_ServerCharacter.CmdSetAimDirection(aim);
        }

        /// <summary>
        /// Picks the foe most aligned with <paramref name="aimDir"/>, within the tight
        /// <see cref="k_AimAssistMaxAngle"/> cone and with clear line of fire. Returns false when
        /// nothing qualifies, so the caller fires exactly where the player pointed.
        ///
        /// <para>This is now the <i>only</i> targeting in the game. It both draws the reticle and
        /// corrects the shot, which is what keeps the two honest.</para>
        /// </summary>
        bool TryGetAimAssistTarget(Vector3 aimDir, out NetworkIdentity foe)
        {
            foe = null;

            Vector3 myPos = m_PhysicsWrapper.Transform.position;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) return false;
            aimDir.Normalize();

            ulong myNetId = m_ServerCharacter.NetworkObjectId;
            int numHits = Physics.OverlapSphereNonAlloc(myPos, k_AimAssistRange, m_AimCandidateHits, m_AimCandidateMask);

            // Mouse only: the cursor's actual ground point, for point-based scoring. Touch and
            // gamepad aim are bearings — there is no point to be near — so they stay pure-angle.
            Vector3 cursorPoint = default;
            bool hasCursor = !TryGetTouchAimDirection(out _)
                             && !TryGetGamepadAimDirection(out _)
                             && TryGetCursorGroundPoint(out cursorPoint);

            ServerCharacter best = null;
            // Scored on angle plus, with a mouse, distance from the cursor — minus a bonus for
            // foe players so they beat the imps milling around them, minus a stickiness bonus for
            // the current target so the lock doesn't flicker between two similar candidates. The
            // cone test still uses the raw angle, so the bonuses only reorder candidates that
            // were all going to be acceptable anyway.
            float bestScore = float.MaxValue;
            for (int i = 0; i < numHits; i++)
            {
                var candidate = m_AimCandidateHits[i].GetComponentInParent<ServerCharacter>();
                if (candidate == null) continue;
                if ((ulong)(uint)candidate.netId == myNetId) continue;
                if (candidate.LifeState != LifeState.Alive) continue;
                if (!candidate.IsNpc && !GameDataSource.IsPvPMode) continue;
                if (candidate.physicsWrapper == null) continue;

                Vector3 foePos = candidate.physicsWrapper.Transform.position;
                Vector3 toFoe = foePos - myPos;
                toFoe.y = 0f;
                float dist = toFoe.magnitude;
                if (dist < 0.01f) continue;

                float cursorDist = float.MaxValue;
                bool hovered = false;
                if (hasCursor)
                {
                    Vector3 cursorToFoe = foePos - cursorPoint;
                    cursorToFoe.y = 0f;
                    cursorDist = cursorToFoe.magnitude;
                    hovered = cursorDist <= k_CursorSnapRadius;
                }

                float angle = Vector3.Angle(aimDir, toFoe / dist);
                // A hovered foe is exempt from the cone: the cursor sitting on somebody is the
                // least ambiguous aim a mouse can express, and in a top-down game the aim
                // direction points at the cursor anyway.
                if (!hovered && angle > k_AimAssistMaxAngle) continue;

                float score = angle;
                // Nearest-first, as a smooth ramp rather than a tie-break, so it shapes the whole
                // ordering instead of only settling exact ties.
                score += dist * k_SelfDistanceWeightDegreesPerMetre;
                if (hasCursor)
                {
                    score += Mathf.Min(cursorDist, k_AimAssistRange) * k_CursorDistanceWeightDegreesPerMetre;
                }
                if (hovered)
                {
                    // Dominates everything except another hovered foe (then the closer-to-cursor
                    // one wins through the distance term).
                    score -= 1000f;
                }
                if (!candidate.IsNpc)
                {
                    score -= k_PvPPlayerPriorityBonus;
                }
                else if (dist <= k_PointBlankRange)
                {
                    // An imp this close is the one hitting you. Preferring a player over it is
                    // correct as a default and wrong in exactly this case, so the exception is
                    // spelled out rather than left to the distance ramp to maybe win.
                    score -= k_PointBlankBonus;
                }
                if (candidate == m_LocalAssistTarget)
                {
                    score -= k_StickyTargetBonusDegrees;
                }

                if (score > bestScore) continue;             // worse than the current best

                // Don't snap through walls.
                if (!HasLineOfFire(myPos, foePos)) continue;

                bestScore = score;
                best = candidate;
            }

            if (best == null) return false;
            foe = best.netIdentity;
            return true;
        }

        /// <summary>
        /// True if nothing solid stands between us and a foe at <paramref name="foePos"/>.
        /// Characters never count as cover: the old version used a plain Linecast, which ends
        /// *inside* the target's own body, so any collider of theirs (or of ours at the start
        /// point) that happened to sit on a blocking layer reported "behind a wall" and the
        /// foe could never be locked — the main reason auto-aim onto other players failed.
        /// </summary>
        bool HasLineOfFire(Vector3 myPos, Vector3 foePos)
        {
            Vector3 eye = myPos + Vector3.up * k_AimEyeHeight;
            Vector3 foeEye = foePos + Vector3.up * k_AimEyeHeight;
            Vector3 delta = foeEye - eye;
            float dist = delta.magnitude;
            if (dist < 0.01f) return true;

            int numHits = Physics.RaycastNonAlloc(new Ray(eye, delta / dist), m_LineOfFireHits, dist,
                m_LineOfFireMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < numHits; i++)
            {
                // A body (ours, the target's, or a bystander's) is not cover.
                if (m_LineOfFireHits[i].transform.GetComponentInParent<ServerCharacter>() != null) continue;
                return false;
            }

            return true;
        }

        void UpdateAction1()
        {
            var heldNetId = NetworkIdentityUtils.FindByNetId((uint)m_ServerCharacter.HeldNetworkObject);
            bool isHoldingNetworkObject = heldNetId != null;

            var selection = NetworkIdentityUtils.FindByNetId((uint)m_ServerCharacter.TargetId);
            ulong selectionNetObjId = selection != null ? (ulong)(uint)selection.netId : 0;

            var isSelectable = true;
            if (isHoldingNetworkObject)
            {
                actionState1.actionID = GameDataSource.Instance.DropActionPrototype.ActionID;
            }
            else if (m_ServerCharacter.TargetId != 0 && selection != null && selection.TryGetComponent(out PickUpState pickUpState))
            {
                actionState1.actionID = GameDataSource.Instance.PickUpActionPrototype.ActionID;
            }
            else if (!GameDataSource.IsPvPMode
                     && m_ServerCharacter.TargetId != 0
                     && selection != null
                     && selectionNetObjId != m_ServerCharacter.NetworkObjectId
                     && selection.TryGetComponent(out ServerCharacter charState)
                     && !charState.IsNpc)
            {
                // Co-op only: targeting a fellow player offers Revive (usable when they're
                // down). In PvP other players are enemies, so we keep Skill1 to attack them.
                actionState1.actionID = GameDataSource.Instance.ReviveActionPrototype.ActionID;
                isSelectable = charState.NetLifeState.LifeState != LifeState.Alive;
            }
            else
            {
                actionState1.SetActionState(CharacterClass.Skill1.ActionID);
            }

            actionState1.selectable = isSelectable;
            action1ModifiedCallback?.Invoke();
        }

        public class ActionState
        {
            public ActionID actionID { get; internal set; }
            public bool selectable { get; internal set; }

            internal void SetActionState(ActionID newActionID, bool isSelectable = true)
            {
                actionID = newActionID;
                selectable = isSelectable;
            }
        }
    }
}
