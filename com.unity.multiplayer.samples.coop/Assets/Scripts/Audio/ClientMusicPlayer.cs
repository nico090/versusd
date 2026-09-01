using UnityEngine;

namespace Unity.BossRoom.Audio
{
    /// <summary>
    /// Music player that handles start of boss battle, victory and restart
    /// </summary>
    /// <remarks>
    /// <para><b>The game's own track.</b> Menus and gameplay both run on
    /// <see cref="k_LoopResourcePath"/>, loaded from Resources at startup and substituted for the
    /// theme and boss clips the sample shipped with. It is loaded rather than wired into the
    /// prefab because prefab edits do not reliably reach a build from this project — the Editor
    /// serves its own cached copy of an asset — and because one clip in one place is easier to
    /// swap than three serialized references.</para>
    ///
    /// <para>Theme and battle being the <i>same</i> clip is what makes it a continuous loop:
    /// <see cref="PlayTrack"/> leaves a track alone when the same clip is already playing, so the
    /// drums carry across menu, character select and the match without ever restarting.</para>
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    public class ClientMusicPlayer : MonoBehaviour
    {
        /// <summary>
        /// The loop, under a Resources folder, without its extension. Swapping the game's music is
        /// dropping a file next to this one and changing this string.
        /// </summary>
        const string k_LoopResourcePath = "Music/odin_drums_loop";

        [SerializeField]
        private AudioClip m_ThemeMusic;

        [SerializeField]
        private AudioClip m_BossMusic;

        [SerializeField]
        private AudioClip m_VictoryMusic;

        [SerializeField]
        private AudioSource m_source;

        /// <summary>The clip loaded from Resources, or null if it was not found.</summary>
        private AudioClip m_GameLoop;

        /// <summary>
        /// static accessor for ClientMusicPlayer
        /// </summary>
        public static ClientMusicPlayer Instance { get; private set; }

        public void PlayThemeMusic(bool restart)
        {
            PlayTrack(m_ThemeMusic, true, restart);
        }

        public void PlayBossMusic()
        {
            // this can be caled multiple times - play with restart = false
            PlayTrack(m_BossMusic, true, false);
        }

        public void PlayVictoryMusic()
        {
            // This fires when the boss dies — which in this game is a scoring event, not the end
            // of the match: the clock keeps running. A one-shot victory sting would therefore cut
            // the music and leave the arena silent for the rest of the round. When the victory
            // clip is the game's own loop (the default), it is played as one; and since it is
            // already playing by then, PlayTrack leaves it running untouched.
            PlayTrack(m_VictoryMusic, m_VictoryMusic == m_GameLoop, false);
        }

        private void PlayTrack(AudioClip clip, bool looping, bool restart)
        {
            if (m_source.isPlaying)
            {
                // if we dont want to restart the clip, do nothing if it is playing
                if (!restart && m_source.clip == clip) { return; }
                m_source.Stop();
            }
            m_source.clip = clip;
            m_source.loop = looping;
            m_source.time = 0;
            m_source.Play();
        }

        private void Awake()
        {
            m_source = GetComponent<AudioSource>();

            if (Instance != null)
            {
                throw new System.Exception("Multiple ClientMuscPlayers!");
            }
            DontDestroyOnLoad(gameObject);
            Instance = this;

            LoadGameLoop();
        }

        /// <summary>
        /// Points every track at this game's own loop. The serialized clips are left as the
        /// fallback: if the file is ever missing, the sample's music still plays rather than the
        /// game running silent.
        /// </summary>
        private void LoadGameLoop()
        {
            var loop = Resources.Load<AudioClip>(k_LoopResourcePath);

            if (loop == null)
            {
                Debug.LogWarning($"[Music] No encontré '{k_LoopResourcePath}' en Resources; " +
                                 "se usa la música original del sample.");
                return;
            }

            m_GameLoop = loop;
            m_ThemeMusic = loop;
            m_BossMusic = loop;
            m_VictoryMusic = loop;
        }
    }
}
