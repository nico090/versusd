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

        /// <summary>
        /// Canvases this pass must not touch, matched as substrings of the root canvas name.
        /// The in-game HUD is the whole list: it is either dressed by <see cref="HudSkin"/>
        /// already, drawn in world space over a character, or a debug overlay.
        /// </summary>
        static readonly string[] k_SkippedCanvases =
        {
            "BossRoomHudCanvas", "Hero Action Bar", "Hero Emote Bar", "PartyHUD", "AllyHUD",
            "DeathmatchHUD", "Debug Overlay", "ControlsHintPanel", "MobileMovementJoystick",
            "MobileZoomBar", "AimIndicator", "UIHealth", "UIName", "NetworkOverlay",
            k_BackdropName,
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
                if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace || IsSkipped(canvas.name))
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

                    Apply(graphic, canvasSize);
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

        static bool IsSkipped(string canvasName)
        {
            foreach (var skipped in k_SkippedCanvases)
            {
                if (canvasName.IndexOf(skipped, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        void Apply(Graphic graphic, Vector2 canvasSize)
        {
            switch (graphic)
            {
                case TMP_Text tmpText:
                    StyleTmpText(tmpText);
                    break;
                case Text text:
                    ToonMenuSkin.StyleText(text);
                    break;
                case Image image:
                    StyleImage(image, canvasSize);
                    break;
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
            // icon backing. The button has already been dressed as one piece.
            if (gameObject.GetComponentInParent<Selectable>(true) != null)
            {
                return;
            }

            if (!IsReplaceable(image))
            {
                MaybeGlowArt(image);
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
                // Keep the art, take the feel: an illustrated button still gets to swell and
                // squash under the pointer.
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
            return image.sprite == null || k_ReplaceableSprites.Contains(image.sprite.name);
        }

        static void MaybeGlowArt(Image image)
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
