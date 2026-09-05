using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The switch on the character select screen that puts the in-match control walkthrough
    /// (<see cref="WarmupTutorial"/>) back on for the next warm-up.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is needed.</b> The walkthrough runs once per install and Escape retires it
    /// for good — which is right for the player who already knows the controls, and a dead end for
    /// everyone else: somebody who skipped it by reflex, who handed the game to a friend, or who
    /// simply wants it again had no way back. This is that way back.</para>
    ///
    /// <para><b>Why here.</b> Character select is the last screen before a match and the one where
    /// a player is already thinking about which class they are about to learn, so a lesson turned
    /// on here starts in the warm-up seconds later. It is a toggle rather than a one-way button so
    /// a mis-click costs nothing.</para>
    ///
    /// <para>Self-bootstrapping and code-built like the rest of the runtime chrome
    /// (<see cref="HowToPlayWizard"/>, <see cref="ControlsHintPanel"/>), so it needs no edit to the
    /// character select prefab.</para>
    /// </remarks>
    public class TutorialReplayButton : MonoBehaviour
    {
        /// <summary>Overlay level: above the screen it sits on, below any modal.</summary>
        const int k_SortingOrder = 200;

        /// <summary>The one screen this belongs on.</summary>
        const string k_Scene = "CharSelect";

        /// <summary>
        /// Left edge, under the room name box (which ends at -326) and clear of the seat strip
        /// along the bottom.
        /// </summary>
        static readonly Vector2 k_Position = new Vector2(24f, -348f);

        static readonly Vector2 k_Size = new Vector2(330f, UIKit.ControlHeight);

        /// <summary>Height of the caption line under the button.</summary>
        const float k_CaptionHeight = 24f;

        static TutorialReplayButton s_Instance;

        RectTransform m_Host;
        Button m_Button;
        TextMeshProUGUI m_Caption;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(TutorialReplayButton));
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<TutorialReplayButton>();
        }

        void Awake()
        {
            UIKit.Root(gameObject, nameof(TutorialReplayButton), k_SortingOrder);
            Build();

            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply(SceneManager.GetActiveScene());
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(scene);

        void Apply(Scene scene)
        {
            bool here = scene.name == k_Scene;
            m_Host.gameObject.SetActive(here);

            if (here)
            {
                // Re-read rather than trusted: the walkthrough retires itself at the end of a
                // match, so the state this button shows can have changed while it was hidden.
                Refresh();
            }
        }

        void Build()
        {
            m_Host = UIKit.Column(transform, "TutorialToggle", UIKit.Unit, 0f, TextAnchor.UpperLeft);
            m_Host.anchorMin = m_Host.anchorMax = m_Host.pivot = new Vector2(0f, 1f);
            m_Host.anchoredPosition = k_Position;
            m_Host.sizeDelta = new Vector2(k_Size.x, k_Size.y + UIKit.Unit + k_CaptionHeight);

            m_Caption = UIKit.Text(m_Host, string.Empty, UIKit.TextStyle.Caption,
                TextAlignmentOptions.Left);
            UIKit.Flexible(m_Caption.rectTransform, k_CaptionHeight, k_Size.x);
        }

        /// <summary>
        /// Draws the button for the state the walkthrough is actually in. The plate is rebuilt
        /// rather than relabelled: label, icon and role all change together, and building one is a
        /// handful of objects on a click nobody presses twice a second.
        /// </summary>
        void Refresh()
        {
            if (m_Button != null)
            {
                // Taken out of the column before it is destroyed: a child queued for destruction is
                // still a child for the rest of the frame, and the layout would lay out both.
                m_Button.transform.SetParent(null, false);
                Destroy(m_Button.gameObject);
            }

            bool armed = WarmupTutorial.Armed;

            m_Button = UIKit.Button(m_Host,
                armed ? "Tutorial activado" : "Ver tutorial",
                armed ? UIKit.Role.Primary : UIKit.Role.Secondary,
                () => Toggle(!armed),
                armed ? UIIcons.Icon.Check : UIIcons.Icon.Refresh,
                k_Size.x);

            // The button is built last but has to read first: the caption explains it.
            m_Button.transform.SetAsFirstSibling();

            m_Caption.text = armed
                ? "Se muestra al empezar el calentamiento"
                : "Volvé a ver cómo se juega en el calentamiento";
        }

        void Toggle(bool armed)
        {
            WarmupTutorial.SetArmed(armed);
            Refresh();
        }
    }
}
