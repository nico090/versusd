using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// A floating on-screen movement joystick for touch devices. It builds its own UI at
    /// runtime (no scene/prefab wiring needed) and auto-instantiates once via
    /// <see cref="Bootstrap"/>, so it "just works" in a build.
    ///
    /// It exposes the current stick direction through <see cref="MovementInput"/>, which
    /// <see cref="ClientInputSender"/> adds to its keyboard/gamepad movement each frame.
    /// While a movement touch is engaged, <see cref="IsActive"/> is true so the input sender
    /// can suppress tap-to-select for that touch (otherwise starting to move would also pick a
    /// random target).
    ///
    /// Behaviour:
    ///  - Only enabled when a touchscreen exists (never interferes with PC mouse/keyboard).
    ///  - Only visible/active while a local player object is spawned (hidden in menus).
    ///  - "Floating": the stick appears wherever you first press within the movement zone
    ///    (left portion of the screen), which is the friendliest scheme for twin-stick-less
    ///    mobile play.
    /// </summary>
    public class MobileMovementJoystick : MonoBehaviour
    {
        /// <summary>Current normalized stick direction (magnitude 0..1). Zero when not touched.</summary>
        public static Vector2 MovementInput { get; private set; }

        /// <summary>True while a movement touch is currently being tracked.</summary>
        public static bool IsActive { get; private set; }

        // The movement joystick only claims touches that start inside this fraction of the
        // screen (measured from the left edge). The right side stays free for the action bar
        // and tap-to-select.
        const float k_MovementZoneWidthFraction = 0.5f;

        RectTransform m_Background;
        RectTransform m_Handle;

        int m_ActiveTouchId = -1;
        Vector2 m_Origin;      // screen-space anchor where the touch began
        float m_Radius;        // max handle travel in pixels

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // No point on platforms without touch (desktop editor with no touch simulation, etc.).
            if (Touchscreen.current == null && !Application.isMobilePlatform)
            {
                return;
            }

            var go = new GameObject("MobileMovementJoystick");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileMovementJoystick>();
        }

        void Awake()
        {
            BuildUI();
            Hide();
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000; // above gameplay HUD, below full-screen popups if any
            canvasGO.AddComponent<CanvasScaler>();
            // Deliberately no GraphicRaycaster: we poll touches directly, and we don't want the
            // joystick to register as "UI under the pointer" for the rest of the input system.

            var circle = CreateCircleSprite(128);

            m_Background = CreateImage("Background", canvasGO.transform, circle, new Color(1f, 1f, 1f, 0.25f));
            m_Handle = CreateImage("Handle", m_Background, circle, new Color(1f, 1f, 1f, 0.5f));
        }

        static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;

            // Centre anchors + pivot so sizeDelta is an absolute pixel size and anchoredPosition
            // is measured from the parent's centre.
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        void Update()
        {
            var touchscreen = Touchscreen.current;

            // Hide and reset whenever there's no touch device or no local player in the scene
            // (e.g. we're sitting in a menu).
            if (touchscreen == null || NetworkClient.localPlayer == null)
            {
                if (IsActive || MovementInput != Vector2.zero)
                {
                    ReleaseStick();
                }
                if (m_Background.gameObject.activeSelf)
                {
                    Hide();
                }
                return;
            }

            if (m_ActiveTouchId == -1)
            {
                TryBeginTouch(touchscreen);
            }
            else
            {
                ContinueTouch(touchscreen);
            }
        }

        void TryBeginTouch(Touchscreen touchscreen)
        {
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.wasPressedThisFrame)
                {
                    continue;
                }

                Vector2 pos = touch.position.ReadValue();

                // Only claim presses that start in the movement zone and not on top of a UI
                // element (so the action bar / menu buttons still work).
                if (pos.x > Screen.width * k_MovementZoneWidthFraction)
                {
                    continue;
                }
                if (IsPointerOverUI((int)touch.touchId.ReadValue()))
                {
                    continue;
                }

                m_ActiveTouchId = (int)touch.touchId.ReadValue();
                m_Origin = pos;
                m_Radius = Mathf.Max(60f, Screen.height * 0.12f);

                float bgSize = m_Radius * 2f;
                m_Background.sizeDelta = new Vector2(bgSize, bgSize);
                m_Handle.sizeDelta = new Vector2(bgSize * 0.5f, bgSize * 0.5f);
                m_Background.position = m_Origin;
                m_Handle.anchoredPosition = Vector2.zero;

                m_Background.gameObject.SetActive(true);
                IsActive = true;
                MovementInput = Vector2.zero;
                return;
            }
        }

        void ContinueTouch(Touchscreen touchscreen)
        {
            foreach (var touch in touchscreen.touches)
            {
                if ((int)touch.touchId.ReadValue() != m_ActiveTouchId)
                {
                    continue;
                }

                if (touch.press.isPressed)
                {
                    Vector2 delta = touch.position.ReadValue() - m_Origin;
                    Vector2 clamped = Vector2.ClampMagnitude(delta, m_Radius);
                    m_Handle.anchoredPosition = clamped;
                    MovementInput = clamped / m_Radius;
                }
                else
                {
                    ReleaseStick();
                    Hide();
                }
                return;
            }

            // The touch vanished without a clean release (can happen on some devices).
            ReleaseStick();
            Hide();
        }

        void ReleaseStick()
        {
            m_ActiveTouchId = -1;
            MovementInput = Vector2.zero;
            IsActive = false;
        }

        void Hide()
        {
            if (m_Background != null)
            {
                m_Background.gameObject.SetActive(false);
            }
        }

        static bool IsPointerOverUI(int pointerId)
        {
            if (EventSystem.current == null)
            {
                return false;
            }
            return EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        /// <summary>
        /// Builds a soft-edged filled white circle sprite at runtime, so the joystick needs no
        /// imported art. Tint/opacity are applied via the Image color.
        /// </summary>
        static Sprite CreateCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            float radius = size * 0.5f;
            Vector2 center = new Vector2(radius, radius);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    // 2px feathered edge for anti-aliasing.
                    float alpha = Mathf.Clamp01((radius - dist) / 2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
