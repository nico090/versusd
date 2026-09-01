using System.Collections.Generic;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The game's icon set, drawn from vector outlines into sprites the first time each one is
    /// asked for. Tint them with the <see cref="Image"/> colour — every icon is baked white with
    /// only its alpha carrying the shape.
    /// </summary>
    /// <remarks>
    /// <para><b>Why drawn and not imported.</b> Same reason <see cref="HudSkin"/> and
    /// <see cref="ToonMenuSkin"/> bake their plates: an imported PNG has to survive an asset
    /// import the Editor likes to serve from its own cache, and it has to be wired into a prefab
    /// that a build can then lose. A shape defined in code is in the build the moment it
    /// compiles, and resizing the whole set is one constant.</para>
    ///
    /// <para><b>Why polygons.</b> Almost every icon here is one closed outline, so the primitive
    /// worth having is an exact polygon distance — concave shapes (the bolt, the crown, the
    /// arrow) come out as easily as convex ones, and the few round things left are a circle and a
    /// stroked segment. Everything is expressed in a [-1,1] square with y up, which keeps the
    /// vertex tables readable and makes the bake size a detail.</para>
    ///
    /// <para><b>Weight.</b> Icons sit next to short upper-case labels with a thick contour, so
    /// they are drawn solid and heavy rather than as hairline strokes — <see cref="k_Stroke"/> is
    /// the one number that decides how heavy, and it is deliberately closer to a marker than to a
    /// pen.</para>
    /// </remarks>
    public static class UIIcons
    {
        /// <summary>Every icon the UI can ask for.</summary>
        public enum Icon
        {
            Play,
            Pause,
            Gear,
            Close,
            Back,
            Forward,
            Refresh,
            Plus,
            Search,
            Lock,
            User,
            Users,
            Globe,
            Key,
            Sword,
            Swords,
            Shield,
            Skull,
            Trophy,
            Crown,
            Clock,
            Heart,
            Check,
            Warning,
            Signal,
            Exit,
            Speaker,
            Home,
            Copy,
            Dice,
            Bolt,
            Flag,
        }

        /// <summary>Bitmap side. Icons are drawn at button-label scale and never blown up much.</summary>
        const int k_Size = 96;

        /// <summary>Half-width of a stroked line, in the [-1,1] drawing space.</summary>
        const float k_Stroke = 0.1f;

        static readonly Dictionary<Icon, Sprite> s_Sprites = new Dictionary<Icon, Sprite>();

        /// <summary>The sprite for <paramref name="icon"/>, baked on first use and cached after.</summary>
        public static Sprite Get(Icon icon)
        {
            // A domain reload can leave a destroyed texture behind a live dictionary entry, so the
            // hit has to be checked against Unity's null, not the dictionary's.
            if (s_Sprites.TryGetValue(icon, out var cached) && cached != null)
            {
                return cached;
            }

            var sprite = Bake(icon);
            s_Sprites[icon] = sprite;
            return sprite;
        }

        /// <summary>
        /// The alpha mask of an icon, in a texture whose pixels can still be read.
        /// </summary>
        /// <remarks>
        /// <para>The runtime bake ends with <c>Apply(false, true)</c>, which throws the CPU copy
        /// away — the right trade for a sprite only ever sampled by the GPU. Editor tools that
        /// compose an icon into an image they are writing out need those pixels, and asking the
        /// cached sprite for them throws "Texture is not readable".</para>
        ///
        /// <para>Returns a fresh texture every call and does not cache it: this is a build-time
        /// path, and a readable copy of every icon kept alive for the session is exactly the memory
        /// the runtime bake is careful not to spend. <b>The caller owns it and should destroy
        /// it.</b></para>
        /// </remarks>
        public static Texture2D BakeReadable(Icon icon)
        {
            return BakeTexture(icon, keepReadable: true);
        }

        static Sprite Bake(Icon icon)
        {
            var texture = BakeTexture(icon, keepReadable: false);

            return Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
        }

        static Texture2D BakeTexture(Icon icon, bool keepReadable)
        {
            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false)
            {
                name = "UIIcon_" + icon,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[k_Size * k_Size];
            float half = k_Size * 0.5f;

            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    // Pixel centre in the [-1,1] drawing space, with a margin so a shape that
                    // reaches ±1 still has room for its antialiased edge.
                    var p = new Vector2((x + 0.5f - half) / (half - 2f), (y + 0.5f - half) / (half - 2f));

                    // Distance comes back in drawing units; one of those is (half-2) pixels, and
                    // covering half a pixel either side of the edge is what antialiases it.
                    float distance = Distance(icon, p) * (half - 2f);
                    float alpha = Mathf.Clamp01(0.5f - distance);

                    pixels[y * k_Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, !keepReadable);

            return texture;
        }

        // ── Shapes ────────────────────────────────────────────────────────────────────────────

        static float Distance(Icon icon, Vector2 p)
        {
            switch (icon)
            {
                case Icon.Play:
                    return Poly(p, k_PlayVerts);

                case Icon.Pause:
                    return Min(Box(p, new Vector2(-0.3f, 0f), new Vector2(0.15f, 0.6f), 0.06f),
                               Box(p, new Vector2(0.3f, 0f), new Vector2(0.15f, 0.6f), 0.06f));

                case Icon.Gear:
                    return Gear(p);

                case Icon.Close:
                    return Min(Segment(p, new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f), k_Stroke),
                               Segment(p, new Vector2(-0.5f, 0.5f), new Vector2(0.5f, -0.5f), k_Stroke));

                case Icon.Back:
                    return Chevron(p, 1f);

                case Icon.Forward:
                    return Chevron(p, -1f);

                case Icon.Refresh:
                    return Refresh(p);

                case Icon.Plus:
                    return Min(Segment(p, new Vector2(-0.55f, 0f), new Vector2(0.55f, 0f), k_Stroke),
                               Segment(p, new Vector2(0f, -0.55f), new Vector2(0f, 0.55f), k_Stroke));

                case Icon.Search:
                    return Min(Ring(p, new Vector2(-0.12f, 0.14f), 0.42f, k_Stroke),
                               Segment(p, new Vector2(0.14f, -0.16f), new Vector2(0.56f, -0.58f), k_Stroke * 1.15f));

                case Icon.Lock:
                    return Lock(p);

                case Icon.User:
                    return Person(p, Vector2.zero, 1f);

                case Icon.Users:
                    // The one behind is drawn first and smaller; the front one carries a cut-out
                    // gap so the two read as separate silhouettes rather than one blob.
                    return Min(Sub(Person(p, new Vector2(0.34f, 0.1f), 0.78f),
                                   Person(p, new Vector2(-0.24f, -0.06f), 0.92f)),
                               Person(p, new Vector2(-0.24f, -0.06f), 0.8f));

                case Icon.Globe:
                    return Globe(p);

                case Icon.Key:
                    return Key(p);

                case Icon.Sword:
                    return Sword(p, 0f);

                case Icon.Swords:
                    return Min(Sword(p, 0.62f), Sword(p, -0.62f));

                case Icon.Shield:
                    return Poly(p, k_ShieldVerts);

                case Icon.Skull:
                    return Skull(p);

                case Icon.Trophy:
                    return Trophy(p);

                case Icon.Crown:
                    return Poly(p, k_CrownVerts);

                case Icon.Clock:
                    return Min(Ring(p, Vector2.zero, 0.62f, k_Stroke),
                               Min(Segment(p, Vector2.zero, new Vector2(0f, 0.34f), k_Stroke * 0.9f),
                                   Segment(p, Vector2.zero, new Vector2(0.28f, -0.04f), k_Stroke * 0.9f)));

                case Icon.Heart:
                    return Heart(p);

                case Icon.Check:
                    return Min(Segment(p, new Vector2(-0.55f, 0.05f), new Vector2(-0.15f, -0.4f), k_Stroke * 1.2f),
                               Segment(p, new Vector2(-0.15f, -0.4f), new Vector2(0.58f, 0.45f), k_Stroke * 1.2f));

                case Icon.Warning:
                    return Warning(p);

                case Icon.Signal:
                    return Signal(p);

                case Icon.Exit:
                    return Exit(p);

                case Icon.Speaker:
                    return Speaker(p);

                case Icon.Home:
                    return Sub(Min(Poly(p, k_RoofVerts), Box(p, new Vector2(0f, -0.3f), new Vector2(0.5f, 0.42f), 0.08f)),
                               Box(p, new Vector2(0f, -0.46f), new Vector2(0.15f, 0.26f), 0.04f));

                case Icon.Copy:
                    return Min(BoxOutline(p, new Vector2(0.14f, -0.14f), new Vector2(0.42f, 0.5f), 0.1f),
                               Sub(BoxOutline(p, new Vector2(-0.2f, 0.18f), new Vector2(0.42f, 0.5f), 0.1f),
                                   Box(p, new Vector2(0.14f, -0.14f), new Vector2(0.54f, 0.62f), 0.1f)));

                case Icon.Dice:
                    return Dice(p);

                case Icon.Bolt:
                    return Poly(p, k_BoltVerts);

                case Icon.Flag:
                    return Min(Segment(p, new Vector2(-0.42f, -0.7f), new Vector2(-0.42f, 0.7f), k_Stroke * 0.9f),
                               Poly(p, k_FlagVerts));

                default:
                    return Circle(p, Vector2.zero, 0.5f);
            }
        }

        // Composed icons that need more than one expression, kept out of the switch so it stays
        // readable as a table of contents.

        static float Gear(Vector2 p)
        {
            float body = Ring(p, Vector2.zero, 0.4f, 0.19f);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                var rotated = Rotate(p, -angle);
                body = Min(body, Box(rotated, new Vector2(0f, 0.58f), new Vector2(0.16f, 0.16f), 0.05f));
            }

            return body;
        }

        static float Chevron(Vector2 p, float direction)
        {
            var tip = new Vector2(-0.24f * direction, 0f);
            var top = new Vector2(0.3f * direction, 0.54f);
            var bottom = new Vector2(0.3f * direction, -0.54f);

            return Min(Segment(p, top, tip, k_Stroke * 1.2f), Segment(p, tip, bottom, k_Stroke * 1.2f));
        }

        static float Refresh(Vector2 p)
        {
            // A ring with a bite taken out of its upper right, and an arrowhead parked in the
            // bite — which is what makes it read as "going round" rather than as a broken circle.
            float arc = Sub(Ring(p, Vector2.zero, 0.52f, k_Stroke * 1.05f),
                            Box(p, new Vector2(0.62f, 0.62f), new Vector2(0.62f, 0.62f), 0f));

            return Min(arc, Poly(p, k_ArrowHeadVerts));
        }

        static float Lock(Vector2 p)
        {
            float body = Box(p, new Vector2(0f, -0.26f), new Vector2(0.47f, 0.38f), 0.12f);
            float keyhole = Min(Circle(p, new Vector2(0f, -0.2f), 0.12f),
                                Box(p, new Vector2(0f, -0.36f), new Vector2(0.05f, 0.13f), 0.02f));

            // The shackle is a ring with its lower half removed, so it sits on the body instead
            // of hooping through it.
            float shackle = Sub(Ring(p, new Vector2(0f, 0.14f), 0.28f, k_Stroke),
                                Box(p, new Vector2(0f, -0.45f), new Vector2(0.6f, 0.6f), 0f));

            return Min(Sub(body, keyhole), shackle);
        }

        static float Person(Vector2 p, Vector2 offset, float scale)
        {
            var q = (p - offset) / scale;

            float head = Circle(q, new Vector2(0f, 0.4f), 0.27f);
            // The bust is the top half of a disc: one shape, and its curve matches the head's.
            float bust = Max(Circle(q, new Vector2(0f, -0.72f), 0.6f), -0.72f - q.y);

            return Min(head, bust) * scale;
        }

        static float Globe(Vector2 p)
        {
            float outline = Ring(p, Vector2.zero, 0.62f, k_Stroke * 0.9f);
            float meridian = EllipseRing(p, 0.3f, 0.62f, k_Stroke * 0.85f);
            float equator = Box(p, Vector2.zero, new Vector2(0.62f, k_Stroke * 0.85f), 0f);

            // The equator is a straight bar, so it has to be clipped back to the sphere or it
            // would poke out either side.
            return Min(outline, Min(meridian, Max(equator, Circle(p, Vector2.zero, 0.62f))));
        }

        static float Key(Vector2 p)
        {
            float bow = Ring(p, new Vector2(-0.4f, 0f), 0.28f, k_Stroke * 1.1f);
            float shaft = Segment(p, new Vector2(-0.12f, 0f), new Vector2(0.62f, 0f), k_Stroke * 0.85f);
            float teeth = Min(Segment(p, new Vector2(0.34f, 0f), new Vector2(0.34f, -0.3f), k_Stroke * 0.8f),
                              Segment(p, new Vector2(0.58f, 0f), new Vector2(0.58f, -0.24f), k_Stroke * 0.8f));

            return Min(bow, Min(shaft, teeth));
        }

        /// <summary>
        /// An upright sword, optionally leaned over by <paramref name="tilt"/> radians so two of
        /// them can be crossed. Drawn upright rather than on the diagonal because a diagonal
        /// blade thin enough to look like a blade reads as a scratch at icon size.
        /// </summary>
        static float Sword(Vector2 p, float tilt)
        {
            var q = Rotate(p, tilt);

            float blade = Poly(q, k_BladeVerts);
            float guard = Box(q, new Vector2(0f, -0.2f), new Vector2(0.46f, 0.085f), 0.04f);
            float grip = Box(q, new Vector2(0f, -0.46f), new Vector2(0.08f, 0.22f), 0.03f);
            float pommel = Circle(q, new Vector2(0f, -0.72f), 0.14f);

            return Min(Min(blade, guard), Min(grip, pommel));
        }

        static float Skull(Vector2 p)
        {
            float cranium = Circle(p, new Vector2(0f, 0.16f), 0.56f);
            float jaw = Box(p, new Vector2(0f, -0.44f), new Vector2(0.33f, 0.22f), 0.1f);
            float eyes = Min(Circle(p, new Vector2(-0.23f, 0.2f), 0.17f), Circle(p, new Vector2(0.23f, 0.2f), 0.17f));
            float nose = Poly(p, k_NoseVerts);
            // The jaw line: a thin gap between skull and jaw, so the two read as separate bones.
            float gap = Box(p, new Vector2(0f, -0.29f), new Vector2(0.2f, 0.045f), 0f);

            return Sub(Min(cranium, jaw), Min(Min(eyes, nose), gap));
        }

        static float Trophy(Vector2 p)
        {
            // The cup is the bottom half of a disc, capped by a straight rim; the handles are ring
            // stubs whose inner halves are cut away where they meet it.
            float cup = Max(Circle(p, new Vector2(0f, 0.52f), 0.5f), p.y - 0.52f);
            float rim = Box(p, new Vector2(0f, 0.56f), new Vector2(0.52f, 0.1f), 0.04f);
            float handles = Sub(Min(Ring(p, new Vector2(-0.52f, 0.35f), 0.2f, k_Stroke * 0.75f),
                                    Ring(p, new Vector2(0.52f, 0.35f), 0.2f, k_Stroke * 0.75f)),
                                Box(p, Vector2.zero, new Vector2(0.46f, 1f), 0f));
            float stem = Box(p, new Vector2(0f, -0.24f), new Vector2(0.11f, 0.3f), 0.03f);
            float foot = Box(p, new Vector2(0f, -0.62f), new Vector2(0.36f, 0.14f), 0.06f);

            return Min(Min(cup, rim), Min(handles, Min(stem, foot)));
        }

        static float Heart(Vector2 p)
        {
            float lobes = Min(Circle(p, new Vector2(-0.29f, 0.28f), 0.34f), Circle(p, new Vector2(0.29f, 0.28f), 0.34f));

            return Min(lobes, Poly(p, k_HeartTipVerts));
        }

        static float Warning(Vector2 p)
        {
            float bar = Box(p, new Vector2(0f, 0.06f), new Vector2(0.085f, 0.26f), 0.04f);
            float dot = Circle(p, new Vector2(0f, -0.34f), 0.11f);

            return Sub(Poly(p, k_WarningVerts), Min(bar, dot));
        }

        static float Signal(Vector2 p)
        {
            float bars = Box(p, new Vector2(-0.46f, -0.44f), new Vector2(0.13f, 0.22f), 0.05f);
            bars = Min(bars, Box(p, new Vector2(-0.05f, -0.28f), new Vector2(0.13f, 0.38f), 0.05f));
            bars = Min(bars, Box(p, new Vector2(0.36f, -0.08f), new Vector2(0.13f, 0.58f), 0.05f));

            return bars;
        }

        static float Exit(Vector2 p)
        {
            // A doorframe missing its right edge, with an arrow leaving through the gap.
            float frame = Sub(BoxOutline(p, new Vector2(-0.22f, 0f), new Vector2(0.44f, 0.68f), 0.1f),
                              Box(p, new Vector2(0.24f, 0f), new Vector2(0.28f, 0.34f), 0f));
            float shaft = Segment(p, new Vector2(0.02f, 0f), new Vector2(0.56f, 0f), k_Stroke * 0.85f);
            float head = Poly(p, k_ExitArrowVerts);

            return Min(frame, Min(shaft, head));
        }

        static float Speaker(Vector2 p)
        {
            float cone = Min(Box(p, new Vector2(-0.46f, 0f), new Vector2(0.16f, 0.24f), 0.04f), Poly(p, k_SpeakerVerts));
            // Two sound arcs: rings with everything left of the cone cut away.
            float waves = Min(Ring(p, new Vector2(-0.05f, 0f), 0.42f, k_Stroke * 0.7f),
                              Ring(p, new Vector2(-0.05f, 0f), 0.68f, k_Stroke * 0.7f));

            return Min(cone, Sub(waves, Box(p, new Vector2(-0.6f, 0f), new Vector2(0.75f, 1f), 0f)));
        }

        static float Dice(Vector2 p)
        {
            float box = BoxOutline(p, Vector2.zero, new Vector2(0.62f, 0.62f), 0.16f);
            float pips = Circle(p, new Vector2(-0.28f, 0.28f), 0.11f);
            pips = Min(pips, Circle(p, Vector2.zero, 0.11f));
            pips = Min(pips, Circle(p, new Vector2(0.28f, -0.28f), 0.11f));

            return Min(box, pips);
        }

        // ── Vertex tables ─────────────────────────────────────────────────────────────────────

        static readonly Vector2[] k_PlayVerts =
        {
            new Vector2(-0.4f, -0.62f), new Vector2(0.62f, 0f), new Vector2(-0.4f, 0.62f),
        };

        static readonly Vector2[] k_ShieldVerts =
        {
            new Vector2(-0.5f, 0.58f), new Vector2(0f, 0.74f), new Vector2(0.5f, 0.58f),
            new Vector2(0.5f, 0.02f), new Vector2(0.29f, -0.44f), new Vector2(0f, -0.74f),
            new Vector2(-0.29f, -0.44f), new Vector2(-0.5f, 0.02f),
        };

        static readonly Vector2[] k_CrownVerts =
        {
            new Vector2(-0.68f, 0.5f), new Vector2(-0.34f, 0.02f), new Vector2(0f, 0.56f),
            new Vector2(0.34f, 0.02f), new Vector2(0.68f, 0.5f), new Vector2(0.56f, -0.5f),
            new Vector2(-0.56f, -0.5f),
        };

        static readonly Vector2[] k_BoltVerts =
        {
            new Vector2(0.28f, 0.78f), new Vector2(-0.42f, 0.04f), new Vector2(-0.02f, 0.04f),
            new Vector2(-0.24f, -0.78f), new Vector2(0.44f, 0.06f), new Vector2(0.06f, 0.06f),
        };

        static readonly Vector2[] k_FlagVerts =
        {
            new Vector2(-0.42f, 0.68f), new Vector2(0.56f, 0.4f), new Vector2(-0.42f, 0.12f),
        };

        static readonly Vector2[] k_ArrowHeadVerts =
        {
            new Vector2(0.12f, 0.5f), new Vector2(0.74f, 0.5f), new Vector2(0.43f, 0.94f),
        };

        static readonly Vector2[] k_BladeVerts =
        {
            new Vector2(-0.14f, -0.22f), new Vector2(0.14f, -0.22f), new Vector2(0.14f, 0.5f),
            new Vector2(0f, 0.8f), new Vector2(-0.14f, 0.5f),
        };

        static readonly Vector2[] k_NoseVerts =
        {
            new Vector2(0f, 0.06f), new Vector2(0.13f, -0.18f), new Vector2(-0.13f, -0.18f),
        };

        static readonly Vector2[] k_HeartTipVerts =
        {
            new Vector2(-0.6f, 0.3f), new Vector2(0.6f, 0.3f), new Vector2(0f, -0.7f),
        };

        static readonly Vector2[] k_WarningVerts =
        {
            new Vector2(0f, 0.72f), new Vector2(0.76f, -0.6f), new Vector2(-0.76f, -0.6f),
        };

        static readonly Vector2[] k_RoofVerts =
        {
            new Vector2(0f, 0.76f), new Vector2(0.8f, 0.06f), new Vector2(-0.8f, 0.06f),
        };

        static readonly Vector2[] k_ExitArrowVerts =
        {
            new Vector2(0.34f, 0.34f), new Vector2(0.72f, 0f), new Vector2(0.34f, -0.34f),
        };

        static readonly Vector2[] k_SpeakerVerts =
        {
            new Vector2(-0.46f, 0.24f), new Vector2(-0.1f, 0.54f), new Vector2(-0.1f, -0.54f),
            new Vector2(-0.46f, -0.24f),
        };

        // ── Primitives ────────────────────────────────────────────────────────────────────────

        static float Min(float a, float b) => Mathf.Min(a, b);

        static float Max(float a, float b) => Mathf.Max(a, b);

        /// <summary>Everything in <paramref name="shape"/> that is not in <paramref name="hole"/>.</summary>
        static float Sub(float shape, float hole) => Mathf.Max(shape, -hole);

        static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;

        static float Ring(Vector2 p, Vector2 centre, float radius, float thickness)
            => Mathf.Abs((p - centre).magnitude - radius) - thickness;

        /// <summary>
        /// Ring of an ellipse. The exact distance to an ellipse needs a quartic solve; scaling the
        /// point into circle space and scaling the result back by the smaller radius is off by a
        /// few percent away from the axes, which no one can see in a 40px icon.
        /// </summary>
        static float EllipseRing(Vector2 p, float radiusX, float radiusY, float thickness)
        {
            var scaled = new Vector2(p.x / radiusX, p.y / radiusY);

            return Mathf.Abs(scaled.magnitude - 1f) * Mathf.Min(radiusX, radiusY) - thickness;
        }

        static float Box(Vector2 p, Vector2 centre, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;

            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        static float BoxOutline(Vector2 p, Vector2 centre, Vector2 half, float radius)
            => Mathf.Abs(Box(p, centre, half, radius)) - k_Stroke * 0.85f;

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float thickness)
        {
            var pa = p - a;
            var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));

            return (pa - ba * h).magnitude - thickness;
        }

        static Vector2 Rotate(Vector2 p, float radians)
        {
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);

            return new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
        }

        /// <summary>
        /// Exact signed distance to a closed polygon, negative inside. Distance is the nearest
        /// edge; the sign comes from a crossing count folded into the same loop.
        /// </summary>
        static float Poly(Vector2 p, Vector2[] vertices)
        {
            float squared = Vector2.Dot(p - vertices[0], p - vertices[0]);
            float sign = 1f;

            for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i, i++)
            {
                var edge = vertices[j] - vertices[i];
                var toPoint = p - vertices[i];
                var offset = toPoint - edge * Mathf.Clamp01(Vector2.Dot(toPoint, edge) / Vector2.Dot(edge, edge));

                squared = Mathf.Min(squared, Vector2.Dot(offset, offset));

                // Three conditions that are either all true or all false when the ray from p
                // crosses this edge; each crossing flips inside/outside.
                bool above = p.y >= vertices[i].y;
                bool below = p.y < vertices[j].y;
                bool leftOf = edge.x * toPoint.y > edge.y * toPoint.x;

                if ((above && below && leftOf) || (!above && !below && !leftOf))
                {
                    sign = -sign;
                }
            }

            return sign * Mathf.Sqrt(squared);
        }
    }
}
