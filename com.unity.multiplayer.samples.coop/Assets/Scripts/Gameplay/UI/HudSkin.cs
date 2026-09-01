using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The one place the code-built HUD gets its look from: palette, rounded panel sprites and
    /// text styling, shared by every widget this project draws from code (the deathmatch HUD, the
    /// controls hint card, and whatever comes next).
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists.</b> The code-built widgets each invented their own look — raw white
    /// Text floating on nothing, a flat black quad — because there was nowhere shared to get a
    /// better one from. The result read as debug overlay, not as game UI. One skin class means the
    /// pieces agree with each other, and a look change is one edit instead of a hunt.</para>
    ///
    /// <para><b>Why sprites are generated and not imported.</b> Same reason the joystick and the
    /// aim indicator are code-built: no imported asset means no prefab wiring, nothing the build
    /// can strip, and nothing the Editor's asset cache can serve stale. The rounded rectangle is
    /// drawn once into a small texture with a signed-distance function and 9-sliced from
    /// there.</para>
    /// </remarks>
    public static class HudSkin
    {
        // ── Palette ───────────────────────────────────────────────────────────────────────────
        // Matches the world restyle: near-black blue slate for surfaces, cyan for chrome, warm
        // white for reading text. Alerts keep their conventional colours (red, gold) — those are
        // semantics, not decoration, and they stay where players expect them.

        /// <summary>Translucent panel ground. Dark enough to hold white text over lava.</summary>
        public static readonly Color PanelColor = new Color(0.04f, 0.055f, 0.095f, 0.72f);

        /// <summary>Thin edge line on panels; the "chrome" of the kit.</summary>
        public static readonly Color PanelBorderColor = new Color(0f, 0.9f, 1f, 0.35f);

        /// <summary>Body text.</summary>
        public static readonly Color TextPrimary = new Color(0.92f, 0.96f, 1f, 1f);

        /// <summary>De-emphasised text (labels, the scoreboard's lower rows).</summary>
        public static readonly Color TextDim = new Color(0.62f, 0.7f, 0.8f, 1f);

        /// <summary>Accent for numbers that matter right now (the timer).</summary>
        public static readonly Color AccentCyan = new Color(0.45f, 0.95f, 1f, 1f);

        // ── Sprites ───────────────────────────────────────────────────────────────────────────

        const int k_SpriteSize = 64;
        const float k_CornerRadius = 14f;
        const float k_BorderThickness = 2f;

        static Sprite s_PanelSprite;
        static Sprite s_BorderSprite;

        /// <summary>Filled rounded rectangle, 9-sliced. Tint it with the Image colour.</summary>
        public static Sprite PanelSprite => s_PanelSprite != null ? s_PanelSprite : s_PanelSprite = BakeRounded(filled: true);

        /// <summary>Just the rounded outline, 9-sliced, for layering a border over a panel.</summary>
        public static Sprite BorderSprite => s_BorderSprite != null ? s_BorderSprite : s_BorderSprite = BakeRounded(filled: false);

        /// <summary>
        /// Signed distance to a rounded box, sampled per pixel. Positive outside. The two sprites
        /// are the same field thresholded differently: fill is "inside", border is "near the
        /// edge".
        /// </summary>
        static Sprite BakeRounded(bool filled)
        {
            var texture = new Texture2D(k_SpriteSize, k_SpriteSize, TextureFormat.RGBA32, false)
            {
                name = filled ? "HudSkin_Panel" : "HudSkin_Border",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float half = k_SpriteSize * 0.5f;
            // Inset so the antialiased edge never touches the bitmap border, where clamping
            // would smear it across a stretched 9-slice.
            float extent = half - 3f;

            var pixels = new Color32[k_SpriteSize * k_SpriteSize];
            for (int y = 0; y < k_SpriteSize; y++)
            {
                for (int x = 0; x < k_SpriteSize; x++)
                {
                    // SDF of a rounded box centred on the texture.
                    Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y))
                                - new Vector2(extent - k_CornerRadius, extent - k_CornerRadius);
                    float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                    float distance = outside + inside - k_CornerRadius;

                    float alpha = filled
                        ? Mathf.Clamp01(0.5f - distance)                                  // inside
                        : Mathf.Clamp01(k_BorderThickness * 0.5f + 0.75f - Mathf.Abs(distance)); // edge band

                    pixels[y * k_SpriteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            // Border of 24px leaves a 16px stretchable centre — comfortably past the corner
            // radius, so slicing never distorts the curve.
            return Sprite.Create(texture, new Rect(0, 0, k_SpriteSize, k_SpriteSize),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(24, 24, 24, 24));
        }

        // ── Widgets ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts a skinned panel (fill + border) behind <paramref name="content"/>: same parent,
        /// same anchor, same position, grown by <paramref name="padding"/>, inserted at a lower
        /// sibling index so it draws underneath. No re-parenting, so the content's own layout
        /// stays exactly as its builder wrote it.
        /// </summary>
        public static void WrapInPanel(RectTransform content, Vector2 padding)
        {
            var panel = new GameObject(content.name + "Panel");
            panel.transform.SetParent(content.parent, false);
            panel.transform.SetSiblingIndex(content.GetSiblingIndex());

            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = content.anchorMin;
            rt.anchorMax = content.anchorMax;
            rt.pivot = content.pivot;
            rt.anchoredPosition = content.anchoredPosition;
            rt.sizeDelta = content.sizeDelta + padding * 2f;

            var fill = panel.AddComponent<Image>();
            fill.sprite = PanelSprite;
            fill.type = Image.Type.Sliced;
            fill.color = PanelColor;
            fill.raycastTarget = false;

            AddBorder(panel);
        }

        /// <summary>Lays the border sprite over <paramref name="panel"/>, stretched to fit.</summary>
        public static void AddBorder(GameObject panel)
        {
            var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(panel.transform, false);

            var rt = borderGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var border = borderGO.AddComponent<Image>();
            border.sprite = BorderSprite;
            border.type = Image.Type.Sliced;
            border.color = PanelBorderColor;
            border.raycastTarget = false;
        }

        /// <summary>
        /// Readability pass for HUD text: themed colour plus a dark outline, which is what lets
        /// the same text survive lava, snow and neon behind it.
        /// </summary>
        public static void StyleText(Text text, bool dim = false)
        {
            text.color = dim ? TextDim : TextPrimary;

            var outline = text.gameObject.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.25f, -1.25f);
        }
    }
}
