using Mirror;
using Unity.BossRoom.CameraUtils;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// On-screen switch for <see cref="CameraAutoRotate"/>, pinned to the top-right corner and also
    /// bound to the <b>Y</b> key, with the choice persisted in <see cref="ClientPrefs"/>.
    ///
    /// Like <see cref="MobileZoomBar"/> and <see cref="MobileMovementJoystick"/> it builds its own UI
    /// at runtime and self-bootstraps, so it needs no scene or prefab wiring — and, deliberately, no
    /// EventSystem either: it polls the mouse and touches itself instead of being a Unity
    /// <c>Button</c>, so it can't be taken down by whatever else is wrong with a scene's UI. Same
    /// reason the icon is drawn from generated sprites rather than text: no font dependency.
    ///
    /// It is built on desktop too, but only shows itself while the auto-rotate is something the
    /// current control scheme actually uses (see <see cref="CameraAutoRotate.AllowedByScheme"/>) —
    /// i.e. it appears the moment a keyboard+mouse player picks up a gamepad, and disappears again
    /// when they go back to the mouse, where the camera is theirs to drag instead.
    /// </summary>
    [DefaultExecutionOrder(-100)] // poll before ClientInputSender, so IsActive is already true on
                                  // the frame the button is pressed and the tap doesn't also select.
    public class CameraAutoRotateToggle : MonoBehaviour
    {
        /// <summary>True while a press on this button is being tracked (so tap-to-select ignores it).</summary>
        public static bool IsActive { get; private set; }

        // Geometry, as fractions of the screen. Clamped so it stays thumb-sized on a phone without
        // becoming a billboard on a 4K monitor.
        const float k_SizeFraction = 0.07f;
        const float k_MinSize = 42f;
        const float k_MaxSize = 84f;
        const float k_MarginFraction = 0.02f;

        // Sentinels for m_ActivePointer, which otherwise holds a touch id.
        const int k_NoPointer = -1;
        const int k_MousePointer = -2;

        RectTransform m_Button;
        Image m_ButtonImage;
        Image m_RingImage;
        Image m_DotImage;
        Image m_SlashImage;

        int m_ActivePointer = k_NoPointer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // This side of the assembly line is where the preference is read: CameraAutoRotate lives
            // in Unity.BossRoom.CameraUtils, which only references Cinemachine and can't see
            // ClientPrefs. Both bootstraps run before any Update, so the order between them doesn't
            // matter.
            CameraAutoRotate.Enabled = ClientPrefs.GetCameraAutoRotate();

            // Seed of the control-scheme gate, from the devices that exist rather than from one
            // that's been used: a phone has no mouse, a desktop has no touchscreen, and that's the
            // answer for both until the player touches something and
            // ClientInputSender.UpdateCameraControlScheme latches it properly. Without a seed a
            // desktop player would get one auto-swing's worth of camera movement at spawn before
            // the latch caught up — and this button would blink into view for those frames.
            CameraAutoRotate.AllowedByScheme = Application.isMobilePlatform
                || Touchscreen.current != null
                || Gamepad.current != null
                || (Mouse.current == null && Keyboard.current == null);

            var go = new GameObject("CameraAutoRotateToggle");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraAutoRotateToggle>();
        }

        void Awake()
        {
            BuildUI();
            ApplyState();
            SetVisible(false);
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001; // just above MobileZoomBar's canvas
            canvasGO.AddComponent<CanvasScaler>();
            // No GraphicRaycaster on purpose — see the class docs.

            var disc = CreateDiscSprite(64, 0f);
            var ring = CreateDiscSprite(64, 0.16f);

            m_Button = CreateImage("Button", canvasGO.transform, disc, new Color(0f, 0f, 0f, 0.4f));
            m_ButtonImage = m_Button.GetComponent<Image>();

            // A ring with a dot on its rim: a camera orbiting what it looks at.
            m_RingImage = CreateImage("Ring", m_Button, ring, Color.white).GetComponent<Image>();
            m_DotImage = CreateImage("Dot", m_Button, disc, Color.white).GetComponent<Image>();
            // Plain quad (no sprite) struck across the icon when the feature is off.
            m_SlashImage = CreateImage("Slash", m_Button, null, Color.white).GetComponent<Image>();
            m_SlashImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        void Update()
        {
            // Hidden — and inert, hotkey included — on the schemes the auto-rotate doesn't apply to:
            // a switch that does nothing is worse than no switch.
            if (NetworkClient.localPlayer == null || !CameraAutoRotate.AllowedByScheme)
            {
                Release();
                if (m_Button.gameObject.activeSelf)
                {
                    SetVisible(false);
                }
                return;
            }

            if (!m_Button.gameObject.activeSelf)
            {
                SetVisible(true);
            }

            Layout();
            PollInput();
            PollHotkey();
            ApplyState();
        }

        void PollHotkey()
        {
            // Y flips it too, so it can be A/B'd on desktop without reaching for the corner. Read
            // straight off the device rather than through PlayerActions: this is a client-side
            // display preference, not gameplay input, and it needs no rebinding.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.yKey.wasPressedThisFrame)
            {
                Toggle();
            }
        }

        void Layout()
        {
            float size = Mathf.Clamp(Screen.height * k_SizeFraction, k_MinSize, k_MaxSize);
            float margin = Screen.height * k_MarginFraction;

            m_Button.sizeDelta = new Vector2(size, size);
            m_Button.position = new Vector3(
                Screen.width - margin - size * 0.5f,
                Screen.height - margin - size * 0.5f,
                0f);

            float ring = size * 0.52f;
            m_RingImage.rectTransform.sizeDelta = new Vector2(ring, ring);

            float dot = size * 0.16f;
            m_DotImage.rectTransform.sizeDelta = new Vector2(dot, dot);
            // Parked on the ring's rim, upper-right, where the orbit reads as going clockwise.
            m_DotImage.rectTransform.anchoredPosition =
                new Vector2(ring * 0.354f, ring * 0.354f); // 0.5 * sin/cos(45°)

            m_SlashImage.rectTransform.sizeDelta = new Vector2(size * 0.62f, Mathf.Max(2f, size * 0.07f));
        }

        void PollInput()
        {
            if (m_ActivePointer == k_NoPointer)
            {
                TryBeginPress();
                return;
            }

            // Held: just watch for the release. The toggle already fired on press.
            bool stillDown = m_ActivePointer == k_MousePointer
                ? Mouse.current != null && Mouse.current.leftButton.isPressed
                : IsTouchPressed(m_ActivePointer);

            if (!stillDown)
            {
                Release();
            }
        }

        void TryBeginPress()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                IsInside(mouse.position.ReadValue()) && !IsPointerOverUI(PointerInputModule.kMouseLeftId))
            {
                Claim(k_MousePointer);
                return;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.wasPressedThisFrame)
                {
                    continue;
                }

                int touchId = (int)touch.touchId.ReadValue();
                if (IsInside(touch.position.ReadValue()) && !IsPointerOverUI(touchId))
                {
                    Claim(touchId);
                    return;
                }
            }
        }

        void Claim(int pointer)
        {
            m_ActivePointer = pointer;
            IsActive = true;
            Toggle();
        }

        static void Toggle()
        {
            bool enabled = !CameraAutoRotate.Enabled;
            CameraAutoRotate.Enabled = enabled;
            ClientPrefs.SetCameraAutoRotate(enabled);
            // Written out now rather than at quit: on mobile the process is often just killed.
            PlayerPrefs.Save();
        }

        void Release()
        {
            m_ActivePointer = k_NoPointer;
            IsActive = false;
        }

        bool IsTouchPressed(int touchId)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            foreach (var touch in touchscreen.touches)
            {
                if ((int)touch.touchId.ReadValue() == touchId)
                {
                    return touch.press.isPressed;
                }
            }
            return false;
        }

        bool IsInside(Vector2 screenPos)
        {
            var rect = m_Button.rect;
            Vector2 center = m_Button.position;
            return Mathf.Abs(screenPos.x - center.x) <= rect.width * 0.5f &&
                   Mathf.Abs(screenPos.y - center.y) <= rect.height * 0.5f;
        }

        static bool IsPointerOverUI(int pointerId)
        {
            if (EventSystem.current == null)
            {
                return false;
            }
            return EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        void ApplyState()
        {
            bool on = CameraAutoRotate.Enabled;

            m_ButtonImage.color = new Color(0f, 0f, 0f, m_ActivePointer != k_NoPointer ? 0.6f : 0.4f);

            var iconColor = on ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.4f);
            m_RingImage.color = iconColor;
            m_DotImage.color = iconColor;
            m_SlashImage.color = iconColor;
            m_SlashImage.enabled = !on;
        }

        void SetVisible(bool visible)
        {
            if (m_Button != null)
            {
                m_Button.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Builds a white disc sprite at runtime, so the button needs no imported art.
        /// </summary>
        /// <param name="thickness">0 for a filled disc, otherwise the ring's width as a fraction of the radius.</param>
        static Sprite CreateDiscSprite(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            float radius = size * 0.5f;
            float innerRadius = thickness > 0f ? radius * (1f - thickness) : 0f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // One pixel of feather on each edge, so it doesn't look jagged when scaled up.
                    float alpha = Mathf.Clamp01(radius - dist);
                    if (innerRadius > 0f)
                    {
                        alpha = Mathf.Min(alpha, Mathf.Clamp01(dist - innerRadius));
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
