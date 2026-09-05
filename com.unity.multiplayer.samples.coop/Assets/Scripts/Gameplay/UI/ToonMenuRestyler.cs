using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Walks the menu canvases and dresses whatever it finds in <see cref="ToonMenuSkin"/>:
    /// dialogs become cards, generic buttons become stickers that squash when pressed and pick up
    /// an icon and a colour from what they do, input fields sink, the sample's logo is replaced by
    /// this game's wordmark, light text gets a contour, and the screen gets a vignette to sit
    /// against. Self-bootstrapping — there is nothing to wire in a scene or a prefab.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a runtime pass instead of restyled prefabs.</b> Editing the serialized UI
    /// prefabs by hand does not reliably reach a build while the Editor is open — it serves its
    /// own cached copy of the asset and can overwrite the file (this has cost this project a
    /// build/test cycle before). A pass that runs at load time is immune to that, and it also
    /// catches UI nobody would think to restyle: popups, session rows and player seats that only
    /// exist once they are instantiated.</para>
    ///
    /// <para><b>Why it keeps scanning.</b> Most menu UI is not there when the scene finishes
    /// loading — the session list fills in from the master server, popups are spawned on demand,
    /// seats appear as players join. A periodic sweep costs a <c>GetComponentsInChildren</c> over
    /// a few hundred objects and means late arrivals are dressed like everything else. Each
    /// graphic is only ever touched once (<see cref="m_Styled"/>), and the styling routines are
    /// idempotent anyway.</para>
    ///
    /// <para><b>Why buttons get icons here.</b> The screens that came from the sample are
    /// prefabs, and this project cannot reliably edit those (the Editor serves its own cached
    /// copy). So the only place a prefab button can be told apart from its neighbours is at run
    /// time, from the one thing it does carry: its label. <see cref="TryClassify"/> turns that
    /// label into an icon and a <see cref="UIKit.Role"/>, which is what gives a screen full of
    /// identical blue rectangles a primary action, a way out, and something to read at a glance.
    /// A label the table does not know stays a plain neutral button.</para>
    ///
    /// <para><b>Why it is conservative.</b> It only replaces sprites it recognises as generic
    /// surfaces (<see cref="k_ReplaceableSprites"/>) or plain untextured quads. Anything with real
    /// art on it — the logo, class portraits, the exit cross, health bars — keeps its sprite and
    /// gets nothing but motion. That is what keeps a blanket pass over every canvas from turning
    /// an illustration into a grey box.</para>
    /// </remarks>
    public class ToonMenuRestyler : MonoBehaviour
    {
        /// <summary>Scenes that are menus. Only these get the vignette backdrop.</summary>
        static readonly string[] k_MenuScenes = { "Startup", "MainMenu", "CharSelect", "PostGame" };

        /// <summary>How much of this pass a given canvas gets.</summary>
        enum Pass
        {
            /// <summary>Left alone entirely.</summary>
            Skip,

            /// <summary>
            /// Colour only: text is put into the palette and imported art is repainted, but
            /// nothing is reshaped, re-roled or given an icon.
            /// </summary>
            ChromeOnly,

            /// <summary>The lot: shapes, roles, icons, cards, translation.</summary>
            Full,
        }

        /// <summary>
        /// Canvases this pass must not touch at all: everything this project draws itself, which
        /// is already wearing <see cref="HudSkin"/>, plus the debug overlays.
        /// </summary>
        static readonly string[] k_SkippedCanvases =
        {
            "DeathmatchHUD", "Debug Overlay", "ControlsHintPanel", "MobileMovementJoystick",
            "MobileZoomBar", "AimIndicator", "UIHealth", "UIName", "NetworkOverlay",
            k_BackdropName,
        };

        /// <summary>
        /// The sample's in-game HUD: repainted but never restructured.
        /// </summary>
        /// <remarks>
        /// These used to be on the skip list outright, which is why the action bar and the party
        /// panel stayed gold and brown while every menu around them went blue. They cannot take
        /// the full pass — their plates are laid out to the pixel around ability icons, and
        /// turning those into cards would wreck a HUD that works — but there is nothing stopping
        /// their colours from joining the rest of the game.
        /// </remarks>
        static readonly string[] k_ChromeOnlyCanvases =
        {
            "BossRoomHudCanvas", "Hero Action Bar", "Hero Emote Bar", "PartyHUD", "AllyHUD",
        };

        /// <summary>
        /// Art that must keep the colours it was drawn with: the ability, emote and class icons
        /// (a player reads those by colour as much as by shape), the gauges, and the credits
        /// logos, which belong to other people.
        /// </summary>
        static readonly string[] k_ProtectedArt =
        {
            "_atk", "_skill", "_symbol", "emote", "action_", "_help_", "healthbar",
            "checkmark", "logo", "br_icon",
        };

        /// <summary>
        /// Repainted even when <see cref="k_ProtectedArt"/> matches: plates, frames, backgrounds
        /// and banners are chrome whatever they are named after. The emote bar is the case that
        /// needs this — its buttons are "ui_emote_btn" and its icons are "ui_emote_dance", and
        /// only one of those two is a drawing worth keeping.
        /// </summary>
        static readonly string[] k_ChromeArt =
        {
            "_btn", "_bg", "_frame", "_box", "dialog", "panel", "title",
        };

        /// <summary>Art that reads as "leave": repainted in the magenta ramp instead.</summary>
        static readonly string[] k_DangerArt = { "exit" };

        /// <summary>
        /// Art that already arrives wearing the theme, so this pass leaves it exactly as it is:
        /// neither swapped for a generated surface nor pushed through the palette ramp.
        /// </summary>
        /// <remarks>
        /// <para>These PNGs were regenerated from the Age of Darkness / gothic skill-tree
        /// references in the blue-violet gamut (see <c>UIThemeGen/</c>), reading their palette
        /// from <see cref="HudSkin"/> — the same source this pass uses. Recolouring them a second
        /// time is not a no-op: the ramp maps luminance onto one hue, which flattens the
        /// deliberate split between the violet pieces and the blue ones (the tank abilities and
        /// the pick-up actions are blue on purpose).</para>
        ///
        /// <para>The reason this pass replaces prefab art in the first place — that hand-edited
        /// UI prefabs do not reliably reach a build — does not apply here. These are texture
        /// files, and a texture swap survives the Editor cache and the build untouched.</para>
        /// </remarks>
        static readonly HashSet<string> k_ThemedArt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "button_Disabled", "inputfield_Blank", "ui_action_pickup", "ui_action_putdown",
            "ui_archer_atk", "ui_archer_skill1", "ui_archer_skill2", "ui_archer_skill3",
            "ui_archer_symbol_active", "ui_archer_symbol_inactive", "ui_bg_gradient", "ui_bg_gradient2",
            "ui_blurred_square", "ui_btn_blank", "ui_btn_disabled", "ui_btn_exit",
            "ui_btn_randomize", "ui_btn_ready_dwn", "ui_btn_ready_up", "ui_char_box_bg_selected",
            "ui_char_box_glow", "ui_char_box_ovr_avail", "ui_char_box_ovr_selected", "ui_char_info_frame",
            "ui_char_select_title", "ui_char_select_title2", "ui_checkmark", "ui_connecting",
            "ui_dialog", "ui_dropdown_arrow", "ui_emote_cheer", "ui_emote_dance",
            "ui_emote_sit", "ui_emote_wave", "ui_healthbar", "ui_healthbar_bg",
            "ui_hero_bg", "ui_mage_atk", "ui_mage_skill1", "ui_mage_skill2",
            "ui_mage_symbol_active", "ui_mage_symbol_inactive", "ui_ptag_1", "ui_ptag_2",
            "ui_ptag_3", "ui_ptag_4", "ui_ptag_5", "ui_ptag_6",
            "ui_ptag_7", "ui_ptag_8", "ui_ptag_glow", "ui_revive",
            "ui_rogue_atk", "ui_rogue_skill1", "ui_rogue_skill2", "ui_rogue_symbol_active",
            "ui_rogue_symbol_inactive", "ui_scroll_frame", "ui_sound_settings", "ui_tank_atk",
            "ui_tank_skill1", "ui_tank_skill2", "ui_tank_symbol_active", "ui_tank_symbol_inactive",
        };

        /// <summary>
        /// Sprites that carry no art — either this project's blank UI plates or Unity's built-in
        /// grey defaults. These are the ones worth replacing; everything else is somebody's
        /// drawing.
        /// </summary>
        static readonly HashSet<string> k_ReplaceableSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ui_btn_blank", "ui_dialog", "inputfield_Blank", "ui_scroll_frame",
            "UISprite", "Background", "InputFieldBackground", "UIMask", "Knob",
        };

        /// <summary>
        /// Logos belonging to the sample this game was built from. They are replaced outright by
        /// <see cref="BrandMark"/> — a borrowed logo is the one piece of art that cannot stay.
        /// </summary>
        static readonly HashSet<string> k_ReplacedLogos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ui_title_logo",
        };

        /// <summary>Art that should look lit: a soft accent glow goes in behind it.</summary>
        static readonly HashSet<string> k_GlowingArt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ui_title_logo", "ui_char_select_title2",
        };

        const string k_BackdropName = "ToonMenuBackdrop";

        // A menu is mostly waiting, so it can afford a brisk sweep; in game the only thing this
        // pass owns is the settings panel and the odd popup, which nobody opens twice a second.
        const float k_MenuScanSeconds = 0.35f;
        const float k_GameplayScanSeconds = 1.5f;

        // Below this, an image is a divider, a chip or an icon backing — not a panel.
        const float k_MinimumCardSize = 48f;

        // At or above this fraction of the canvas, an image is a full-screen dimmer or blocker.
        // Rounding its corners and giving it a contour would frame the entire screen.
        const float k_ScreenCoverFraction = 0.9f;

        readonly HashSet<int> m_Styled = new HashSet<int>();

        readonly List<Graphic> m_GraphicsBuffer = new List<Graphic>();

        GameObject m_Backdrop;
        float m_NextScanTime;
        bool m_InMenuScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // The dedicated server runs this same build headless. It draws no UI, so baking
            // textures and sweeping canvases there would be pure waste.
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }

            var host = new GameObject(nameof(ToonMenuRestyler));
            DontDestroyOnLoad(host);
            host.AddComponent<ToonMenuRestyler>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Instance ids are only unique among live objects, so the set has to be dropped when
            // the objects behind them are.
            m_Styled.Clear();
            m_NextScanTime = 0f;

            m_InMenuScene = Array.IndexOf(k_MenuScenes, scene.name) >= 0;
            EnsureBackdrop();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextScanTime)
            {
                return;
            }

            m_NextScanTime = Time.unscaledTime + (m_InMenuScene ? k_MenuScanSeconds : k_GameplayScanSeconds);
            Scan();
        }

        void Scan()
        {
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                // Nested canvases are reached through their root, so visiting them again would
                // only re-walk the same subtree.
                if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace)
                {
                    continue;
                }

                var pass = PassFor(canvas.name);
                if (pass == Pass.Skip)
                {
                    continue;
                }

                m_GraphicsBuffer.Clear();
                canvas.GetComponentsInChildren(true, m_GraphicsBuffer);

                var canvasSize = ((RectTransform)canvas.transform).rect.size;

                foreach (var graphic in m_GraphicsBuffer)
                {
                    if (graphic == null || graphic.name.StartsWith(ToonMenuSkin.OverlayPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!m_Styled.Add(graphic.GetInstanceID()))
                    {
                        continue;
                    }

                    // Kit-built UI has already chosen its sprites, roles and type sizes; a second
                    // pass over it could only undo those decisions.
                    if (graphic.GetComponentInParent<UIKitBuilt>(true) != null)
                    {
                        continue;
                    }

                    Apply(graphic, canvasSize, pass);
                }

                if (pass != Pass.Full)
                {
                    continue;
                }

                // Translation is deliberately outside the once-only gate above: half of these
                // labels are written by a script after the first sweep ("Waiting for other
                // players…"), and a string that arrives late still has to arrive in Spanish.
                foreach (var graphic in m_GraphicsBuffer)
                {
                    if (graphic == null || graphic.GetComponentInParent<UIKitBuilt>(true) != null)
                    {
                        continue;
                    }

                    Translate(graphic);
                }
            }
        }

        static Pass PassFor(string canvasName)
        {
            if (Matches(canvasName, k_SkippedCanvases))
            {
                return Pass.Skip;
            }

            return Matches(canvasName, k_ChromeOnlyCanvases) ? Pass.ChromeOnly : Pass.Full;
        }

        static bool Matches(string name, string[] needles)
        {
            foreach (var needle in needles)
            {
                if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        void Apply(Graphic graphic, Vector2 canvasSize, Pass pass)
        {
            switch (graphic)
            {
                case TMP_Text tmpText:
                    if (pass == Pass.ChromeOnly)
                    {
                        // isLabel false on purpose: upper-casing and tracking out a player's name
                        // or a cooldown number would be restructuring, not repainting.
                        ToonMenuSkin.StyleText(tmpText, false);
                    }
                    else
                    {
                        StyleTmpText(tmpText);
                    }

                    break;
                case Text text:
                    ToonMenuSkin.StyleText(text);
                    break;
                case Image image:
                    if (pass == Pass.ChromeOnly)
                    {
                        RepaintArt(image);
                    }
                    else
                    {
                        StyleImage(image, canvasSize);
                    }

                    break;
            }
        }

        /// <summary>
        /// Puts imported art into the palette in place: the drawing is kept, the hue is not.
        /// </summary>
        /// <remarks>
        /// A tint on the <see cref="Image"/> cannot do this — multiplying gold by blue gives mud —
        /// so the pixels themselves are remapped once, by <see cref="UIPaletteRecolor"/>, and
        /// cached. Icons, gauges and other people's logos are left exactly as they were.
        /// </remarks>
        static void RepaintArt(Image image)
        {
            var sprite = image.sprite;
            if (sprite == null || image.type == Image.Type.Filled
                || image.GetComponent<Mask>() != null
                || image.GetComponent<RectMask2D>() != null)
            {
                return;
            }

            if (k_ThemedArt.Contains(sprite.name))
            {
                return;
            }

            if (Matches(sprite.name, k_ProtectedArt) && !Matches(sprite.name, k_ChromeArt))
            {
                return;
            }

            var ramp = Matches(sprite.name, k_DangerArt)
                ? UIPaletteRecolor.Ramp.Danger
                : UIPaletteRecolor.Ramp.Cold;

            image.sprite = UIPaletteRecolor.Get(sprite, ramp);

            // A coloured tint over the repainted sprite would drag it straight back out of the
            // palette, so a saturated one is dropped and only its alpha kept.
            Color.RGBToHSV(image.color, out _, out float saturation, out _);
            if (saturation > 0.12f)
            {
                image.color = new Color(1f, 1f, 1f, image.color.a);
            }
        }

        // ── Language ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts a label into Spanish if <see cref="UIStrings"/> knows it. Strings it does not know
        /// — names, codes, scores — are left exactly as they were.
        /// </summary>
        static void Translate(Graphic graphic)
        {
            switch (graphic)
            {
                case TMP_Text tmpText:
                    if (UIStrings.TryTranslate(tmpText.text, out string tmpTranslated))
                    {
                        tmpText.text = tmpTranslated;
                    }

                    break;

                case Text text:
                    if (UIStrings.TryTranslate(text.text, out string translated))
                    {
                        text.text = translated;
                    }

                    break;
            }
        }

        // ── Text ──────────────────────────────────────────────────────────────────────────────

        static void StyleTmpText(TMP_Text text)
        {
            // The text a player is typing, and the placeholder standing in for it, belong to the
            // field's logic. Upper-casing what someone typed would be a bug, not a style.
            //
            // The inactive-inclusive overload matters everywhere in this pass: hidden panels are
            // scanned too, and without it a parent that happens to be switched off reads as absent.
            bool insideInputField = text.GetComponentInParent<TMP_InputField>(true) != null
                                    || text.GetComponentInParent<InputField>(true) != null;

            if (insideInputField)
            {
                if (text.name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text.color = HudSkin.TextDim;
                    text.fontStyle |= FontStyles.Italic;
                }
                else
                {
                    text.color = HudSkin.TextPrimary;
                }

                return;
            }

            bool isLabel = text.GetComponentInParent<Selectable>(true) != null;
            ToonMenuSkin.StyleText(text, isLabel);
        }

        // ── Images ────────────────────────────────────────────────────────────────────────────

        void StyleImage(Image image, Vector2 canvasSize)
        {
            var gameObject = image.gameObject;

            // A mask's sprite is its shape, and a filled image's sprite is a gauge. Replacing
            // either changes behaviour, not looks.
            if (gameObject.GetComponent<Mask>() != null
                || gameObject.GetComponent<RectMask2D>() != null
                || image.type == Image.Type.Filled
                || image.type == Image.Type.Tiled)
            {
                return;
            }

            var selectable = gameObject.GetComponent<Selectable>();
            bool isSelectableSurface = selectable != null && selectable.targetGraphic == image;

            if (isSelectableSurface)
            {
                StyleSelectable(selectable, image);
                return;
            }

            // Anything inside a button is part of that button's face — its own highlight, its
            // icon backing — and the button has already been dressed as one piece. It still gets
            // repainted, though: the exit cross and the ready badge live here, and leaving them
            // out is exactly how a screen ends up half restyled.
            if (gameObject.GetComponentInParent<Selectable>(true) != null)
            {
                RepaintArt(image);
                return;
            }

            if (!IsReplaceable(image))
            {
                StyleArt(image);
                return;
            }

            var size = ((RectTransform)image.transform).rect.size;

            // Full-screen blockers and dimmers stay flat: rounding one puts a framed card around
            // the whole screen.
            if (canvasSize.x > 0f && canvasSize.y > 0f
                && size.x >= canvasSize.x * k_ScreenCoverFraction
                && size.y >= canvasSize.y * k_ScreenCoverFraction)
            {
                return;
            }

            if (size.x < k_MinimumCardSize || size.y < k_MinimumCardSize)
            {
                return;
            }

            ToonMenuSkin.StyleCard(image);

            // Only the panels that read as "a thing that appeared" get the pop — a card nested
            // inside another card is a section of it, and popping both looks like a stutter.
            if (image.transform.parent != null
                && image.transform.parent.GetComponent<Image>() == null
                && image.GetComponent<ToonPanelPop>() == null)
            {
                image.gameObject.AddComponent<ToonPanelPop>();
            }
        }

        void StyleSelectable(Selectable selectable, Image image)
        {
            // Sliders and scrollbars are made of parts whose colours mean something (fill vs
            // track). Round them off and leave the palette alone.
            if (selectable is Slider || selectable is Scrollbar)
            {
                if (IsReplaceable(image))
                {
                    ToonMenuSkin.StyleBar(image);
                }

                return;
            }

            // Invisible hit areas — the transparent rectangle over a character seat, for instance.
            // Giving one a sticker face would draw a box on top of the art it covers.
            float alpha = image.color.a;
            if (selectable.transition == Selectable.Transition.ColorTint)
            {
                alpha *= selectable.colors.normalColor.a;
            }

            if (alpha < 0.05f)
            {
                return;
            }

            if (!IsReplaceable(image))
            {
                // Keep the drawing, take the palette and the feel: an illustrated button is
                // repainted in place and still gets to swell and squash under the pointer.
                RepaintArt(image);
                ToonMenuSkin.AddMotion(selectable, image, null);
                return;
            }

            if (selectable is InputField || selectable is TMP_InputField)
            {
                ToonMenuSkin.StyleInput(selectable, image);
                return;
            }

            // A UITinter owns this image's colour (it paints the session tabs' selected state).
            // Take the shape, leave the palette to it.
            bool tintable = image.GetComponent<UITinter>() == null;
            ToonMenuSkin.StyleButton(selectable, image, tintable);
            Decorate(selectable, image, tintable);
        }

        // ── Meaning ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Gives a freshly styled button the icon and the colour its label implies, and marks its
        /// label as handled so the text pass does not paint over the result.
        /// </summary>
        void Decorate(Selectable selectable, Image image, bool tintable)
        {
            var label = selectable.GetComponentInChildren<TMP_Text>(true);
            string content = label != null ? label.text : LegacyLabelText(selectable);

            if (string.IsNullOrEmpty(content) || !TryClassify(content, out var icon, out var role))
            {
                return;
            }

            var rect = (RectTransform)image.transform;
            var size = rect.rect.size;

            // Below this there is no gutter to put an icon in without it landing on the label.
            const float k_MinimumIconButtonWidth = 150f;

            if (size.x >= k_MinimumIconButtonWidth && size.y >= 32f)
            {
                AddLeadingIcon(rect, icon, role, size.y);
            }

            // A UITinter owns this button's colours (the session tabs). It gets the icon, not the
            // palette.
            if (!tintable)
            {
                return;
            }

            ApplyRole(selectable, role);

            if (label != null)
            {
                ToonMenuSkin.StyleText(label, true);
                label.color = RoleLabelColor(role);

                // Classification ran against the English label above; the player should still see
                // Spanish.
                if (UIStrings.TryTranslate(label.text, out string translated))
                {
                    label.text = translated;
                }

                // Claiming the label here is what stops the text pass, which runs over the same
                // canvas moments later, from resetting it to the neutral body colour.
                m_Styled.Add(label.GetInstanceID());
            }
        }

        /// <summary>The label of a button whose text is still legacy uGUI.</summary>
        static string LegacyLabelText(Selectable selectable)
        {
            var text = selectable.GetComponentInChildren<Text>(true);

            return text != null ? text.text : null;
        }

        /// <summary>Parks an icon in the button's left gutter, sized off the button's height.</summary>
        static void AddLeadingIcon(RectTransform host, UIIcons.Icon icon, UIKit.Role role, float height)
        {
            const string k_Name = ToonMenuSkin.OverlayPrefix + "Icon";
            if (host.Find(k_Name) != null)
            {
                return;
            }

            var glyph = new GameObject(k_Name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)glyph.transform;
            rect.SetParent(host, false);

            float side = Mathf.Clamp(height * 0.42f, 18f, 34f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(side, side);
            rect.anchoredPosition = new Vector2(side * 0.7f, 0f);

            var image = glyph.GetComponent<Image>();
            image.sprite = UIIcons.Get(icon);
            image.color = RoleLabelColor(role);
            image.preserveAspect = true;
            image.raycastTarget = false;

            if (host.GetComponent<LayoutGroup>() != null)
            {
                glyph.AddComponent<LayoutElement>().ignoreLayout = true;
            }
        }

        /// <summary>Repaints a button's four states for its role, keeping the toon plate.</summary>
        static void ApplyRole(Selectable selectable, UIKit.Role role)
        {
            Color fill;
            switch (role)
            {
                case UIKit.Role.Primary: fill = ToonMenuSkin.Accent; break;
                case UIKit.Role.Danger: fill = UIKit.Danger; break;
                case UIKit.Role.Positive: fill = UIKit.Positive; break;
                default: return;
            }

            var colors = selectable.colors;
            colors.normalColor = fill;
            colors.highlightedColor = Color.Lerp(fill, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(fill, ToonMenuSkin.Ink, 0.45f);
            colors.selectedColor = fill;
            colors.disabledColor = new Color(fill.r * 0.4f, fill.g * 0.4f, fill.b * 0.4f, 0.45f);
            selectable.colors = colors;
        }

        static Color RoleLabelColor(UIKit.Role role)
        {
            switch (role)
            {
                case UIKit.Role.Primary:
                case UIKit.Role.Danger:
                case UIKit.Role.Positive:
                    return UIKit.OnAccent;

                default:
                    return HudSkin.TextPrimary;
            }
        }

        /// <summary>
        /// Reads a button's label and decides what kind of button it is. The table is ordered: a
        /// specific phrase has to be tested before the word it contains, which is why "find and
        /// join" (a browse action) is listed above "join" (the action itself).
        /// </summary>
        static bool TryClassify(string label, out UIIcons.Icon icon, out UIKit.Role role)
        {
            string text = Normalize(label);

            icon = UIIcons.Icon.Play;
            role = UIKit.Role.Secondary;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (var entry in k_LabelTable)
            {
                if (text.IndexOf(entry.keyword, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                icon = entry.icon;
                role = entry.role;
                return true;
            }

            return false;
        }

        /// <summary>Lower-cases and drops rich-text tags, so a styled label still matches.</summary>
        static string Normalize(string label)
        {
            var builder = new StringBuilder(label.Length);
            bool insideTag = false;

            foreach (char c in label)
            {
                if (c == '<')
                {
                    insideTag = true;
                }
                else if (c == '>')
                {
                    insideTag = false;
                }
                else if (!insideTag)
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// Label fragment to icon and role. Both of the languages the project's UI is written in
        /// are listed, because the menus inherited from the sample are English while the strings
        /// this project added are Spanish.
        /// </summary>
        static readonly (string keyword, UIIcons.Icon icon, UIKit.Role role)[] k_LabelTable =
        {
            ("return to menu", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("find", UIIcons.Icon.Search, UIKit.Role.Secondary),
            ("buscar", UIIcons.Icon.Search, UIKit.Role.Secondary),
            ("quick", UIIcons.Icon.Bolt, UIKit.Role.Secondary),
            ("try again", UIIcons.Icon.Refresh, UIKit.Role.Primary),
            ("replay", UIIcons.Icon.Refresh, UIKit.Role.Primary),
            ("refresh", UIIcons.Icon.Refresh, UIKit.Role.Secondary),
            ("ready", UIIcons.Icon.Check, UIKit.Role.Positive),
            ("listo", UIIcons.Icon.Check, UIKit.Role.Positive),
            ("copy", UIIcons.Icon.Copy, UIKit.Role.Secondary),
            ("copiar", UIIcons.Icon.Copy, UIKit.Role.Secondary),
            ("join", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("unir", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("create", UIIcons.Icon.Plus, UIKit.Role.Primary),
            ("crear", UIIcons.Icon.Plus, UIKit.Role.Primary),
            ("host", UIIcons.Icon.Globe, UIKit.Role.Primary),
            ("login", UIIcons.Icon.Key, UIKit.Role.Primary),
            ("log in", UIIcons.Icon.Key, UIKit.Role.Primary),
            ("entrar", UIIcons.Icon.Key, UIKit.Role.Primary),
            ("register", UIIcons.Icon.Plus, UIKit.Role.Secondary),
            ("registr", UIIcons.Icon.Plus, UIKit.Role.Secondary),
            ("guest", UIIcons.Icon.User, UIKit.Role.Secondary),
            ("invitado", UIIcons.Icon.User, UIKit.Role.Secondary),
            ("profile", UIIcons.Icon.User, UIKit.Role.Secondary),
            ("perfil", UIIcons.Icon.User, UIKit.Role.Secondary),
            ("settings", UIIcons.Icon.Gear, UIKit.Role.Secondary),
            ("ajustes", UIIcons.Icon.Gear, UIKit.Role.Secondary),
            ("random", UIIcons.Icon.Dice, UIKit.Role.Secondary),
            ("azar", UIIcons.Icon.Dice, UIKit.Role.Secondary),
            ("quit", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("leave", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("exit", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("salir", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("abandonar", UIIcons.Icon.Exit, UIKit.Role.Danger),
            ("cancel", UIIcons.Icon.Back, UIKit.Role.Ghost),
            ("cancelar", UIIcons.Icon.Back, UIKit.Role.Ghost),
            ("back", UIIcons.Icon.Back, UIKit.Role.Ghost),
            ("volver", UIIcons.Icon.Back, UIKit.Role.Ghost),
            ("confirm", UIIcons.Icon.Check, UIKit.Role.Primary),
            ("aceptar", UIIcons.Icon.Check, UIKit.Role.Primary),
            ("start", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("play", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("jugar", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("resume", UIIcons.Icon.Play, UIKit.Role.Primary),
            ("reanudar", UIIcons.Icon.Play, UIKit.Role.Primary),
        };

        static bool IsReplaceable(Image image)
        {
            // A plain untextured quad is a surface by definition — there is no art to lose.
            if (image.sprite == null)
            {
                return true;
            }

            // Themed art is art, however generic its name sounds: ui_btn_blank and ui_dialog are
            // now drawn plates, not blank surfaces waiting for one.
            return k_ReplaceableSprites.Contains(image.sprite.name)
                   && !k_ThemedArt.Contains(image.sprite.name);
        }

        static void StyleArt(Image image)
        {
            if (image.sprite == null)
            {
                return;
            }

            if (k_ReplacedLogos.Contains(image.sprite.name))
            {
                ReplaceWithWordmark(image);
                return;
            }

            // Everything the pass will not reshape still gets repainted: the character-select
            // banner, the frames, the plates. This is the half of the restyle that used to be
            // missing — a screen where only the blank plates changed colour reads as half-finished.
            RepaintArt(image);

            if (!k_GlowingArt.Contains(image.sprite.name))
            {
                return;
            }

            var glow = ToonMenuSkin.AddBackGlow((RectTransform)image.transform,
                new Color(ToonMenuSkin.Accent.r, ToonMenuSkin.Accent.g, ToonMenuSkin.Accent.b, 0.3f), 1.15f);

            if (glow != null)
            {
                glow.gameObject.AddComponent<ToonGlowPulse>().SetRange(0.16f, 0.4f);
            }
        }

        /// <summary>
        /// Swaps the sample's logo for this game's wordmark, in place: the image is emptied rather
        /// than destroyed, so whatever laid it out keeps its child and its size.
        /// </summary>
        static void ReplaceWithWordmark(Image image)
        {
            var rect = (RectTransform)image.transform;
            if (rect.Find("BrandMark") != null)
            {
                return;
            }

            image.enabled = false;

            // A title plate is tall enough for the tagline under the name; a small header is not,
            // and a second line there would only be noise.
            BrandMark.Build(rect, rect.rect.height >= 90f);
        }

        // ── Backdrop ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One screen-wide vignette behind every menu canvas. It is what gives the cards
        /// something to sit against on top of the 3D scene, and it costs one quad.
        /// </summary>
        void EnsureBackdrop()
        {
            if (m_Backdrop == null)
            {
                var host = new GameObject(k_BackdropName, typeof(Canvas));
                host.transform.SetParent(transform, false);

                var canvas = host.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // Far below anything the game authors, so it can never cover a menu. It carries
                // no GraphicRaycaster either, so it cannot swallow a click.
                canvas.sortingOrder = -500;

                var vignette = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)vignette.transform;
                rect.SetParent(host.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var image = vignette.GetComponent<Image>();
                image.sprite = ToonMenuSkin.VignetteSprite;
                image.color = new Color(ToonMenuSkin.Ink.r, ToonMenuSkin.Ink.g, ToonMenuSkin.Ink.b, 0.55f);
                image.raycastTarget = false;

                m_Backdrop = host;
            }

            m_Backdrop.SetActive(m_InMenuScene);
        }
    }
}
