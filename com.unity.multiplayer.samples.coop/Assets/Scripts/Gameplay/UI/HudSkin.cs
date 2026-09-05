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
        // Cyberpunk Egypt: the stone is basalt with a violet cast — cool, never warm — and the
        // only light left in the place is two neon tubes, one blue and one violet. Both are
        // deliberately dulled: a saturated neon reads as an arcade, a dusty one reads as a tube
        // that has been burning in a tomb for a long time. The inlays are lapis and amethyst, the
        // two stones this look would actually have been cut from. Gold survives in exactly one
        // job — first place — because there it is meaning rather than decoration, and red survives
        // as the alarm on the last thirty seconds.

        /// <summary>Translucent panel ground. Dark enough to hold pale text over lava.</summary>
        public static readonly Color PanelColor = new Color(0.042f, 0.038f, 0.070f, 0.74f);

        /// <summary>Thin edge line on panels; the "chrome" of the kit.</summary>
        public static readonly Color PanelBorderColor = new Color(0.34f, 0.58f, 0.86f, 0.38f);

        /// <summary>Body text. Cool bone — white would read as a spreadsheet.</summary>
        public static readonly Color TextPrimary = new Color(0.90f, 0.91f, 0.98f, 1f);

        /// <summary>De-emphasised text (labels, the scoreboard's lower rows).</summary>
        public static readonly Color TextDim = new Color(0.62f, 0.62f, 0.76f, 1f);

        /// <summary>
        /// The cold light: chrome, and numbers that matter right now (the timer).
        /// </summary>
        /// <remarks>
        /// Held down in both saturation and value, because it has to sit on stone without turning
        /// the screen into a sci-fi console — the neon is the ruin's last working fixture, not its
        /// theme.
        /// </remarks>
        public static readonly Color AccentBlue = new Color(0.36f, 0.68f, 0.94f, 1f);

        /// <summary>
        /// The second light: the violet tube, and the colour of anything going wrong.
        /// </summary>
        /// <remarks>
        /// Paired with <see cref="AccentBlue"/> rather than used on its own. One light gives a
        /// flat screen; two opposed ones give the stone a top and a bottom, which is what sells it
        /// as lit rather than printed. Violet and blue are close enough on the wheel to read as
        /// one palette and far enough apart to still be two lights.
        /// </remarks>
        public static readonly Color AccentViolet = new Color(0.60f, 0.38f, 0.92f, 1f);

        /// <summary>Lapis: the deep blue the carved lines in the stone are filled with.</summary>
        public static readonly Color Lapis = new Color(0.28f, 0.44f, 0.86f, 1f);

        /// <summary>Amethyst: the second inlay, and the violet the ornament is cut in.</summary>
        public static readonly Color Amethyst = new Color(0.56f, 0.42f, 0.88f, 1f);

        /// <summary>
        /// Gold leaf. The one warm colour left in the game, and it means exactly one thing: first
        /// place. Anywhere else, use <see cref="Lapis"/> or <see cref="Amethyst"/>.
        /// </summary>
        public static readonly Color Gold = new Color(0.86f, 0.74f, 0.42f, 1f);

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
