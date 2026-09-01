using Mirror;
using Unity.BossRoom.CameraUtils;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// An on-screen vertical zoom bar for touch devices, pinned to the right edge of the screen.
    /// Like <see cref="MobileMovementJoystick"/> it builds its own UI at runtime and
    /// auto-instantiates once, so it needs no scene/prefab wiring.
    ///
    /// It drives <see cref="CinemachineOrbitalFollow.VerticalAxis"/> on the gameplay camera — the
    /// same axis the camera prefab's "Look Orbit Y" controller drives, i.e. what reads as zoom
    /// in game (the camera swings between a low, close view and a high, far one).
    ///
    /// It also strips every touchscreen binding from the actions that Cinemachine controller
    /// reads. The PlayerActions asset binds the ScrollWheel action to a TwoModifiers composite
    /// (touch0/press + touch1/press -> touchscreen delta), which is why putting two fingers on
    /// the screen used to move the camera. That gesture is now inert; zoom is bar-only on touch,
    /// and the mouse wheel is untouched on desktop.
    /// </summary>
    [DefaultExecutionOrder(-100)] // poll touches before ClientInputSender, so IsActive is
                                  // already true on the frame the bar is grabbed.
    public class MobileZoomBar : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        /// <summary>True while a zoom-bar touch is being tracked (so tap-to-select can ignore it).</summary>
        public static bool IsActive { get; private set; }

        // Bar geometry, as fractions of the screen.
        const float k_BarWidthFraction = 0.045f;
        const float k_BarHeightFraction = 0.4f;
        const float k_BarRightMarginFraction = 0.02f;

        // Extra slack around the bar so it is easy to grab with a thumb without drawing it fat.
        const float k_GrabPaddingFraction = 0.03f;

        // Where the handle sits before a camera has been resolved. Taken from CameraController so
        // the two can't drift apart when the starting zoom is retuned.
        const float k_DefaultVertical = CameraController.DefaultZoom;

        // Set in Awake so OwnsScreenPoint can answer for whoever asks. Only ever one of these:
        // Bootstrap creates a single DontDestroyOnLoad instance.
        static MobileZoomBar s_Instance;

        RectTransform m_Track;
        RectTransform m_Fill;
        RectTransform m_Handle;
        Image m_HandleImage;

        CinemachineOrbitalFollow m_OrbitalFollow;

        int m_ActiveTouchId = -1;
        // 0 = low/close (bottom of the bar), 1 = high/far (top). Maps straight onto VerticalAxis.
        float m_Normalized = k_DefaultVertical;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Touchscreen.current == null && !Application.isMobilePlatform)
            {
                return;
            }

            var go = new GameObject("MobileZoomBar");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileZoomBar>();
        }

        void Awake()
        {
            s_Instance = this;
            BuildUI();
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            canvasGO.AddComponent<CanvasScaler>();
            // No GraphicRaycaster on purpose: we poll touches ourselves and don't want the bar to
            // count as "UI under the pointer" for the rest of the input system.

            var sprite = CreateRoundedSprite(64);

            m_Track = CreateImage("Track", canvasGO.transform, sprite, new Color(1f, 1f, 1f, 0.18f));
            m_Fill = CreateImage("Fill", m_Track, sprite, new Color(1f, 1f, 1f, 0.32f));
            m_Handle = CreateImage("Handle", m_Track, sprite, new Color(1f, 1f, 1f, 0.75f));
            m_HandleImage = m_Handle.GetComponent<Image>();

            // Fill grows from the bottom of the track.
            m_Fill.anchorMin = m_Fill.anchorMax = new Vector2(0.5f, 0f);
            m_Fill.pivot = new Vector2(0.5f, 0f);
        }

        static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        void Update()
        {
            var touchscreen = Touchscreen.current;

            if (touchscreen == null || NetworkClient.localPlayer == null)
            {
                Release();
                if (m_Track.gameObject.activeSelf)
                {
                    SetVisible(false);
                }
                return;
            }

            ResolveCamera();

            if (!m_Track.gameObject.activeSelf)
            {
                // Pick up wherever the camera currently sits so the bar doesn't snap on show.
                if (m_OrbitalFollow != null)
                {
                    m_Normalized = Mathf.Clamp01(m_OrbitalFollow.VerticalAxis.Value);
                }
                SetVisible(true);
            }

            Layout();

            if (m_ActiveTouchId == -1)
            {
                TryBeginTouch(touchscreen);
            }
            else
            {
                ContinueTouch(touchscreen);
            }

            ApplyZoom();
            m_HandleImage.color = new Color(1f, 1f, 1f, m_ActiveTouchId != -1 ? 1f : 0.75f);
        }

        void Layout()
        {
            float width = Mathf.Max(26f, Screen.width * k_BarWidthFraction);
            float height = Screen.height * k_BarHeightFraction;

            m_Track.sizeDelta = new Vector2(width, height);
            m_Track.position = new Vector3(
                Screen.width - Screen.width * k_BarRightMarginFraction - width * 0.5f,
                Screen.height * 0.5f,
                0f);

            m_Fill.sizeDelta = new Vector2(width, height * m_Normalized);
            m_Handle.sizeDelta = new Vector2(width * 1.4f, width * 1.4f);
            m_Handle.anchoredPosition = new Vector2(0f, (m_Normalized - 0.5f) * height);
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
                if (!IsInsideGrabArea(pos))
                {
                    continue;
                }
                if (IsPointerOverUI((int)touch.touchId.ReadValue()))
                {
                    continue;
                }

                m_ActiveTouchId = (int)touch.touchId.ReadValue();
                IsActive = true;
                UpdateFromScreenY(pos.y);
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
                    UpdateFromScreenY(touch.position.ReadValue().y);
                }
                else
                {
                    Release();
                }
                return;
            }

            // Touch vanished without a clean release (happens on some devices).
            Release();
        }

        void Release()
        {
            m_ActiveTouchId = -1;
            IsActive = false;
        }

        /// <summary>
        /// Whether a screen point falls in this bar's grab area, for other touch widgets that poll
        /// raw touches and have to keep off it. False while the bar is hidden, and false before it
        /// has been built. Exposed because the geometry belongs here: <see cref="TouchCameraOrbit"/>
        /// needs the same answer and duplicating the fractions would let the two drift apart.
        /// </summary>
        public static bool OwnsScreenPoint(Vector2 screenPos)
        {
            return s_Instance != null
                   && s_Instance.m_Track != null
                   && s_Instance.m_Track.gameObject.activeSelf
                   && s_Instance.IsInsideGrabArea(screenPos);
        }

        bool IsInsideGrabArea(Vector2 screenPos)
        {
            var rect = m_Track.rect;
            float padding = Screen.width * k_GrabPaddingFraction;
            Vector2 center = m_Track.position;

            return Mathf.Abs(screenPos.x - center.x) <= rect.width * 0.5f + padding &&
                   Mathf.Abs(screenPos.y - center.y) <= rect.height * 0.5f + padding;
        }

        void UpdateFromScreenY(float screenY)
        {
            float height = m_Track.rect.height;
            float bottom = m_Track.position.y - height * 0.5f;
            m_Normalized = Mathf.Clamp01((screenY - bottom) / height);
        }

        void ResolveCamera()
        {
            if (m_OrbitalFollow != null)
            {
                return;
            }

            var cameraGO = GameObject.FindGameObjectWithTag(k_CMCameraTag);
            if (cameraGO == null)
            {
                return;
            }

            m_OrbitalFollow = cameraGO.GetComponent<CinemachineOrbitalFollow>();
            if (m_OrbitalFollow == null)
            {
                return;
            }

            DisableTouchCameraBindings(cameraGO.GetComponent<CinemachineInputAxisController>());
        }

        /// <summary>
        /// Neutralizes every touchscreen binding on the actions the camera's input controller
        /// reads. This is what kills the two-finger gesture: the ScrollWheel action's TwoModifiers
        /// composite (touch0/press + touch1/press -> touchscreen delta) stops resolving to any
        /// control, while its mouse-wheel binding keeps working on desktop.
        ///
        /// Done at runtime rather than by editing PlayerActions.inputactions so the fix survives
        /// regardless of whether the Editor re-imports the asset before a build.
        /// </summary>
        static void DisableTouchCameraBindings(CinemachineInputAxisController controller)
        {
            if (controller == null)
            {
                return;
            }

            foreach (var axisController in controller.Controllers)
            {
                var action = axisController?.Input?.InputAction != null
                    ? axisController.Input.InputAction.action
                    : null;
                if (action == null)
                {
                    continue;
                }

                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i];
                    if (binding.isComposite || string.IsNullOrEmpty(binding.path))
                    {
                        continue;
                    }
                    if (binding.path.IndexOf("Touchscreen", System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    // An empty override path leaves the binding unresolved, so it never fires.
                    action.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
                }
            }
        }

        void ApplyZoom()
        {
            if (m_OrbitalFollow == null)
            {
                return;
            }

            var axis = m_OrbitalFollow.VerticalAxis;
            axis.Value = Mathf.Lerp(axis.Range.x, axis.Range.y, m_Normalized);
            m_OrbitalFollow.VerticalAxis = axis;
        }

        void SetVisible(bool visible)
        {
            if (m_Track != null)
            {
                m_Track.gameObject.SetActive(visible);
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
        /// Builds a white rounded-rect sprite at runtime (9-sliced) so the bar needs no imported art.
        /// </summary>
        static Sprite CreateRoundedSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            float radius = size * 0.5f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance outside the rounded-corner region.
                    float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                    float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((radius - dist) / 2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
