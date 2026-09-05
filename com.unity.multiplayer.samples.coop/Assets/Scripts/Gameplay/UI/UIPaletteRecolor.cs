using System.Collections.Generic;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Re-paints the sample's imported UI art into this game's palette: reads a sprite's pixels,
    /// maps each one's brightness onto a ramp of the project's own colours, and hands back a new
    /// sprite with the same geometry.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> <see cref="ToonMenuRestyler"/> can only replace plates it
    /// recognises as blank; everything with real art on it — the character-select banner, the
    /// frames, the plates behind the action bar, the scroll frames — kept the sample's gold and
    /// brown. On a blue-violet screen those are the pieces that still look like a different game,
    /// and there is no amount of palette editing elsewhere that fixes them, because their colour
    /// is inside the texture.</para>
    ///
    /// <para><b>Why a duotone ramp and not a tint.</b> An <see cref="Image"/>'s colour multiplies:
    /// a blue tint over a gold banner gives dark mud, because multiplying opposite hues only ever
    /// removes light. Mapping brightness onto a ramp keeps the drawing — every highlight, edge and
    /// shadow stays exactly where the artist put it — and replaces only the hue.</para>
    ///
    /// <para><b>Why it can read textures that are not readable.</b> Imported sprites almost never
    /// have Read/Write enabled, and turning it on is an import setting this project cannot
    /// reliably change (the Editor serves its own cached copy of an asset). Blitting through a
    /// RenderTexture gets the pixels off the GPU regardless of the import settings, which is what
    /// makes this a pure runtime pass like everything else here.</para>
    ///
    /// <para>Results are cached per source sprite, so the same banner is converted once per run
    /// however many screens show it.</para>
    /// </remarks>
    public static class UIPaletteRecolor
    {
        /// <summary>Which end of the palette a sprite should be painted in.</summary>
        public enum Ramp
        {
            /// <summary>Chrome: basalt through lapis to a cold highlight. The default.</summary>
            Cold,

            /// <summary>Leaving, closing, destroying — the magenta family.</summary>
            Danger,
        }

        /// <summary>
        /// Ramp stops, dark to light. Deliberately not a straight line between two colours: the
        /// midtones carry most of a UI texture's pixels, so they get their own stop (lapis) and
        /// only the last quarter goes violet. Without that split the art comes back looking
        /// washed out rather than lit.
        /// </summary>
        static readonly (float at, Color color)[] k_Cold =
        {
            (0f, new Color(0.030f, 0.028f, 0.052f)),
            (0.35f, new Color(0.105f, 0.115f, 0.210f)),
            (0.62f, new Color(0.250f, 0.400f, 0.780f)),
            (0.84f, new Color(0.560f, 0.470f, 0.930f)),
            (1f, new Color(0.910f, 0.930f, 1f)),
        };

        static readonly (float at, Color color)[] k_Danger =
        {
            (0f, new Color(0.055f, 0.020f, 0.045f)),
            (0.38f, new Color(0.260f, 0.060f, 0.160f)),
            (0.70f, new Color(0.760f, 0.190f, 0.430f)),
            (1f, new Color(1f, 0.780f, 0.900f)),
        };

        static readonly Dictionary<Sprite, Sprite> s_Cache = new Dictionary<Sprite, Sprite>();

        /// <summary>
        /// The palette version of <paramref name="source"/>, converted on first use and cached
        /// after. Returns the sprite unchanged if its pixels cannot be read at all.
        /// </summary>
        public static Sprite Get(Sprite source, Ramp ramp)
        {
            if (source == null)
            {
                return null;
            }

            // Unity's null, not the dictionary's: a domain reload can destroy the texture behind a
            // live entry and leave the reference looking fine.
            if (s_Cache.TryGetValue(source, out var cached))
            {
                return cached != null ? cached : source;
            }

            var converted = Convert(source, ramp);
            s_Cache[source] = converted;

            return converted != null ? converted : source;
        }

        static Sprite Convert(Sprite source, Ramp ramp)
        {
            var texture = source.texture;
            if (texture == null)
            {
                return null;
            }

            var pixels = ReadPixels(texture);
            if (pixels == null)
            {
                return null;
            }

            var stops = ramp == Ramp.Danger ? k_Danger : k_Cold;

            // First pass: the range this texture actually uses. Several of the sample's plates
            // live inside a fifth of the scale — a mid-brown banner is 0.2 to 0.3 luma and
            // nothing else — and mapping that straight onto the ramp gives one flat navy slab
            // with the drawing gone. Stretching first is what keeps the highlights, the bevels
            // and the carved edges that made it worth repainting instead of replacing.
            float darkest = 1f;
            float lightest = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a <= 0.002f)
                {
                    continue;
                }

                float l = Luma(pixels[i]);
                darkest = Mathf.Min(darkest, l);
                lightest = Mathf.Max(lightest, l);
            }

            // Below this the texture is one flat colour, and stretching it would be amplifying
            // compression noise into a gradient.
            float range = lightest - darkest;
            bool stretch = range > 0.06f;

            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a <= 0.002f)
                {
                    // Fully transparent pixels still carry a colour, and a texture's transparent
                    // border is often pure white. Left alone it bleeds into the edge when the
                    // sprite is filtered.
                    pixels[i] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float luma = Luma(pixel);
                var mapped = Sample(stops, stretch ? (luma - darkest) / range : luma);
                pixels[i] = new Color(mapped.r, mapped.g, mapped.b, pixel.a);
            }

            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
            {
                name = texture.name + "_Palette",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = texture.wrapMode,
                filterMode = texture.filterMode,
            };

            copy.SetPixels(pixels);
            copy.Apply(false, true);

            // Same rect, border and pivot as the original, so the new sprite drops into the same
            // Image without moving or re-slicing anything. A packed sprite's pixels are somewhere
            // inside an atlas, and textureRect is the only rect that says where.
            var rect = source.packed ? source.textureRect : source.rect;
            var pivot = new Vector2(
                rect.width > 0f ? source.pivot.x / rect.width : 0.5f,
                rect.height > 0f ? source.pivot.y / rect.height : 0.5f);

            var result = Sprite.Create(copy, rect, pivot, source.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, source.border);
            result.name = source.name + "_Palette";
            result.hideFlags = HideFlags.HideAndDontSave;

            return result;
        }

        /// <summary>
        /// The texture's pixels, whether or not the import settings made it readable: a straight
        /// read when they did, and a GPU round-trip through a RenderTexture when they did not.
        /// </summary>
        static Color[] ReadPixels(Texture2D texture)
        {
            if (texture.isReadable)
            {
                return texture.GetPixels();
            }

            var temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;

            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;

                var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                readable.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
                readable.Apply(false, false);

                var pixels = readable.GetPixels();
                Object.Destroy(readable);

                return pixels;
            }
            catch (System.Exception e)
            {
                // A texture the GPU will not hand back is not worth taking the menu down for; the
                // caller keeps the original art.
                Debug.LogWarning($"[UI] Could not read '{texture.name}' for recolouring: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        /// <summary>
        /// Rec. 601 luma: the weights the eye actually uses, so a gold pixel and the grey pixel
        /// that looks equally bright land on the same stop.
        /// </summary>
        static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        /// <summary>The ramp colour at <paramref name="t"/>, interpolating between stops.</summary>
        static Color Sample((float at, Color color)[] stops, float t)
        {
            t = Mathf.Clamp01(t);

            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].at)
                {
                    continue;
                }

                float span = stops[i].at - stops[i - 1].at;
                float k = span > 0f ? (t - stops[i - 1].at) / span : 0f;
                return Color.Lerp(stops[i - 1].color, stops[i].color, k);
            }

            return stops[stops.Length - 1].color;
        }
    }
}
