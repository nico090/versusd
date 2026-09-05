using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.UI;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UserInput
{
    /// <summary>
    /// Draws where the shot is going to go: a line from the character along the current aim, ending
    /// in a small marker, tinted differently when the aim assist has a foe.
    ///
    /// <para><b>Why.</b> Nothing in the game used to show this. The only aiming feedback was the
    /// reticle under the soft-locked target, and that reticle was routinely wrong — it sat under one
    /// foe while a skillshot flew at the cursor. A player could not tell where their attack was
    /// about to go, which is the difference between "this game is hard" and "this game is
    /// arbitrary". Now the reticle and this line come from the same aim, so they agree by
    /// construction.</para>
    ///
    /// <para>Drawn as screen-space UI (a rotated, stretched <see cref="Image"/>) rather than as a
    /// LineRenderer, for the same reason the joystick and the zoom bar are: it needs no imported
    /// material and no shader that has to survive a build's shader stripping. Self-bootstrapping, so
    /// it needs no scene or prefab wiring.</para>
    /// </summary>
    [DefaultExecutionOrder(50)] // after ClientInputSender, so the aim we draw is this frame's
    public class AimIndicator : MonoBehaviour
    {
        // How far down the aim the line is drawn, in metres, when nothing is being assisted onto.
        const float k_FreeAimLength = 7f;

        // Clamp for the drawn length, so a cursor parked at the horizon doesn't stripe the screen.
        const float k_MinLength = 2.5f;
        const float k_MaxLength = 14f;

        // Line thickness in pixels, at a 1080p-tall screen. Scaled for other resolutions.
        const float k_ThicknessAt1080 = 4f;
        const float k_MarkerSizeAt1080 = 18f;

        // Lifted off the floor so the line isn't buried in the ground mesh at grazing camera angles.
        const float k_GroundOffset = 0.15f;

        static readonly Color k_FreeAimColor = new Color(1f, 1f, 1f, 0.32f);
        static readonly Color k_AssistedColor = new Color(HudSkin.AccentViolet.r, HudSkin.AccentViolet.g,
            HudSkin.AccentViolet.b, 0.8f);

        Camera m_Camera;
        RectTransform m_Line;
        RectTransform m_Marker;
        Image m_LineImage;
        Image m_MarkerImage;
        GameObject m_Root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject(nameof(AimIndicator));
            DontDestroyOnLoad(go);
            go.AddComponent<AimIndicator>();
        }

        void Awake()
        {
            BuildUI();
            SetVisible(false);
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below the HUD proper: this is a hint, it should never sit on top of a button.
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();
            // No GraphicRaycaster on purpose — the indicator must never count as "UI under the
            // pointer", or it would swallow clicks across half the screen.

            m_Root = canvasGO;

            var fade = CreateFadeSprite(64);
            var dot = CreateDotSprite(64);

            m_Line = CreateImage("Line", canvasGO.transform, fade, k_FreeAimColor);
            m_LineImage = m_Line.GetComponent<Image>();
            // Stretch from the character's end, so the fade runs away from the player.
            m_Line.pivot = new Vector2(0f, 0.5f);

            m_Marker = CreateImage("Marker", canvasGO.transform, dot, k_FreeAimColor);
            m_MarkerImage = m_Marker.GetComponent<Image>();
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
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        void LateUpdate()
        {
            var sender = ClientInputSender.LocalInstance;
            if (sender == null || !ShouldDraw(sender))
            {
                SetVisible(false);
                return;
            }

            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    SetVisible(false);
                    return;
                }
            }

            Vector3 origin = sender.AimOrigin + Vector3.up * k_GroundOffset;
            Vector3 aimDir = sender.CurrentAimDirection;

            // Stop the line on the foe the assist has picked, so the line and the reticle point at
            // the same thing. Otherwise run it a fixed distance down the aim — the cursor's actual
            // distance is deliberately not used, because a shot doesn't stop at the cursor.
            var assist = sender.CurrentAssistTarget;
            float length = k_FreeAimLength;
            bool assisted = false;

            if (assist != null && assist.physicsWrapper != null && assist.LifeState == LifeState.Alive)
            {
                Vector3 toFoe = assist.physicsWrapper.Transform.position - sender.AimOrigin;
                toFoe.y = 0f;
                if (toFoe.sqrMagnitude > 0.01f)
                {
                    // Draw along the corrected direction, not the raw aim: that correction is what
                    // the player is being promised.
                    aimDir = toFoe.normalized;
                    length = toFoe.magnitude;
                    assisted = true;
                }
            }

            length = Mathf.Clamp(length, k_MinLength, k_MaxLength);
            Vector3 endWorld = origin + aimDir * length;

            Vector3 startScreen = m_Camera.WorldToScreenPoint(origin);
            Vector3 endScreen = m_Camera.WorldToScreenPoint(endWorld);

            // Behind the camera: WorldToScreenPoint mirrors the point through the origin, which
            // would draw a line shooting off the wrong way.
            if (startScreen.z <= 0f || endScreen.z <= 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            Vector2 start = startScreen;
            Vector2 delta = (Vector2)endScreen - start;
            float pixels = delta.magnitude;
            if (pixels < 1f)
            {
                SetVisible(false);
                return;
            }

            float scale = Screen.height / 1080f;

            m_Line.position = startScreen;
            m_Line.sizeDelta = new Vector2(pixels, Mathf.Max(2f, k_ThicknessAt1080 * scale));
            m_Line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            float markerSize = Mathf.Max(8f, k_MarkerSizeAt1080 * scale);
            m_Marker.position = endScreen;
            m_Marker.sizeDelta = new Vector2(markerSize, markerSize);

            Color color = assisted ? k_AssistedColor : k_FreeAimColor;
            m_LineImage.color = color;
            m_MarkerImage.color = color;
        }

        /// <summary>
        /// Only drawn for a living character. A corpse with an aim line looks like a bug, and the
        /// aim means nothing while you can't act on it.
        /// </summary>
        static bool ShouldDraw(ClientInputSender sender)
        {
            return sender.isActiveAndEnabled
                   && sender.TryGetComponent(out ServerCharacter character)
                   && character.LifeState == LifeState.Alive;
        }

        void SetVisible(bool visible)
        {
            if (m_Root != null && m_Root.activeSelf != visible)
            {
                m_Root.SetActive(visible);
            }
        }

        /// <summary>
        /// A horizontal gradient, opaque at the left and gone at the right, so the line fades away
        /// from the character instead of ending in a hard edge.
        /// </summary>
        static Sprite CreateFadeSprite(int size)
        {
            var tex = new Texture2D(size, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size];

            for (int x = 0; x < size; x++)
            {
                float t = x / (float)(size - 1);
                // Squared falloff: holds its weight near the character, thins out quickly further on.
                float alpha = 1f - (t * t);
                pixels[x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, 1), new Vector2(0f, 0.5f), 100f);
        }

        /// <summary>A soft round dot, built at runtime so the indicator needs no imported art.</summary>
        static Sprite CreateDotSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // Ring rather than a disc: a hollow marker doesn't hide the thing it marks.
                    float outer = Mathf.Clamp01((radius - dist) / 2f);
                    float inner = Mathf.Clamp01((dist - radius * 0.55f) / 2f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(outer * inner * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
