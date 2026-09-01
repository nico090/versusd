using Unity.Cinemachine;
using UnityEngine;

namespace Unity.BossRoom.CameraUtils
{
    /// <summary>
    /// Publishes the gameplay camera's orbit yaw, in world degrees, for building a camera-relative
    /// movement basis. <c>ClientInputSender.CameraRelativeMove</c> is the only caller.
    ///
    /// <para>The obvious alternative — reading <c>Camera.main.transform</c> — is wrong here, and
    /// that is the whole reason this exists. The orbital follow damps the camera's <i>position</i>,
    /// so while the camera is being swung the transform lags the orbit by a good fraction of a
    /// second. A movement basis built from a lagging yaw drifts and then snaps back under the
    /// player's thumb mid-walk, which reads as the controls sliding around. The orbit axis has no
    /// such lag: it is the value the player is driving directly.</para>
    ///
    /// <para>This is what is left of <c>CameraAutoRotate</c>, which used to swing the camera to
    /// follow the walk and exposed the same yaw as <c>BasisYaw</c>. That feature is gone — it had to
    /// freeze the movement basis while it turned, which is what made walking sideways come out as
    /// walking forward, and both control schemes now have a manual camera instead
    /// (<c>MouseCameraOrbit</c> on desktop, <c>TouchCameraOrbit</c> on touch). The yaw bookkeeping
    /// survived the deletion because the basis still needs it.</para>
    /// </summary>
    public class CameraOrbitYaw : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>
        /// World yaw of the camera's orbit, in degrees. NaN until the camera has been found, in
        /// which case callers should fall back to the camera transform.
        /// </summary>
        public static float Yaw { get; private set; } = float.NaN;

        /// <summary>
        /// Converts a 2D input — a stick, a joystick, a drag out of a skill button — into a
        /// world-space direction on the ground plane, in the camera's basis, so "up" always means
        /// "away from the camera". Returns zero for a degenerate input or before a camera exists.
        ///
        /// <para>Lives here rather than in each caller because the same basis has to serve movement
        /// and aiming: if the two ever derived "forward" differently, walking and shooting would
        /// disagree about which way the stick was pointing.</para>
        /// </summary>
        public static Vector3 ToWorldDirection(Vector2 input)
        {
            Vector3 forward;

            float yaw = Yaw;
            if (!float.IsNaN(yaw))
            {
                forward = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            }
            else
            {
                // No camera resolved yet: fall back to the rendered one.
                var camera = Camera.main;
                if (camera == null)
                {
                    return Vector3.zero;
                }

                forward = camera.transform.forward;
                forward.y = 0f;
                // If it looks nearly straight down, use its "up" projected on the ground so the
                // direction stays stable.
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = camera.transform.up;
                    forward.y = 0f;
                }

                if (forward.sqrMagnitude < 0.001f)
                {
                    return Vector3.zero;
                }

                forward.Normalize();
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 result = forward * input.y + right * input.x;
            return result.sqrMagnitude > 0.000001f ? result.normalized : Vector3.zero;
        }

        CinemachineOrbitalFollow m_OrbitalFollow;
        Camera m_Camera;

        // Constant conversion from axis value to world yaw. Measured once, see MeasureYawOffset.
        float m_YawOffset;
        bool m_YawOffsetLatched;
        float m_LastMeasuredYaw = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Self-bootstrapping like the input widgets: no scene or prefab wiring, so the behaviour
            // can't be lost to an Editor re-import of an asset.
            var go = new GameObject(nameof(CameraOrbitYaw));
            DontDestroyOnLoad(go);
            go.AddComponent<CameraOrbitYaw>();
        }

        void LateUpdate()
        {
            ResolveCamera();
            if (m_OrbitalFollow == null)
            {
                Yaw = float.NaN;
                return;
            }

            float axisValue = m_OrbitalFollow.HorizontalAxis.Value;
            MeasureYawOffset(axisValue);
            Yaw = Mathf.DeltaAngle(0f, axisValue + m_YawOffset);
        }

        /// <summary>
        /// Works out the constant offset between the axis value and the camera's world yaw. Latched
        /// after one stable reading and never touched again: the orbital follow is in WorldSpace
        /// binding mode, so the mapping really is constant (and ~0), while the camera transform it is
        /// read from wobbles a little whenever position damping is catching up with the character.
        /// Re-measuring every frame would feed that wobble straight into the movement basis.
        /// </summary>
        void MeasureYawOffset(float axisValue)
        {
            if (m_YawOffsetLatched)
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
