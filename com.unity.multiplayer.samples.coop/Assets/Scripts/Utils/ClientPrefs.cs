using UnityEngine;

namespace Unity.BossRoom.Utils
{
    /// <summary>
    /// Singleton class which saves/loads local-client settings.
    /// (This is just a wrapper around the PlayerPrefs system,
    /// so that all the calls are in the same place.)
    /// </summary>
    public static class ClientPrefs
    {
        const string k_MasterVolumeKey = "MasterVolume";
        const string k_MusicVolumeKey = "MusicVolume";
        const string k_ClientGUIDKey = "client_guid";
        const string k_AvailableProfilesKey = "AvailableProfiles";
        const string k_CameraAutoRotateKey = "CameraAutoRotate";
        const string k_ControlsHintExpandedKey = "ControlsHintExpanded";

        const float k_DefaultMasterVolume = 0.5f;
        const float k_DefaultMusicVolume = 0.8f;
        // On by default. Playtesting on 2026-07-25 had defaulted this off: a camera that follows the
        // walk makes the controls drift away from the screen while it turns, which was worse than
        // the fixed camera it replaced. What changed is that the preference no longer applies to
        // keyboard+mouse at all — there the camera is fixed and the player swings it themselves with
        // a middle-mouse drag (MouseCameraOrbit). All that's left under this preference is touch and
        // gamepad, which have no spare input for a manual camera, so the drift is the lesser evil
        // there. The on-screen toggle still turns it off for anyone who disagrees.
        const bool k_DefaultCameraAutoRotate = true;

        public static float GetMasterVolume()
        {
            return PlayerPrefs.GetFloat(k_MasterVolumeKey, k_DefaultMasterVolume);
        }

        public static void SetMasterVolume(float volume)
        {
            PlayerPrefs.SetFloat(k_MasterVolumeKey, volume);
        }

        public static float GetMusicVolume()
        {
            return PlayerPrefs.GetFloat(k_MusicVolumeKey, k_DefaultMusicVolume);
        }

        public static void SetMusicVolume(float volume)
        {
            PlayerPrefs.SetFloat(k_MusicVolumeKey, volume);
        }

        /// <summary>
        /// Either loads a Guid string from Unity preferences, or creates one and checkpoints it, then returns it.
        /// </summary>
        /// <returns>The Guid that uniquely identifies this client install, in string form. </returns>
        public static string GetGuid()
        {
            if (PlayerPrefs.HasKey(k_ClientGUIDKey))
            {
                return PlayerPrefs.GetString(k_ClientGUIDKey);
            }

            var guid = System.Guid.NewGuid();
            var guidString = guid.ToString();

            PlayerPrefs.SetString(k_ClientGUIDKey, guidString);
            return guidString;
        }

        /// <summary>Whether the camera swings around to follow the direction of travel.</summary>
        public static bool GetCameraAutoRotate()
        {
            return PlayerPrefs.GetInt(k_CameraAutoRotateKey, k_DefaultCameraAutoRotate ? 1 : 0) != 0;
        }

        public static void SetCameraAutoRotate(bool autoRotate)
        {
            PlayerPrefs.SetInt(k_CameraAutoRotateKey, autoRotate ? 1 : 0);
        }

        /// <summary>Whether the bottom-left controls card is unfolded (it starts unfolded, then the
        /// player folds it away with H once they know the bindings).</summary>
        public static bool GetControlsHintExpanded()
        {
            return PlayerPrefs.GetInt(k_ControlsHintExpandedKey, 1) != 0;
        }

        public static void SetControlsHintExpanded(bool expanded)
        {
            PlayerPrefs.SetInt(k_ControlsHintExpandedKey, expanded ? 1 : 0);
        }

        public static string GetAvailableProfiles()
        {
            return PlayerPrefs.GetString(k_AvailableProfilesKey, "");
        }

        public static void SetAvailableProfiles(string availableProfiles)
        {
            PlayerPrefs.SetString(k_AvailableProfilesKey, availableProfiles);
        }

    }
}
