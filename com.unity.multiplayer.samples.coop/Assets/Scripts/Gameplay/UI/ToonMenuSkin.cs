using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The look of the menus: palette, procedurally baked "sticker" sprites (fat rounded shapes
    /// with a thick dark contour) and the small styling routines that turn a stock uGUI widget
    /// into one. <see cref="ToonMenuRestyler"/> decides <i>what</i> gets styled; this decides how.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a second skin next to <see cref="HudSkin"/>.</b> HudSkin dresses widgets this
    /// project draws itself, so it only needs a panel and a text style. The menus are the
    /// opposite case: the widgets already exist, built by someone else, and have to be dressed
    /// after the fact — buttons with hover states, input fields, cards. The palette here is
    /// deliberately the same one (see <see cref="Accent"/>, which <i>is</i> HudSkin's cyan) so the
    /// menus and the in-game HUD read as one game.</para>
    ///
    /// <para><b>Why baked and not imported sprites.</b> Same reason as HudSkin: no asset means no
    /// prefab wiring to lose, nothing for the Editor's asset cache to serve stale, and a look
    /// change is an edit here instead of a re-import.</para>
    ///
    /// <para><b>The toon part.</b> Cartoon UI is three things — shapes far rounder than the
    /// content needs, one contour thick enough to read as ink, and a hard offset shadow so every
    /// surface looks like a cut-out laid on the one below. All three are cheap: two extra
    /// stretched child images and a <see cref="Shadow"/> component per widget.</para>
    /// </remarks>
    public static class ToonMenuSkin
    {
        // ── Palette ───────────────────────────────────────────────────────────────────────────
        // Dark neon: the surfaces stay near-black blue so the cyan chrome and the 3D backdrop
        // behind the menu are the only bright things on screen.

        /// <summary>The contour colour. Not pure black — pure black reads as a hole, not ink.</summary>
        public static readonly Color Ink = new Color(0.016f, 0.027f, 0.055f, 1f);

        /// <summary>Card / dialog ground.</summary>
        public static readonly Color CardFill = new Color(0.055f, 0.078f, 0.14f, 0.96f);

        /// <summary>Button ground, a step lighter than a card so it lifts off it.</summary>
        public static readonly Color ButtonFill = new Color(0.10f, 0.16f, 0.27f, 1f);

        /// <summary>Button under the pointer.</summary>
        public static readonly Color ButtonHighlight = new Color(0.17f, 0.30f, 0.47f, 1f);

        /// <summary>Button being pressed — darker, which sells the push together with the squash.</summary>
        public static readonly Color ButtonPressed = new Color(0.06f, 0.10f, 0.18f, 1f);

        /// <summary>Greyed-out button.</summary>
        public static readonly Color ButtonDisabled = new Color(0.09f, 0.11f, 0.15f, 0.55f);

        /// <summary>Input fields sink instead of lifting, so they get the darkest ground.</summary>
        public static readonly Color InputFill = new Color(0.025f, 0.04f, 0.075f, 0.95f);

        /// <summary>Chrome accent. Shared with the HUD on purpose.</summary>
        public static readonly Color Accent = HudSkin.AccentCyan;

        /// <summary>Accent at card strength: present, not shouting.</summary>
        public static readonly Color AccentSoft = new Color(Accent.r, Accent.g, Accent.b, 0.30f);

        /// <summary>The hard cut-out shadow every surface drops.</summary>
        public static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>Top-edge sheen laid over buttons and cards.</summary>
        public static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.07f);

        // ── Baked sprites ─────────────────────────────────────────────────────────────────────

        /// <summary>What <see cref="Bake"/> should write into the alpha channel.</summary>
        enum ShapeMode
        {
            /// <summary>Everything inside the rounded box.</summary>
            Fill,

            /// <summary>A band straddling the rounded box's edge — the contour.</summary>
            Outline,

            /// <summary>Inside the box, fading in towards the top edge.</summary>
            Gloss,

            /// <summary>Radial falloff, bright at the centre.</summary>
            Glow,

            /// <summary>Radial falloff, bright at the corners — a screen vignette.</summary>
            Vignette,
        }

        static readonly Dictionary<string, Sprite> s_Sprites = new Dictionary<string, Sprite>();

        /// <summary>Rounded ground for buttons and other one-line-tall widgets.</summary>
        public static Sprite ButtonFillSprite => Get("btn.fill", 64, 15f, 0f, ShapeMode.Fill);

        /// <summary>Contour for the same. Thick — this is what makes it read as drawn.</summary>
        public static Sprite ButtonOutlineSprite => Get("btn.line", 64, 15f, 4.5f, ShapeMode.Outline);

        /// <summary>Sheen for the same.</summary>
        public static Sprite ButtonGlossSprite => Get("btn.gloss", 64, 15f, 0f, ShapeMode.Gloss);

        /// <summary>Rounded ground for dialogs and panels — rounder, since they are bigger.</summary>
        public static Sprite CardFillSprite => Get("card.fill", 96, 26f, 0f, ShapeMode.Fill);

        /// <summary>Contour for a card.</summary>
        public static Sprite CardOutlineSprite => Get("card.line", 96, 26f, 5.5f, ShapeMode.Outline);

        /// <summary>Sheen for a card.</summary>
        public static Sprite CardGlossSprite => Get("card.gloss", 96, 26f, 0f, ShapeMode.Gloss);

        /// <summary>Near-pill ground for input fields and bars.</summary>
        public static Sprite InputFillSprite => Get("input.fill", 64, 22f, 0f, ShapeMode.Fill);

        /// <summary>Contour for an input field — thinner, it is not a thing you press.</summary>
        public static Sprite InputOutlineSprite => Get("input.line", 64, 22f, 3f, ShapeMode.Outline);

        /// <summary>Soft radial light. Sits behind logos and under hovered buttons.</summary>
        public static Sprite GlowSprite => Get("glow", 128, 0f, 0f, ShapeMode.Glow);

        /// <summary>Full-screen corner darkening, so bright UI has something to sit against.</summary>
        public static Sprite VignetteSprite => Get("vignette", 128, 0f, 0f, ShapeMode.Vignette);

        static Sprite Get(string key, int size, float radius, float outline, ShapeMode mode)
        {
            // A leaked domain reload can leave a destroyed texture behind a live dictionary entry,
            // so the check has to be Unity's null, not a plain dictionary hit.
            if (s_Sprites.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = Bake(key, size, radius, outline, mode);
            s_Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Draws one shape into a small texture by sampling a signed distance field per pixel, and
        /// 9-slices it so the same bitmap serves a 40px button and a 900px dialog without the
        /// corner radius growing with the widget.
        /// </summary>
        static Sprite Bake(string key, int size, float radius, float outline, ShapeMode mode)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ToonMenuSkin_" + key,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float half = size * 0.5f;
            // Inset so the antialiased edge never touches the bitmap border, where clamping would
            // smear it along a stretched 9-slice.
            float extent = half - 3f;

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float alpha;

                    if (mode == ShapeMode.Glow || mode == ShapeMode.Vignette)
                    {
                        float r = Mathf.Clamp01(p.magnitude / extent);
                        alpha = mode == ShapeMode.Glow ? Mathf.Pow(1f - r, 2.2f) : Mathf.Pow(r, 2.6f);
                    }
                    else
                    {
                        // Signed distance to a rounded box centred on the texture, negative inside.
                        var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y))
                                - new Vector2(extent - radius, extent - radius);
                        float outsideDistance = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                        float insideDistance = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                        float distance = outsideDistance + insideDistance - radius;

                        switch (mode)
                        {
                            case ShapeMode.Outline:
                                alpha = Mathf.Clamp01(outline * 0.5f + 0.75f - Mathf.Abs(distance));
                                break;
                            case ShapeMode.Gloss:
                                // Inside the shape, and only near its top edge. The ramp starts
                                // below the halfway line so the sheen has room to fade out.
                                float top = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size * 0.42f, size * 0.99f, y));
                                alpha = Mathf.Clamp01(0.5f - distance) * top;
                                break;
                            default:
                                alpha = Mathf.Clamp01(0.5f - distance);
                                break;
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            // Slice past the corner radius (plus the contour and its antialiasing) so stretching
            // never distorts the curve. The radial shapes are stretched whole instead.
            int border = radius > 0f
                ? Mathf.Min(size / 2 - 1, Mathf.CeilToInt(radius + outline + 3f))
                : 0;

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ── Widget styling ────────────────────────────────────────────────────────────────────

        /// <summary>Prefix every child object this class adds is named with.</summary>
        public const string OverlayPrefix = "ToonSkin_";

        /// <summary>Smaller side under which a panel is built from the button shapes instead.</summary>
        const float k_CardGeometryThreshold = 140f;

        /// <summary>
        /// Stretched, non-interactive child image laid over <paramref name="host"/>. Reused if it
        /// already exists, so a second styling pass over the same widget is a no-op rather than a
        /// stack of duplicate contours.
        /// </summary>
        public static Image AddOverlay(GameObject host, string suffix, Sprite sprite, Color color)
        {
            string name = OverlayPrefix + suffix;
            var existing = host.transform.Find(name);

            var overlay = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)overlay.transform;

            if (existing == null)
            {
                rect.SetParent(host.transform, false);

                // Children draw over their parent's own graphic and in sibling order, so the
                // overlays go in front of the widget's fill but *before* whatever it already
                // contained. Appending instead would paint the contour and the sheen across the
                // button's own label.
                int overlayCount = 0;
                foreach (Transform child in host.transform)
                {
                    if (child != rect && child.name.StartsWith(OverlayPrefix, StringComparison.Ordinal))
                    {
                        overlayCount++;
                    }
                }

                rect.SetSiblingIndex(overlayCount);

                // A widget that lays its children out would otherwise treat the contour as
                // content and give it a slot of its own.
                if (host.GetComponent<LayoutGroup>() != null)
                {
                    overlay.AddComponent<LayoutElement>().ignoreLayout = true;
                }
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var image = overlay.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            // The contour of a short button is a big chunk of a 64px bitmap; without this the
            // 9-slice borders meet in the middle and Unity squashes the corners.
            image.pixelsPerUnitMultiplier = 1.6f;

            return image;
        }

        /// <summary>
        /// The offset cut-out shadow. Applied to the widget's own graphic, so it follows the shape
        /// of whatever sprite we just gave it.
        /// </summary>
        public static void AddDropShadow(Graphic graphic, float distance)
        {
            // Outline derives from Shadow, so this needs an exact type check — otherwise a text
            // outline on the same object would be mistaken for our shadow and retuned into one.
            Shadow shadow = null;
            foreach (var candidate in graphic.GetComponents<Shadow>())
            {
                if (candidate.GetType() == typeof(Shadow))
                {
                    shadow = candidate;
                    break;
                }
            }

            if (shadow == null)
            {
                shadow = graphic.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(0f, -distance);
            shadow.useGraphicAlpha = true;
        }

        /// <summary>
        /// Dresses a dialog / panel background as a card.
        /// </summary>
        /// <remarks>
        /// The card sprite's 9-slice corners are ~35px on a side. On a short panel those corners
        /// would meet in the middle and Unity would squash the curve into something lumpy, so
        /// anything narrower than <see cref="k_CardGeometryThreshold"/> is built out of the button
        /// shapes instead — same look, radius scaled to the widget.
        /// </remarks>
        public static void StyleCard(Image image)
        {
            var size = ((RectTransform)image.transform).rect.size;
            bool small = Mathf.Min(size.x, size.y) < k_CardGeometryThreshold;

            image.sprite = small ? ButtonFillSprite : CardFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            image.color = CardFill;

            var gloss = AddOverlay(image.gameObject, "Gloss", small ? ButtonGlossSprite : CardGlossSprite, GlossColor);
            var accent = AddOverlay(image.gameObject, "Accent", small ? ButtonOutlineSprite : CardOutlineSprite, AccentSoft);
            var ink = AddOverlay(image.gameObject, "Ink", small ? ButtonOutlineSprite : CardOutlineSprite, Ink);

            gloss.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            accent.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            // Ink is drawn from the same band at a lower multiplier, which is what makes the
            // contour read as thicker than the accent line inside it.
            ink.pixelsPerUnitMultiplier = small ? 0.85f : 0.62f;

            AddDropShadow(image, small ? 7f : 10f);
        }

        /// <summary>
        /// Dresses a button: sticker shape, the four state colours, and the hover/press motion.
        /// </summary>
        /// <param name="tintable">
        /// False when something else already owns this image's colour — <see cref="UITinter"/>
        /// paints the session tabs, for instance. Then only the shape changes.
        /// </param>
        public static void StyleButton(Selectable selectable, Image image, bool tintable)
        {
            image.sprite = ButtonFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.6f;

            if (tintable)
            {
                image.color = Color.white;
                selectable.transition = Selectable.Transition.ColorTint;
                selectable.targetGraphic = image;
                selectable.colors = new ColorBlock
                {
                    normalColor = ButtonFill,
                    highlightedColor = ButtonHighlight,
                    pressedColor = ButtonPressed,
                    selectedColor = ButtonFill,
                    disabledColor = ButtonDisabled,
                    colorMultiplier = 1f,
                    fadeDuration = 0.08f,
                };
            }

            AddOverlay(image.gameObject, "Gloss", ButtonGlossSprite, GlossColor);
            var accent = AddOverlay(image.gameObject, "Accent", ButtonOutlineSprite,
                new Color(Accent.r, Accent.g, Accent.b, 0f));
            AddOverlay(image.gameObject, "Ink", ButtonOutlineSprite, Ink).pixelsPerUnitMultiplier = 1.1f;

            AddDropShadow(image, 6f);

            AddMotion(selectable, image, accent);
        }

        /// <summary>
        /// Gives a button the hover/press motion, for callers that keep the widget's own art and
        /// only want the feel.
        /// </summary>
        public static void AddMotion(Selectable selectable, Graphic graphic, Graphic accentRing)
        {
            // UIHUDButton already shrinks itself on press. Two scripts driving one localScale
            // would fight over it every frame.
            if (selectable is UIHUDButton)
            {
                return;
            }

            var motion = graphic.GetComponent<ToonButtonMotion>();
            if (motion == null)
            {
                motion = graphic.gameObject.AddComponent<ToonButtonMotion>();
            }

            motion.Bind(selectable, accentRing);
        }

        /// <summary>Dresses an input field: sunken ground, thin accent contour.</summary>
        public static void StyleInput(Selectable selectable, Image image)
        {
            image.sprite = InputFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.6f;
            image.color = Color.white;

            selectable.transition = Selectable.Transition.ColorTint;
            selectable.targetGraphic = image;
            selectable.colors = new ColorBlock
            {
                normalColor = InputFill,
                highlightedColor = new Color(InputFill.r + 0.03f, InputFill.g + 0.05f, InputFill.b + 0.08f, InputFill.a),
                pressedColor = InputFill,
                selectedColor = InputFill,
                disabledColor = ButtonDisabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            AddOverlay(image.gameObject, "Accent", InputOutlineSprite, AccentSoft);
            AddOverlay(image.gameObject, "Ink", InputOutlineSprite, new Color(Ink.r, Ink.g, Ink.b, 0.9f))
                .pixelsPerUnitMultiplier = 1.1f;
        }

        /// <summary>Rounds off a bar (slider track, scrollbar handle) without recolouring it.</summary>
        public static void StyleBar(Image image)
        {
            image.sprite = InputFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2.2f;
        }

        /// <summary>
        /// Readability pass for menu text: a dark contour, which is what keeps light text legible
        /// over the 3D scene the menus are drawn on top of.
        /// </summary>
        /// <remarks>
        /// Fonts and sizes are deliberately left alone. The menus are laid out for the metrics
        /// they already have, and swapping a face here would overflow labels that fit today.
        /// </remarks>
        public static void StyleText(TMP_Text text, bool isLabel)
        {
            // Only tint text that has no colour of its own. Gold titles, red warnings and class
            // colours are information, not decoration.
            Color.RGBToHSV(text.color, out _, out float saturation, out float value);
            if (saturation < 0.18f && value > 0.35f)
            {
                text.color = HudSkin.TextPrimary;
            }

            // An outline costs a material instance, so it goes only where it earns one: headings
            // and button labels. On 12px body text it would close the letterforms up anyway.
            if (isLabel || text.fontSize >= 20f)
            {
                text.outlineColor = Ink;
                text.outlineWidth = isLabel ? 0.16f : 0.2f;
            }

            if (isLabel)
            {
                text.fontStyle |= FontStyles.UpperCase;
                text.characterSpacing = Mathf.Max(text.characterSpacing, 4f);
            }
        }

        /// <summary>Legacy <see cref="Text"/> equivalent of <see cref="StyleText(TMP_Text, bool)"/>.</summary>
        /// <remarks>
        /// Not a call into <see cref="HudSkin.StyleText"/>: that one owns the colour outright,
        /// which is right for a HUD it builds itself and wrong here, where a red error line or a
        /// gold heading was somebody's decision.
        /// </remarks>
        public static void StyleText(Text text)
        {
            Color.RGBToHSV(text.color, out _, out float saturation, out float value);
            if (saturation < 0.18f && value > 0.35f)
            {
                text.color = HudSkin.TextPrimary;
            }

            var outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
            outline.effectDistance = new Vector2(1.25f, -1.25f);
        }

        /// <summary>
        /// Puts a soft accent light behind <paramref name="target"/> — used on the game logo and
        /// on anything else that should look lit rather than printed. Inserted as a preceding
        /// sibling, so the target keeps its own place in the layout.
        /// </summary>
        public static Image AddBackGlow(RectTransform target, Color color, float scale)
        {
            string name = OverlayPrefix + "Glow";
            if (target.parent == null || target.parent.Find(name) != null)
            {
                return null;
            }

            var glow = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)glow.transform;
            rect.SetParent(target.parent, false);
            rect.SetSiblingIndex(target.GetSiblingIndex());

            rect.anchorMin = target.anchorMin;
            rect.anchorMax = target.anchorMax;
            rect.pivot = target.pivot;
            rect.anchoredPosition = target.anchoredPosition;
            rect.sizeDelta = target.sizeDelta * scale;

            var image = glow.GetComponent<Image>();
            image.sprite = GlowSprite;
            image.color = color;
            image.raycastTarget = false;

            return image;
        }
    }
}
