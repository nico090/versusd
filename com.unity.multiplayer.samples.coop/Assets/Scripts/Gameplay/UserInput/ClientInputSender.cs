using System;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
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
        const float k_MaxNavMeshDistance = 1f;
        RaycastHitComparer m_RaycastHitComparer;

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
        bool m_MoveRequest;
        // true while the last frame sent a directional (WASD/stick) move, so we know
        // to send a single "stop" when the stick is released.
        bool m_WasDirectMoving;
        Camera m_MainCamera;

        // ── Continuous auto-target (aim-based "line of fire" lock) ───────────────
        const float k_AutoTargetRange = 8f;        // meters
        // Half-cone around the aim direction. Kept fairly tight so the lock behaves like
        // a line of fire (the foe you're actually pointing at) rather than "anything in
        // front". Widen toward ~70 if gamepad/mobile aiming feels too unforgiving.
        const float k_AutoTargetMaxAngle = 45f;
        const float k_AutoTargetInterval = 0.15f;  // re-evaluate ~6x/sec
        // How strongly alignment with the aim beats proximity when scoring candidates.
        // Higher = the foe most directly in the line of fire wins even if a closer foe
        // sits off to the side. Score is degrees-off-aim * weight + distance-in-metres.
        const float k_AutoTargetAngleWeight = 1.5f;
        // Eye height for the line-of-sight check, so we test against waist/chest-high
        // geometry instead of the floor under the characters' feet.
        const float k_AutoTargetEyeHeight = 1f;
        float m_LastAutoTarget;
        readonly Collider[] m_AutoTargetHits = new Collider[16];
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

        public event Action<Vector3> ClientMoveEvent;

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
            m_RaycastHitComparer = new RaycastHitComparer();

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
                    }
                    else
                    {
                        PerformSkill(actionPrototype.ActionID, m_ActionRequests[i].TriggerStyle, m_ActionRequests[i].TargetId);
                    }
                }
            }

            m_ActionRequestCount = 0;

            // Continuous directional movement (WASD / gamepad / mobile joystick).
            // Takes priority over click-to-move and bypasses the EventSystem guard so
            // it keeps working regardless of any stray selected UI object.
            Vector2 moveInput = m_MoveActionResolved != null ? m_MoveActionResolved.ReadValue<Vector2>() : Vector2.zero;
            if (moveInput.sqrMagnitude > 0.01f)
            {
                m_MoveRequest = false; // don't also fire a click-move this frame
                if ((Time.time - m_LastSentMove) > k_MoveSendRateSeconds)
                {
                    m_LastSentMove = Time.time;
                    m_ServerCharacter.CmdSetMovementDirection(CameraRelativeMove(moveInput));
                    m_WasDirectMoving = true;
                }
            }
            else if (m_WasDirectMoving)
            {
                // stick/keys released — tell the server to stop once
                m_WasDirectMoving = false;
                m_ServerCharacter.CmdSetMovementDirection(Vector3.zero);
            }

            if (EventSystem.current.currentSelectedGameObject != null)
            {
                return;
            }

            if (m_MoveRequest)
            {
                m_MoveRequest = false;
                if ((Time.time - m_LastSentMove) > k_MoveSendRateSeconds)
                {
                    m_LastSentMove = Time.time;
                    var ray = m_MainCamera.ScreenPointToRay(m_PointAction.action.ReadValue<Vector2>());
                    var groundHits = Physics.RaycastNonAlloc(ray, k_CachedHit, k_MouseInputRaycastDistance, m_GroundLayerMask);

                    if (groundHits > 0)
                    {
                        if (groundHits > 1)
                            Array.Sort(k_CachedHit, 0, groundHits, m_RaycastHitComparer);

                        bool sampled = NavMesh.SamplePosition(k_CachedHit[0].point, out var hit, k_MaxNavMeshDistance, NavMesh.AllAreas);

                        if (sampled)
                        {
                            m_ServerCharacter.CmdSendCharacterInput(hit.position);
                            ClientMoveEvent?.Invoke(hit.position);
                        }
                    }
                }
            }
        }

        void PerformSkill(ActionID actionID, SkillTriggerStyle triggerStyle, ulong targetId = 0)
        {
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
            UpdateAutoTarget();

            if (!EventSystem.current.IsPointerOverGameObject() && m_CurrentSkillInput == null)
            {
                if (m_Skill1Action.action.WasPressedThisFrame())
                    RequestAction(CharacterClass.Skill1.ActionID, SkillTriggerStyle.MouseClick);

                if (m_TargetAction.action.WasPressedThisFrame())
                    RequestAction(GameDataSource.Instance.GeneralTargetActionPrototype.ActionID, SkillTriggerStyle.MouseClick);
                else if (m_TargetAction.action.IsPressed())
                    m_MoveRequest = true;
            }
        }

        // Converts a 2D move input (WASD/stick) into a world-space direction on the
        // ground plane, relative to the camera so "up" always means "away from camera".
        Vector3 CameraRelativeMove(Vector2 input)
        {
            Vector3 forward = m_MainCamera.transform.forward;
            forward.y = 0f;
            // If the camera looks nearly straight down, fall back to its "up" projected
            // on the ground so the direction stays stable.
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = m_MainCamera.transform.up;
                forward.y = 0f;
            }
            forward.Normalize();

            Vector3 right = m_MainCamera.transform.right;
            right.y = 0f;
            right.Normalize();

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

                Vector3 foePos = candidate.physicsWrapper.Transform.position;
                Vector3 toFoe = foePos - myPos;
                toFoe.y = 0f;
                float dist = toFoe.magnitude;
                if (dist < 0.01f) continue;

                float angle = Vector3.Angle(aimDir, toFoe / dist);
                if (angle > k_AutoTargetMaxAngle) continue;

                // Line of fire: skip foes behind a wall so we never lock through cover.
                Vector3 eye = myPos + Vector3.up * k_AutoTargetEyeHeight;
                Vector3 foeEye = foePos + Vector3.up * k_AutoTargetEyeHeight;
                if (Physics.Linecast(eye, foeEye, m_LineOfFireMask, QueryTriggerInteraction.Ignore))
                    continue;

                // Prefer the foe most directly in the line of fire; distance is the
                // tie-breaker (closer wins) rather than the dominant term.
                float score = angle * k_AutoTargetAngleWeight + dist;
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
            float bestAngle = k_AimAssistMaxAngle;   // only accept foes inside this angle
            for (int i = 0; i < numHits; i++)
            {
                var candidate = m_AutoTargetHits[i].GetComponentInParent<ServerCharacter>();
                if (candidate == null) continue;
                if ((ulong)(uint)candidate.netId == myNetId) continue;
                if (candidate.LifeState != LifeState.Alive) continue;
                if (!candidate.IsNpc && !GameDataSource.IsPvPMode) continue;

                Vector3 foePos = candidate.physicsWrapper.Transform.position;
                Vector3 toFoe = foePos - myPos;
                toFoe.y = 0f;
                float dist = toFoe.magnitude;
                if (dist < 0.01f) continue;

                float angle = Vector3.Angle(aimDir, toFoe / dist);
                if (angle > bestAngle) continue;   // outside the cone, or worse than current best

                // Don't snap through walls.
                Vector3 eye = myPos + Vector3.up * k_AutoTargetEyeHeight;
                Vector3 foeEye = foePos + Vector3.up * k_AutoTargetEyeHeight;
                if (Physics.Linecast(eye, foeEye, m_LineOfFireMask, QueryTriggerInteraction.Ignore))
                    continue;

                bestAngle = angle;   // prefer the foe most directly in the line of fire
                best = candidate;
            }

            if (best == null) return false;
            foe = best.netIdentity;
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
