using System.IO;
using UnityEditor;
using UnityEngine;
using Action = Unity.BossRoom.Gameplay.Actions.Action;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Draws proper icons for the three new powers — Twisting Slash, Meteor, Frost Nova — and
    /// assigns them to the action assets, replacing the placeholders borrowed from other skills.
    /// </summary>
    /// <remarks>
    /// <para><b>Why borrowed icons had to go.</b> The action bar is the one place a power is
    /// identified at a glance, and the placeholder scheme put the SAME image on two different
    /// buttons of the same class (Skill3 borrowed Skill2's art). Two buttons that look identical
    /// and do different things is worse than an ugly icon — it is a misread under pressure.</para>
    ///
    /// <para><b>Why they are drawn in code.</b> There is no icon art for these powers anywhere in
    /// the project (the stock set stops at each class's shipped skills), and the restyle pipeline
    /// is deliberately asset-free where it can be. Each icon is rendered once into a small PNG
    /// with signed-distance functions — the same technique the HUD's rounded panels use — which
    /// yields clean antialiased shapes that read at 64px. Simple geometric glyphs on a dark plate
    /// with a class-coloured rim also happen to match the cyberpunk restyle better than the stock
    /// painted icons do.</para>
    ///
    /// <para>Re-running overwrites the PNGs in place (same GUIDs, nothing re-wires) and re-assigns
    /// them. <c>NewPowersInstaller.BorrowIconFrom</c> only fills a null icon, so re-running the
    /// installer later cannot stomp these.</para>
    /// </remarks>
    public static class HeroIconPass
    {
        const int k_Size = 128;
        const string k_OutputFolder = "Assets/Textures/UI/Generated";

        // The glyph is drawn in the power's own colour; only the rim is class-coded. A frost
        // burst tinted Tank-crimson would win the style war and lose the "this is ice" one.
        static readonly Color k_PlateColor = new Color(0.043f, 0.055f, 0.086f, 1f);

        struct IconDef
        {
            public string FileName;
            public string ActionAssetPath;
            public string ClassName;   // rim colour comes from HeroAccentPalette
            public Color GlyphColor;
            public System.Func<Vector2, float> Glyph; // SDF, pixels, centred on (0,0)
        }

        [MenuItem("Boss Room/Actions/Generate Icons For New Powers")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(k_OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/Textures/UI", "Generated");
            }

            var icons = new[]
            {
                // ── Tank ──────────────────────────────────────────────────────────────────────
                Def("ui_tank_atk.png", "Tank/TankBaseAttack", "Tank", HammerGlyph),
                Def("ui_tank_skill1.png", "Tank/TankShieldBuff", "Tank", ShieldBuffGlyph),
                Def("ui_tank_skill2.png", "Tank/TankShieldRush", "Tank", ShieldRushGlyph),
                Def("ui_tank_skill3.png", "Tank/TankFrostNova", "Tank", FrostNovaGlyph),

                // ── Archer ────────────────────────────────────────────────────────────────────
                Def("ui_archer_atk.png", "Archer/ArcherBaseAttack", "Archer", ArrowGlyph),
                Def("ui_archer_skill1.png", "Archer/ArcherChargedShot", "Archer", ChargedShotGlyph),
                Def("ui_archer_skill2.png", "Archer/ArcherVolley", "Archer", VolleyGlyph),

                // ── Mage ──────────────────────────────────────────────────────────────────────
                Def("ui_mage_atk.png", "Mage/MageBaseAttack", "Mage", ArcaneBoltGlyph),
                Def("ui_mage_skill1.png", "Mage/MageHeal", "Mage", HealGlyph),
                Def("ui_mage_skill3.png", "Mage/MageMeteorStrike", "Mage", MeteorGlyph),

                // ── Rogue ─────────────────────────────────────────────────────────────────────
                Def("ui_rogue_atk.png", "Rogue/RogueBaseAttack", "Rogue", DaggerGlyph),
                Def("ui_rogue_skill1.png", "Rogue/RogueDashAttack", "Rogue", DashGlyph),
                Def("ui_rogue_skill2.png", "Rogue/RogueStealthMode", "Rogue", StealthGlyph),
                Def("ui_rogue_skill3.png", "Rogue/RogueTwistingSlash", "Rogue", TwistingSlashGlyph),

                // TankTestAoeAttack is deliberately absent: it is a sample test asset that no
                // hero's action bar points at, and giving it an icon would only imply otherwise.
            };

            int done = 0;
            foreach (var icon in icons)
            {
                if (GenerateOne(icon))
                {
                    done++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Icons] Generated and assigned {done}/{icons.Length} power icon(s) under {k_OutputFolder}. " +
                      "Re-run any time — the files are replaced in place and nothing re-wires.");
        }

        /// <summary>
        /// One row of the table above. Exists so the table reads as a list of decisions — which
        /// glyph goes on which ability — instead of fifteen near-identical object initialisers.
        /// </summary>
        static IconDef Def(string fileName, string actionPath, string className,
            System.Func<Vector2, float> glyph)
        {
            return new IconDef
            {
                FileName = fileName,
                ActionAssetPath = $"Assets/GameData/Action/{actionPath}.asset",
                ClassName = className,
                GlyphColor = GlyphColorFor(className),
                Glyph = glyph,
            };
        }

        /// <summary>
        /// The class accent, lifted towards white so a thin stroke still reads on the icon plate.
        /// </summary>
        /// <remarks>
        /// Taken from <see cref="HeroAccentPalette"/> rather than hand-picked per power, which is
        /// the whole reason that table exists: the icons now move with the same colour the armour
        /// and the weapons use, and a whole class's bar reads as one set instead of three unrelated
        /// glyphs that happened to be drawn on different days.
        /// </remarks>
        static Color GlyphColorFor(string className)
        {
            return Color.Lerp(HeroAccentPalette.For(className), Color.white, 0.42f);
        }

        static bool GenerateOne(IconDef icon)
        {
            string path = $"{k_OutputFolder}/{icon.FileName}";
            File.WriteAllBytes(path, Render(icon).EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            // Imported as a UI sprite: no mips (it is never minified in 3D), uncompressed
            // (128px — compression saves nothing and chews the thin lines).
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 128;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var action = AssetDatabase.LoadAssetAtPath<Action>(icon.ActionAssetPath);
            if (sprite == null || action == null)
            {
                Debug.LogWarning($"[Icons] Could not assign {icon.FileName}: " +
                                 (action == null ? $"no action at {icon.ActionAssetPath} — run 'Install New Powers' first." : "sprite import failed."));
                return false;
            }

            action.Config.Icon = sprite;
            EditorUtility.SetDirty(action);
            return true;
        }

        // ── Rendering ─────────────────────────────────────────────────────────────────────────

        static Texture2D Render(IconDef icon)
        {
            Color rim = HeroAccentPalette.For(icon.ClassName);
            var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
            var pixels = new Color[k_Size * k_Size];

            float half = k_Size * 0.5f;

            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);

                    // Plate: rounded box with a class-coloured rim.
                    float plate = RoundedBox(p, half - 4f, 22f);
                    float plateAlpha = Mathf.Clamp01(0.5f - plate);
                    float rimAlpha = Mathf.Clamp01(2f - Mathf.Abs(plate)) * 0.85f;

                    var color = k_PlateColor;
                    // Subtle grounding gradient so the plate doesn't read as a flat sticker.
                    color = Color.Lerp(color, Color.black, Mathf.Clamp01((-p.y + half) / (k_Size * 2.2f)));
                    color = Color.Lerp(color, rim, rimAlpha * 0.9f);

                    // Glyph: hot core plus a soft halo, which is the icon wearing the same bloom
                    // the world now has.
                    float glyph = icon.Glyph(p);
                    float glow = Mathf.Exp(-Mathf.Max(glyph, 0f) / 6f) * 0.4f;
                    float core = Mathf.Clamp01(0.75f - glyph);

                    color = Color.Lerp(color, icon.GlyphColor, Mathf.Clamp01(glow) * plateAlpha);
                    var hot = Color.Lerp(icon.GlyphColor, Color.white, Mathf.Clamp01(0.45f - glyph * 0.18f));
                    color = Color.Lerp(color, hot, core * plateAlpha);

                    color.a = Mathf.Max(plateAlpha, rimAlpha);
                    pixels[y * k_Size + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // ── Glyphs ────────────────────────────────────────────────────────────────────────────

        /// <summary>A sword with two arrows orbiting it — the blade caught mid-spin.</summary>
        /// <remarks>
        /// The previous version was three swept arcs around a hub, which read as a loading
        /// spinner: nothing in it said "sword". Drawing the weapon itself and putting the motion
        /// around it costs the same few primitives and says both things at once. The blade's point
        /// and the pommel deliberately break out past the orbit ring — that overlap is what stops
        /// the two halves reading as one flat badge.
        /// </remarks>
        /// <summary>
        /// A cyclone of blade-strokes around a swung sword.
        /// </summary>
        /// <remarks>
        /// <para>Follows the read MU uses for its own Twisting Slash — the whirl is the subject and
        /// the blade sits inside it — but drawn in this kit's flat stroke language rather than as a
        /// painted miniature, so it sits beside the other action-bar icons instead of looking like
        /// something pasted in from another game.</para>
        ///
        /// <para>The previous version put an upright sword inside two arrows going round it, which
        /// reads as "rotate this item" — the same glyph a refresh button uses. Three tapered
        /// strokes is what turns it into motion instead: a stroke that thins along its sweep has a
        /// direction, where an arrowhead at this size just fills in as a blob.</para>
        /// </remarks>
        static float TwistingSlashGlyph(Vector2 p)
        {
            // Tilted, so the blade reads as caught mid-swing rather than standing at attention.
            var q = Rotate(p, -0.6f);

            float d = Poly(q, k_SlashBladeVerts);
            d = Mathf.Min(d, Segment(q, new Vector2(-15f, -6f), new Vector2(15f, -6f), 4f)); // crossguard
            d = Mathf.Min(d, Segment(q, new Vector2(0f, -9f), new Vector2(0f, -22f), 4f));   // grip
            d = Mathf.Min(d, Circle(q - new Vector2(0f, -26f), 5f));                         // pommel

            // Three strokes at 120 degrees, each thinning along its sweep, so they read as the
            // trail of one continuous spin rather than three static rings.
            for (int i = 0; i < 3; i++)
            {
                d = Mathf.Min(d, TaperedArc(p, 40f, i * 120f * Mathf.Deg2Rad, 74f * Mathf.Deg2Rad, 7f, 1.2f));
            }

            return d;
        }

        /// <summary>The blade for the slash icon: the shape of <see cref="k_BladeVerts"/>, shrunk
        /// to leave the outer ring to the strokes.</summary>
        static readonly Vector2[] k_SlashBladeVerts =
        {
            new Vector2(-6f, -4f), new Vector2(6f, -4f), new Vector2(6f, 19f),
            new Vector2(0f, 33f), new Vector2(-6f, 19f),
        };

        /// <summary>The blade, from the shoulders above the guard up to the point.</summary>
        static readonly Vector2[] k_BladeVerts =
        {
            new Vector2(-9f, -6f), new Vector2(9f, -6f), new Vector2(9f, 28f),
            new Vector2(0f, 48f), new Vector2(-9f, 28f),
        };

        /// <summary>
        /// One curved arrow on the orbit ring: an arc from <paramref name="startDegrees"/>
        /// sweeping anticlockwise, with a head on its leading end.
        /// </summary>
        static float OrbitArrow(Vector2 p, float startDegrees, float sweepDegrees)
        {
            const float k_Radius = 38f;
            const float k_Thickness = 4.5f;
            const float k_HeadLength = 17f;

            float start = startDegrees * Mathf.Deg2Rad;
            float sweep = sweepDegrees * Mathf.Deg2Rad;

            float d = Arc(p, k_Radius, start, sweep, k_Thickness);

            // The head goes on the leading end, with both barbs swept back along the tangent, so
            // the arrow reads as travelling around the circle instead of pointing out of it.
            float tipAngle = start + sweep;
            var tip = new Vector2(Mathf.Cos(tipAngle), Mathf.Sin(tipAngle)) * k_Radius;
            var back = new Vector2(Mathf.Sin(tipAngle), -Mathf.Cos(tipAngle));

            d = Mathf.Min(d, Segment(p, tip, tip + Rotate(back, 0.62f) * k_HeadLength, 4f));
            d = Mathf.Min(d, Segment(p, tip, tip + Rotate(back, -0.62f) * k_HeadLength, 4f));

            return d;
        }

        /// <summary>A rock streaking in from the upper right toward a crater rim.</summary>
        static float MeteorGlyph(Vector2 p)
        {
            float d = Circle(p - new Vector2(14f, 10f), 17f); // the rock

            // Trail, three streaks of different weights.
            d = Mathf.Min(d, Segment(p, new Vector2(26f, 22f), new Vector2(48f, 44f), 5f));
            d = Mathf.Min(d, Segment(p, new Vector2(32f, 8f), new Vector2(52f, 28f), 3.2f));
            d = Mathf.Min(d, Segment(p, new Vector2(14f, 28f), new Vector2(32f, 46f), 3.2f));

            // Crater rim at the impact point, lower left.
            d = Mathf.Min(d, Arc(p - new Vector2(-28f, -34f), 16f, 20f * Mathf.Deg2Rad, 140f * Mathf.Deg2Rad, 4.5f));

            return d;
        }

        /// <summary>
        /// An impact that freezes: a six-armed flake inside a cracked shock ring.
        /// </summary>
        /// <remarks>
        /// The flake on its own said "cold", which is only half of what the power does. This is the
        /// Tank's one piece of hard control — it lands a blow and the blow is what freezes — so the
        /// ring is there to say it goes off, and it is broken into three arcs because a ring with
        /// gaps reads as expanding while a closed one reads as a border. The arms taper for the
        /// same reason the slash strokes do: shards, not spokes.
        /// </remarks>
        static float FrostNovaGlyph(Vector2 p)
        {
            float d = Circle(p, 6f);

            for (int i = 0; i < 6; i++)
            {
                float angle = (90f + i * 60f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                d = Mathf.Min(d, TaperedSegment(p, dir * 8f, dir * 32f, 5.5f, 1.8f));

                // Two branches per arm.
                Vector2 branchBase = dir * 21f;
                foreach (float branch in new[] { 0.92f, -0.92f })
                {
                    float branchAngle = angle + branch;
                    var branchDir = new Vector2(Mathf.Cos(branchAngle), Mathf.Sin(branchAngle));
                    d = Mathf.Min(d, TaperedSegment(p, branchBase, branchBase + branchDir * 11f, 3.4f, 1.4f));
                }
            }

            // Shock ring: three arcs, with the gaps falling on the flake's arms so they look
            // deliberate rather than like a ring that failed to close.
            for (int i = 0; i < 3; i++)
            {
                d = Mathf.Min(d, Arc(p, 42f, (i * 120f + 22f) * Mathf.Deg2Rad, 76f * Mathf.Deg2Rad, 4f));
            }

            return d;
        }

        // ── Tapering ──────────────────────────────────────────────────────────────────────────
        // There is no closed-form SDF for a stroke that changes width along its length, so both of
        // these union a run of short constant-width pieces, each overlapping the next so no seam
        // opens up. At 96 pixels the joins are invisible, and a tapered stroke is what gives a
        // glyph direction — which is the whole difference between a spin and a refresh symbol.

        /// <summary>An arc whose stroke thins from <paramref name="thickStart"/> to
        /// <paramref name="thickEnd"/> along its sweep.</summary>
        static float TaperedArc(Vector2 p, float radius, float start, float sweep,
            float thickStart, float thickEnd)
        {
            const int k_Steps = 12;
            float d = float.MaxValue;

            for (int i = 0; i < k_Steps; i++)
            {
                float t = i / (float)k_Steps;
                d = Mathf.Min(d, Arc(p, radius, start + sweep * t, sweep / k_Steps * 1.4f,
                    Mathf.Lerp(thickStart, thickEnd, t)));
            }

            return d;
        }

        /// <summary>A straight stroke that thins from <paramref name="thickStart"/> at
        /// <paramref name="a"/> to <paramref name="thickEnd"/> at <paramref name="b"/>.</summary>
        static float TaperedSegment(Vector2 p, Vector2 a, Vector2 b, float thickStart, float thickEnd)
        {
            const int k_Steps = 8;
            float d = float.MaxValue;

            for (int i = 0; i < k_Steps; i++)
            {
                float t0 = i / (float)k_Steps;
                float t1 = Mathf.Min((i + 1.4f) / k_Steps, 1f);
                d = Mathf.Min(d, Segment(p, Vector2.Lerp(a, b, t0), Vector2.Lerp(a, b, t1),
                    Mathf.Lerp(thickStart, thickEnd, t0)));
            }

            return d;
        }

        // ── Tank ──────────────────────────────────────────────────────────────────────────────

        /// <summary>A warhammer mid-swing, with the impact spark at the head.</summary>
        static float HammerGlyph(Vector2 p)
        {
            var q = Rotate(p, 0.5f);

            float d = Poly(q, k_HammerHeadVerts);
            d = Mathf.Min(d, Segment(q, new Vector2(0f, 18f), new Vector2(0f, -38f), 5f)); // shaft
            d = Mathf.Min(d, Circle(q - new Vector2(0f, -42f), 5.5f));                     // pommel

            // Two short sparks off the striking face, which is what separates a hammer being
            // swung from a hammer sitting in an inventory.
            d = Mathf.Min(d, Segment(q, new Vector2(24f, 30f), new Vector2(36f, 40f), 3.4f));
            d = Mathf.Min(d, Segment(q, new Vector2(26f, 18f), new Vector2(40f, 22f), 3.2f));

            return d;
        }

        static readonly Vector2[] k_HammerHeadVerts =
        {
            new Vector2(-22f, 20f), new Vector2(22f, 20f),
            new Vector2(22f, 38f), new Vector2(-22f, 38f),
        };

        /// <summary>A shield with a rising chevron: the buff, not the block.</summary>
        static float ShieldBuffGlyph(Vector2 p)
        {
            float d = PolyOutline(p, k_ShieldVerts, 4.5f);

            // Two chevrons pointing up. Stacked arrows are the one shape that reads as "this goes
            // up" at 96 pixels without a word next to it.
            for (int i = 0; i < 2; i++)
            {
                float y = -6f + i * 15f;
                d = Mathf.Min(d, Segment(p, new Vector2(-13f, y), new Vector2(0f, y + 12f), 4.2f));
                d = Mathf.Min(d, Segment(p, new Vector2(13f, y), new Vector2(0f, y + 12f), 4.2f));
            }

            return d;
        }

        /// <summary>The same shield, leaning into a charge, with the speed behind it.</summary>
        static float ShieldRushGlyph(Vector2 p)
        {
            var q = Rotate(p - new Vector2(9f, 0f), -0.32f);
            float d = PolyOutline(q, k_ShieldVerts, 4.5f);
            d = Mathf.Min(d, Poly(q, k_ShieldBossVerts));

            // Trailing lines, staggered so they read as motion rather than as a grille.
            d = Mathf.Min(d, Segment(p, new Vector2(-46f, 18f), new Vector2(-22f, 18f), 3.6f));
            d = Mathf.Min(d, Segment(p, new Vector2(-42f, 0f), new Vector2(-14f, 0f), 4.2f));
            d = Mathf.Min(d, Segment(p, new Vector2(-46f, -18f), new Vector2(-22f, -18f), 3.6f));

            return d;
        }

        static readonly Vector2[] k_ShieldVerts =
        {
            new Vector2(-24f, 30f), new Vector2(24f, 30f),
            new Vector2(24f, 2f), new Vector2(0f, -34f), new Vector2(-24f, 2f),
        };

        static readonly Vector2[] k_ShieldBossVerts =
        {
            new Vector2(-7f, 8f), new Vector2(7f, 8f), new Vector2(7f, -6f), new Vector2(-7f, -6f),
        };

        // ── Archer ────────────────────────────────────────────────────────────────────────────

        /// <summary>A single arrow, flying up and to the right.</summary>
        static float ArrowGlyph(Vector2 p)
        {
            var q = Rotate(p, -0.785f); // 45 degrees, so it flies rather than points

            float d = Segment(q, new Vector2(0f, -40f), new Vector2(0f, 30f), 4f);
            d = Mathf.Min(d, Poly(q, k_ArrowHeadVerts));

            // Fletching: one swept line per side, deliberately NOT closed into a triangle. Two
            // segments meeting at a point is an arrowhead, and an arrow with a head at both ends
            // reads as a resize handle rather than as a shot.
            for (int side = -1; side <= 1; side += 2)
            {
                d = Mathf.Min(d, Segment(q, new Vector2(0f, -22f),
                    new Vector2(side * 12f, -38f), 3.4f));
            }

            return d;
        }

        static readonly Vector2[] k_ArrowHeadVerts =
        {
            new Vector2(-11f, 26f), new Vector2(11f, 26f), new Vector2(0f, 44f),
        };

        /// <summary>A bow at full draw, with the charge marked on the string side.</summary>
        static float ChargedShotGlyph(Vector2 p)
        {
            // The limbs, as a deep arc well over to the left, so the bow and its string do not
            // enclose a symmetrical shape — at 96 pixels a closed lens reads as an eye.
            const float k_Limb = 1.35f;
            var centre = new Vector2(-20f, 0f);
            float d = Arc(p - centre, 34f, -k_Limb, k_Limb * 2f, 4.5f);

            var top = new Vector2(Mathf.Cos(-k_Limb), Mathf.Sin(-k_Limb)) * 34f + centre;
            var bottom = new Vector2(Mathf.Cos(k_Limb), Mathf.Sin(k_Limb)) * 34f + centre;
            var nock = new Vector2(-2f, 0f);

            // String, drawn back to a point: two straight runs, not one curve.
            d = Mathf.Min(d, Segment(p, top, nock, 2.6f));
            d = Mathf.Min(d, Segment(p, bottom, nock, 2.6f));

            // Arrow, running clear of the limbs so the head is unmistakably outside the bow.
            d = Mathf.Min(d, Segment(p, new Vector2(-34f, 0f), new Vector2(26f, 0f), 3.6f));
            d = Mathf.Min(d, Poly(p, k_NockedHeadVerts));

            return d;
        }

        static readonly Vector2[] k_NockedHeadVerts =
        {
            new Vector2(22f, -9f), new Vector2(22f, 9f), new Vector2(40f, 0f),
        };

        /// <summary>Three arrows on the way down: the volley is the count, not the arrow.</summary>
        static float VolleyGlyph(Vector2 p)
        {
            float d = float.MaxValue;

            // Angled well past vertical so they are visibly coming DOWN — a volley is arrows
            // arriving, and three arrows pointing up is a stat-increase icon.
            for (int i = 0; i < 3; i++)
            {
                var offset = new Vector2(-25f + i * 25f, 14f - i * 12f);
                var q = Rotate(p - offset, 0.42f);

                d = Mathf.Min(d, Segment(q, new Vector2(0f, 26f), new Vector2(0f, -16f), 3.4f));
                d = Mathf.Min(d, Poly(q, k_SmallHeadVerts));
                d = Mathf.Min(d, Segment(q, new Vector2(0f, 18f), new Vector2(-9f, 30f), 2.6f));
                d = Mathf.Min(d, Segment(q, new Vector2(0f, 18f), new Vector2(9f, 30f), 2.6f));
            }

            return d;
        }

        static readonly Vector2[] k_SmallHeadVerts =
        {
            new Vector2(-7f, -16f), new Vector2(7f, -16f), new Vector2(0f, -30f),
        };

        // ── Mage ──────────────────────────────────────────────────────────────────────────────

        /// <summary>A bolt of arcane: a core with the energy coming off it.</summary>
        static float ArcaneBoltGlyph(Vector2 p)
        {
            float d = Circle(p, 13f);

            // Four tapered flares on the diagonals, and four short ticks between them, so the
            // orb reads as radiating rather than as a filled dot.
            for (int i = 0; i < 4; i++)
            {
                float angle = (45f + i * 90f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                d = Mathf.Min(d, TaperedSegment(p, dir * 17f, dir * 42f, 5f, 1.4f));

                float tick = (i * 90f) * Mathf.Deg2Rad;
                var tickDir = new Vector2(Mathf.Cos(tick), Mathf.Sin(tick));
                d = Mathf.Min(d, TaperedSegment(p, tickDir * 19f, tickDir * 30f, 3.6f, 1.2f));
            }

            return d;
        }

        /// <summary>A cross, with the light coming off it.</summary>
        static float HealGlyph(Vector2 p)
        {
            float d = Segment(p, new Vector2(0f, -26f), new Vector2(0f, 26f), 8f);
            d = Mathf.Min(d, Segment(p, new Vector2(-26f, 0f), new Vector2(26f, 0f), 8f));

            // Three motes rising off it, decreasing in size — the difference between a heal and
            // a first-aid symbol.
            d = Mathf.Min(d, Circle(p - new Vector2(-34f, 26f), 5f));
            d = Mathf.Min(d, Circle(p - new Vector2(33f, 33f), 3.8f));
            d = Mathf.Min(d, Circle(p - new Vector2(24f, -32f), 3f));

            return d;
        }

        // ── Rogue ─────────────────────────────────────────────────────────────────────────────

        /// <summary>A dagger: shorter and leaner than the sword, so the two never read alike.</summary>
        static float DaggerGlyph(Vector2 p)
        {
            var q = Rotate(p, 0.35f);

            float d = Poly(q, k_DaggerVerts);
            d = Mathf.Min(d, Segment(q, new Vector2(-14f, -8f), new Vector2(14f, -8f), 4f));
            d = Mathf.Min(d, Segment(q, new Vector2(0f, -11f), new Vector2(0f, -30f), 4.5f));
            d = Mathf.Min(d, Circle(q - new Vector2(0f, -34f), 5f));

            return d;
        }

        static readonly Vector2[] k_DaggerVerts =
        {
            new Vector2(-7f, -6f), new Vector2(7f, -6f), new Vector2(7f, 22f),
            new Vector2(0f, 40f), new Vector2(-7f, 22f),
        };

        /// <summary>The dagger thrown forward, with the ground it covered behind it.</summary>
        static float DashGlyph(Vector2 p)
        {
            // Up and to the right, with the trail coming from down-left along the same line, so
            // the icon has one diagonal instead of a blade lying across three bars.
            var q = Rotate(p - new Vector2(15f, 15f), -0.79f);

            float d = Poly(q, k_DaggerVerts);
            d = Mathf.Min(d, Segment(q, new Vector2(-12f, -8f), new Vector2(12f, -8f), 3.6f));
            d = Mathf.Min(d, Segment(q, new Vector2(0f, -11f), new Vector2(0f, -24f), 4f));

            // Trail: tapered, because a dash has a direction and three equal bars do not. Offset
            // perpendicular to the travel so it flanks the blade rather than crossing it.
            for (int i = -1; i <= 1; i++)
            {
                var from = new Vector2(-10f + i * 9f, -26f + i * 9f);
                var to = new Vector2(-42f + i * 6f, -6f + i * 6f);
                d = Mathf.Min(d, TaperedSegment(p, from, to, i == 0 ? 5.5f : 4.5f, 1.3f));
            }

            return d;
        }

        /// <summary>An eye struck through: seen, then not.</summary>
        static float StealthGlyph(Vector2 p)
        {
            // The lens, as two arcs meeting at the corners.
            float d = Arc(p - new Vector2(0f, -22f), 36f, 0.62f, 1.9f, 4.2f);
            d = Mathf.Min(d, Arc(p - new Vector2(0f, 22f), 36f, 3.76f, 1.9f, 4.2f));
            d = Mathf.Min(d, Circle(p, 10f));

            // The strike. Slightly proud of the lens on both ends so it reads as crossing out the
            // eye rather than as a scratch inside it.
            d = Mathf.Min(d, Segment(p, new Vector2(-34f, -26f), new Vector2(34f, 26f), 5f));

            return d;
        }

        /// <summary>
        /// The outline of a polygon: the shape, with a smaller copy of it taken back out.
        /// </summary>
        /// <remarks>
        /// Shields are the one shape here that has to be hollow — a filled shield is a blob at this
        /// size — and the kit has no stroke-a-path primitive, so it is done by subtraction.
        /// </remarks>
        static float PolyOutline(Vector2 p, Vector2[] verts, float thickness)
        {
            float outer = Poly(p, verts);
            return Mathf.Max(outer, -(outer + thickness));
        }

        // ── SDF primitives ────────────────────────────────────────────────────────────────────

        static float Circle(Vector2 p, float radius) => p.magnitude - radius;

        static float RoundedBox(Vector2 p, float extent, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(extent - radius, extent - radius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        /// <summary>
        /// Exact signed distance to a closed polygon, negative inside. Distance is to the nearest
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

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);

            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - thickness;
        }

        /// <summary>Ring segment from <paramref name="start"/> sweeping CCW by <paramref name="sweep"/>.</summary>
        static float Arc(Vector2 p, float radius, float start, float sweep, float thickness)
        {
            float angle = Mathf.Atan2(p.y, p.x);
            // Angular offset from the arc's start, wrapped to [0, 2pi).
            float local = Mathf.Repeat(angle - start, Mathf.PI * 2f);

            if (local <= sweep)
            {
                return Mathf.Abs(p.magnitude - radius) - thickness;
            }

            // Past either end: distance to the nearer endpoint cap.
            Vector2 a = new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * radius;
            float end = start + sweep;
            Vector2 b = new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius;
            return Mathf.Min((p - a).magnitude, (p - b).magnitude) - thickness;
        }
    }
}
