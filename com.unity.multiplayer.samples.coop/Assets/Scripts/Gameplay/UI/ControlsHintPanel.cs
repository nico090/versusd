using Mirror;
using Unity.BossRoom.CameraUtils;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Small "how do I play this" card pinned to the bottom-left corner while a match is running.
    /// Desktop only: it lists the keyboard+mouse bindings, which is the half of the control scheme
    /// nothing on screen already shows — touch players have the joystick, the zoom bar and the
    /// auto-rotate button in front of them, and a list of key names would only be in their way.
    ///
    /// <b>H</b> folds it down to a single line and back, and the choice is remembered between
    /// sessions, so it can teach the camera drag once and then get out of the way.
    ///
    /// Self-bootstrapping and code-built like the input widgets it documents
    /// (<see cref="UserInput.MouseCameraOrbit"/>, <see cref="UserInput.CameraAutoRotateToggle"/>):
    /// no scene or prefab wiring to lose to an Editor re-import. It carries no GraphicRaycaster
    /// either, so it never swallows a click meant for the world underneath it.
    /// </summary>
    public class ControlsHintPanel : MonoBehaviour
    {
        // Kept in the same order a new player needs them: move first, then look around, then fight.
        const string k_ExpandedText =
            "<b>Controles</b>   <color=#FFFFFF80>(H para ocultar)</color>\n" +
            "W A S D  ·  Mover\n" +
            "Rueda (mantener) + mouse  ·  Girar cámara\n" +
            "Rueda (girar)  ·  Zoom\n" +
            "Click izq.  ·  Elegir objetivo\n" +
            "Click der.  ·  Poder\n" +
            "1  2  3  ·  Habilidades";

        const string k_CollapsedText = "<color=#FFFFFFB0><b>H</b>  ·  Controles</color>";

        const float k_MarginPixels = 16f;
        const float k_PaddingPixels = 10f;

        RectTransform m_Background;
        Text m_Text;

        bool m_Expanded;
        // Last state the widgets were built for, so Update only touches them when something moved.
        bool m_BuiltExpanded;
        int m_BuiltScreenHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isMobilePlatform)
            {
                return;
            }

            var go = new GameObject(nameof(ControlsHintPanel));
            DontDestroyOnLoad(go);
            go.AddComponent<ControlsHintPanel>();
        }

        void Awake()
        {
            m_Expanded = ClientPrefs.GetControlsHintExpanded();
            BuildUI();
            SetVisible(false);
        }

        void Update()
        {
            // Only while we're actually controlling a character: the same gate the camera drag uses,
            // so the card doesn't sit over the main menu or the post-game screen. And only while the
            // latched control scheme is keyboard+mouse (CameraAutoRotate.AllowedByScheme is the flag
            // that split gets latched into) — a player on a pad or a touchscreen has different
            // controls and the on-screen widgets to go with them, so a list of key names would just
            // be sitting on top of their joystick.
            var keyboard = Keyboard.current;
            bool show = NetworkClient.localPlayer != null
                && !CameraAutoRotate.AllowedByScheme
                && (keyboard != null || Mouse.current != null);
            SetVisible(show);
            if (!show)
            {
                return;
            }

            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
            {
                m_Expanded = !m_Expanded;
                ClientPrefs.SetControlsHintExpanded(m_Expanded);
            }

            if (m_Expanded != m_BuiltExpanded || Screen.height != m_BuiltScreenHeight)
            {
                Refresh();
            }
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20000; // above the match HUD, below the on-screen input widgets
            canvasGO.AddComponent<CanvasScaler>();
            // No GraphicRaycaster on purpose — this is a label, not a button.

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.45f);
            bgImage.raycastTarget = false;

            m_Background = bgGO.GetComponent<RectTransform>();
            m_Background.anchorMin = Vector2.zero;
            m_Background.anchorMax = Vector2.zero;
            m_Background.pivot = Vector2.zero;
            m_Background.anchoredPosition = new Vector2(k_MarginPixels, k_MarginPixels);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(m_Background, false);
            m_Text = textGO.AddComponent<Text>();
            m_Text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Text.color = Color.white;
            m_Text.alignment = TextAnchor.UpperLeft;
            m_Text.raycastTarget = false;
            m_Text.supportRichText = true;
            // The card is sized to the text, so let it measure itself instead of wrapping to a rect
            // that doesn't exist yet.
            m_Text.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Text.verticalOverflow = VerticalWrapMode.Overflow;
            m_Text.lineSpacing = 1.15f;

            var textRT = m_Text.rectTransform;
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(k_PaddingPixels, k_PaddingPixels);
            textRT.offsetMax = new Vector2(-k_PaddingPixels, -k_PaddingPixels);

            Refresh();
        }

        // Re-fills the card and re-sizes it around the text. Cheap, and only called when the folded
        // state or the screen height actually changed.
        void Refresh()
        {
            if (m_Text == null || m_Background == null)
            {
                return;
            }

            // Scaled off the screen height so it stays legible on a 4K monitor without turning into
            // a billboard on a small window, then clamped to a sane range.
            m_Text.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.017f), 12, 22);
            m_Text.text = m_Expanded ? k_ExpandedText : k_CollapsedText;

            m_Background.sizeDelta = new Vector2(
                m_Text.preferredWidth + k_PaddingPixels * 2f,
                m_Text.preferredHeight + k_PaddingPixels * 2f);

            m_BuiltExpanded = m_Expanded;
            m_BuiltScreenHeight = Screen.height;
        }

        void SetVisible(bool visible)
        {
            if (m_Background != null && m_Background.gameObject.activeSelf != visible)
            {
                m_Background.gameObject.SetActive(visible);
            }
        }
    }
}
