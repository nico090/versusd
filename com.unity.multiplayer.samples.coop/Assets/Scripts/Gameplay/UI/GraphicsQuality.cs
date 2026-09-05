using System;
using Unity.BossRoom.Utils;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Decides which quality level the game starts on: the one the player picked last, or
    /// <see cref="k_DefaultLevelName"/> on an install that has never picked one.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> Unity's own default is the per-platform entry in
    /// QualitySettings, which on this project lands on a low preset on Android and on whatever the
    /// Editor was last left on elsewhere — so the game could open with shadows off and half-size
    /// textures for no reason the player ever asked for. The default belongs in code, where it is
    /// the same in the Editor and in every build, rather than in a project asset the Editor's cache
    /// can quietly hand back unchanged (the same reason
    /// <see cref="UISettingsCanvas"/> rewires its buttons at runtime).</para>
    ///
    /// <para><b>And why the choice is remembered.</b> <see cref="QualityButton"/> only ever called
    /// <c>QualitySettings.SetQualityLevel</c>, which does not survive a restart, so turning the
    /// quality down was undone by closing the game. It now writes through here.</para>
    /// </remarks>
    public static class GraphicsQuality
    {
        /// <summary>What a fresh install runs at.</summary>
        const string k_DefaultLevelName = "High";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyStoredLevel()
        {
            // The dedicated server renders nothing, and its own platform default (Server: 0) is
            // the right answer for it. Raising it would only cost the host machine work.
            if (Application.isBatchMode)
            {
                return;
            }

            int level = IndexOf(ClientPrefs.GetGraphicsQuality());
            if (level < 0)
            {
                level = IndexOf(k_DefaultLevelName);
            }

            if (level >= 0 && level != QualitySettings.GetQualityLevel())
            {
                QualitySettings.SetQualityLevel(level, true);
            }
        }

        /// <summary>Stores the level the game is on now as the player's choice.</summary>
        public static void Remember()
        {
            var names = QualitySettings.names;
            int level = QualitySettings.GetQualityLevel();
            if (level >= 0 && level < names.Length)
            {
                ClientPrefs.SetGraphicsQuality(names[level]);
            }
        }

        /// <summary>
        /// The index of the level called <paramref name="name"/>, or -1 if there is none.
        /// </summary>
        /// <remarks>
        /// Searched from the end because the project carries two sets of presets whose names
        /// repeat (Low/Medium/High/Ultra, then Low/Medium/High again). The later set is the one in
        /// use, so the last match is the one a player asking for "High" means.
        /// </remarks>
        static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            var names = QualitySettings.names;
            for (int i = names.Length - 1; i >= 0; i--)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
