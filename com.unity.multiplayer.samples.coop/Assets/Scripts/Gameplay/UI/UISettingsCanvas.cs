using System;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Controls the special Canvas that has the settings icon and the settings window.
    /// The window itself is controlled by UISettingsPanel; the button is controlled here.
    /// </summary>
    public class UISettingsCanvas : MonoBehaviour
    {
        [SerializeField]
        private GameObject m_SettingsPanelRoot;

        [SerializeField]
        private GameObject m_QuitPanelRoot;

        void Awake()
        {
            // hide the settings window at startup (this is just to handle the common case where an artist forgets to disable the window in the prefab)
            DisablePanels();
            RewireDeadButtons();
            LiftAboveEverything();
        }

        /// <summary>
        /// Puts this canvas above the other screens, permanently.
        /// </summary>
        /// <remarks>
        /// <para>The prefab ships at sorting order <b>-1</b>, which is under everything the game
        /// draws. It only ever appeared to work because the pause menu lifts it on the way in — and
        /// that lift is useless for the corner buttons, which have to be clickable <i>before</i>
        /// anything opens.</para>
        ///
        /// <para>The main menu is where that showed. <c>LoginUI</c> builds its canvas at order 100
        /// and lays a full-screen <c>UIKit.Scrim</c> over it, and that scrim is a raycast target
        /// carrying a Button precisely so it swallows clicks. At -1 the gear and the X were drawn
        /// underneath it and never saw a pointer — while CharSelect, which has no login screen over
        /// it, worked fine. Same buttons, same code, different neighbour.</para>
        ///
        /// <para>Above the pause menu's 300 as well. That does not fight it: PauseMenuUI hides
        /// these two corner icons for the duration of a match, so the only place they draw over
        /// anything is a screen where they are meant to be reachable.</para>
        /// </remarks>
        void LiftAboveEverything()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.overrideSorting = true;
            canvas.sortingOrder = k_StandaloneSortingOrder;
        }

        /// <summary>
        /// Re-binds this prefab's own buttons in code, because their serialized UnityEvents no
        /// longer resolve.
        /// </summary>
        /// <remarks>
        /// The gear icon is wired to <c>BossRoom.Visual.UISettingsCanvas, BossRoom.Client</c> — a
        /// namespace and an assembly that both stopped existing at the Mirror port — so it silently
        /// does nothing. The close button is worse in practice: its one surviving binding is a bare
        /// <c>GameObject.SetActive</c>, which leaves no way back out of the window if it is pointed
        /// at the wrong object. Rather than repair the events in the asset (where Unity's cache can
        /// quietly revert an on-disk edit before a build), the listeners are simply added here at
        /// runtime, where they are part of the assembly and cannot drift.
        /// </remarks>
        void RewireDeadButtons()
        {
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                // Told apart by the component it carries rather than by its name: the quality
                // preset button inside the window is ALSO called "Settings Button", so matching on
                // the name alone would give it the corner gear's job and lose the only control
                // the settings window actually has.
                if (button.TryGetComponent(out QualityButton quality))
                {
                    Rebind(button, quality.SetQualitySettings);
                    continue;
                }

                switch (button.name)
                {
                    case "Settings Button":
                        Rebind(button, () => ShowSettings(k_StandaloneSortingOrder));
                        break;
                    case "Close Button":
                    case "Cancel Button":
                        Rebind(button, CloseSettings);
                        break;
                    case "Quit Button":
                        Rebind(button, OnClickQuitButton);
                        break;
                    case "Confirm Button":
                        Rebind(button, ReturnToMainMenu);
                        break;
                }
            }
        }

        /// <summary>
        /// Replaces everything a button does with <paramref name="action"/>.
        /// </summary>
        /// <remarks>
        /// The whole event is swapped for a new one rather than cleared with
        /// <c>RemoveAllListeners</c>, which only drops the ones added in code — the persistent
        /// entries wired in the Inspector survive it. That matters here because those entries are
        /// precisely the broken ones: they name types the Mirror port removed, and a UnityEvent
        /// that hits an unresolvable persistent call can stop there without ever reaching the
        /// listener added behind it. Handing the button a fresh event leaves nothing to trip over.
        /// </remarks>
        static void Rebind(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(action);
        }

        /// <summary>
        /// Leaves the session and goes back to the main menu — what the quit panel's Confirm
        /// button is for.
        /// </summary>
        /// <remarks>
        /// <para>The prefab points that button at <c>UIQuitPanel.Quit</c> in
        /// <c>Unity.Multiplayer.Samples.BossRoom.Client</c>, an assembly the Mirror port replaced,
        /// so the event resolves to nothing. Rather than re-point it at the class's new home, the
        /// shutdown is called here directly: <see cref="UIQuitPanel"/> reaches the
        /// <c>ConnectionManager</c> through a VContainer <c>[Inject]</c> field, and if the scene's
        /// scope does not inject it — which is the second thing the port can break, and is
        /// invisible until someone presses the button — the method throws on a null instead of
        /// leaving. Looking the manager up is one dependency instead of two.</para>
        /// </remarks>
        void ReturnToMainMenu()
        {
            CloseSettings();

            // In a match the confirm button means "leave"; in the main menu there is nothing to
            // leave and it means "quit the game". The prefab expresses that with a QuitMode field
            // on UIQuitPanel, which is only reachable through the VContainer injection that may
            // not have run — so the same question is answered here from the thing that actually
            // decides it: whether this client is in a session at all.
            bool inSession = NetworkClient.active || NetworkServer.active;

            if (!inSession)
            {
                QuitApplication();
                return;
            }

            var connectionManager = FindAnyObjectByType<ConnectionManager>();
            if (connectionManager == null)
            {
                Debug.LogWarning("[Settings] No ConnectionManager in this scene — quitting instead.");
                QuitApplication();
                return;
            }

            connectionManager.RequestShutdown();
        }

        /// <summary>
        /// Ends the application, the same way <c>ApplicationController</c> does.
        /// </summary>
        /// <remarks>
        /// Duplicated rather than routed through the QuitApplicationMessage channel on purpose:
        /// that subscriber is these two lines and nothing else, so publishing would buy no cleanup
        /// and would add a dependency on an injected publisher — which is the failure this whole
        /// rewire exists to route around.
        /// </remarks>
        static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Order used when the window is opened by its own gear icon rather than by the pause menu,
        /// which passes one of its own. High enough to clear the HUD; the shipped value is -1.
        /// </summary>
        const int k_StandaloneSortingOrder = 310;

        /// <summary>
        /// Escape closes the window wherever it was opened from.
        /// </summary>
        /// <remarks>
        /// It lives here rather than in <c>PauseMenuUI</c> because that component gives up early
        /// outside a gameplay scene, and this window is reachable from the main menu too — where
        /// its own close button was the only way out and that button was dead.
        /// </remarks>
        void Update()
        {
            if (!IsSettingsOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseSettings();
            }
        }

        void DisablePanels()
        {
            if (m_SettingsPanelRoot != null) m_SettingsPanelRoot.SetActive(false);
            if (m_QuitPanelRoot != null) m_QuitPanelRoot.SetActive(false);
        }

        /// <summary>True while the settings window is up.</summary>
        public bool IsSettingsOpen => m_SettingsPanelRoot != null && m_SettingsPanelRoot.activeInHierarchy;

        /// <summary>
        /// Shows the settings window, from a caller that wants it open rather than toggled, and
        /// makes sure it can actually be seen and clicked.
        /// </summary>
        /// <remarks>
        /// <para>Three separate things had to be true for this window to work, and none of them
        /// were. Its own GameObject can be inactive, in which case activating the panel inside it
        /// changes nothing on screen. This prefab's Canvas sits at <b>sorting order -1</b> — under
        /// the HUD, and far under the pause menu's 300 — so even when active it drew behind the
        /// match. And the prefab's own button is wired to
        /// <c>BossRoom.Visual.UISettingsCanvas, BossRoom.Client</c>, a namespace and assembly that
        /// stopped existing at the Mirror port, so that UnityEvent no longer resolves to anything
        /// and the corner icon does nothing at all.</para>
        ///
        /// <para><paramref name="sortingOrder"/> is what the caller must pass to lift the window
        /// above whatever opened it.</para>
        /// </remarks>
        public void ShowSettings(int sortingOrder)
        {
            gameObject.SetActive(true);

            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
            }

            if (m_QuitPanelRoot != null) m_QuitPanelRoot.SetActive(false);
            if (m_SettingsPanelRoot != null)
            {
                Centre(m_SettingsPanelRoot);
                m_SettingsPanelRoot.SetActive(true);
            }

            Debug.Log("[Settings] Settings window opened.");
        }

        /// <summary>
        /// Moves a panel to the middle of the screen.
        /// </summary>
        /// <remarks>
        /// Both panels are anchored to the top-right corner at (-164, -160) with a size of
        /// 500x420 — which puts 86 pixels of the panel past the right edge of the screen and 50
        /// above the top. It opens, but it opens clipped into a corner behind the buttons that
        /// summoned it, which is indistinguishable from not opening at all on a busy screen. They
        /// are a modal window and a confirmation prompt; the middle is where both belong.
        /// </remarks>
        static void Centre(GameObject panel)
        {
            if (panel.transform is not RectTransform rect)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Closes both windows, leaving the canvas itself alone.</summary>
        public void CloseSettings()
        {
            DisablePanels();
        }

        /// <summary>
        /// Called directly by the settings button in the UI prefab
        /// </summary>
        public void OnClickSettingsButton()
        {
            m_SettingsPanelRoot.SetActive(!m_SettingsPanelRoot.activeSelf);
            m_QuitPanelRoot.SetActive(false);
        }

        /// <summary>
        /// Called directly by the quit button in the UI prefab
        /// </summary>
        public void OnClickQuitButton()
        {
            if (m_QuitPanelRoot == null)
            {
                Debug.LogWarning("[Settings] No quit panel wired on this canvas.");
                return;
            }

            bool opening = !m_QuitPanelRoot.activeSelf;

            if (opening)
            {
                // The same lift ShowSettings does. Without it the prompt opens at the canvas's
                // shipped sorting order of -1, under whatever screen is asking the question.
                gameObject.SetActive(true);

                var canvas = GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = k_StandaloneSortingOrder;
                }

                Centre(m_QuitPanelRoot);
            }

            m_QuitPanelRoot.SetActive(opening);
            if (m_SettingsPanelRoot != null)
            {
                m_SettingsPanelRoot.SetActive(false);
            }

            Debug.Log($"[Settings] Quit prompt {(opening ? "opened" : "closed")}.");
        }

    }
}
