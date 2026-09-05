using System;
using Unity.BossRoom.ConnectionManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The in-game menu: a pause button in the corner of the HUD, the panel it opens (resume,
    /// settings, leave the match), and the Escape key that does the same thing. Self-bootstrapping
    /// — nothing to wire in a scene.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it was missing.</b> Escape did nothing anywhere in this project, and the only
    /// way out of a match was a 100×100 gear parked in the top-right corner by the original
    /// sample — directly on top of where this game draws its scoreboard. On a phone, where there
    /// is no Escape key at all, two overlapping icons were the entire pause UI.</para>
    ///
    /// <para><b>What it takes over.</b> In a match this hides the sample's two corner icons and
    /// puts one pause button in their place. The settings <i>window</i> they used to open is
    /// still the right window, so the menu opens that one rather than growing its own volume
    /// sliders — see <see cref="OpenSettings"/>.</para>
    ///
    /// <para><b>Why the match keeps running.</b> This is a multiplayer game on a dedicated
    /// server: there is nothing to pause. The menu is a modal sheet over a live match, so it
    /// blocks clicks (the scrim is what does that) but deliberately does not touch
    /// <see cref="Time.timeScale"/>.</para>
    /// </remarks>
    public class PauseMenuUI : MonoBehaviour
    {
        /// <summary>Scenes with a match in them. Everywhere else the sample's corner icons are fine.</summary>
        static readonly string[] k_GameplayScenes = { "BossRoom" };

        /// <summary>The sample's corner icons, which this replaces while a match is running.</summary>
        static readonly string[] k_ReplacedButtons = { "Settings Button", "Quit Button" };

        const float k_CornerButtonSize = 60f;
        const float k_CornerMargin = 20f;

        /// <summary>This menu's own canvas order. Above the HUD and the restyled menus.</summary>
        const int k_SortingOrder = 300;

        /// <summary>Where the settings window is lifted to while it is open, i.e. above us.</summary>
        const int k_SettingsSortingOrder = k_SortingOrder + 10;

        GameObject m_Panel;
        Canvas m_Canvas;
        bool m_InGameplayScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // The dedicated server runs this same build headless and draws no UI.
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }

            var host = new GameObject(nameof(PauseMenuUI));
            DontDestroyOnLoad(host);
            host.AddComponent<PauseMenuUI>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_InGameplayScene = Array.IndexOf(k_GameplayScenes, scene.name) >= 0;

            // The canvas lives on this object, which survives the load; its contents refer to the
            // scene that has just gone away, so they are rebuilt on demand.
            if (m_Canvas != null)
            {
                Destroy(m_Canvas.gameObject);
                m_Canvas = null;
                m_Panel = null;
            }

            if (m_InGameplayScene)
            {
                BuildChrome();
            }
        }

        void Update()
        {
            if (!m_InGameplayScene)
            {
                return;
            }

            var keyboard = Keyboard.current;
            bool escaped = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            var gamepad = Gamepad.current;
            bool started = gamepad != null && gamepad.startButton.wasPressedThisFrame;

            if (!escaped && !started)
            {
                return;
            }

            // Settings first: it opens on top of the pause menu, so the same key that dismisses it
            // has to put the player back where they came from instead of toggling the layer
            // underneath. Without this the window could be opened and never closed — the prefab's
            // own close button is wired to a type that no longer exists.
            var settingsCanvas = FindAnyObjectByType<UISettingsCanvas>(FindObjectsInactive.Include);
            if (settingsCanvas != null && settingsCanvas.IsSettingsOpen)
            {
                settingsCanvas.CloseSettings();
                Open();
                return;
            }

            // Then the warm-up walkthrough, if it is up: Escape there means "I know the controls",
            // and spending the same press on opening this menu over the match would be the wrong
            // answer to it. Skipped while the panel is already open — that is what the key closes
            // then — and TryDismiss returns false whenever nothing is showing, so on every other
            // press this costs one comparison and falls straight through.
            if ((m_Panel == null || !m_Panel.activeSelf) && WarmupTutorial.TryDismiss())
            {
                return;
            }

            Toggle();
        }

        // ── Chrome ────────────────────────────────────────────────────────────────────────────

        /// <summary>Builds the corner button and the (initially hidden) panel behind it.</summary>
        void BuildChrome()
        {
            var host = new GameObject("PauseMenuCanvas");
            host.transform.SetParent(transform, false);

            // Above the HUD and the restyled menus, below nothing: this is the modal layer.
            m_Canvas = UIKit.Root(host, "PauseMenuCanvas", k_SortingOrder);

            BuildCornerButton(host.transform);
            BuildPanel(host.transform);

            HideSampleCornerButtons();
        }

        void BuildCornerButton(Transform parent)
        {
            var anchor = UIKit.NewRect(parent, "PauseCorner");
            anchor.anchorMin = anchor.anchorMax = new Vector2(1f, 1f);
            anchor.pivot = new Vector2(1f, 1f);
            anchor.anchoredPosition = new Vector2(-k_CornerMargin, -k_CornerMargin);
            anchor.sizeDelta = new Vector2(k_CornerButtonSize, k_CornerButtonSize);

            UIKit.IconButton(anchor, UIIcons.Icon.Pause, UIKit.Role.Secondary, Open, k_CornerButtonSize);
        }

        void BuildPanel(Transform parent)
        {
            m_Panel = UIKit.Screen(parent, "PausePanel").gameObject;

            // Clicking the darkened match behind the card closes the menu, which is what everyone
            // expects a modal sheet to do.
            var scrim = UIKit.Scrim(m_Panel.transform);
            scrim.GetComponent<Button>().onClick.AddListener(Close);

            var card = UIKit.Card(m_Panel.transform, "Card", new Vector2(560f, 0f), UIKit.Unit * 4f, UIKit.Unit * 2f);

            UIKit.Text(card, "Pausa", UIKit.TextStyle.Title);
            UIKit.Divider(card);
            UIKit.Spacer(card, UIKit.Unit);

            UIKit.Button(card, "Reanudar", UIKit.Role.Primary, Close, UIIcons.Icon.Play);
            UIKit.Button(card, "Ajustes", UIKit.Role.Secondary, OpenSettings, UIIcons.Icon.Gear);
            UIKit.Button(card, "Abandonar partida", UIKit.Role.Danger, LeaveMatch, UIIcons.Icon.Exit);

            UIKit.Spacer(card, UIKit.Unit * 0.5f);
            UIKit.Text(card, "La partida sigue corriendo mientras este menú está abierto.",
                UIKit.TextStyle.Caption);

            m_Panel.SetActive(false);
        }

        /// <summary>
        /// Switches off the sample's gear and exit icons for the duration of the match. They are
        /// only hidden, never destroyed — the settings window hanging off the same canvas is still
        /// wanted, and the icons come back with the scene in every other part of the game.
        /// </summary>
        void HideSampleCornerButtons()
        {
            var settingsCanvas = FindAnyObjectByType<UISettingsCanvas>(FindObjectsInactive.Include);
            if (settingsCanvas == null)
            {
                return;
            }

            foreach (Transform child in settingsCanvas.transform)
            {
                if (Array.IndexOf(k_ReplacedButtons, child.name) >= 0)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        // ── Actions ───────────────────────────────────────────────────────────────────────────

        /// <summary>Opens the menu.</summary>
        public void Open()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(true);
            }
        }

        /// <summary>Closes the menu.</summary>
        public void Close()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(false);
            }
        }

        /// <summary>Closes the menu if it is open, opens it if it is not.</summary>
        public void Toggle()
        {
            if (m_Panel == null)
            {
                return;
            }

            m_Panel.SetActive(!m_Panel.activeSelf);
        }

        /// <summary>
        /// Hands over to the settings window the rest of the game already uses, rather than
        /// growing a second set of volume sliders that would drift out of step with it.
        /// </summary>
        void OpenSettings()
        {
            var settingsCanvas = FindAnyObjectByType<UISettingsCanvas>(FindObjectsInactive.Include);
            if (settingsCanvas == null)
            {
                Debug.LogWarning("[PauseMenu] No settings canvas in this scene.");
                return;
            }

            // Above this menu's own canvas, not merely visible: at the prefab's shipped -1 the
            // window renders under the HUD. The pause menu closes behind it so the scrim does not
            // eat the clicks meant for the sliders; Escape brings it back.
            Close();
            settingsCanvas.ShowSettings(k_SettingsSortingOrder);
        }

        /// <summary>
        /// Leaves the match. Shutting the connection down is what sends this client back to the
        /// main menu; the connection state machine owns the rest.
        /// </summary>
        void LeaveMatch()
        {
            Close();

            var connectionManager = FindAnyObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                Debug.LogWarning("[PauseMenu] No ConnectionManager to shut down.");
                return;
            }

            connectionManager.RequestShutdown();
        }
    }
}
