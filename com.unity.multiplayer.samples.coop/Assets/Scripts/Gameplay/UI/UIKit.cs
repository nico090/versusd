using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The widget kit every screen this project builds from code is assembled out of: canvases,
    /// cards, buttons with a role and an icon, input fields, list rows, badges and the type scale
    /// they all share.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a kit.</b> Before this, each code-built screen invented its widgets inline —
    /// its own greys, its own font sizes, its own hand-placed pixel offsets. The screens ended up
    /// disagreeing with each other and with the restyled prefab menus, and every one of them
    /// broke at a phone aspect ratio because the offsets were absolute. One kit means a screen is
    /// a list of intentions ("a card, a title, three buttons") and the look, the spacing and the
    /// layout behaviour are decided once, here.</para>
    ///
    /// <para><b>Where the look comes from.</b> Colours and plate sprites are
    /// <see cref="ToonMenuSkin"/>'s, so kit-built screens and restyled prefab screens are the same
    /// design. What the kit adds on top is <i>hierarchy</i>: a type scale, and the
    /// <see cref="Role"/> that says which of the buttons on screen is the one you came here to
    /// press.</para>
    ///
    /// <para><b>Layout, not coordinates.</b> Everything is built out of layout groups and
    /// anchors. That is what lets the same screen sit in a 21:9 window and on a phone held
    /// upright, which matters here because this game ships to both.</para>
    /// </remarks>
    public static class UIKit
    {
        // ── Scale ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Design resolution every kit canvas scales from.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>The spacing unit. Every gap in the kit is a small multiple of it.</summary>
        public const float Unit = 8f;

        /// <summary>Height of a standard button or input field. Comfortably past a thumb target.</summary>
        public const float ControlHeight = 56f;

        /// <summary>The kit's type sizes.</summary>
        public enum TextStyle
        {
            /// <summary>Screen-owning statement: a result, a game over.</summary>
            Display,

            /// <summary>The name of the screen.</summary>
            Title,

            /// <summary>Section heading inside a card.</summary>
            Heading,

            /// <summary>Ordinary reading text.</summary>
            Body,

            /// <summary>Secondary text: hints, counts, timestamps.</summary>
            Caption,

            /// <summary>Button and tab labels — upper case, tracked out.</summary>
            Label,
        }

        /// <summary>What a button is <i>for</i>, which is what decides how loud it is.</summary>
        public enum Role
        {
            /// <summary>The one action the screen exists for. Filled with the accent.</summary>
            Primary,

            /// <summary>A reasonable alternative. Dark plate, accent label.</summary>
            Secondary,

            /// <summary>Back, cancel, dismiss. Barely there until you touch it.</summary>
            Ghost,

            /// <summary>Leaving, deleting, giving up. Red.</summary>
            Danger,

            /// <summary>Confirming something good happened. Green.</summary>
            Positive,
        }

        // ── Role palette ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Warning / leave / destructive. Magenta rather than red: it has to be the loudest thing
        /// on a blue-violet screen, and pure red on this palette reads as an error dialog from
        /// another program.
        /// </summary>
        public static readonly Color Danger = new Color(0.92f, 0.28f, 0.55f, 1f);

        /// <summary>Success / available / ready. Held to teal so it stays inside the cold half.</summary>
        public static readonly Color Positive = new Color(0.34f, 0.82f, 0.72f, 1f);

        /// <summary>Rewards, winners, and anything else worth a medal. The one warm colour left.</summary>
        public static readonly Color Gold = HudSkin.Gold;

        /// <summary>Text laid on top of a filled accent or danger plate.</summary>
        public static readonly Color OnAccent = new Color(0.035f, 0.030f, 0.060f, 1f);

        // ── Canvases ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A screen-space canvas that scales with the window, plus the event system the UI needs
        /// to be clickable at all.
        /// </summary>
        /// <param name="sortingOrder">
        /// Higher draws later. The kit's own convention: screens 100, overlays 200, modals 300.
        /// </param>
        public static Canvas Root(GameObject host, string name, int sortingOrder)
        {
            var canvas = host.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = host.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            host.name = name;

            var scaler = host.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = host.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // Halfway between matching width and height: a phone held upright loses no width, and
            // an ultrawide window does not blow the type up.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (host.GetComponent<GraphicRaycaster>() == null)
            {
                host.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystem();
            Mark(host);

            return canvas;
        }

        /// <summary>
        /// Makes sure something is listening for clicks. A scene that only ever showed code-built
        /// UI can legitimately have no EventSystem, and without one every button is dead.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var host = new GameObject("EventSystem", typeof(EventSystem));
            // The project ships both input backends depending on platform; StandaloneInputModule
            // is the one that exists in either, and the input system package swaps itself in when
            // it is the active backend.
            host.AddComponent<StandaloneInputModule>();
        }

        // ── Layout ────────────────────────────────────────────────────────────────────────────

        /// <summary>An empty rect stretched over its parent.</summary>
        public static RectTransform Screen(Transform parent, string name)
        {
            var rect = NewRect(parent, name);
            Stretch(rect);

            return rect;
        }

        /// <summary>Stretches <paramref name="rect"/> to fill its parent.</summary>
        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);

            return rect;
        }

        /// <summary>A column of children, top to bottom.</summary>
        public static RectTransform Column(Transform parent, string name, float spacing = Unit * 1.5f,
            float padding = 0f, TextAnchor alignment = TextAnchor.UpperCenter)
        {
            var rect = NewRect(parent, name);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout, spacing, padding, alignment);

            return rect;
        }

        /// <summary>A row of children, left to right.</summary>
        public static RectTransform Row(Transform parent, string name, float spacing = Unit * 1.5f,
            float padding = 0f, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rect = NewRect(parent, name);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            ConfigureLayout(layout, spacing, padding, alignment);

            return rect;
        }

        static void ConfigureLayout(HorizontalOrVerticalLayoutGroup layout, float spacing, float padding,
            TextAnchor alignment)
        {
            layout.spacing = spacing;
            layout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            layout.childAlignment = alignment;
            // Children keep their own width/height unless something explicitly asks to be
            // stretched — the kit's widgets set that on themselves via LayoutElement.
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
        }

        /// <summary>
        /// The card a screen's content sits on: the toon plate, a column layout inside it, and a
        /// pop when it appears.
        /// </summary>
        /// <param name="size">
        /// Fixed size. Pass a zero component to let that axis size itself to its content instead.
        /// </param>
        public static RectTransform Card(Transform parent, string name, Vector2 size,
            float padding = Unit * 3f, float spacing = Unit * 1.5f)
        {
            var rect = NewRect(parent, name);
            // StyleCard reads the rect to decide whether to draw card geometry or the tighter
            // button geometry, so an axis left to size itself gets a plausible stand-in first —
            // otherwise a card that has not been laid out yet measures zero and comes back looking
            // like a button.
            rect.sizeDelta = new Vector2(size.x > 0f ? size.x : 400f, size.y > 0f ? size.y : 400f);

            var image = rect.gameObject.AddComponent<Image>();

            // The layout group goes on first, and only then is the card dressed: the contour and
            // sheen StyleCard lays over it are child images, and they are marked as ignoring
            // layout only if a layout group is already there to notice them. The other way round,
            // the card's own outline would be laid out as its first row and shove the content
            // down.
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureLayout(layout, spacing, padding, TextAnchor.UpperCenter);
            layout.childForceExpandWidth = true;

            ToonMenuSkin.StyleCard(image);

            if (size.x <= 0f || size.y <= 0f)
            {
                FitToContentIfUnmanaged(rect, size.x <= 0f, size.y <= 0f);
            }

            // Inside a parent layout, the card asks for the width it was given and lets its own
            // vertical group report the height.
            if (parent != null && parent.GetComponent<LayoutGroup>() != null)
            {
                Flexible(rect, size.y > 0f ? size.y : -1f, size.x > 0f ? size.x : -1f);
            }
            else
            {
                rect.sizeDelta = new Vector2(size.x > 0f ? size.x : rect.sizeDelta.x,
                    size.y > 0f ? size.y : rect.sizeDelta.y);
            }

            rect.gameObject.AddComponent<ToonPanelPop>();

            return rect;
        }

        /// <summary>A hairline rule between sections.</summary>
        public static Image Divider(Transform parent, float thickness = 2f)
        {
            var rect = NewRect(parent, "Divider");
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ToonMenuSkin.InputFillSprite;
            image.type = Image.Type.Sliced;
            // Lapis, not accent: a rule between sections is another incised line in the stone,
            // and the bright blue is reserved for the tubes and for what you are meant to press.
            image.color = ToonMenuSkin.InlayColor;
            image.raycastTarget = false;

            Flexible(rect, height: thickness, expandWidth: true);

            return image;
        }

        /// <summary>Empty space in a layout, for when a gap has to be bigger than the spacing.</summary>
        public static RectTransform Spacer(Transform parent, float height)
        {
            var rect = NewRect(parent, "Spacer");
            Flexible(rect, height: height, expandWidth: true);

            return rect;
        }

        /// <summary>
        /// Tells the parent layout how much room this rect wants. <paramref name="flexibleHeight"/>
        /// above zero is what makes one child (a list, usually) absorb whatever is left over.
        /// </summary>
        public static LayoutElement Flexible(RectTransform rect, float height = -1f, float width = -1f,
            bool expandWidth = false, float flexibleHeight = -1f)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
            }

            element.preferredHeight = height;
            element.minHeight = height;
            element.preferredWidth = width;
            element.flexibleWidth = expandWidth ? 1f : 0f;
            element.flexibleHeight = flexibleHeight;

            return element;
        }

        // ── Text ──────────────────────────────────────────────────────────────────────────────

        /// <summary>A line (or block) of text in one of the kit's sizes.</summary>
        public static TextMeshProUGUI Text(Transform parent, string content, TextStyle style,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
        {
            var rect = NewRect(parent, style.ToString());
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();

            text.text = content;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.richText = true;
            text.fontSize = FontSize(style);
            text.color = color ?? DefaultColor(style);

            switch (style)
            {
                case TextStyle.Display:
                case TextStyle.Title:
                    text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
                    text.characterSpacing = 6f;
                    text.outlineColor = ToonMenuSkin.Ink;
                    text.outlineWidth = 0.22f;
                    break;

                case TextStyle.Heading:
                case TextStyle.Label:
                    text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
                    text.characterSpacing = 5f;
                    text.outlineColor = ToonMenuSkin.Ink;
                    text.outlineWidth = 0.16f;
                    break;

                default:
                    text.fontStyle = FontStyles.Normal;
                    break;
            }

            text.enableWordWrapping = true;

            // A layout group asks its children how tall they want to be. TMP answers that
            // correctly for wrapped text, so preferredHeight is left unset (-1) and TMP's own
            // answer wins — that is what lets a two-line caption take two lines. The floor is one
            // line, so a label that is empty right now (a status line waiting for an error) still
            // holds its space instead of making the card jump when it fills.
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.preferredHeight = style == TextStyle.Label ? FontSize(style) * 1.45f : -1f;
            element.minHeight = FontSize(style) * 1.45f;

            return text;
        }

        static float FontSize(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Display: return 64f;
                case TextStyle.Title: return 40f;
                case TextStyle.Heading: return 24f;
                case TextStyle.Label: return 20f;
                case TextStyle.Caption: return 16f;
                default: return 20f;
            }
        }

        static Color DefaultColor(TextStyle style)
        {
            switch (style)
            {
                // Titles are cut in amethyst; the blue belongs to the controls. Splitting the
                // two cold hues by job is what keeps a screen from reading as all chrome, and it
                // leaves gold meaning nothing but first place.
                case TextStyle.Display:
                case TextStyle.Title: return HudSkin.Amethyst;
                case TextStyle.Caption: return HudSkin.TextDim;
                default: return HudSkin.TextPrimary;
            }
        }

        // ── Buttons ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// How far a button label may shrink to stay on its plate. Low enough to rescue a long
        /// Spanish label next to a second button, high enough that a shrunk label still reads as
        /// the same UI as the ones beside it rather than as a mistake.
        /// </summary>
        const float k_MinLabelScale = 0.72f;

        /// <summary>
        /// A labelled button, optionally with an icon in front of the label.
        /// </summary>
        /// <remarks>
        /// The icon is a child image rather than a sprite on the button itself, because the button
        /// already spends its own sprite on the toon plate. Sizing it off the label's font size is
        /// what keeps the pair looking like one drawn object at any scale.
        /// </remarks>
        public static Button Button(Transform parent, string label, Role role, UnityAction onClick,
            UIIcons.Icon? icon = null, float width = -1f, float height = ControlHeight)
        {
            var rect = NewRect(parent, "Btn " + label);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();

            ToonMenuSkin.StyleButton(button, image, tintable: true);
            ApplyRole(button, image, role);

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            // The label row is a child so the icon and the text can be laid out together and
            // centred as a unit, whatever the button's width turns out to be.
            var content = Row(rect, "Content", Unit * 1.25f, 0f, TextAnchor.MiddleCenter);
            Stretch(content, Unit * 1.5f);
            content.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            if (icon.HasValue)
            {
                var glyph = Icon(content, icon.Value, LabelColor(role), FontSize(TextStyle.Label) * 1.15f);
                glyph.raycastTarget = false;
            }

            if (!string.IsNullOrEmpty(label))
            {
                var text = Text(content, label, TextStyle.Label, TextAlignmentOptions.Center, LabelColor(role));
                text.enableWordWrapping = false;
                text.GetComponent<LayoutElement>().preferredWidth = -1f;

                // A label wider than its plate used to just run off both ends: nothing here wraps,
                // and nothing shrank. "Entrar como invitado" sharing a row with a second button is
                // the case that shows it — the words ended up wider than the button drawn under
                // them. Auto-sizing with the style's own size as the CEILING means a label that
                // already fits is left exactly as it was, and only an overlong one gives ground.
                text.enableAutoSizing = true;
                text.fontSizeMax = FontSize(TextStyle.Label);
                text.fontSizeMin = FontSize(TextStyle.Label) * k_MinLabelScale;
            }

            rect.sizeDelta = new Vector2(width > 0f ? width : 260f, height);
            Flexible(rect, height, width, expandWidth: width <= 0f);

            return button;
        }

        /// <summary>A square button that is nothing but an icon — close, refresh, settings.</summary>
        public static Button IconButton(Transform parent, UIIcons.Icon icon, Role role, UnityAction onClick,
            float size = ControlHeight)
        {
            var rect = NewRect(parent, "Btn " + icon);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();

            ToonMenuSkin.StyleButton(button, image, tintable: true);
            ApplyRole(button, image, role);

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var glyph = Icon(rect, icon, LabelColor(role), size * 0.5f);
            glyph.raycastTarget = false;
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = glyphRect.anchorMax = glyphRect.pivot = new Vector2(0.5f, 0.5f);
            glyphRect.anchoredPosition = Vector2.zero;
            // Icon() already gave the glyph a LayoutElement; this is the same one, told to stay
            // out of any layout the button itself might be sitting in.
            glyph.GetComponent<LayoutElement>().ignoreLayout = true;

            rect.sizeDelta = new Vector2(size, size);
            var element = Flexible(rect, size, size);
            element.flexibleWidth = 0f;

            return button;
        }

        /// <summary>Paints a button's four states for its role.</summary>
        static void ApplyRole(Selectable selectable, Image image, Role role)
        {
            Color fill;
            Color highlight;

            switch (role)
            {
                case Role.Primary:
                    fill = ToonMenuSkin.Accent;
                    highlight = Color.Lerp(ToonMenuSkin.Accent, Color.white, 0.25f);
                    break;

                case Role.Danger:
                    fill = Danger;
                    highlight = Color.Lerp(Danger, Color.white, 0.2f);
                    break;

                case Role.Positive:
                    fill = Positive;
                    highlight = Color.Lerp(Positive, Color.white, 0.2f);
                    break;

                case Role.Ghost:
                    // Reads as an outline until the pointer arrives, then fills in like the rest.
                    fill = new Color(ToonMenuSkin.ButtonFill.r, ToonMenuSkin.ButtonFill.g, ToonMenuSkin.ButtonFill.b, 0.25f);
                    highlight = ToonMenuSkin.ButtonHighlight;
                    break;

                default:
                    fill = ToonMenuSkin.ButtonFill;
                    highlight = ToonMenuSkin.ButtonHighlight;
                    break;
            }

            image.color = Color.white;
            selectable.transition = Selectable.Transition.ColorTint;
            selectable.targetGraphic = image;
            selectable.colors = new ColorBlock
            {
                normalColor = fill,
                highlightedColor = highlight,
                // Pressing darkens whatever the plate is, which sells the push alongside the
                // squash ToonButtonMotion adds.
                pressedColor = Color.Lerp(fill, ToonMenuSkin.Ink, 0.45f),
                selectedColor = fill,
                disabledColor = new Color(fill.r * 0.4f, fill.g * 0.4f, fill.b * 0.4f, 0.45f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        /// <summary>Label colour that stays legible on the role's plate.</summary>
        static Color LabelColor(Role role)
        {
            switch (role)
            {
                case Role.Primary:
                case Role.Danger:
                case Role.Positive:
                    return OnAccent;

                case Role.Ghost:
                    return ToonMenuSkin.Accent;

                default:
                    return HudSkin.TextPrimary;
            }
        }

        // ── Icons and badges ──────────────────────────────────────────────────────────────────

        /// <summary>A bare icon image, sized square.</summary>
        public static Image Icon(Transform parent, UIIcons.Icon icon, Color color, float size)
        {
            var rect = NewRect(parent, "Icon " + icon);
            var image = rect.gameObject.AddComponent<Image>();

            image.sprite = UIIcons.Get(icon);
            image.color = color;
            image.preserveAspect = true;
            rect.sizeDelta = new Vector2(size, size);

            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = size;
            element.preferredHeight = size;
            element.minWidth = size;

            return image;
        }

        /// <summary>
        /// A small tinted chip: a player count, a "private" marker, a mode. Sized to its content,
        /// so a badge never has to be measured by hand.
        /// </summary>
        public static RectTransform Badge(Transform parent, string label, Color color,
            UIIcons.Icon? icon = null)
        {
            var rect = Row(parent, "Badge " + label, Unit * 0.75f, Unit * 0.75f, TextAnchor.MiddleCenter);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ToonMenuSkin.InputFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2.4f;
            image.color = new Color(color.r, color.g, color.b, 0.16f);
            image.raycastTarget = false;

            var outline = ToonMenuSkin.AddOverlay(rect.gameObject, "Accent", ToonMenuSkin.InputOutlineSprite,
                new Color(color.r, color.g, color.b, 0.55f));
            outline.pixelsPerUnitMultiplier = 2.4f;

            if (icon.HasValue)
            {
                Icon(rect, icon.Value, color, 20f);
            }

            var text = Text(rect, label, TextStyle.Caption, TextAlignmentOptions.Center, color);
            text.enableWordWrapping = false;
            text.fontStyle = FontStyles.Bold;
            text.GetComponent<LayoutElement>().preferredWidth = -1f;

            FitToContentIfUnmanaged(rect, horizontal: true, vertical: false);

            Flexible(rect, 32f);

            return rect;
        }

        // ── Input ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A text field with an optional icon in its left gutter.
        /// </summary>
        public static TMP_InputField Input(Transform parent, string placeholder, UIIcons.Icon? icon = null,
            bool password = false, int characterLimit = 0)
        {
            var rect = NewRect(parent, "Input");
            // TMP_InputField wakes up the moment it is added and expects a viewport, a text
            // component and a placeholder to already be there. Building it switched off means it
            // wakes up once, complete, instead of complaining about the half of itself that does
            // not exist yet.
            rect.gameObject.SetActive(false);

            var image = rect.gameObject.AddComponent<Image>();
            var field = rect.gameObject.AddComponent<TMP_InputField>();

            ToonMenuSkin.StyleInput(field, image);

            float gutter = icon.HasValue ? Unit * 6f : Unit * 2f;

            if (icon.HasValue)
            {
                var glyph = Icon(rect, icon.Value, new Color(ToonMenuSkin.Accent.r, ToonMenuSkin.Accent.g,
                    ToonMenuSkin.Accent.b, 0.75f), 24f);
                glyph.raycastTarget = false;
                var glyphRect = (RectTransform)glyph.transform;
                glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0f, 0.5f);
                glyphRect.pivot = new Vector2(0f, 0.5f);
                glyphRect.anchoredPosition = new Vector2(Unit * 2f, 0f);
                glyph.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            // TMP_InputField needs a viewport with a mask, a text component and a placeholder;
            // building them here is what lets a caller ask for a field in one line.
            var viewport = NewRect(rect, "TextArea");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(gutter, Unit);
            viewport.offsetMax = new Vector2(-Unit * 2f, -Unit);
            viewport.gameObject.AddComponent<RectMask2D>();

            var placeholderText = Text(viewport, placeholder, TextStyle.Body, TextAlignmentOptions.Left,
                HudSkin.TextDim);
            placeholderText.fontStyle = FontStyles.Italic;
            Stretch((RectTransform)placeholderText.transform);

            var inputText = Text(viewport, string.Empty, TextStyle.Body, TextAlignmentOptions.Left,
                HudSkin.TextPrimary);
            Stretch((RectTransform)inputText.transform);

            field.textViewport = viewport;
            field.textComponent = inputText;
            field.placeholder = placeholderText;
            field.characterLimit = characterLimit;
            field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            field.selectionColor = new Color(ToonMenuSkin.Accent.r, ToonMenuSkin.Accent.g, ToonMenuSkin.Accent.b, 0.35f);
            field.caretColor = ToonMenuSkin.Accent;
            field.customCaretColor = true;

            Flexible(rect, ControlHeight, expandWidth: true);
            rect.gameObject.SetActive(true);

            return field;
        }

        /// <summary>A toggle drawn as a check chip plus a label, sized like a control.</summary>
        public static Toggle Checkbox(Transform parent, string label, bool value = false)
        {
            var rect = Row(parent, "Toggle " + label, Unit * 1.5f, 0f, TextAnchor.MiddleLeft);
            var toggle = rect.gameObject.AddComponent<Toggle>();

            var box = NewRect(rect, "Box");
            var boxImage = box.gameObject.AddComponent<Image>();
            ToonMenuSkin.StyleInput(toggle, boxImage);
            box.sizeDelta = new Vector2(34f, 34f);
            var boxElement = box.gameObject.AddComponent<LayoutElement>();
            boxElement.preferredWidth = 34f;
            boxElement.preferredHeight = 34f;
            boxElement.minWidth = 34f;

            var check = Icon(box, UIIcons.Icon.Check, ToonMenuSkin.Accent, 22f);
            check.raycastTarget = false;
            var checkRect = (RectTransform)check.transform;
            checkRect.anchorMin = checkRect.anchorMax = checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;

            var text = Text(rect, label, TextStyle.Body, TextAlignmentOptions.Left);
            text.enableWordWrapping = false;

            toggle.targetGraphic = boxImage;
            toggle.graphic = check;
            toggle.isOn = value;

            Flexible(rect, 40f, expandWidth: true);

            return toggle;
        }

        // ── Lists ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A vertically scrolling list. Returns the scroll rect; <paramref name="content"/> is the
        /// column rows should be parented to.
        /// </summary>
        public static ScrollRect List(Transform parent, string name, out RectTransform content,
            float spacing = Unit)
        {
            var rect = NewRect(parent, name);
            var scroll = rect.gameObject.AddComponent<ScrollRect>();

            var viewport = NewRect(rect, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = Column(viewport, "Content", spacing, 0f, TextAnchor.UpperCenter);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.scrollSensitivity = 32f;

            Flexible(rect, height: -1f, expandWidth: true, flexibleHeight: 1f);

            return scroll;
        }

        /// <summary>
        /// A selectable row for a list: a plate that lights up under the pointer, laid out as a
        /// left-to-right strip its caller fills.
        /// </summary>
        public static (Button button, RectTransform content) ListRow(Transform parent, string name,
            UnityAction onClick, float height = 64f)
        {
            var rect = NewRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();

            image.sprite = ToonMenuSkin.ButtonFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.6f;
            image.color = Color.white;

            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = new Color(ToonMenuSkin.ButtonFill.r, ToonMenuSkin.ButtonFill.g,
                    ToonMenuSkin.ButtonFill.b, 0.55f),
                highlightedColor = ToonMenuSkin.ButtonHighlight,
                pressedColor = ToonMenuSkin.ButtonPressed,
                selectedColor = new Color(ToonMenuSkin.ButtonFill.r, ToonMenuSkin.ButtonFill.g,
                    ToonMenuSkin.ButtonFill.b, 0.55f),
                disabledColor = ToonMenuSkin.ButtonDisabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var content = Row(rect, "Content", Unit * 1.5f, 0f, TextAnchor.MiddleLeft);
            Stretch(content, Unit * 1.75f);
            content.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            Flexible(rect, height, expandWidth: true);

            return (button, content);
        }

        /// <summary>
        /// A plain, non-interactive row plate: the same shape as <see cref="ListRow"/> for a list
        /// nobody clicks, like a results table. Returns the row rect, which is already a
        /// left-to-right layout its caller fills.
        /// </summary>
        public static RectTransform Strip(Transform parent, string name, Color fill, float height = 56f)
        {
            var rect = Row(parent, name, Unit * 1.5f, Unit * 1.75f, TextAnchor.MiddleLeft);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ToonMenuSkin.InputFillSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2f;
            image.color = fill;
            image.raycastTarget = false;

            Flexible(rect, height, expandWidth: true);

            return rect;
        }

        /// <summary>Lays an accent contour over a plate — a row's "this one is you" marker.</summary>
        public static Image Outline(GameObject host, Color color)
        {
            var image = ToonMenuSkin.AddOverlay(host, "Accent", ToonMenuSkin.InputOutlineSprite, color);
            image.pixelsPerUnitMultiplier = 2f;

            return image;
        }

        /// <summary>
        /// Marks a row as the selected one, by laying an accent contour over its plate. Called
        /// with <c>false</c> it takes the contour away again.
        /// </summary>
        public static void SetRowSelected(Button row, bool selected)
        {
            var image = ToonMenuSkin.AddOverlay(row.gameObject, "Selected", ToonMenuSkin.ButtonOutlineSprite,
                selected ? ToonMenuSkin.Accent : Color.clear);
            image.pixelsPerUnitMultiplier = 1.6f;

            var colors = row.colors;
            colors.normalColor = selected
                ? new Color(ToonMenuSkin.Accent.r * 0.35f, ToonMenuSkin.Accent.g * 0.35f, ToonMenuSkin.Accent.b * 0.4f, 0.9f)
                : new Color(ToonMenuSkin.ButtonFill.r, ToonMenuSkin.ButtonFill.g, ToonMenuSkin.ButtonFill.b, 0.55f);
            colors.selectedColor = colors.normalColor;
            row.colors = colors;
        }

        // ── Backdrop ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The dimmed sheet a modal sits on. It swallows clicks, which is what makes the thing in
        /// front of it modal in the first place.
        /// </summary>
        public static Image Scrim(Transform parent, float opacity = 0.72f)
        {
            var rect = Screen(parent, "Scrim");
            var image = rect.gameObject.AddComponent<Image>();

            image.color = new Color(ToonMenuSkin.Ink.r, ToonMenuSkin.Ink.g, ToonMenuSkin.Ink.b, opacity);
            image.raycastTarget = true;

            // Raycast-blocking is not enough on its own: the gameplay input sender deliberately
            // only yields to UI that would *do* something with the click, because the HUD is full
            // of decorative graphics that used to eat attacks. A button with no listener and no
            // transition is the cheapest thing that answers "yes, this click is mine".
            var blocker = rect.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            return image;
        }

        // ── Plumbing ──────────────────────────────────────────────────────────────────────────

        /// <summary>A bare RectTransform child, which is what every widget here starts as.</summary>
        /// <remarks>
        /// Centred and given a real size on the way out. A RectTransform added from code starts
        /// anchored to a corner with a zero size, so a widget dropped somewhere without a layout
        /// group to size it would otherwise be built correctly and then drawn as nothing.
        /// </remarks>
        public static RectTransform NewRect(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(200f, ControlHeight);

            return rect;
        }

        /// <summary>
        /// Adds a <see cref="ContentSizeFitter"/> only when nothing else is already sizing this
        /// rect. Inside a layout group the group is the one measuring, and a fitter there is both
        /// redundant and something Unity warns about — a nested layout group already reports its
        /// own preferred size to its parent, which is how a card sizes itself to its content in
        /// either situation.
        /// </summary>
        static void FitToContentIfUnmanaged(RectTransform rect, bool horizontal, bool vertical)
        {
            if (rect.parent != null && rect.parent.GetComponent<LayoutGroup>() != null)
            {
                return;
            }

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>Tells <see cref="ToonMenuRestyler"/> that this subtree is already dressed.</summary>
        public static void Mark(GameObject host)
        {
            if (host.GetComponent<UIKitBuilt>() == null)
            {
                host.AddComponent<UIKitBuilt>();
            }
        }
    }
}
