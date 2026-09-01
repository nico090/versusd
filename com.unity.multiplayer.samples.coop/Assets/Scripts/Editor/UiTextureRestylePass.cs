using System.Collections.Generic;
using System.IO;
using Unity.BossRoom.Gameplay.UI;
using UnityEditor;
using UnityEngine;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Repaints the sample's hand-drawn UI sprites in this project's own look.
    /// </summary>
    /// <remarks>
    /// <para><b>Where the look comes from.</b> Nothing here invents a style. The colours are
    /// <see cref="ToonMenuSkin"/>'s and the glyphs are <see cref="UIIcons"/>'s — the same two the
    /// code-built screens already draw from — so a repainted sprite and a kit-built button agree by
    /// construction rather than by someone matching them by eye. That is the whole reason this is
    /// an Editor pass over the shared palette instead of a folder of new PNGs.</para>
    ///
    /// <para><b>Why it overwrites in place.</b> These files are referenced from 96 places across
    /// scenes, prefabs and ScriptableObjects. Writing new files would mean re-pointing every one of
    /// them; writing over the originals keeps every reference, every GUID and — critically — every
    /// import setting, including the nine-slice borders that decide how a plate stretches. Each
    /// sprite is regenerated at its <i>original pixel size</i> for exactly that reason: those
    /// borders are in pixels, so a different size silently invalidates them.</para>
    ///
    /// <para><b>Originals are kept.</b> The first run copies each file it is about to touch into
    /// <c>Assets/Textures/UI/_Original/</c>, and never overwrites a backup after that — so
    /// re-running keeps comparing against the real original rather than against the last
    /// generated version. Revert puts them back.</para>
    ///
    /// <para><b>What is deliberately not here.</b> The character portraits, the help-screen
    /// illustrations and the logos are paintings, not chrome. A generator can rebuild a plate or a
    /// glyph honestly; it can only vandalise a portrait. Those need either re-rendering from the
    /// models or an artist.</para>
    /// </remarks>
    public static class UiTextureRestylePass
    {
        const string k_UiFolder = "Assets/Textures/UI";
        const string k_BackupFolder = "Assets/Textures/UI/_Original";

        enum Shape
        {
            /// <summary>Nine-slice plate: rounded rectangle, filled, with an accent border.</summary>
            Plate,

            /// <summary>Capsule for a progress or health bar.</summary>
            Bar,

            /// <summary>Round button plate with a glyph centred on it.</summary>
            IconButton,

            /// <summary>Glyph alone on transparency.</summary>
            Glyph,

            /// <summary>Round badge with a number on it.</summary>
            NumberBadge,

            /// <summary>
            /// A class portrait: the original's figure, relit as neon on a dark card.
            /// </summary>
            /// <remarks>
            /// The only one of these shapes that keeps any of the original art. These cards are a
            /// flat colour with a white figure on it, and the figure is the one thing worth
            /// keeping — it is what tells the two genders apart, and the four class colours behind
            /// it are so close in value that the stock set barely distinguishes the classes at all.
            /// So the figure is lifted out as a mask and relit, and everything around it is
            /// rebuilt from the palette.
            /// </remarks>
            Portrait,
        }

        readonly struct Entry
        {
            public readonly string FileName;
            public readonly Shape Shape;
            public readonly Color Fill;
            public readonly Color Stroke;
            public readonly UIIcons.Icon? Icon;
            public readonly int Number;

            public Entry(string fileName, Shape shape, Color fill, Color stroke,
                UIIcons.Icon? icon = null, int number = 0)
            {
                FileName = fileName;
                Shape = shape;
                Fill = fill;
                Stroke = stroke;
                Icon = icon;
                Number = number;
            }
        }

        static Color Accent => ToonMenuSkin.Accent;

        /// <summary>
        /// The neon a portrait is lit with: the class accent, dimmed hard when the seat is not the
        /// one selected.
        /// </summary>
        /// <remarks>
        /// Dimming rather than greying, because the inactive card still has to say <i>which</i>
        /// class it is — a desaturated portrait would make the row of seats unreadable at exactly
        /// the moment the player is scanning it.
        /// </remarks>
        static Color PortraitAccent(string className, bool active)
        {
            // The palette is keyed with capitalised class names.
            string key = char.ToUpperInvariant(className[0]) + className.Substring(1);
            Color accent = HeroAccentPalette.For(key);
            Color lifted = Color.Lerp(accent, Color.white, 0.25f);
            return active ? lifted : new Color(lifted.r, lifted.g, lifted.b, 0.35f);
        }

        static Entry[] BuildTable()
        {
            var entries = new List<Entry>
            {
                // ── Nine-slice plates ─────────────────────────────────────────────────────────
                new("ui_btn_blank", Shape.Plate, ToonMenuSkin.ButtonFill, Accent),
                new("ui_btn_disabled", Shape.Plate, ToonMenuSkin.ButtonDisabled, ToonMenuSkin.ButtonPressed),
                new("ui_dialog", Shape.Plate, ToonMenuSkin.CardFill, Accent),
                new("ui_scroll_frame", Shape.Plate, ToonMenuSkin.InputFill, ToonMenuSkin.AccentSoft),
                new("inputfield_Blank", Shape.Plate, ToonMenuSkin.InputFill, ToonMenuSkin.AccentSoft),
                new("ui_char_info_frame", Shape.Plate, ToonMenuSkin.CardFill, Accent),
                new("ui_hero_bg", Shape.Plate, ToonMenuSkin.CardFill, ToonMenuSkin.AccentSoft),
                new("ui_char_box_bg_selected", Shape.Plate, ToonMenuSkin.ButtonHighlight, Accent),
                new("ui_char_box_ovr_avail", Shape.Plate, new Color(0f, 0f, 0f, 0f), ToonMenuSkin.AccentSoft),
                new("ui_char_box_ovr_selected", Shape.Plate, new Color(0f, 0f, 0f, 0f), Accent),

                // ── Bars ──────────────────────────────────────────────────────────────────────
                new("ui_healthbar", Shape.Bar, UIKit.Positive, UIKit.Positive),
                new("ui_healthbar_bg", Shape.Bar, ToonMenuSkin.Ink, ToonMenuSkin.AccentSoft),

                // ── Round icon buttons ────────────────────────────────────────────────────────
                new("ui_sound_settings", Shape.IconButton, ToonMenuSkin.ButtonFill, Accent, UIIcons.Icon.Gear),
                new("ui_btn_exit", Shape.IconButton, ToonMenuSkin.ButtonFill, UIKit.Danger, UIIcons.Icon.Close),
                new("ui_btn_randomize", Shape.IconButton, ToonMenuSkin.ButtonFill, Accent, UIIcons.Icon.Dice),
                new("ui_emote_btn", Shape.IconButton, ToonMenuSkin.ButtonFill, Accent, UIIcons.Icon.User),
                new("ui_btn_ready_up", Shape.IconButton, ToonMenuSkin.ButtonFill, UIKit.Positive, UIIcons.Icon.Check),
                new("ui_btn_ready_dwn", Shape.IconButton, ToonMenuSkin.ButtonPressed, UIKit.Positive, UIIcons.Icon.Check),

                // ── Glyphs on transparency ────────────────────────────────────────────────────
                new("ui_checkmark", Shape.Glyph, Accent, Accent, UIIcons.Icon.Check),
                new("ui_connecting", Shape.Glyph, Accent, Accent, UIIcons.Icon.Refresh),
                new("ui_revive", Shape.Glyph, UIKit.Positive, UIKit.Positive, UIIcons.Icon.Heart),
                new("ui_action_pickup", Shape.Glyph, Accent, Accent, UIIcons.Icon.Plus),
                new("ui_action_putdown", Shape.Glyph, Accent, Accent, UIIcons.Icon.Back),
            };

            // ── Portraits ─────────────────────────────────────────────────────────────────────
            // Colour comes from HeroAccentPalette, so a portrait, the armour it belongs to and the
            // action-bar icons all move together when a class is retuned. Inactive is the same
            // card dimmed rather than a second drawing.
            foreach (var className in new[] { "tank", "archer", "mage", "rogue" })
            {
                foreach (var gender in new[] { "M", "F" })
                {
                    foreach (var state in new[] { "active", "inactive" })
                    {
                        entries.Add(new Entry($"ui_portrait_{className}{gender}_{state}",
                            Shape.Portrait, ToonMenuSkin.Ink, PortraitAccent(className, state == "active")));
                    }
                }
            }

            // ── Player tags ───────────────────────────────────────────────────────────────────
            // Eight seats, each a badge with its number. Drawn from one entry rather than eight
            // files of art, which is what made them inconsistent in the first place.
            for (int i = 1; i <= 8; i++)
            {
                entries.Add(new Entry($"ui_ptag_{i}", Shape.NumberBadge, ToonMenuSkin.ButtonFill, Accent,
                    null, i));
            }

            return entries.ToArray();
        }

        // ── Menu ──────────────────────────────────────────────────────────────────────────────

        [MenuItem("Boss Room/Style/10. Restyle UI Textures")]
        public static void Apply()
        {
            EnsureFolder(k_BackupFolder);

            int done = 0;
            int missing = 0;

            foreach (var entry in BuildTable())
            {
                string path = $"{k_UiFolder}/{entry.FileName}.png";
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[UiRestyle] {path} not found — skipped.");
                    missing++;
                    continue;
                }

                BackUpOnce(entry.FileName, path);

                try
                {
                    RepaintOne(entry, path);
                    done++;
                }
                catch (System.Exception e)
                {
                    // Isolated per sprite. One that threw used to take the whole run with it — and
                    // because the backup is written first, the console said nothing about the other
                    // thirty-nine simply never being reached.
                    Debug.LogError($"[UiRestyle] {entry.FileName} failed: {e.Message}");
                    missing++;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[UiRestyle] Repainted {done} sprite(s), {missing} skipped. " +
                      $"Originals kept in {k_BackupFolder}; 'Revert UI Textures' puts them back.");
        }

        [MenuItem("Boss Room/Style/Revert UI Textures")]
        public static void Revert()
        {
            int restored = 0;

            foreach (var entry in BuildTable())
            {
                string backup = $"{k_BackupFolder}/{entry.FileName}.png";
                string path = $"{k_UiFolder}/{entry.FileName}.png";

                if (!File.Exists(backup))
                {
                    continue;
                }

                File.Copy(backup, path, overwrite: true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                restored++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[UiRestyle] Restored {restored} original sprite(s).");
        }

        /// <summary>Repaints one sprite over its own file, at its own size.</summary>
        static void RepaintOne(Entry entry, string path)
        {
            var original = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (original == null)
            {
                throw new System.InvalidOperationException($"{path} could not be loaded.");
            }

            var source = LoadOriginalPixels(entry.FileName);
            var baked = Render(entry, original.width, original.height, source);

            File.WriteAllBytes(path, baked.EncodeToPNG());
            Object.DestroyImmediate(baked);
            if (source != null)
            {
                Object.DestroyImmediate(source);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Copies the original aside, once and only once.
        /// </summary>
        /// <remarks>
        /// The guard matters more than it looks: without it a second run would back up the
        /// already-repainted file, and the originals would be gone with no way to tell.
        /// </remarks>
        static void BackUpOnce(string fileName, string path)
        {
            string backup = $"{k_BackupFolder}/{fileName}.png";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────────────────────────

        static Texture2D Render(Entry entry, int width, int height, Texture2D source)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.clear;
            }

            switch (entry.Shape)
            {
                case Shape.Plate:
                    DrawRounded(pixels, width, height, Mathf.Min(width, height) * 0.18f,
                        Mathf.Max(2f, Mathf.Min(width, height) * 0.045f), entry.Fill, entry.Stroke);
                    break;

                case Shape.Bar:
                    DrawRounded(pixels, width, height, height * 0.5f,
                        Mathf.Max(2f, height * 0.09f), entry.Fill, entry.Stroke);
                    break;

                case Shape.IconButton:
                    DrawDisc(pixels, width, height, entry.Fill, entry.Stroke);
                    break;

                case Shape.NumberBadge:
                    DrawDisc(pixels, width, height, entry.Fill, entry.Stroke);
                    DrawDigit(pixels, width, height, entry.Number, entry.Stroke);
                    break;

                case Shape.Portrait:
                    DrawRounded(pixels, width, height, Mathf.Min(width, height) * 0.09f,
                        Mathf.Max(2f, Mathf.Min(width, height) * 0.02f), entry.Fill, entry.Stroke);
                    DrawNeonFigure(pixels, width, height, entry, source);
                    break;
            }

            if (entry.Icon.HasValue)
            {
                // Sized against the shorter side so a glyph on a wide plate stays a glyph.
                float inset = entry.Shape == Shape.Glyph ? 0.92f : 0.52f;
                BlitIcon(pixels, width, height, entry.Icon.Value, entry.Fill, inset);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Rounded rectangle, filled and stroked, drawn from its distance field.</summary>
        static void DrawRounded(Color[] pixels, int width, int height, float radius, float stroke,
            Color fill, Color strokeColor)
        {
            var half = new Vector2(width * 0.5f, height * 0.5f);
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y) - 1f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                    var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y))
                            - new Vector2(half.x - radius - 1f, half.y - radius - 1f);
                    float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                              + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;

                    pixels[y * width + x] = Shade(d, stroke, fill, strokeColor);
                }
            }
        }

        static void DrawDisc(Color[] pixels, int width, int height, Color fill, Color strokeColor)
        {
            var half = new Vector2(width * 0.5f, height * 0.5f);
            float radius = Mathf.Min(half.x, half.y) - 1f;
            float stroke = Mathf.Max(2f, radius * 0.10f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                    pixels[y * width + x] = Shade(p.magnitude - radius, stroke, fill, strokeColor);
                }
            }
        }

        /// <summary>
        /// One pixel of a stroked shape, from its signed distance.
        /// </summary>
        /// <remarks>
        /// Inside the shape but within <paramref name="stroke"/> of the edge is border; further in
        /// is fill; outside fades over one pixel, which is the whole of the antialiasing.
        /// </remarks>
        static Color Shade(float distance, float stroke, Color fill, Color strokeColor)
        {
            if (distance > 0.5f)
            {
                return Color.clear;
            }

            float coverage = Mathf.Clamp01(0.5f - distance);
            Color colour = distance > -stroke ? strokeColor : fill;
            colour.a *= coverage;
            return colour;
        }

        /// <summary>
        /// Draws one of <see cref="UIIcons"/>' glyphs into the buffer, scaled to fit.
        /// </summary>
        /// <remarks>
        /// Sampled from the kit's own baked sprite rather than redrawn here. That is the point:
        /// the settings gear on a prefab and the gear the pause menu builds at runtime are then
        /// the same drawing, and stay the same drawing when the kit's is changed.
        /// </remarks>
        static void BlitIcon(Color[] pixels, int width, int height, UIIcons.Icon icon,
            Color plateFill, float inset)
        {
            // BakeReadable, not Get: the cached runtime sprite has had its CPU copy discarded, so
            // reading a pixel out of it throws — which is what silently stopped this pass at its
            // first icon and left every entry after it untouched.
            var source = UIIcons.BakeReadable(icon);
            if (source == null)
            {
                Debug.LogWarning($"[UiRestyle] No sprite for icon {icon}.");
                return;
            }

            Color glyphColour = ContrastAgainst(plateFill);

            float side = Mathf.Min(width, height) * inset;
            float left = (width - side) * 0.5f;
            float bottom = (height - side) * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f - bottom) / side;
                if (v < 0f || v > 1f)
                {
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f - left) / side;
                    if (u < 0f || u > 1f)
                    {
                        continue;
                    }

                    float a = source.GetPixelBilinear(u, v).a;
                    if (a <= 0.003f)
                    {
                        continue;
                    }

                    int index = y * width + x;
                    var under = pixels[index];
                    var over = glyphColour;
                    over.a *= a;

                    // Straight source-over, so a glyph on a transparent plate keeps its own edge.
                    float outAlpha = over.a + under.a * (1f - over.a);
                    if (outAlpha <= 0.0001f)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    var rgb = (new Vector3(over.r, over.g, over.b) * over.a
                               + new Vector3(under.r, under.g, under.b) * under.a * (1f - over.a)) / outAlpha;
                    pixels[index] = new Color(rgb.x, rgb.y, rgb.z, outAlpha);
                }
            }

            // Ours to clean up: BakeReadable hands over a fresh texture on every call.
            Object.DestroyImmediate(source);
        }

        /// <summary>A glyph colour that will be legible on <paramref name="background"/>.</summary>
        static Color ContrastAgainst(Color background)
        {
            float luminance = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            return background.a < 0.2f || luminance < 0.4f ? ToonMenuSkin.Accent : UIKit.OnAccent;
        }

        /// <summary>
        /// A digit, drawn as segments of a seven-segment cell.
        /// </summary>
        /// <remarks>
        /// Deliberately not text: baking a TMP glyph from an Editor script means owning a font
        /// asset, a material and a render target for eight numerals that are never read as prose.
        /// Seven segments is legible at badge size and is nine lines of data.
        /// </remarks>
        static void DrawDigit(Color[] pixels, int width, int height, int digit, Color colour)
        {
            if (digit < 0 || digit > 9)
            {
                return;
            }

            // Segments: top, top-left, top-right, middle, bottom-left, bottom-right, bottom.
            bool[][] map =
            {
                new[] { true, true, true, false, true, true, true },      // 0
                new[] { false, false, true, false, false, true, false },  // 1
                new[] { true, false, true, true, true, false, true },     // 2
                new[] { true, false, true, true, false, true, true },     // 3
                new[] { false, true, true, true, false, true, false },    // 4
                new[] { true, true, false, true, false, true, true },     // 5
                new[] { true, true, false, true, true, true, true },      // 6
                new[] { true, false, true, false, false, true, false },   // 7
                new[] { true, true, true, true, true, true, true },       // 8
                new[] { true, true, true, true, false, true, true },      // 9
            };

            var on = map[digit];
            float h = Mathf.Min(width, height) * 0.26f;   // half the digit's height
            float w = h * 0.50f;                          // half its width
            float t = Mathf.Max(1.5f, h * 0.18f);         // segment half-thickness

            // A seven-segment 1 lights only the right-hand pair, so it sits off to the right of
            // its own cell. On a rectangle nobody notices; inside a round badge it reads as a
            // mistake, so the 1 is nudged back over the centre.
            float cx = width * 0.5f + (digit == 1 ? -w : 0f);
            float cy = height * 0.5f;

            var segments = new (Vector2 A, Vector2 B)[]
            {
                (new Vector2(cx - w, cy + h), new Vector2(cx + w, cy + h)), // top
                (new Vector2(cx - w, cy + h), new Vector2(cx - w, cy)),     // top-left
                (new Vector2(cx + w, cy + h), new Vector2(cx + w, cy)),     // top-right
                (new Vector2(cx - w, cy), new Vector2(cx + w, cy)),         // middle
                (new Vector2(cx - w, cy), new Vector2(cx - w, cy - h)),     // bottom-left
                (new Vector2(cx + w, cy), new Vector2(cx + w, cy - h)),     // bottom-right
                (new Vector2(cx - w, cy - h), new Vector2(cx + w, cy - h)), // bottom
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;

                    for (int s = 0; s < segments.Length; s++)
                    {
                        if (on[s])
                        {
                            d = Mathf.Min(d, SegmentDistance(p, segments[s].A, segments[s].B) - t);
                        }
                    }

                    if (d > 0.5f)
                    {
                        continue;
                    }

                    int index = y * width + x;
                    var over = colour;
                    over.a *= Mathf.Clamp01(0.5f - d);
                    var under = pixels[index];

                    float outAlpha = over.a + under.a * (1f - over.a);
                    if (outAlpha <= 0.0001f)
                    {
                        continue;
                    }

                    var rgb = (new Vector3(over.r, over.g, over.b) * over.a
                               + new Vector3(under.r, under.g, under.b) * under.a * (1f - over.a)) / outAlpha;
                    pixels[index] = new Color(rgb.x, rgb.y, rgb.z, outAlpha);
                }
            }
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        /// <summary>
        /// The untouched original, decoded so its pixels can be read.
        /// </summary>
        /// <remarks>
        /// <para>Read from the backup, never from the file about to be overwritten. Grading the
        /// current file would compound: every re-run would relight an already-relit image and the
        /// portraits would drift further from the source each time. Reading the backup makes the
        /// pass converge instead — running it ten times gives the same result as running it once,
        /// which is the property every other pass in this project has.</para>
        ///
        /// <para>Decoded from the PNG bytes rather than via the imported asset because an imported
        /// texture is not readable unless its importer says so, and flipping that on every
        /// portrait would be a persistent project change made for one Editor pass.</para>
        /// </remarks>
        static Texture2D LoadOriginalPixels(string fileName)
        {
            string backup = $"{k_BackupFolder}/{fileName}.png";
            string path = File.Exists(backup) ? backup : $"{k_UiFolder}/{fileName}.png";
            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }

        /// <summary>
        /// Lifts the figure out of the original card and draws it back as neon, with a bloom.
        /// </summary>
        /// <remarks>
        /// The figure is found by looking for what is bright and colourless: on these cards it is
        /// white on a saturated flat ground, so "light and unsaturated" separates the two cleanly
        /// without needing to know what colour the ground happens to be. That matters because the
        /// ground colour is the one thing that differs across the sixteen files.
        /// </remarks>
        static void DrawNeonFigure(Color[] pixels, int width, int height, Entry entry, Texture2D source)
        {
            if (source == null)
            {
                Debug.LogWarning($"[UiRestyle] {entry.FileName}: no original to lift a figure from.");
                return;
            }

            var mask = new float[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = source.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height);
                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    float saturation = max <= 0.001f ? 0f : (max - min) / max;

                    // Bright and near-colourless: the figure. Ramped rather than thresholded so
                    // the original's antialiased edge survives instead of turning into stairs.
                    float m = Mathf.Clamp01((max - 0.55f) / 0.35f) * Mathf.Clamp01(1f - saturation / 0.28f);
                    mask[y * width + x] = m * c.a;
                }
            }

            var glow = Blur(Blur(mask, width, height, Mathf.Max(2, Mathf.Min(width, height) / 22)),
                width, height, Mathf.Max(2, Mathf.Min(width, height) / 22));

            Color neon = entry.Stroke;
            for (int i = 0; i < pixels.Length; i++)
            {
                float halo = Mathf.Clamp01(glow[i] * 2.2f);
                if (halo > 0.002f)
                {
                    pixels[i] = Over(new Color(neon.r, neon.g, neon.b, neon.a * halo * 0.55f), pixels[i]);
                }

                float core = mask[i];
                if (core > 0.002f)
                {
                    // The core is pushed towards white so the figure still reads as a lit shape
                    // rather than as a flat block of the class colour.
                    Color hot = Color.Lerp(neon, Color.white, 0.45f);
                    pixels[i] = Over(new Color(hot.r, hot.g, hot.b, neon.a * core), pixels[i]);
                }
            }
        }

        /// <summary>Separable box blur over a single channel.</summary>
        static float[] Blur(float[] source, int width, int height, int radius)
        {
            var horizontal = new float[source.Length];
            var result = new float[source.Length];
            float span = radius * 2 + 1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        sum += source[y * width + Mathf.Clamp(x + k, 0, width - 1)];
                    }

                    horizontal[y * width + x] = sum / span;
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        sum += horizontal[Mathf.Clamp(y + k, 0, height - 1) * width + x];
                    }

                    result[y * width + x] = sum / span;
                }
            }

            return result;
        }

        /// <summary>Straight source-over of one colour onto another.</summary>
        static Color Over(Color over, Color under)
        {
            float outAlpha = over.a + under.a * (1f - over.a);
            if (outAlpha <= 0.0001f)
            {
                return Color.clear;
            }

            float r = (over.r * over.a + under.r * under.a * (1f - over.a)) / outAlpha;
            float g = (over.g * over.a + under.g * under.a * (1f - over.a)) / outAlpha;
            float b = (over.b * over.a + under.b * under.a * (1f - over.a)) / outAlpha;
            return new Color(r, g, b, outAlpha);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int lastSlash = folder.LastIndexOf('/');
            EnsureFolder(folder.Substring(0, lastSlash));
            AssetDatabase.CreateFolder(folder.Substring(0, lastSlash), folder.Substring(lastSlash + 1));
        }
    }
}
