using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The game's wordmark, drawn from type rather than from an image, and the one place its
    /// name and tagline are spelled.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists.</b> The menus still showed the original sample's logo — a game
    /// called Boss Room — on the title screen of a game called VersusD. A wordmark is the one
    /// piece of art a game cannot borrow, so this draws its own.</para>
    ///
    /// <para><b>Why type and not a bitmap.</b> A drawn logo would be an imported asset wired into
    /// a prefab, which is the arrangement this project has repeatedly lost to the Editor's asset
    /// cache. Set heavy, tracked out, contoured and lit from behind, type reads as a logo at
    /// title size, and it stays sharp at any resolution the game runs at.</para>
    ///
    /// <para><b>The second colour.</b> The rest of the UI is cool bone and lapis on basalt, lit
    /// by a blue tube. The mark is where the other tube shows up: the D burns violet, because a
    /// logo has to be identifiable at a glance and monochrome chrome is not. It used to be a
    /// magenta that belonged to no other part of the game; taking it into the palette's own violet
    /// is what makes the wordmark look lit by the same room as everything behind it.
    /// </remarks>
    public static class BrandMark
    {
        /// <summary>The game's name, split where the accent colour takes over.</summary>
        public const string NameStem = "VERSUS";

        /// <summary>The tail of the name, drawn in <see cref="MarkViolet"/>.</summary>
        public const string NameAccent = "D";

        /// <summary>Sits under the mark wherever there is room for it.</summary>
        public const string Tagline = "ARENA MULTIJUGADOR";

        /// <summary>
        /// The mark's second hue: the violet tube, pushed brighter than the one on the cards so
        /// the D still carries at logo size.
        /// </summary>
        public static readonly Color MarkViolet = new Color(0.72f, 0.46f, 1f, 1f);

        /// <summary>
        /// Builds the wordmark as a child of <paramref name="parent"/>, stretched over it. The
        /// caller owns the rect, so the mark inherits whatever anchoring the layout already had —
        /// which is what lets it stand in for an existing logo image without moving anything.
        /// </summary>
        /// <param name="withTagline">
        /// False in tight spots (a corner watermark), where a second line would only be noise.
        /// </param>
        public static RectTransform Build(RectTransform parent, bool withTagline = true)
        {
            var root = UIKit.NewRect(parent, "BrandMark");
            UIKit.Stretch(root);
            UIKit.Mark(root.gameObject);

            // The glow sits behind the type and past the edges of the rect, so the light spills
            // out of the logo's box the way a neon sign spills onto its wall. It is anchored
            // rather than sized, because the mark is usually built into a rect a layout group has
            // not measured yet — where parent.rect is still zero.
            var glow = UIKit.NewRect(root, "Glow");
            glow.anchorMin = new Vector2(-0.3f, -0.45f);
            glow.anchorMax = new Vector2(1.3f, 1.45f);
            glow.offsetMin = Vector2.zero;
            glow.offsetMax = Vector2.zero;

            var glowImage = glow.gameObject.AddComponent<Image>();
            glowImage.sprite = ToonMenuSkin.GlowSprite;
            glowImage.color = new Color(HudSkin.Amethyst.r, HudSkin.Amethyst.g, HudSkin.Amethyst.b, 0.24f);
            glowImage.raycastTarget = false;
            glow.gameObject.AddComponent<ToonGlowPulse>().SetRange(0.12f, 0.3f);

            var column = UIKit.Column(root, "Type", UIKit.Unit * 0.5f, 0f, TextAnchor.MiddleCenter);
            UIKit.Stretch(column);

            var name = UIKit.Text(column, Wordmark(), UIKit.TextStyle.Display, TextAlignmentOptions.Center,
                ToonMenuSkin.Accent);
            name.enableWordWrapping = false;
            name.characterSpacing = 12f;
            name.outlineColor = ToonMenuSkin.Ink;
            name.outlineWidth = 0.28f;

            // Auto-sizing is what makes one builder serve a full title plate and a small header:
            // the type fills whatever rect the caller happened to have.
            name.enableAutoSizing = true;
            name.fontSizeMin = 24f;
            name.fontSizeMax = 140f;

            var nameElement = name.GetComponent<LayoutElement>();
            nameElement.preferredHeight = -1f;
            nameElement.flexibleHeight = 1f;

            // A hard offset shadow, the same trick the menu plates use, so the mark reads as a
            // cut-out sitting above the scene behind it.
            var shadow = name.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -6f);

            if (withTagline)
            {
                var rule = UIKit.Divider(column);
                var ruleRect = (RectTransform)rule.transform;
                ruleRect.sizeDelta = new Vector2(0f, 2f);

                var tagline = UIKit.Text(column, Tagline, UIKit.TextStyle.Caption, TextAlignmentOptions.Center,
                    HudSkin.TextDim);
                tagline.characterSpacing = 14f;
                tagline.enableWordWrapping = false;
                tagline.GetComponent<LayoutElement>().preferredHeight = 22f;
            }

            return root;
        }

        /// <summary>The name with its accent tail already coloured, for use in any TMP field.</summary>
        public static string Wordmark()
        {
            return NameStem + "<color=#" + ColorUtility.ToHtmlStringRGB(MarkViolet) + ">" + NameAccent + "</color>";
        }
    }
}
