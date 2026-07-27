using System;
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

        readonly RaycastHit[] k_CachedHit = new RaycastHit[4];

        LayerMask m_GroundLayerMask;
        LayerMask m_ActionLayerMask;

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
        // true while the last frame sent a directional (WASD/stick) move, so we know
        // to send a single "stop" when the stick is released.
        bool m_WasDirectMoving;
        // The server drops movement commands it can't apply right now (e.g. while a knockback
        // or charge is in progress), so a single "stop" packet sent at that moment was lost and
        // the character kept running forever. Resend the stop a few times to cover that window.
        int m_PendingStopSends;
        const int k_StopResendCount = 4;
        // How long the camera keeps holding still after a duel with a foe *player* stops being the
        // current pick. Covers the soft-lock briefly handing the target to a passing NPC.
        const float k_DuelCameraHoldSeconds = 2f;
        float m_DuelCameraHoldUntil;
        Camera m_MainCamera;

        // ── Continuous auto-target (aim-based "line of fire" lock) ───────────────
        // Range of the soft-lock. Raised from the original 8m: in PvP the fight happens at
        // ranged-weapon distances, and an 8m lock meant you simply couldn't auto-aim at the
        // player you were shooting at.
        const float k_AutoTargetRange = 14f;       // meters
        // Half-cone around the aim direction. Widened from the original 45° so the soft-lock
        // can grab foes that are off to the side / not directly faced, which combats the
        // "combat feels stiff" complaint. Manual left-click selection (see m_ManualTargetUntil)
        // still overrides this and can pick foes in any direction.
        const float k_AutoTargetMaxAngle = 80f;
        const float k_AutoTargetInterval = 0.15f;  // re-evaluate ~6x/sec
        // After a deliberate left-click selection, don't let the continuous auto-target steal
        // the pick for this long (as long as the chosen foe stays alive). Lets the player lock
        // an enemy they aren't facing and keep firing at it.
        const float k_ManualTargetHoldSeconds = 3f;
        float m_ManualTargetUntil;
        // Unconditional part of the hold: right after the click the server hasn't confirmed the
        // new TargetId yet, so we can't validate the pick. For this long we honour the hold no
        // matter what; after it, the hold only survives while the picked foe is still a valid,
        // in-range target (see UpdateAutoTarget). Without this the lock used to stick for the
        // full 3s onto a foe that had already died or run away.
        const float k_ManualTargetGraceSeconds = 0.6f;
        float m_ManualTargetGraceUntil;
        // In PvP, other players are the point of the fight — but the map is full of imps that
        // would otherwise win the "closest to my aim" contest and steal the lock. This many
        // "virtual degrees" of bonus are subtracted from a player candidate's score so a foe
        // player beats a nearby NPC unless the NPC is much better aligned.
        const float k_PvPPlayerPriorityBonus = 40f;
        // How strongly alignment with the aim beats proximity when scoring candidates.
        // Higher = the foe most directly in the line of fire wins even if a closer foe
        // sits off to the side. Score is degrees-off-aim * weight + distance-in-metres.
        const float k_AutoTargetAngleWeight = 1.5f;
        // Eye height for the line-of-sight check, so we test against waist/chest-high
        // geometry instead of the floor under the characters' feet.
        const float k_AutoTargetEyeHeight = 1f;
        float m_LastAutoTarget;
        // Every character contributes several colliders, so 16 overflowed easily in a crowd
        // (and an overflow silently drops candidates — including the player you're aiming at).
        readonly Collider[] m_AutoTargetHits = new Collider[32];
        readonly RaycastHit[] m_LineOfFireHits = new RaycastHit[8];
        LayerMask m_AutoTargetMask;
        // Geometry that blocks line of fire. Mirrors PhysicsProjectile's blocker mask so
        // "if a projectile would hit a wall, the auto-target won't lock through it".
        LayerMask m_LineOfFireMask;

        // How the player aims the auto-target cone:
        //  - Pointer  (PC): toward the mouse cursor's ground position.
        //  - Movement (gamepad / mobile): toward where the character is walking.
        enum AimMode { Pointer, Movement }
        AimMode m_AimMode = AimMode.Pointer;
        // Timestamps of the last pointer (mouse) vs movement (WASD/stick/touch) input.
        // Whichever happened most recently decides the aim mode, so the two schemes
        // coexist and switch live: touch the mouse → aim at cursor; press WASD → aim
        // where you face. Both start at 0 so the initial mode stays Pointer.
        float m_LastPointerInputTime;
        float m_LastMovementInputTime;

        // Same idea, one level coarser, for who turns the camera: a scheme with a manual camera
        // turns it itself, one without gets CameraAutoRotate swinging it. Whichever family of
        // devices was used last wins, so a PC player with a pad plugged in gets whichever one they
        // actually have in their hands. Both start at 0 so the seed CameraAutoRotateToggle takes
        // from the present devices holds until the player touches something.
        float m_LastManualCameraSchemeTime;
        float m_LastGamepadTime;

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

        public override void OnStartClient()
        {
            if (!isOwned)
            {
                enabled = false;
                return;
            }

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
            m_ActionLayerMask = LayerMask.GetMask("PCs", "NPCs", "Ground");
            m_AutoTargetMask = LayerMask.GetMask("PCs", "NPCs");
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

        void FinishSkill() => m_CurrentSkillInput = null;

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
                    if (IsReleaseStyle(m_ActionRequests[i].TriggerStyle))
                        m_CurrentSkillInput.OnReleaseKey();
                }
                else if (!IsReleaseStyle(m_ActionRequests[i].TriggerStyle))
                {
                    var actionPrototype = GameDataSource.Instance.GetActionPrototypeByID(m_ActionRequests[i].RequestedActionID);
                    if (actionPrototype.Config.ActionInput != null)
                    {
                        var skillPlayer = Instantiate(actionPrototype.Config.ActionInput);
                        skillPlayer.Initiate(m_ServerCharacter, m_PhysicsWrapper.Transform.position, actionPrototype.ActionID, SendInput, FinishSkill);
                        m_CurrentSkillInput = skillPlayer;
                        m_SkillInputStartTime = Time.time;
                    }
                    else
                    {
                        PerformSkill(actionPrototype.ActionID, m_ActionRequests[i].TriggerStyle, m_ActionRequests[i].TargetId);
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

            if (ShouldSuspendCameraAutoRotate())
            {
                CameraAutoRotate.Suspend();
            }

            // The camera auto-rotate follows where we intend to walk, not the character's forward
            // (which actions keep overwriting). See CameraAutoRotate.
            Vector3 moveDir = moving ? CameraRelativeMove(moveInput) : Vector3.zero;
            if (moving)
            {
                CameraAutoRotate.ReportMoveIntent(moveDir);
            }
            else
            {
                CameraAutoRotate.ClearMoveIntent();
            }

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

        void PerformSkill(ActionID actionID, SkillTriggerStyle triggerStyle, ulong targetId = 0)
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

            Transform hitTransform = null;

            if (targetId != 0)
            {
                var targetNetId = NetworkIdentityUtils.FindByNetId((uint)targetId);
                if (targetNetId != null)
                    hitTransform = targetNetId.transform;
            }
            else
            {
                int numHits = 0;
                if (triggerStyle == SkillTriggerStyle.MouseClick)
                {
                    var ray = m_MainCamera.ScreenPointToRay(m_PointAction.action.ReadValue<Vector2>());
                    numHits = Physics.RaycastNonAlloc(ray, k_CachedHit, k_MouseInputRaycastDistance, m_ActionLayerMask);
                }

                int networkedHitIndex = -1;
                for (int i = 0; i < numHits; i++)
                {
                    if (k_CachedHit[i].transform.GetComponentInParent<NetworkIdentity>())
                    {
                        networkedHitIndex = i;
                        break;
                    }
                }

                hitTransform = networkedHitIndex >= 0 ? k_CachedHit[networkedHitIndex].transform : null;

                // Forgiving selection: if the click didn't land squarely on a character, grab
                // the nearest valid enemy to where the cursor hit the ground. Lets you select
                // foes you aren't directly facing and makes target-picking far less fiddly.
                if (hitTransform == null && triggerStyle == SkillTriggerStyle.MouseClick && numHits > 0
                    && TryGetEnemyNearestToPoint(k_CachedHit[0].point, k_SelectAssistRadius, out var nearestEnemy))
                {
                    hitTransform = nearestEnemy.transform;
                }
            }

            if (GetActionRequestForTarget(hitTransform, actionID, triggerStyle, out ActionRequestData playerAction))
            {
                m_LastSentMove = Time.time + k_TargetMoveTimeout;
                SendInput(playerAction);
            }
            else if (!GameDataSource.Instance.GetActionPrototypeByID(actionID).IsGeneralTargetAction)
            {
                var data = new ActionRequestData();
                Vector3 aimPoint;
                if (triggerStyle == SkillTriggerStyle.MouseClick)
                {
                    // Mouse click already raycast the ground into k_CachedHit this frame.
                    aimPoint = k_CachedHit[0].point;
                }
                else
                {
                    // Keyboard/gamepad with no target: there is no fresh cursor ray, so
                    // k_CachedHit holds a stale point from a previous click. Aim where the
                    // player faces instead, so the attack fires forward (toward where you
                    // look) rather than snapping the character to some old direction.
                    Vector3 aimDir = GetAimDirection();
                    if (aimDir.sqrMagnitude < 0.001f) aimDir = m_PhysicsWrapper.Transform.forward;
                    aimPoint = m_PhysicsWrapper.Transform.position + aimDir.normalized * 5f;
                }
                PopulateSkillRequest(aimPoint, actionID, ref data);
                SendInput(data);
            }
        }

        bool GetActionRequestForTarget(Transform hit, ActionID actionID, SkillTriggerStyle triggerStyle, out ActionRequestData resultData)
        {
            resultData = new ActionRequestData();

            var targetNetId = hit != null ? hit.GetComponentInParent<NetworkIdentity>() : null;

            if (!targetNetId && !GameDataSource.Instance.GetActionPrototypeByID(actionID).IsGeneralTargetAction)
            {
                var logic = GameDataSource.Instance.GetActionPrototypeByID(actionID).Config.Logic;
                bool offensive = logic == ActionLogic.Melee || logic == ActionLogic.LaunchProjectile
                                 || logic == ActionLogic.RangedFXTargeted || logic == ActionLogic.DashAttack;

                if (offensive && triggerStyle != SkillTriggerStyle.MouseClick)
                {
                    // Tight aim-assist for keyboard/gamepad attacks: snap onto a foe only if
                    // it's within a small angle of where you aim. If none, leave the target
                    // null so the skill fires straight ahead (handled by PerformSkill). This
                    // is the "small auto-aim" on top of the wider auto-select reticle.
                    if (TryGetAimAssistTarget(out var assistNetId))
                        targetNetId = assistNetId;
                }
                else if ((logic == ActionLogic.RangedFXTargeted || logic == ActionLogic.LaunchProjectile)
                         && triggerStyle == SkillTriggerStyle.MouseClick)
                {
                    // Ranged skillshots (Mage bolt, Archer arrow): if the click didn't land on a
                    // foe, aim at the cursor point (handled by PerformSkill's no-target branch)
                    // instead of snapping to the soft-locked auto-target off to the side. The shot
                    // always flies toward the mouse, independent of where the character is facing.
                    // Leaving targetNetId null makes this method return false so that fallback runs.
                }
                else
                {
                    // Mouse, or non-offensive skills (revive/pickup): use the active target.
                    targetNetId = NetworkIdentityUtils.FindByNetId((uint)m_ServerCharacter.TargetId);
                }
            }

            ulong targetNetObjId = targetNetId != null ? (ulong)(uint)targetNetId.netId : 0;

            if (targetNetId == null || !ActionUtils.IsValidTarget(targetNetObjId))
                return false;

            if (targetNetId.TryGetComponent<ServerCharacter>(out var serverCharacter))
            {
                if (!GameDataSource.IsPvPMode && actionID == CharacterClass.Skill1.ActionID && triggerStyle == SkillTriggerStyle.MouseClick)
                {
                    if (!serverCharacter.IsNpc && serverCharacter.LifeState == LifeState.Fainted)
                        actionID = GameDataSource.Instance.ReviveActionPrototype.ActionID;
                }
            }

            Vector3 targetHitPoint;
            if (PhysicsWrapper.TryGetPhysicsWrapper(targetNetObjId, out var movementContainer))
                targetHitPoint = movementContainer.Transform.position;
            else
                targetHitPoint = targetNetId.transform.position;

            resultData.ActionID = actionID;
            resultData.TargetIds = new ulong[] { targetNetObjId };
            PopulateSkillRequest(targetHitPoint, actionID, ref resultData);
            return true;
        }

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
                    return;
                case ActionLogic.PickUp:
                    resultData.CancelMovement = true;
                    resultData.ShouldQueue = false;
                    return;
            }
        }

        public void RequestAction(ActionID actionID, SkillTriggerStyle triggerStyle, ulong targetId = 0)
        {
            Assert.IsNotNull(GameDataSource.Instance.GetActionPrototypeByID(actionID),
                $"Action {actionID} must be in GameDataSource prototypes!");

            if (m_ActionRequestCount < m_ActionRequests.Length)
            {
                m_ActionRequests[m_ActionRequestCount].RequestedActionID = actionID;
                m_ActionRequests[m_ActionRequestCount].TriggerStyle = triggerStyle;
                m_ActionRequests[m_ActionRequestCount].TargetId = targetId;
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
            UpdateAimMode();
            UpdateCameraControlScheme();
            UpdateAutoTarget();

            // Safety net: never let a charge-up input (Tank's Shield Aura, Archer's charged shot)
            // hold the input system hostage. If its release never arrived, end it ourselves —
            // otherwise the hero can still run but every attack click is swallowed.
            if (m_CurrentSkillInput != null && (Time.time - m_SkillInputStartTime) > k_MaxSkillInputHoldSeconds)
            {
                var stuckInput = m_CurrentSkillInput;
                m_CurrentSkillInput = null;
                stuckInput.OnReleaseKey();
            }

            if (!EventSystem.current.IsPointerOverGameObject() && m_CurrentSkillInput == null)
            {
                // Right click: cast the hero's primary power (Skill1).
                if (m_Skill1Action.action.WasPressedThisFrame())
                    RequestAction(CharacterClass.Skill1.ActionID, SkillTriggerStyle.MouseClick);

                // Left click: only selects the character to attack. Click-to-move was removed
                // on purpose — movement is WASD / stick / on-screen joystick — so aiming and
                // firing no longer fight with a walk-to-cursor command. On touch we ignore the
                // press while the movement joystick, the zoom bar or the auto-rotate toggle is
                // engaged, so starting to walk, to zoom or to flip the toggle doesn't also select a
                // random target. (Those widgets carry no GraphicRaycaster, so the EventSystem check
                // above doesn't see them.)
                if (m_TargetAction.action.WasPressedThisFrame() && !MobileMovementJoystick.IsActive &&
                    !MobileZoomBar.IsActive && !CameraAutoRotateToggle.IsActive)
                {
                    m_ManualTargetUntil = Time.time + k_ManualTargetHoldSeconds;
                    m_ManualTargetGraceUntil = Time.time + k_ManualTargetGraceSeconds;
                    RequestAction(GameDataSource.Instance.GeneralTargetActionPrototype.ActionID, SkillTriggerStyle.MouseClick);
                }
            }
        }

        // Situations where swinging the camera to follow the walk costs more than it gives.
        bool ShouldSuspendCameraAutoRotate()
        {
            // With mouse aim the reticle *is* the cursor raycast to the ground (see
            // GetAimDirection), so sweeping the camera under a motionless cursor would silently
            // re-aim and hand the auto-target a different victim. Note this also means a PC player
            // who touches the mouse turns the feature off for themselves and a keyboard-only one
            // keeps it, which is roughly the behaviour we'd have hard-coded per platform anyway.
            if (m_AimMode == AimMode.Pointer)
            {
                return true;
            }

            // PvP comes first: while the target is another player, the camera holds still, and it
            // keeps holding for a moment after the pick is lost. The soft-lock re-evaluates ~6x/sec
            // and a passing imp can steal the pick mid-duel; without the tail the camera would start
            // swinging in the middle of the fight and then stop again once the rival is re-acquired.
            if (m_ServerCharacter.TargetId != 0 && m_TargetServerCharacter != null && !m_TargetServerCharacter.IsNpc)
            {
                m_DuelCameraHoldUntil = Time.time + k_DuelCameraHoldSeconds;
            }

            if (Time.time < m_DuelCameraHoldUntil)
            {
                return true;
            }

            // A deliberate left-click pick also means "I'm fighting this one", even if it's an NPC.
            // Note the NPC soft-lock on its own is not enough: it grabs any imp inside a 14 m / 80°
            // cone, so gating on "has a target at all" would leave the camera frozen through most of
            // the map — exactly where the feature is meant to help.
            if (m_ServerCharacter.TargetId != 0 && Time.time < m_ManualTargetUntil)
            {
                return true;
            }

            return false;
        }

        // Converts a 2D move input (WASD/stick) into a world-space direction on the
        // ground plane, relative to the camera so "up" always means "away from camera".
        Vector3 CameraRelativeMove(Vector2 input)
        {
            Vector3 forward;

            // Straight from the camera's orbit yaw, with the auto-rotation's own contribution taken
            // back out (see CameraAutoRotate.BasisYaw). Two reasons not to use the camera transform
            // here: the camera is turning *because* of this very input, so leaving that rotation in
            // would make a held sideways input redefine "sideways" every frame and curve the walk
            // into a circle; and the transform lags the orbit while the camera moves, because the
            // orbital follow damps its position — a lagging basis drifts and snaps back under the
            // player's thumb.
            float basisYaw = CameraAutoRotate.BasisYaw;
            if (!float.IsNaN(basisYaw))
            {
                forward = Quaternion.AngleAxis(basisYaw, Vector3.up) * Vector3.forward;
            }
            else
            {
                // No camera resolved yet: fall back to the rendered one.
                forward = m_MainCamera.transform.forward;
                forward.y = 0f;
                // If it looks nearly straight down, use its "up" projected on the ground so the
                // direction stays stable.
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = m_MainCamera.transform.up;
                    forward.y = 0f;
                }
                forward.Normalize();
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            return (forward * input.y + right * input.x).normalized;
        }

        /// <summary>
        /// Continuous, facing-based soft lock: repeatedly picks the best enemy inside a
        /// frontal cone (relative to where the character faces) and makes it the active
        /// target, so attacks land reliably without precise mouse aiming. Mobile-friendly,
        /// and fixes player-vs-player melee that previously needed a pixel-perfect click.
        /// Mouse click-targeting still works and simply overrides the current pick.
        /// </summary>
        // Latches the aim mode to the input device the player is actually using, so a
        // PC player with a gamepad plugged in still gets mouse aim until they touch the
        // stick (and vice-versa). On a phone there's no mouse, so it stays Movement.
        void UpdateAimMode()
        {
            float now = Time.unscaledTime;

            // Pointer intent: the mouse was moved or a mouse button is held.
            if (Mouse.current != null &&
                (Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f
                 || Mouse.current.leftButton.isPressed
                 || Mouse.current.rightButton.isPressed))
            {
                m_LastPointerInputTime = now;
            }

            // Movement intent: WASD keys, gamepad stick, or touch are active.
            var keyboard = Keyboard.current;
            bool wasdActive = keyboard != null &&
                (keyboard.wKey.isPressed || keyboard.aKey.isPressed ||
                 keyboard.sKey.isPressed || keyboard.dKey.isPressed);
            var gamepad = Gamepad.current;
            bool stickActive = gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > 0.04f;
            bool touchActive = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed;
            if (wasdActive || stickActive || touchActive)
            {
                m_LastMovementInputTime = now;
            }

            // Most recent input wins. Ties (both this frame, or both still 0 at start)
            // keep the current mode, so nothing flickers when the player is idle.
            if (m_LastPointerInputTime > m_LastMovementInputTime) m_AimMode = AimMode.Pointer;
            else if (m_LastMovementInputTime > m_LastPointerInputTime) m_AimMode = AimMode.Movement;
        }

        /// <summary>
        /// Decides who is in charge of the camera's yaw, by the same most-recent-device latch as
        /// <see cref="UpdateAimMode"/>:
        /// <list type="bullet">
        /// <item><b>keyboard+mouse</b> — nobody turns it on its own. The player does it with a
        /// middle-mouse drag (<see cref="MouseCameraOrbit"/>) and zooms with the wheel, so a camera
        /// that also wandered by itself would be fighting them.</item>
        /// <item><b>touch</b> — same as keyboard+mouse. The player drags on the right half of the
        /// screen (<see cref="TouchCameraOrbit"/>) and zooms with the bar.</item>
        /// <item><b>gamepad</b> — <see cref="CameraAutoRotate"/> swings it to follow the walk. The
        /// only scheme left with no spare input for a camera of its own.</item>
        /// </list>
        /// <para>Touch used to be grouped with the gamepad, and that is what made walking sideways
        /// come out as walking forward: the auto-rotation has to freeze the movement basis while it
        /// turns (see the class docs on <see cref="CameraAutoRotate"/>), so the stick stops matching
        /// the screen, and the mismatch accumulates. Now that touch has a manual camera the trade is
        /// no longer worth making, and it is gated off here rather than defaulted off in
        /// <c>ClientPrefs</c> — a saved preference from an earlier build would have kept the old
        /// behaviour alive, and a scheme with its own camera should never be asking the question.</para>
        /// Note this deliberately keys off the device family rather than
        /// <see cref="m_AimMode"/>: a keyboard-only PC player never trips the pointer latch, but
        /// they do have a wheel to press, so the camera is theirs to turn too.
        /// </summary>
        void UpdateCameraControlScheme()
        {
            float now = Time.unscaledTime;

            var mouse = Mouse.current;
            bool mouseActive = mouse != null &&
                (mouse.delta.ReadValue().sqrMagnitude > 0.5f
                 || mouse.leftButton.isPressed
                 || mouse.rightButton.isPressed
                 || mouse.middleButton.isPressed
                 || mouse.scroll.ReadValue().sqrMagnitude > 0.01f);

            // Any key, not just WASD: the point is which device is in their hands, and reaching for
            // a hotkey says that just as well as walking does.
            var keyboard = Keyboard.current;
            bool keyboardActive = keyboard != null && keyboard.anyKey.isPressed;

            bool touchActive = (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                || MobileMovementJoystick.IsActive;

            if (mouseActive || keyboardActive || touchActive)
            {
                m_LastManualCameraSchemeTime = now;
            }

            var gamepad = Gamepad.current;
            bool padActive = gamepad != null &&
                (gamepad.leftStick.ReadValue().sqrMagnitude > 0.04f
                 || gamepad.rightStick.ReadValue().sqrMagnitude > 0.04f
                 || gamepad.dpad.ReadValue().sqrMagnitude > 0.04f);

            if (padActive)
            {
                m_LastGamepadTime = now;
            }

            if (m_LastManualCameraSchemeTime > m_LastGamepadTime)
            {
                CameraAutoRotate.AllowedByScheme = false;
            }
            else if (m_LastGamepadTime > m_LastManualCameraSchemeTime)
            {
                CameraAutoRotate.AllowedByScheme = true;
            }
        }

        // The direction the player is aiming, used as the centre of the auto-target cone.
        Vector3 GetAimDirection()
        {
            Vector3 pos = m_PhysicsWrapper.Transform.position;

            if (m_AimMode == AimMode.Pointer && m_MainCamera != null && Mouse.current != null)
            {
                // Aim toward where the mouse cursor hits the ground.
                var ray = m_MainCamera.ScreenPointToRay(m_PointAction.action.ReadValue<Vector2>());
                if (Physics.RaycastNonAlloc(ray, k_CachedHit, k_MouseInputRaycastDistance, m_GroundLayerMask) > 0)
                {
                    Vector3 toCursor = k_CachedHit[0].point - pos;
                    toCursor.y = 0f;
                    if (toCursor.sqrMagnitude > 0.001f) return toCursor.normalized;
                }
            }

            // Movement mode (gamepad / mobile): aim where we walk, i.e. the way we face.
            Vector3 forward = m_PhysicsWrapper.Transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.zero;
        }

        void UpdateAutoTarget()
        {
            if (Time.time - m_LastAutoTarget < k_AutoTargetInterval) return;
            m_LastAutoTarget = Time.time;

            // Respect a recent manual pick, but only while it's still worth respecting: past the
            // short grace window the hold is dropped as soon as the picked foe is dead, gone or
            // out of range, so the player isn't locked onto nothing for the rest of the 3s.
            if (Time.time < m_ManualTargetUntil && Time.time >= m_ManualTargetGraceUntil
                && !IsManualTargetStillValid())
            {
                m_ManualTargetUntil = 0f;
            }

            // We used to also check that
            // m_ServerCharacter.TargetId already matched the manual pick before honoring the
            // hold — but TargetId is a server-authoritative SyncVar that only updates after a
            // full CmdPlayAction round-trip. On localhost that round-trip is ~0ms so the race
            // never showed up, but against a real dedicated server (VPS ping) there's a window
            // right after the click where TargetId still holds the *previous* value, so that
            // check failed and auto-target immediately stomped the manual pick before the
            // server's confirmation ever arrived. Hence the grace window above: we trust the
            // hold blindly at first, and only start validating once the server has answered.
            if (Time.time < m_ManualTargetUntil) return;

            Vector3 myPos = m_PhysicsWrapper.Transform.position;
            Vector3 aimDir = GetAimDirection();
            if (aimDir.sqrMagnitude < 0.001f) return;

            ulong myNetId = m_ServerCharacter.NetworkObjectId;
            int numHits = Physics.OverlapSphereNonAlloc(myPos, k_AutoTargetRange, m_AutoTargetHits, m_AutoTargetMask);

            ServerCharacter best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < numHits; i++)
            {
                var candidate = m_AutoTargetHits[i].GetComponentInParent<ServerCharacter>();
                if (candidate == null) continue;
                if ((ulong)(uint)candidate.netId == myNetId) continue;       // never target self
                if (candidate.LifeState != LifeState.Alive) continue;
                // Enemies: NPCs are always hostile; other players only in PvP mode.
                if (!candidate.IsNpc && !GameDataSource.IsPvPMode) continue;
                if (candidate.physicsWrapper == null) continue;

                Vector3 foePos = candidate.physicsWrapper.Transform.position;
                Vector3 toFoe = foePos - myPos;
                toFoe.y = 0f;
                float dist = toFoe.magnitude;
                if (dist < 0.01f) continue;

                float angle = Vector3.Angle(aimDir, toFoe / dist);
                if (angle > k_AutoTargetMaxAngle) continue;

                // Line of fire: skip foes behind a wall so we never lock through cover.
                if (!HasLineOfFire(myPos, foePos)) continue;

                // Prefer the foe most directly in the line of fire; distance is the
                // tie-breaker (closer wins) rather than the dominant term. In PvP a foe
                // player outranks the imps standing around them.
                float score = angle * k_AutoTargetAngleWeight + dist;
                if (!candidate.IsNpc) score -= k_PvPPlayerPriorityBonus;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            ulong bestNetId = best != null ? (ulong)(uint)best.netId : 0;
            if (bestNetId == m_ServerCharacter.TargetId) return; // no change — don't spam the server

            // Drive the existing Target action: a populated target sets+faces it (and shows
            // the reticle); an empty one clears the lock when nothing is in front.
            var data = new ActionRequestData
            {
                ActionID = GameDataSource.Instance.GeneralTargetActionPrototype.ActionID,
                TargetIds = bestNetId != 0 ? new[] { bestNetId } : null,
                ShouldQueue = false,
            };
            SendInput(data);
        }

        // Half-angle of the aim-assist cone. Much tighter than the auto-select cone
        // (k_AutoTargetMaxAngle): an attack only snaps onto a foe this close to your aim,
        // so you mostly hit where you look and only get a small correction. Tune to taste.
        const float k_AimAssistMaxAngle = 18f;

        /// <summary>
        /// Picks the foe most aligned with the aim direction, but only within the tight
        /// <see cref="k_AimAssistMaxAngle"/> cone and with clear line of fire. Returns false
        /// when nothing qualifies, so the caller fires straight ahead. This is the "small
        /// auto-aim" layered on top of the wider auto-select.
        /// </summary>
        bool TryGetAimAssistTarget(out NetworkIdentity foe)
        {
            foe = null;

            Vector3 myPos = m_PhysicsWrapper.Transform.position;
            Vector3 aimDir = GetAimDirection();
            if (aimDir.sqrMagnitude < 0.001f) aimDir = m_PhysicsWrapper.Transform.forward;
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) return false;
            aimDir.Normalize();

            ulong myNetId = m_ServerCharacter.NetworkObjectId;
            int numHits = Physics.OverlapSphereNonAlloc(myPos, k_AutoTargetRange, m_AutoTargetHits, m_AutoTargetMask);

            ServerCharacter best = null;
            // Scored like the soft-lock: raw angle, minus a bonus for foe players so they beat
            // the imps milling around them. The cone test still uses the raw angle.
            float bestScore = k_AimAssistMaxAngle;
            for (int i = 0; i < numHits; i++)
            {
                var candidate = m_AutoTargetHits[i].GetComponentInParent<ServerCharacter>();
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

                float angle = Vector3.Angle(aimDir, toFoe / dist);
                if (angle > k_AimAssistMaxAngle) continue;   // outside the cone

                float score = candidate.IsNpc ? angle : angle - k_PvPPlayerPriorityBonus;
                if (score > bestScore) continue;             // worse than the current best

                // Don't snap through walls.
                if (!HasLineOfFire(myPos, foePos)) continue;

                bestScore = score;   // prefer the foe most directly in the line of fire
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
            Vector3 eye = myPos + Vector3.up * k_AutoTargetEyeHeight;
            Vector3 foeEye = foePos + Vector3.up * k_AutoTargetEyeHeight;
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

        /// <summary>
        /// True while a hand-picked target is still worth keeping the auto-target off:
        /// it exists, is alive and is still in soft-lock range. Used to cut the manual hold
        /// short instead of leaving the player locked onto a corpse.
        /// </summary>
        bool IsManualTargetStillValid()
        {
            if (m_ServerCharacter.TargetId == 0) return false;
            if (m_TargetServerCharacter == null) return false;
            if (m_TargetServerCharacter.LifeState != LifeState.Alive) return false;
            if (m_TargetServerCharacter.physicsWrapper == null) return false;

            Vector3 toTarget = m_TargetServerCharacter.physicsWrapper.Transform.position - m_PhysicsWrapper.Transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= k_AutoTargetRange * k_AutoTargetRange;
        }

        // Radius (metres) around the cursor's ground point within which a left-click will
        // snap onto an enemy even if you didn't click exactly on it.
        const float k_SelectAssistRadius = 4f;

        /// <summary>
        /// Finds the nearest valid enemy to a world point (used for forgiving click-selection).
        /// Enemies are alive NPCs, plus other players in PvP mode; never ourselves.
        /// </summary>
        bool TryGetEnemyNearestToPoint(Vector3 point, float radius, out ServerCharacter enemy)
        {
            enemy = null;
            ulong myNetId = m_ServerCharacter.NetworkObjectId;
            int numHits = Physics.OverlapSphereNonAlloc(point, radius, m_AutoTargetHits, m_AutoTargetMask);

            float bestScore = radius * radius;
            for (int i = 0; i < numHits; i++)
            {
                var candidate = m_AutoTargetHits[i].GetComponentInParent<ServerCharacter>();
                if (candidate == null) continue;
                if ((ulong)(uint)candidate.netId == myNetId) continue;
                if (candidate.LifeState != LifeState.Alive) continue;
                if (!candidate.IsNpc && !GameDataSource.IsPvPMode) continue;
                if (candidate.physicsWrapper == null) continue;

                float distSqr = (candidate.physicsWrapper.Transform.position - point).sqrMagnitude;
                // Clicking near a player and an imp at once should pick the player — that's
                // who you meant. NPCs are scored as if they were a bit further away.
                float score = candidate.IsNpc ? distSqr : distSqr * 0.35f;
                if (distSqr < radius * radius && score < bestScore)
                {
                    bestScore = score;
                    enemy = candidate;
                }
            }

            return enemy != null;
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
