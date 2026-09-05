using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    public class SessionJoiningUI : MonoBehaviour
    {
        [SerializeField] SessionListItemUI m_SessionListItemPrototype;
        [SerializeField] InputField m_JoinCodeField;
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] Graphic m_EmptySessionListLabel;
        [SerializeField] Button m_JoinSessionButton;

        SessionUIMediator m_SessionUIMediator;
        LobbyResponse m_SelectedLobby;

        // Set by BuildUI() when self-constructing; holds the scroll content Transform.
        Transform m_ListContentRoot;
        Image m_SelectedRowBg;

        public void Initialize(SessionUIMediator mediator)
        {
            m_SessionUIMediator = mediator;
            if (m_SessionListItemPrototype)
                m_SessionListItemPrototype.gameObject.SetActive(false);
        }

        void Awake()
        {
            if (m_CanvasGroup == null)
                BuildUI();
        }

        void SetEmptyLabel(string message)
        {
            if (m_EmptySessionListLabel == null) return;
            var t = m_EmptySessionListLabel.GetComponent<Text>();
            if (t != null) t.text = message;
        }

        // ── Lobby list population ─────────────────────────────────────────────

        public void PopulateLobbies(LobbyResponse[] lobbies)
        {
            Transform parent;
            if (m_SessionListItemPrototype != null)
                parent = m_SessionListItemPrototype.transform.parent;
            else if (m_ListContentRoot != null)
                parent = m_ListContentRoot;
            else
                return;

            // Clear previous rows (keep the prototype if present).
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (m_SessionListItemPrototype != null && child == m_SessionListItemPrototype.transform)
                    continue;
                Destroy(child.gameObject);
            }

            m_SelectedLobby = null;
            m_SelectedRowBg = null;
            ResetJoinCodeField();
            if (m_JoinSessionButton) m_JoinSessionButton.interactable = false;

            // null = server unreachable; [] = connected but no rooms.
            if (lobbies == null)
            {
                if (m_EmptySessionListLabel) m_EmptySessionListLabel.gameObject.SetActive(true);
                SetEmptyLabel("No se pudo contactar al servidor.\nComprueba que esté activo y pulsa Actualizar.");
                return;
            }

            bool any = lobbies.Length > 0;
            if (m_EmptySessionListLabel) m_EmptySessionListLabel.gameObject.SetActive(!any);
            if (!any) SetEmptyLabel("No hay salas públicas ahora mismo.\nCrea una o escribe un código abajo.");

            foreach (var lobby in lobbies)
            {
                if (m_SessionListItemPrototype != null)
                {
                    var item = Instantiate(m_SessionListItemPrototype, parent);
                    item.gameObject.SetActive(true);
                    item.SetData(lobby, this);
                }
                else
                {
                    BuildListRow(lobby, parent);
                }
            }
        }

        // Called by SessionListItemUI.OnClick (prefab path).
        public void OnLobbySelected(LobbyResponse lobby)
        {
            m_SelectedLobby = lobby;
            if (m_JoinSessionButton) m_JoinSessionButton.interactable = true;
            UpdateJoinCodeFieldForLobby(lobby);
        }

        public void Show()
        {
            if (m_CanvasGroup)
            {
                m_CanvasGroup.alpha = 1f;
                m_CanvasGroup.blocksRaycasts = true;
                m_CanvasGroup.interactable = true;
            }
            m_SessionUIMediator?.QuerySessionRequest(false);
        }

        public void Hide()
        {
            if (m_CanvasGroup)
            {
                m_CanvasGroup.alpha = 0f;
                m_CanvasGroup.blocksRaycasts = false;
                m_CanvasGroup.interactable = false;
            }
        }

        public void OnJoinCodeInputTextChanged() { }

        public void OnJoinButtonPressed()
        {
            if (m_SelectedLobby != null)
            {
                string password = (m_SelectedLobby.is_private && m_JoinCodeField != null)
                    ? m_JoinCodeField.text : null;
                m_SessionUIMediator?.JoinLobbyRequest(m_SelectedLobby, password);
            }
            else if (m_JoinCodeField != null && !string.IsNullOrEmpty(m_JoinCodeField.text))
            {
                m_SessionUIMediator?.JoinSessionWithCodeRequest(m_JoinCodeField.text);
            }
        }

        public void OnRefresh()
        {
            m_SessionUIMediator?.QuerySessionRequest(true);
        }

        public void OnQuickJoinClicked()
        {
            m_SessionUIMediator?.QuickJoinRequest();
        }

        void UpdateJoinCodeFieldForLobby(LobbyResponse lobby)
        {
            if (m_JoinCodeField == null) return;
            m_JoinCodeField.text = string.Empty;
            if (lobby.is_private)
            {
                m_JoinCodeField.gameObject.SetActive(true);
                m_JoinCodeField.contentType = InputField.ContentType.Password;
                if (m_JoinCodeField.placeholder is Text ph) ph.text = "Contraseña";
            }
            else
            {
                m_JoinCodeField.gameObject.SetActive(false);
            }
        }

        void ResetJoinCodeField()
        {
            if (m_JoinCodeField == null) return;
            m_JoinCodeField.text = string.Empty;
            m_JoinCodeField.contentType = InputField.ContentType.Standard;
            m_JoinCodeField.gameObject.SetActive(true);
            if (m_JoinCodeField.placeholder is Text ph) ph.text = "Código de sala";
        }

        // ── Procedural list row (used when no prefab prototype is wired) ──────

        void BuildListRow(LobbyResponse lobby, Transform parent)
        {
            var row = new GameObject($"Row_{lobby.session_id}");
            row.transform.SetParent(parent, false);

            var bg = row.AddComponent<Image>();
            bg.color = ToonMenuSkin.ButtonFill;

            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 46f);

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 46f;
            le.flexibleWidth = 1f;

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cols = btn.colors;
            cols.normalColor = ToonMenuSkin.ButtonFill;
            cols.highlightedColor = ToonMenuSkin.ButtonHighlight;
            cols.selectedColor = Color.Lerp(ToonMenuSkin.ButtonFill, ToonMenuSkin.Accent, 0.55f);
            btn.colors = cols;

            var capturedLobby = lobby;
            btn.onClick.AddListener(() => SelectRowProcedural(capturedLobby, bg));

            // Room name (left-aligned)
            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(row.transform, false);
            var nameText = nameGO.AddComponent<Text>();
            nameText.font = GetFont();
            nameText.text = lobby.name;
            nameText.fontSize = 14;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = HudSkin.TextPrimary;
            nameText.alignment = TextAnchor.MiddleLeft;
            var nameR = nameGO.GetComponent<RectTransform>();
            nameR.anchorMin = new Vector2(0f, 0f);
            nameR.anchorMax = new Vector2(0.62f, 1f);
            nameR.offsetMin = new Vector2(12f, 0f);
            nameR.offsetMax = Vector2.zero;

            // Player count (right, green)
            var countGO = new GameObject("Count");
            countGO.transform.SetParent(row.transform, false);
            var countText = countGO.AddComponent<Text>();
            countText.font = GetFont();
            countText.text = $"{lobby.current_players} / {lobby.max_players}";
            countText.fontSize = 13;
            countText.color = lobby.current_players >= lobby.max_players
                ? UIKit.Danger   // red = full
                : UIKit.Positive;  // green = available
            countText.alignment = TextAnchor.MiddleRight;
            var countR = countGO.GetComponent<RectTransform>();
            countR.anchorMin = new Vector2(0.62f, 0f);
            countR.anchorMax = new Vector2(1f, 1f);
            countR.offsetMin = Vector2.zero;
            countR.offsetMax = new Vector2(-12f, 0f);

            // Lock label for private rooms
            if (lobby.is_private)
            {
                var lockGO = new GameObject("Lock");
                lockGO.transform.SetParent(row.transform, false);
                var lockText = lockGO.AddComponent<Text>();
                lockText.font = GetFont();
                lockText.text = "[PRIVADA]";
                lockText.fontSize = 11;
                lockText.color = HudSkin.Amethyst;
                lockText.alignment = TextAnchor.MiddleLeft;
                var lockR = lockGO.GetComponent<RectTransform>();
                lockR.anchorMin = new Vector2(0f, 0f);
                lockR.anchorMax = new Vector2(0.62f, 0.45f);
                lockR.offsetMin = new Vector2(12f, 0f);
                lockR.offsetMax = Vector2.zero;

                // Push name text up to leave room for the PRIVATE badge below it.
                nameR.anchorMin = new Vector2(0f, 0.45f);
                nameR.anchorMax = new Vector2(0.62f, 1f);
            }
        }

        void SelectRowProcedural(LobbyResponse lobby, Image rowBg)
        {
            if (m_SelectedRowBg != null)
                m_SelectedRowBg.color = ToonMenuSkin.ButtonFill;

            m_SelectedLobby = lobby;
            m_SelectedRowBg = rowBg;
            rowBg.color = Color.Lerp(ToonMenuSkin.ButtonFill, ToonMenuSkin.Accent, 0.55f);

            if (m_JoinSessionButton) m_JoinSessionButton.interactable = true;
            UpdateJoinCodeFieldForLobby(lobby);
        }

        // ── Self-build ────────────────────────────────────────────────────────

        void BuildUI()
        {
            if (!GetComponent<Canvas>())
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
            }
            if (!GetComponent<CanvasScaler>()) gameObject.AddComponent<CanvasScaler>();
            if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();

            m_CanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(transform, false);
            bg.AddComponent<Image>().color = ToonMenuSkin.CardFill;
            var bgR = bg.GetComponent<RectTransform>();
            bgR.anchorMin = bgR.anchorMax = new Vector2(0.5f, 0.5f);
            bgR.sizeDelta = new Vector2(560f, 560f);
            bgR.anchoredPosition = Vector2.zero;

            float y = 240f;

            // ── Header ────────────────────────────────────────────────────────
            PlaceText(bg.transform, "Unirse a una sala", y, 540f, 40f, 26, FontStyle.Bold, HudSkin.TextPrimary);
            y -= 44f;
            PlaceText(bg.transform, "Elige una sala pública o escribe un código abajo", y, 540f, 24f, 12,
                FontStyle.Italic, HudSkin.TextDim);
            y -= 30f;
            MakeDivider(bg.transform, y);
            y -= 16f;

            // ── "Salas públicas" label + Refresh button ─────────────────────────
            PlaceText(bg.transform, "Salas públicas", y, -60f, 22f, 13, FontStyle.Bold, HudSkin.TextDim);
            // Position: -60 offset = left of center

            var refreshBtn = MakeSmallButton(bg.transform, "Actualizar", new Vector2(225f, y), OnRefresh);
            y -= 30f;

            // ── Scroll view for room list ─────────────────────────────────────
            const float listH = 190f;
            m_ListContentRoot = BuildScrollList(bg.transform, new Vector2(0f, y - listH * 0.5f), 520f, listH);
            y -= listH + 6f;

            // Empty-state label (hidden when rooms are available)
            var emptyGO = new GameObject("EmptyLabel");
            emptyGO.transform.SetParent(bg.transform, false);
            var emptyText = emptyGO.AddComponent<Text>();
            emptyText.font = GetFont();
            emptyText.text = "No hay salas públicas. Pulsa Actualizar o escribe un código.";
            emptyText.fontSize = 13;
            emptyText.fontStyle = FontStyle.Italic;
            emptyText.color = HudSkin.TextDim;
            emptyText.alignment = TextAnchor.MiddleCenter;
            var emptyR = emptyGO.GetComponent<RectTransform>();
            emptyR.anchorMin = emptyR.anchorMax = new Vector2(0.5f, 0.5f);
            emptyR.sizeDelta = new Vector2(520f, 40f);
            emptyR.anchoredPosition = new Vector2(0f, y + listH * 0.5f); // centred in list area
            m_EmptySessionListLabel = emptyText;
            emptyGO.SetActive(true); // shown until lobbies load

            MakeDivider(bg.transform, y);
            y -= 16f;

            // ── Session code label ────────────────────────────────────────────
            PlaceText(bg.transform, "o escribe un código de sala:", y, 520f, 22f, 12,
                FontStyle.Normal, HudSkin.TextDim);
            y -= 26f;
            m_JoinCodeField = MakeInputField(bg.transform, "Código de sala", ref y, false, 520f);

            // ── Password field (hidden until private room selected) ───────────
            float pwY = y;
            var pwField = MakeInputField(bg.transform, "Contraseña", ref pwY, true, 520f);
            pwField.gameObject.SetActive(false);
            m_JoinCodeField.onValueChanged.AddListener(_ =>
            {
                // When typing a code directly, hide the password field (no room selected).
                if (m_SelectedLobby == null) pwField.gameObject.SetActive(false);
            });
            // We store the password field reference by reusing m_JoinCodeField slot after
            // user selects a private room (UpdateJoinCodeFieldForLobby handles this).

            y -= 16f;

            // ── Action buttons ────────────────────────────────────────────────
            MakeButton(bg.transform, "Partida rápida", new Vector2(-135f, y), OnQuickJoinClicked,
                Color.Lerp(ToonMenuSkin.ButtonFill, UIKit.Positive, 0.5f), 180f, 44f);
            m_JoinSessionButton = MakeButton(bg.transform, "Unirse a la sala", new Vector2(135f, y), OnJoinButtonPressed,
                Color.Lerp(ToonMenuSkin.ButtonFill, ToonMenuSkin.Accent, 0.55f), 200f, 44f);
            m_JoinSessionButton.interactable = false;
        }

        Transform BuildScrollList(Transform parent, Vector2 anchoredPos, float w, float h)
        {
            // ScrollRect root
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(parent, false);
            var scrollImg = scrollGO.AddComponent<Image>();
            scrollImg.color = ToonMenuSkin.InputFill;
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = scrollRT.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRT.sizeDelta = new Vector2(w, h);
            scrollRT.anchoredPosition = anchoredPos;

            // Viewport
            var vpGO = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            var vpImg = vpGO.AddComponent<Image>();
            vpImg.color = Color.clear;
            vpGO.AddComponent<Mask>().showMaskGraphic = false;
            var vpRT = vpGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;

            // Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            if (contentRT == null) contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = new Vector2(0f, 0f);
            contentRT.anchoredPosition = Vector2.zero;

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // ScrollRect
            var sr = scrollGO.AddComponent<ScrollRect>();
            sr.content = contentRT;
            sr.viewport = vpRT;
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 20f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            return contentRT;
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        static Font s_Font;
        static Font GetFont() => s_Font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static void PlaceText(Transform parent, string text, float y, float w, float h,
            int size, FontStyle style, Color color)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = GetFont(); t.text = text; t.fontSize = size;
            t.fontStyle = style; t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            var r = go.GetComponent<RectTransform>();
            // Negative w is treated as an x-offset from center for left-aligned labels.
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(Mathf.Abs(w), h);
            r.anchoredPosition = new Vector2(w < 0 ? w * 0.5f : 0f, y);
            if (w < 0) t.alignment = TextAnchor.MiddleLeft;
        }

        static void MakeDivider(Transform parent, float y)
        {
            var go = new GameObject("Divider");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = ToonMenuSkin.InlayColor;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(520f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
        }

        static InputField MakeInputField(Transform parent, string placeholder, ref float y, bool password, float width)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = ToonMenuSkin.ButtonFill;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, 42f);
            r.anchoredPosition = new Vector2(0f, y - 21f);
            y -= 48f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var it = textGO.AddComponent<Text>();
            it.font = GetFont(); it.fontSize = 14; it.color = HudSkin.TextPrimary;
            it.alignment = TextAnchor.MiddleLeft; it.supportRichText = false;
            var itr = textGO.GetComponent<RectTransform>();
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = new Vector2(10f, 2f); itr.offsetMax = new Vector2(-10f, -2f);

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var ph = phGO.AddComponent<Text>();
            ph.font = GetFont(); ph.text = placeholder; ph.fontSize = 14;
            ph.fontStyle = FontStyle.Italic; ph.color = HudSkin.TextDim;
            ph.alignment = TextAnchor.MiddleLeft;
            var phr = phGO.GetComponent<RectTransform>();
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(10f, 2f); phr.offsetMax = new Vector2(-10f, -2f);

            var field = go.AddComponent<InputField>();
            field.textComponent = it;
            field.placeholder = ph;
            if (password) field.contentType = InputField.ContentType.Password;
            return field;
        }

        static Button MakeSmallButton(Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = ToonMenuSkin.ButtonHighlight;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); // anchored to left
            r.pivot = new Vector2(1f, 0.5f);
            r.sizeDelta = new Vector2(90f, 28f);
            r.anchoredPosition = pos;

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(go.transform, false);
            var t = tGO.AddComponent<Text>();
            t.font = GetFont(); t.text = label; t.fontSize = 12;
            t.color = HudSkin.TextPrimary;
            t.alignment = TextAnchor.MiddleCenter;
            var tr = tGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(action);
            return btn;
        }

        static Button MakeButton(Transform parent, string label, Vector2 pos,
            UnityEngine.Events.UnityAction action, Color color, float w, float h)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = pos;

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(go.transform, false);
            var t = tGO.AddComponent<Text>();
            t.font = GetFont(); t.text = label; t.fontSize = 14;
            t.fontStyle = FontStyle.Bold; t.color = HudSkin.TextPrimary;
            t.alignment = TextAnchor.MiddleCenter;
            var tr = tGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(action);
            return btn;
        }
    }
}
