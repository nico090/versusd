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
        const string k_ControlsHintExpandedKey = "ControlsHintExpanded";
        const string k_TutorialSeenKey = "TutorialSeen";
        const string k_WarmupTutorialSeenKey = "WarmupTutorialSeen";
        const string k_GraphicsQualityKey = "GraphicsQuality";

        const float k_DefaultMasterVolume = 0.5f;
        const float k_DefaultMusicVolume = 0.8f;

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


        /// <summary>Whether the bottom-left controls card is unfolded (it starts unfolded, then the
        /// player folds it away with H once they know the bindings).</summary>
        /// <summary>
        /// Whether the player has been shown the how-to-play wizard.
        /// </summary>
        /// <remarks>
        /// Per install rather than per account: the wizard teaches the controls and the rules, and
        /// those do not change when somebody signs in as someone else. Tying it to a profile would
        /// mean a second account on the same machine sitting through it again.
        /// </remarks>
        public static bool GetTutorialSeen() => PlayerPrefs.GetInt(k_TutorialSeenKey, 0) != 0;

        public static void SetTutorialSeen(bool seen)
        {
            PlayerPrefs.SetInt(k_TutorialSeenKey, seen ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Whether the in-match control walkthrough has already run to its end on this install.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="GetTutorialSeen"/>, which belongs to the rules wizard in the
        /// menu. They teach different things, are dismissed in different places, and a player who
        /// read the rules has still never been shown where the attack button is.
        /// </remarks>
        public static bool GetWarmupTutorialSeen() => PlayerPrefs.GetInt(k_WarmupTutorialSeenKey, 0) != 0;

        public static void SetWarmupTutorialSeen(bool seen)
        {
            PlayerPrefs.SetInt(k_WarmupTutorialSeenKey, seen ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The quality level the player last chose, by name, or an empty string if they never
        /// touched it.
        /// </summary>
        /// <remarks>
        /// Stored by name and not by index on purpose: the project ships two sets of levels with
        /// repeated names, and an index written today would point at a different preset the moment
        /// anybody adds or reorders one. A name that no longer exists simply falls back to the
        /// default, which is the failure we want.
        /// </remarks>
        public static string GetGraphicsQuality() => PlayerPrefs.GetString(k_GraphicsQualityKey, string.Empty);

        public static void SetGraphicsQuality(string levelName)
        {
            PlayerPrefs.SetString(k_GraphicsQualityKey, levelName);
            PlayerPrefs.Save();
        }

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
