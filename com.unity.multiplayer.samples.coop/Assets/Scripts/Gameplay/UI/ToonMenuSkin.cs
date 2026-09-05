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
    /// <para><b>The look.</b> A ruined Egyptian interior lit by two dying neon tubes. Four things
    /// carry it, and all four are cheap — a few stretched child images and a
    /// <see cref="Shadow"/> per widget:</para>
    /// <list type="number">
    /// <item><description><b>Cut corners, not round ones.</b> Every plate is a chamfered box — the
    /// batter of a pylon wall read at UI scale. Round corners are what made the menus look like a
    /// mobile toy; a 45° cut on the same silhouette reads as cut stone.</description></item>
    /// <item><description><b>An incised double line.</b> A thin brass frame set <i>inside</i> the
    /// contour, the way a border is carved inside the edge of a stela rather than drawn on
    /// it.</description></item>
    /// <item><description><b>Grain.</b> A tiled noise wash over the fill, so a card is a slab and
    /// not a flat colour.</description></item>
    /// <item><description><b>Two lights.</b> A blue tube along the top edge and a red one along the
    /// bottom, each with a soft halo. That is what gives a flat plate a top and a bottom, and it is
    /// the only saturated colour on the screen.</description></item>
    /// </list>
    /// </remarks>
    public static class ToonMenuSkin
    {
        // ── Palette ───────────────────────────────────────────────────────────────────────────
        // Stone first, light second. Every surface is basalt with a violet cast, and the only
        // colour in the room is the blue tube above and the violet one below. Nothing here is
        // warm: a single warm value in the field would read as dirt on the screen rather than as
        // part of the palette.

        /// <summary>The contour colour. Not pure black — pure black reads as a hole, not ink.</summary>
        public static readonly Color Ink = new Color(0.026f, 0.023f, 0.045f, 1f);

        /// <summary>Card / dialog ground: basalt.</summary>
        public static readonly Color CardFill = new Color(0.062f, 0.056f, 0.098f, 0.96f);

        /// <summary>Button ground, a step lighter than a card so it lifts off it.</summary>
        public static readonly Color ButtonFill = new Color(0.112f, 0.104f, 0.175f, 1f);

        /// <summary>Button under the pointer — as if one of the tubes had found it.</summary>
        public static readonly Color ButtonHighlight = new Color(0.190f, 0.185f, 0.300f, 1f);

        /// <summary>Button being pressed — darker, which sells the push together with the squash.</summary>
        public static readonly Color ButtonPressed = new Color(0.064f, 0.058f, 0.104f, 1f);

        /// <summary>Greyed-out button.</summary>
        public static readonly Color ButtonDisabled = new Color(0.098f, 0.096f, 0.130f, 0.55f);

        /// <summary>Input fields sink instead of lifting, so they get the darkest ground.</summary>
        public static readonly Color InputFill = new Color(0.034f, 0.030f, 0.058f, 0.95f);

        /// <summary>Chrome accent, the cold tube. Shared with the HUD on purpose.</summary>
        public static readonly Color Accent = HudSkin.AccentBlue;

        /// <summary>The violet tube: the second light, and the colour of anything going wrong.</summary>
        public static readonly Color Violet = HudSkin.AccentViolet;

        /// <summary>Lapis — what the carved lines in the stone are filled with.</summary>
        public static readonly Color Lapis = HudSkin.Lapis;

        /// <summary>Amethyst — the ornament cut into the band along the top of a card.</summary>
        public static readonly Color Amethyst = HudSkin.Amethyst;

        /// <summary>Accent at card strength: present, not shouting.</summary>
        public static readonly Color AccentSoft = new Color(Accent.r, Accent.g, Accent.b, 0.30f);

        /// <summary>The incised frame line set inside a plate's contour.</summary>
        public static readonly Color InlayColor = new Color(Lapis.r, Lapis.g, Lapis.b, 0.42f);

        /// <summary>
        /// The cornice band along the top of a card. Amethyst, not lapis: the band and the line
        /// touch each other, and two cuts of the same stone would read as one smudge.
        /// </summary>
        /// <summary>The corner scroll: amethyst, held back so it decorates without shouting.</summary>
        public static readonly Color FiligreeColor = new Color(Amethyst.r, Amethyst.g, Amethyst.b, 0.55f);

        public static readonly Color CorniceColor = new Color(Amethyst.r, Amethyst.g, Amethyst.b, 0.26f);

        /// <summary>The stone grain wash. Barely there by design — it is texture, not pattern.</summary>
        public static readonly Color GrainColor = new Color(0.82f, 0.86f, 1f, 0.055f);

        /// <summary>The hard cut-out shadow every surface drops.</summary>
        public static readonly Color ShadowColor = new Color(0f, 0f, 0.01f, 0.6f);

        /// <summary>Top-edge sheen laid over buttons and cards. Cool, like everything else.</summary>
        public static readonly Color GlossColor = new Color(0.86f, 0.90f, 1f, 0.06f);

        // ── Light rig ─────────────────────────────────────────────────────────────────────────
        // The two tubes as geometry and strength; their colours are Accent and Violet above.

        /// <summary>Thickness of the tube itself, in reference pixels.</summary>
        const float k_TubeThickness = 3f;

        /// <summary>How far a tube's halo spills past it.</summary>
        const float k_TubeHalo = 18f;

        /// <summary>Tube brightness on a card.</summary>
        const float k_CardTubeAlpha = 0.8f;

        /// <summary>Tube brightness on a button, where it is a hairline rather than a fixture.</summary>
        const float k_ButtonTubeAlpha = 0.45f;

        /// <summary>Height of the cornice band under a card's top tube.</summary>
        const float k_CorniceHeight = 12f;

        // ── Baked sprites ─────────────────────────────────────────────────────────────────────

        /// <summary>What <see cref="Bake"/> should write into the alpha channel.</summary>
        enum ShapeMode
        {
            /// <summary>Everything inside the chamfered box.</summary>
            Fill,

            /// <summary>A band straddling the box's edge — the contour.</summary>
            Outline,

            /// <summary>Inside the box, fading in towards the top edge.</summary>
            Gloss,

            /// <summary>Radial falloff, bright at the centre.</summary>
            Glow,

            /// <summary>Radial falloff, bright at the corners — a screen vignette.</summary>
            Vignette,

            /// <summary>Stone grain: tiled value noise, for breaking up a flat fill.</summary>
            Grain,

            /// <summary>
            /// Corner scroll: two nested quarter-arcs meeting the frame with a diamond pip
            /// between them, in all four corners. The ornament of the references this theme
            /// comes from, and the one thing the chamfered plate was still missing.
            /// </summary>
            Filigree,

            /// <summary>
            /// The cavetto cornice: tiled vertical bars, the band that runs along the top of an
            /// Egyptian wall. Fades downward so it reads as carved into the plate rather than
            /// stuck on it.
            /// </summary>
            Cornice,

            /// <summary>Vertical falloff, opaque at the top edge — the spill from a tube.</summary>
            Ramp,
        }

        static readonly Dictionary<string, Sprite> s_Sprites = new Dictionary<string, Sprite>();

        /// <summary>Chamfered ground for buttons and other one-line-tall widgets.</summary>
        public static Sprite ButtonFillSprite => Get("btn.fill", 64, 11f, 0f, ShapeMode.Fill);

        /// <summary>Contour for the same. Thick — this is what makes it read as cut, not drawn.</summary>
        public static Sprite ButtonOutlineSprite => Get("btn.line", 64, 11f, 4.5f, ShapeMode.Outline);

        /// <summary>Sheen for the same.</summary>
        public static Sprite ButtonGlossSprite => Get("btn.gloss", 64, 11f, 0f, ShapeMode.Gloss);

        /// <summary>The incised line inside a button's contour.</summary>
        public static Sprite ButtonInlaySprite => Get("btn.inlay", 64, 11f, 1.6f, ShapeMode.Outline, 6f);

        /// <summary>Chamfered ground for dialogs and panels — a deeper cut, since they are bigger.</summary>
        public static Sprite CardFillSprite => Get("card.fill", 96, 20f, 0f, ShapeMode.Fill);

        /// <summary>Contour for a card.</summary>
        public static Sprite CardOutlineSprite => Get("card.line", 96, 20f, 5.5f, ShapeMode.Outline);

        /// <summary>Sheen for a card.</summary>
        public static Sprite CardGlossSprite => Get("card.gloss", 96, 20f, 0f, ShapeMode.Gloss);

        /// <summary>The incised line inside a card's contour.</summary>
        public static Sprite CardInlaySprite => Get("card.inlay", 96, 20f, 2.2f, ShapeMode.Outline, 10f);

        /// <summary>Shallow-chamfer ground for input fields and bars.</summary>
        public static Sprite InputFillSprite => Get("input.fill", 64, 9f, 0f, ShapeMode.Fill);

        /// <summary>Contour for an input field — thinner, it is not a thing you press.</summary>
        public static Sprite InputOutlineSprite => Get("input.line", 64, 9f, 3f, ShapeMode.Outline);

        /// <summary>Soft radial light. Sits behind logos, under hovered buttons, and around a tube.</summary>
        public static Sprite GlowSprite => Get("glow", 128, 0f, 0f, ShapeMode.Glow);

        /// <summary>Full-screen corner darkening, so bright UI has something to sit against.</summary>
        public static Sprite VignetteSprite => Get("vignette", 128, 0f, 0f, ShapeMode.Vignette);

        /// <summary>Tiled stone grain, laid over a plate's fill.</summary>
        public static Sprite GrainSprite => Get("grain", 64, 0f, 0f, ShapeMode.Grain);

        /// <summary>Tiled cornice bars, for the band along the top of a card.</summary>
        /// <summary>Corner scrolls for a card. Only the corners are drawn; the rest is empty.</summary>
        public static Sprite CardFiligreeSprite => Get("card.filigree", 96, 20f, 0f, ShapeMode.Filigree, 10f);

        public static Sprite CorniceSprite => Get("cornice", 32, 0f, 0f, ShapeMode.Cornice);

        /// <summary>The glow a neon tube throws onto the surface it is mounted on.</summary>
        public static Sprite TubeHaloSprite => Get("tube.halo", 32, 0f, 0f, ShapeMode.Ramp);

        static Sprite Get(string key, int size, float chamfer, float outline, ShapeMode mode,
            float inset = 0f)
        {
            // A leaked domain reload can leave a destroyed texture behind a live dictionary entry,
            // so the check has to be Unity's null, not a plain dictionary hit.
            if (s_Sprites.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = Bake(key, size, chamfer, outline, mode, inset);
            s_Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Draws one shape into a small texture by sampling a signed distance field per pixel, and
        /// 9-slices it so the same bitmap serves a 40px button and a 900px dialog without the
        /// chamfer growing with the widget.
        /// </summary>
        /// <param name="chamfer">How much of each corner the 45° cut takes off.</param>
        /// <param name="inset">
        /// How far inside the edge an <see cref="ShapeMode.Outline"/> band sits. Zero puts it on
        /// the contour; a positive value carves it into the face, which is the incised frame.
        /// </param>
        static Sprite Bake(string key, int size, float chamfer, float outline, ShapeMode mode,
            float inset = 0f)
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
                    else if (mode == ShapeMode.Grain)
                    {
                        // Two octaves of hashed value noise, biased dark: most of the slab is
                        // untouched and the flecks are what catch the light.
                        float n = Noise(x, y, 1) * 0.65f + Noise(x, y, 3) * 0.35f;
                        alpha = Mathf.Pow(Mathf.Clamp01(n), 2.4f);
                    }
                    else if (mode == ShapeMode.Ramp)
                    {
                        // y counts up from the bottom of the bitmap, so this is brightest along
                        // the top edge. Squared, because light falls off faster than a line.
                        alpha = Mathf.Pow(y / (float)(size - 1), 2.4f);
                    }
                    else if (mode == ShapeMode.Filigree)
                    {
                        // Se pliega el tile a un cuadrante, asi la misma voluta cae en las
                        // cuatro esquinas; el 9-slice despues la mantiene en las esquinas
                        // del rect sea cual sea su tamano.
                        float u = Mathf.Min(x + 0.5f, size - 0.5f - x);
                        float v = Mathf.Min(y + 0.5f, size - 0.5f - y);
                        alpha = FiligreeAlpha(u, v, size * 0.30f);
                    }
                    else if (mode == ShapeMode.Cornice)
                    {
                        // Vertical bars with a gap, fading out towards the bottom of the tile.
                        float bar = (x % 8) < 3.2f ? 1f : 0.18f;
                        float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, size * 0.85f, y));
                        alpha = bar * fade;
                    }
                    else
                    {
                        // Signed distance to a CHAMFERED box centred on the texture, negative
                        // inside: the axis-aligned box, cut by the two 45° planes that take the
                        // corners off. The diagonal term is the whole Egyptian tell — it is what
                        // turns a rounded sticker into a piece of dressed stone.
                        float ax = Mathf.Abs(p.x);
                        float ay = Mathf.Abs(p.y);
                        float box = Mathf.Max(ax - extent, ay - extent);
                        float diagonal = (ax + ay - (2f * extent - chamfer)) * 0.70710678f;
                        float distance = Mathf.Max(box, diagonal);

                        switch (mode)
                        {
                            case ShapeMode.Outline:
                                // Straddles the contour when inset is zero; carved into the face
                                // when it is not.
                                alpha = Mathf.Clamp01(outline * 0.5f + 0.75f - Mathf.Abs(distance + inset));
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

            if (mode == ShapeMode.Grain || mode == ShapeMode.Cornice)
            {
                // Drawn with Image.Type.Tiled, which repeats the bitmap edge to edge. Clamping
                // here would leave a seam down every tile boundary.
                texture.wrapMode = TextureWrapMode.Repeat;
            }

            // Slice past the chamfer (plus the contour, the inset and the antialiasing) so
            // stretching never distorts the cut. Radial and tiled shapes are not sliced at all.
            int border = chamfer > 0f
                ? Mathf.Min(size / 2 - 1, Mathf.CeilToInt(chamfer + outline + inset + 3f))
                : 0;

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>
        /// Alpha of the corner scroll at a point, where <paramref name="u"/> and
        /// <paramref name="v"/> are distances from the nearest corner inwards — one evaluation
        /// serves all four corners once the tile is folded.
        /// </summary>
        /// <remarks>
        /// <para>Both arcs are centred on the diagonal at their own radius, so each one meets the
        /// two edges it sits between: the flourish reads as growing out of the frame rather than
        /// floating inside it.</para>
        ///
        /// <para>Only the quarter facing the corner is kept. Without that clamp the far side of
        /// the same circle sweeps back out past the nine-slice border, and everything beyond it
        /// lands in the edge band — which is stretched, so the two arc ends smear into a bowed
        /// line running the length of the card instead of four separate ornaments.</para>
        /// </remarks>
        static float FiligreeAlpha(float u, float v, float radius)
        {
            // Held off the bitmap edge, where clamping would smear the outermost pixel.
            u -= 2f;
            v -= 2f;
            if (u < 0f || v < 0f || u > radius || v > radius)
            {
                return 0f;
            }

            float outer = Mathf.Abs(Mathf.Sqrt((u - radius) * (u - radius) +
                                               (v - radius) * (v - radius)) - radius);
            float alpha = Mathf.Clamp01(1.6f - outer);

            float innerRadius = radius * 0.58f;
            float inner = Mathf.Abs(Mathf.Sqrt((u - innerRadius) * (u - innerRadius) +
                                               (v - innerRadius) * (v - innerRadius)) - innerRadius);
            alpha = Mathf.Max(alpha, Mathf.Clamp01(1.1f - inner) * 0.7f);

            float pip = radius * 0.40f;
            float diamond = Mathf.Abs(u - pip) + Mathf.Abs(v - pip);
            return Mathf.Clamp01(Mathf.Max(alpha, Mathf.Clamp01(radius * 0.13f - diamond)));
        }

        /// <summary>
        /// Hashed value noise in [0,1] at a given cell size, wrapping at the tile edge so the
        /// grain has no seam. Deterministic: the same bitmap every run, on every machine.
        /// </summary>
        static float Noise(int x, int y, int cell)
        {
            int cx = x / cell;
            int cy = y / cell;
            uint h = (uint)(cx * 73856093 ^ cy * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
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
            var image = AddOverlayChild(host, suffix);
            var rect = (RectTransform)image.transform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            // The contour of a short button is a big chunk of a 64px bitmap; without this the
            // 9-slice borders meet in the middle and Unity squashes the corners.
            image.pixelsPerUnitMultiplier = 1.6f;

            return image;
        }

        /// <summary>
        /// The child every overlay is: found by name if this widget has been styled before, made
        /// if not, and always parked ahead of the widget's own content in the sibling order.
        /// </summary>
        /// <remarks>
        /// Split out of <see cref="AddOverlay"/> because not every overlay fills its host — the
        /// tubes and the cornice are edge-anchored strips, and they need the same find-or-make,
        /// the same "never becomes layout content", and the same place in the draw order.
        /// </remarks>
        static Image AddOverlayChild(GameObject host, string suffix)
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

            rect.localScale = Vector3.one;

            var image = overlay.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A tiled wash over the whole widget — the stone grain, and anything else that has to
        /// repeat at its own scale instead of stretching with the plate.
        /// </summary>
        static Image AddTiledOverlay(GameObject host, string suffix, Sprite sprite, Color color)
        {
            var image = AddOverlayChild(host, suffix);
            var rect = (RectTransform)image.transform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            image.sprite = sprite;
            image.type = Image.Type.Tiled;
            image.color = color;

            return image;
        }

        /// <summary>
        /// Mounts one neon tube along the top or the bottom edge of a widget: the fixture itself
        /// (a hard line) plus the halo it throws onto the stone.
        /// </summary>
        /// <remarks>
        /// Two strips rather than a coloured contour, because a contour lights all four edges
        /// equally and that is exactly what makes UI look flat. A light above and a different
        /// light below is the whole trick: it tells the eye which way is up.
        /// </remarks>
        static void AddTube(GameObject host, string suffix, Color color, float alpha, bool top)
        {
            var halo = AddOverlayChild(host, suffix + "Halo");
            var haloRect = (RectTransform)halo.transform;
            haloRect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            haloRect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            haloRect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            haloRect.anchoredPosition = Vector2.zero;
            haloRect.sizeDelta = new Vector2(0f, k_TubeHalo);
            // The ramp bitmap is brightest along its own top edge; the bottom tube flips it
            // rather than baking a second sprite for the mirror image.
            haloRect.localScale = new Vector3(1f, top ? 1f : -1f, 1f);

            halo.sprite = TubeHaloSprite;
            halo.type = Image.Type.Simple;
            halo.color = new Color(color.r, color.g, color.b, alpha * 0.30f);

            var tube = AddOverlayChild(host, suffix);
            var tubeRect = (RectTransform)tube.transform;
            tubeRect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            tubeRect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            tubeRect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            tubeRect.anchoredPosition = Vector2.zero;
            tubeRect.sizeDelta = new Vector2(0f, k_TubeThickness);

            // No sprite: a bare Image is a white quad, which is what a lit tube seen edge-on is.
            tube.sprite = null;
            tube.type = Image.Type.Simple;
            tube.color = new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>
        /// The banded cornice under a card's top tube: the one piece of ornament in the kit, and
        /// the thing that says "Egypt" before any of the colours do.
        /// </summary>
        static void AddCornice(GameObject host)
        {
            var image = AddTiledOverlay(host, "Cornice", CorniceSprite, CorniceColor);
            var rect = (RectTransform)image.transform;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            // Below the tube, so the fixture reads as mounted on the band rather than through it.
            rect.anchoredPosition = new Vector2(0f, -k_TubeThickness);
            rect.sizeDelta = new Vector2(0f, k_CorniceHeight);
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
        /// <para>The card sprite's 9-slice corners are ~30px on a side. On a short panel those
        /// corners would meet in the middle and Unity would squash the cut into something lumpy,
        /// so anything narrower than <see cref="k_CardGeometryThreshold"/> is built out of the
        /// button shapes instead — same look, chamfer scaled to the widget.</para>
        ///
        /// <para>A card carries the full rig: grain over the stone, a brass line incised inside
        /// the contour, and both tubes. A small panel gets everything but the cornice, which needs
        /// a top edge long enough to read as a band rather than as a stripe.</para>
        /// </remarks>
        public static void StyleCard(Image image)
        {
            var size = ((RectTransform)image.transform).rect.size;
            bool small = Mathf.Min(size.x, size.y) < k_CardGeometryThreshold;

            image.sprite = small ? ButtonFillSprite : CardFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            image.color = CardFill;

            AddTiledOverlay(image.gameObject, "Grain", GrainSprite, GrainColor);

            var gloss = AddOverlay(image.gameObject, "Gloss", small ? ButtonGlossSprite : CardGlossSprite, GlossColor);
            var inlay = AddOverlay(image.gameObject, "Inlay", small ? ButtonInlaySprite : CardInlaySprite, InlayColor);
            var ink = AddOverlay(image.gameObject, "Ink", small ? ButtonOutlineSprite : CardOutlineSprite, Ink);

            gloss.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            inlay.pixelsPerUnitMultiplier = small ? 1.2f : 1f;
            // Ink is drawn from the same band at a lower multiplier, which is what makes the
            // contour read as thicker than the incised line inside it.
            ink.pixelsPerUnitMultiplier = small ? 0.85f : 0.62f;

            if (!small)
            {
                AddCornice(image.gameObject);

                // Al final, para que la voluta quede sobre el contorno y no debajo. El
                // multiplicador vuelve a 1 como el resto de las capas de card: el 1.6 que
                // AddOverlay usa para botones chicos encogeria el borde y partiria el ornamento.
                var filigree = AddOverlay(image.gameObject, "Filigree", CardFiligreeSprite, FiligreeColor);
                filigree.pixelsPerUnitMultiplier = 1f;
            }

            AddTube(image.gameObject, "TubeTop", Accent, k_CardTubeAlpha, top: true);
            AddTube(image.gameObject, "TubeBottom", Violet, k_CardTubeAlpha * 0.85f, top: false);

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

            AddTiledOverlay(image.gameObject, "Grain", GrainSprite, GrainColor);
            AddOverlay(image.gameObject, "Gloss", ButtonGlossSprite, GlossColor);

            // Starts fully transparent: ToonButtonMotion is what fades this ring in under the
            // pointer, and it is the one piece of the plate that is allowed to be bright.
            var accent = AddOverlay(image.gameObject, "Accent", ButtonOutlineSprite,
                new Color(Accent.r, Accent.g, Accent.b, 0f));
            AddOverlay(image.gameObject, "Ink", ButtonOutlineSprite, Ink).pixelsPerUnitMultiplier = 1.1f;

            // A hairline of the cold tube, not the full fixture. Buttons repeat down a screen and
            // two lit edges each would turn a menu into a strip of neon.
            AddTube(image.gameObject, "TubeTop", Accent, k_ButtonTubeAlpha, top: true);

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

            // No tube here on purpose: a field is a recess cut into the stone, and a recess is
            // the one place the light does not reach.
            AddOverlay(image.gameObject, "Accent", InputOutlineSprite, InlayColor);
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
            text.color = MapToPalette(text.color);

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
            text.color = MapToPalette(text.color);

            var outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
            outline.effectDistance = new Vector2(1.25f, -1.25f);
        }

        /// <summary>
        /// The palette's version of a colour somebody else chose.
        /// </summary>
        /// <remarks>
        /// <para>Plain light text simply becomes the body colour. Coloured text is the interesting
        /// case: this used to leave anything saturated alone, on the grounds that a gold heading or
        /// a red warning was information rather than decoration. That was right when the palette
        /// was warm and wrong now — the sample's screens carry two dozen gold and amber labels, and
        /// on a blue-violet screen they are the pieces that still look like a different game.</para>
        ///
        /// <para>So the <i>meaning</i> is kept and the hue is replaced: warm becomes amethyst,
        /// green stays "good" as teal, red stays "bad" as magenta, blue stays blue. A label that
        /// was shouting still shouts, in this game's voice.</para>
        /// </remarks>
        public static Color MapToPalette(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);

            if (saturation < 0.18f)
            {
                // Grey: light text is body copy, dark text is somebody's shadow and stays put.
                return value > 0.35f ? WithAlpha(HudSkin.TextPrimary, color.a) : color;
            }

            if (hue < 0.055f || hue > 0.88f)
            {
                return WithAlpha(UIKit.Danger, color.a);      // red
            }

            if (hue < 0.19f)
            {
                return WithAlpha(HudSkin.Amethyst, color.a);  // orange, amber, gold
            }

            if (hue < 0.45f)
            {
                return WithAlpha(UIKit.Positive, color.a);    // green
            }

            if (hue < 0.72f)
            {
                return WithAlpha(HudSkin.AccentBlue, color.a); // cyan, blue
            }

            return WithAlpha(HudSkin.AccentViolet, color.a);  // violet, already home
        }

        static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

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
