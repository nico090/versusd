using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.BossRoom.CameraUtils
{
    public class CameraController : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>
        /// Where the camera starts on its vertical orbit axis: 0 is the lowest, closest view and 1
        /// the highest, furthest one. Below the middle of the range on purpose — the rig's centre
        /// ring sits about 25 units back, which is further than a fight reads comfortably at.
        /// </summary>
        /// <remarks>Shared with the touch zoom bar so its handle starts where the camera does.</remarks>
        public const float DefaultZoom = 0.38f;

        // The far end of the zoom, as the top orbit ring's horizontal radius and height above the
        // character. Authored at height 35 (about 38 units away), far enough that hero and foes
        // became specks and picking a target was guesswork; pulled in to roughly 30 units, which
        // still shows the surrounding fight.
        //
        // Applied here rather than on the camera prefab for the same reason the balance numbers
        // live in code: an asset edited on disk can be quietly replaced by the Editor's cached
        // copy when a build is made, and this one has to survive that.
        const float k_FarOrbitRadius = 15f;
        const float k_FarOrbitHeight = 26f;

        void Start()
        {
            AttachCamera();
        }

        void AttachCamera()
        {
            var cinemachineCameraGameObject = GameObject.FindGameObjectWithTag(k_CMCameraTag);
            Assert.IsNotNull(cinemachineCameraGameObject);

            var cinemachineCamera = cinemachineCameraGameObject.GetComponent<CinemachineCamera>();
            Assert.IsNotNull(cinemachineCamera, "CameraController.AttachCamera: Couldn't find gameplay CinemachineCamera");

            if (cinemachineCamera != null)
            {
                // camera body / aim
                cinemachineCamera.Follow = transform;
                cinemachineCamera.LookAt = transform;
            }

            var cinemachineOrbitalFollow = cinemachineCameraGameObject.GetComponent<CinemachineOrbitalFollow>();
            Assert.IsNotNull(cinemachineOrbitalFollow, "CameraController.AttachCamera: Couldn't find gameplay CinemachineOrbitalFollow");

            if (cinemachineOrbitalFollow != null)
            {
                // Bring the far end of the zoom in before setting the starting position, so the
                // first frame is already framed the way the rest of the session will be.
                var orbits = cinemachineOrbitalFollow.Orbits;
                orbits.Top.Radius = k_FarOrbitRadius;
                orbits.Top.Height = k_FarOrbitHeight;
                cinemachineOrbitalFollow.Orbits = orbits;

                // default rotation / zoom
                cinemachineOrbitalFollow.HorizontalAxis.Value = 40f;
                cinemachineOrbitalFollow.VerticalAxis.Value = DefaultZoom;
            }
        }
    }
}
