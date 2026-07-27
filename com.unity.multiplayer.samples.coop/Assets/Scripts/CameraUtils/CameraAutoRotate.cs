using Unity.Cinemachine;
using UnityEngine;

namespace Unity.BossRoom.CameraUtils
{
    /// <summary>
    /// Swings the gameplay camera around to follow the direction the player is walking.
    ///
    /// It is fed by <b>move intent</b> (the direction ClientInputSender is about to send to the
    /// server), not by the character's <c>transform.forward</c>: the forward is repeatedly
    /// overwritten by actions (TargetAction faces the selected enemy every frame, melee/projectile
    /// actions snap it on use), so following it would yank the camera every time the auto-target
    /// changes victim. Same reason Cinemachine's native axis Recentering isn't used.
    ///
    /// Only the horizontal axis is touched — VerticalAxis belongs to <c>MobileZoomBar</c>.
    ///
    /// <para>Movement is camera-relative, so camera-follows-movement is a feedback loop: a held
    /// sideways input would turn the camera, which redefines "sideways", which curves the walk into
    /// a circle. Three rules break it and keep the controls from feeling like they wander:</para>
    /// <list type="bullet">
    /// <item><see cref="BasisYaw"/> — what the movement basis is built from — is the orbit's yaw
    /// <i>minus</i> the degrees this component injected, so it holds still for as long as a swing
    /// lasts and the walk stays straight.</item>
    /// <item>A swing only starts once the player has held a heading for <see cref="k_CommitSeconds"/>,
    /// so taps, jiggles and quick direction changes never move the camera at all.</item>
    /// <item>Once started, a swing finishes even if the stick is released, and the basis is only
    /// rebased when everything has come to a stop. Otherwise every brief walk would leave the camera
    /// at some arbitrary half-way angle and shift the controls by an arbitrary amount.</item>
    /// </list>
    ///
    /// <para><b>Touch and gamepad only.</b> On keyboard+mouse the player turns the camera
    /// themselves — middle-mouse drag, see <c>MouseCameraOrbit</c> — so this stays out of the way
    /// entirely (<see cref="AllowedByScheme"/>). It exists for the schemes with no third input to
    /// spare for a camera: a thumb on an on-screen joystick, or a gamepad.</para>
    ///
    /// <para><b>Opt-out, and for a reason.</b> Keeping the basis still is what makes the walk
    /// straight, but it also means that while the camera turns, the stick stops matching the screen —
    /// and since the basis is only rebased once everything comes to a stop, a player who walks
    /// continuously and changes direction accumulates that mismatch across swings. Two 90° swings
    /// without ever releasing and "forward" points backwards. The alternative (basis always taken
    /// live from the camera) keeps the stick glued to the screen but curves the walk instead. There
    /// is no setting that avoids both; the trade-off is inherent to camera-relative movement. This is
    /// left in, opt-in through the on-screen toggle, rather than deleted.</para>
    ///
    /// <para>The gates (mouse aim, locked duel) live in
    /// <c>ClientInputSender.ShouldSuspendCameraAutoRotate</c>, which owns the aim mode and the
    /// current target; this component only takes the <see cref="Suspend"/> signal.</para>
    /// </summary>
    public class CameraAutoRotate : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>
        /// Master switch, defaulting to on. <c>CameraAutoRotateToggle</c> owns it: it seeds this from
        /// <c>ClientPrefs</c> at startup and persists every flip. That lives on the other side of the
        /// assembly line on purpose — this assembly (Unity.BossRoom.CameraUtils) only references
        /// Cinemachine, and it's not worth widening for a preference read. While off, nothing is ever
        /// written to the axis, so movement behaves exactly as it did before this component existed.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether the control scheme the player is currently using is one this feature applies to:
        /// true for touch / on-screen joystick / gamepad, false for keyboard+mouse (where the camera
        /// is the player's to turn, with a middle-mouse drag). Latched per input device by
        /// <c>ClientInputSender.UpdateCameraControlScheme</c>, seeded from the devices present by
        /// <c>CameraAutoRotateToggle.Bootstrap</c>, and both of those live on the other side of the
        /// assembly line for the same reason <see cref="Enabled"/> does — this assembly references
        /// Cinemachine and nothing else.
        ///
        /// Kept separate from <see cref="Enabled"/> rather than folded into it: that one is the
        /// player's own choice and is persisted, and a PC player picking up a gamepad shouldn't find
        /// their preference overwritten.
        /// </summary>
        public static bool AllowedByScheme { get; set; } = true;

        static bool Active => Enabled && AllowedByScheme;

        // Start turning only once the camera is this far off the direction of travel. Above the 45°
        // of a WASD diagonal on purpose, so only a clear sideways walk counts...
        const float k_StartDeadzoneDegrees = 50f;
        // ...and keep going until it is properly behind the walk. The gap between the two is what
        // keeps it from vibrating around the boundary.
        const float k_StopDeadzoneDegrees = 10f;

        // Headings further off than this don't move the camera at all. At 180° there is no right
        // answer — turning left and right are equally valid — so any choice is a coin flip the
        // player didn't ask for, and a whip around the back is the most nauseating thing this
        // component could do. Below the cap it still takes on big swings; it just takes its time.
        const float k_MaxErrorDegrees = 150f;

        // Slow on purpose. This is the main comfort dial: a swing the player barely notices beats a
        // quick one that lands sooner, even when there are 150° to cover. At this rate a 90° swing
        // takes about four seconds.
        const float k_SmoothTimeSeconds = 0.9f;
        const float k_MaxSpeedDegreesPerSecond = 25f;

        // How long a heading has to be held before the camera reacts to it.
        const float k_CommitSeconds = 0.45f;
        // How far the heading may drift without counting as "a different heading" and restarting
        // that clock. Wide enough that easing around a corner still counts as committed.
        const float k_CommitToleranceDegrees = 30f;

        // Intent and suspension are reported from ClientInputSender.FixedUpdate, which doesn't run
        // on every frame, so keep the last report alive briefly instead of demanding a per-frame one.
        const float k_IntentStaleSeconds = 0.25f;

        // Below this much leftover angular velocity there is nothing left to ease out.
        const float k_CoastCutoffDegreesPerSecond = 1f;
        // Friction used to ease that leftover velocity out: from full speed to a stop in ~0.3s.
        // Kept in proportion to the max speed above — braking far harder than the swing itself
        // moves would read as the camera hitting a wall.
        const float k_CoastDecelDegreesPerSecondSq = 90f;

        static Vector3 s_MoveIntent;
        static float s_IntentTime = float.NegativeInfinity;
        static float s_SuspendTime = float.NegativeInfinity;

        static bool HasIntent => Time.time - s_IntentTime <= k_IntentStaleSeconds;
        static bool IsSuspended => Time.time - s_SuspendTime <= k_IntentStaleSeconds;

        /// <summary>
        /// World yaw, in degrees, that a camera-relative movement basis should be built from: the
        /// camera orbit's yaw with this component's own contribution taken back out. NaN until the
        /// camera has been found, in which case callers should fall back to the camera transform.
        ///
        /// Read this rather than the camera transform: the orbital follow damps the camera's
        /// position, so while the camera is swinging its transform lags the orbit by a good fraction
        /// of a second. Mixing a lagging yaw with the up-to-date <see cref="AppliedYaw"/> leaves a
        /// basis that drifts and snaps back mid-walk, which reads as the controls moving around
        /// under the player.
        /// </summary>
        public static float BasisYaw { get; private set; } = float.NaN;

        /// <summary>
        /// Degrees of yaw this component has injected since the basis was last rebased. Bookkeeping
        /// behind <see cref="BasisYaw"/>; exposed for debugging. Deliberately does not include manual
        /// camera rotation: that is the player's own and should steer the basis live.
        /// </summary>
        public static float AppliedYaw { get; private set; }

        /// <summary>
        /// Call on every frame the auto-rotation has to stay out of the player's way (mouse aim,
        /// locked duel). A swing already in progress is abandoned and eased out rather than stopping
        /// dead. Does not rebase the basis: doing that mid-walk would swerve the character, so it
        /// waits until everything is at a standstill.
        /// </summary>
        public static void Suspend()
        {
            s_SuspendTime = Time.time;
        }

        /// <summary>World-space direction the player is trying to walk in. Call while moving.</summary>
        public static void ReportMoveIntent(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f)
            {
                ClearMoveIntent();
                return;
            }

            s_MoveIntent = worldDir.normalized;
            s_IntentTime = Time.time;
        }

        /// <summary>Call when the stick/keys are released.</summary>
        public static void ClearMoveIntent()
        {
            s_MoveIntent = Vector3.zero;
            s_IntentTime = float.NegativeInfinity;
        }

        CinemachineOrbitalFollow m_OrbitalFollow;
        Camera m_Camera;

        bool m_Turning;
        float m_YawVelocity;
        float m_TargetYaw;

        // Heading the player is currently holding, and since when.
        float m_CommitYaw;
        float m_CommitSince = float.NegativeInfinity;

        // Constant conversion from axis value to world yaw. Measured once (see LateUpdate).
        float m_YawOffset;
        bool m_YawOffsetLatched;
        float m_LastMeasuredYaw = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Self-bootstrapping like MobileZoomBar: no scene or prefab wiring, so the behaviour
            // can't be lost to an Editor re-import of an asset.
            var go = new GameObject("CameraAutoRotate");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraAutoRotate>();
        }

        void LateUpdate()
        {
            ResolveCamera();
            if (m_OrbitalFollow == null)
            {
                StopTurning();
                return;
            }

            var axis = m_OrbitalFollow.HorizontalAxis;
            bool atRest = !m_Turning && Mathf.Abs(m_YawVelocity) <= k_CoastCutoffDegreesPerSecond;

            MeasureYawOffset(axis.Value, atRest);

            float orbitYaw = axis.Value + m_YawOffset;
            BasisYaw = Mathf.DeltaAngle(0f, orbitYaw - AppliedYaw);

            if (Active && !IsSuspended && HasIntent)
            {
                float desiredYaw = Mathf.Atan2(s_MoveIntent.x, s_MoveIntent.z) * Mathf.Rad2Deg;

                // Restart the commitment clock whenever the heading really changes.
                if (Mathf.Abs(Mathf.DeltaAngle(desiredYaw, m_CommitYaw)) > k_CommitToleranceDegrees)
                {
                    m_CommitSince = Time.time;
                }
                m_CommitYaw = desiredYaw;

                float error = Mathf.Abs(Mathf.DeltaAngle(orbitYaw, desiredYaw));

                if (m_Turning)
                {
                    // Keep tracking the live heading, so easing around a corner is followed. Except
                    // past the cap: if the player turns around mid-swing, finish the swing already
                    // under way rather than flipping to a target on the other side.
                    if (error <= k_MaxErrorDegrees)
                    {
                        m_TargetYaw = desiredYaw;
                    }
                }
                else if (Time.time - m_CommitSince >= k_CommitSeconds &&
                         error >= k_StartDeadzoneDegrees && error <= k_MaxErrorDegrees)
                {
                    m_Turning = true;
                    m_TargetYaw = desiredYaw;
                }
            }
            else if (!Active || IsSuspended)
            {
                // A closing gate abandons the swing. Losing the intent (stick released) does not:
                // an interrupted swing would leave the basis at an arbitrary angle.
                m_Turning = false;
            }

            if (m_Turning && Mathf.Abs(Mathf.DeltaAngle(orbitYaw, m_TargetYaw)) < k_StopDeadzoneDegrees)
            {
                m_Turning = false;
            }

            if (!m_Turning && Mathf.Abs(m_YawVelocity) <= k_CoastCutoffDegreesPerSecond)
            {
                m_YawVelocity = 0f;

                // Everything has come to a stop. If the player isn't moving either, rebase: from
                // here on the basis is simply where the camera is. Never while they're moving —
                // zeroing AppliedYaw under a held stick would swerve them.
                if (!HasIntent && AppliedYaw != 0f)
                {
                    AppliedYaw = 0f;
                    BasisYaw = Mathf.DeltaAngle(0f, orbitYaw);
                }
                return;
            }

            float previousValue = axis.Value;
            float newValue;

            if (m_Turning)
            {
                float error = Mathf.DeltaAngle(orbitYaw, m_TargetYaw);
                newValue = Mathf.SmoothDampAngle(previousValue, previousValue + error, ref m_YawVelocity,
                    k_SmoothTimeSeconds, k_MaxSpeedDegreesPerSecond, Time.deltaTime);
            }
            else
            {
                // Coasting to a stop. Plain friction rather than a zero-error SmoothDamp: the
                // latter is a spring, so it would carry the camera past its current angle and then
                // walk it back — a visible little bounce every time a gate closes mid-swing.
                m_YawVelocity = Mathf.MoveTowards(m_YawVelocity, 0f, k_CoastDecelDegreesPerSecondSq * Time.deltaTime);
                newValue = previousValue + m_YawVelocity * Time.deltaTime;
            }

            axis.Value = axis.Wrap
                ? WrapIntoRange(newValue, axis.Range)
                : Mathf.Clamp(newValue, axis.Range.x, axis.Range.y);

            // Copy-modify-assign: HorizontalAxis is a struct property (same as MobileZoomBar.ApplyZoom).
            m_OrbitalFollow.HorizontalAxis = axis;

            // Bank exactly what was applied — measured after the wrap/clamp, so a rotation the axis
            // range refused to make doesn't get subtracted out of the movement basis. Kept
            // normalized to +-180: only the value mod 360 matters. Note BasisYaw needs no update:
            // the axis and AppliedYaw moved by the same amount, so their difference is unchanged.
            AppliedYaw = Mathf.DeltaAngle(0f, AppliedYaw + Mathf.DeltaAngle(previousValue, axis.Value));
        }

        /// <summary>
        /// Works out the constant offset between the axis value and the camera's world yaw. Latched
        /// after one stable reading and never touched again: the orbital follow is in WorldSpace
        /// binding mode, so the mapping really is constant (and ~0), while the camera transform it is
        /// read from wobbles a little whenever position damping is catching up with the character.
        /// Re-measuring every frame would feed that wobble straight into the movement basis.
        /// </summary>
        void MeasureYawOffset(float axisValue, bool atRest)
        {
            if (m_YawOffsetLatched || !atRest)
            {
                return;
            }

            float measured = MeasuredCameraYaw();
            if (float.IsNaN(measured))
            {
                return;
            }

            // Two agreeing readings in a row, so the camera flying into place at startup can't be
            // mistaken for its resting orientation.
            if (!float.IsNaN(m_LastMeasuredYaw) &&
                Mathf.Abs(Mathf.DeltaAngle(m_LastMeasuredYaw, measured)) < 0.5f)
            {
                m_YawOffset = Mathf.DeltaAngle(axisValue, measured);
                m_YawOffsetLatched = true;
            }

            m_LastMeasuredYaw = measured;
        }

        void StopTurning()
        {
            m_Turning = false;
            m_YawVelocity = 0f;
        }

        /// <summary>Yaw of the rendered camera, or NaN if it looks too close to straight down to tell.</summary>
        float MeasuredCameraYaw()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    return float.NaN;
                }
            }

            Vector3 forward = m_Camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                return float.NaN;
            }

            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        static float WrapIntoRange(float value, Vector2 range)
        {
            float span = range.y - range.x;
            return span > 0f ? range.x + Mathf.Repeat(value - range.x, span) : range.x;
        }

        void ResolveCamera()
        {
            if (m_OrbitalFollow != null)
            {
                return;
            }

            var cameraGO = GameObject.FindGameObjectWithTag(k_CMCameraTag);
            if (cameraGO != null)
            {
                m_OrbitalFollow = cameraGO.GetComponent<CinemachineOrbitalFollow>();
                if (m_OrbitalFollow != null)
                {
                    // Fresh camera (first resolve, or a new scene): re-measure the mapping.
                    m_YawOffsetLatched = false;
                    m_LastMeasuredYaw = float.NaN;
                }
            }
        }
    }
}
