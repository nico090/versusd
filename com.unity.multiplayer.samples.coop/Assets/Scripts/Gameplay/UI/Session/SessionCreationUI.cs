using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    public class SessionCreationUI : MonoBehaviour
    {
        [SerializeField] InputField m_SessionNameInputField;
        [SerializeField] GameObject m_LoadingIndicatorObject;
        [SerializeField] Toggle m_IsPrivate;
        [SerializeField] Toggle m_UseDedicatedServer;
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] InputField m_PasswordField;
        [SerializeField] GameObject m_PasswordRow;

        SessionUIMediator m_SessionUIMediator;

        public void Initialize(SessionUIMediator mediator)
        {
            m_SessionUIMediator = mediator;
        }

        void Awake()
        {
            if (m_CanvasGroup == null)
                BuildUI();
            if (m_LoadingIndicatorObject) m_LoadingIndicatorObject.SetActive(false);
            if (m_PasswordRow) m_PasswordRow.SetActive(false);
            if (m_IsPrivate) m_IsPrivate.onValueChanged.AddListener(OnPrivateToggleChanged);
        }

        void Start()
        {
            if (GetComponent<Canvas>() == null && m_CanvasGroup != null)
            {
                // The SessionUI prefab never wired a dedicated-server toggle, so
                // m_UseDedicatedServer stays null and OnCreateClick() forces every
                // room to P2P (CreateSessionRequest) — the container is never spawned.
                // Inject the toggle at runtime and wire it so the player can choose.
                if (m_UseDedicatedServer == null)
                {
                    InjectDedicatedToggle(m_CanvasGroup.transform);
                }
            }

            // Outside the Canvas check above, and not guarded on the dedicated toggle. That block
            // only runs for a panel with no Canvas of its own, and the password row is needed
            // whatever this panel's shape turns out to be — it was missing from the prefab
            // entirely, so there is never an existing one to defer to.
            if (m_CanvasGroup != null)
            {
                InjectPasswordRow(m_CanvasGroup.transform);
            }

            // Show() also does this, but Show() may already have run: this panel is the one that
            // opens by default now, and nothing orders this Start() against the mediator's. The
            // toggle is injected switched on, so without this a build with dedicated servers
            // disabled could be left offering the option.
            ApplyDedicatedToggleVisibility();
        }

        void OnDestroy()
        {
            if (m_IsPrivate) m_IsPrivate.onValueChanged.RemoveListener(OnPrivateToggleChanged);
        }

        void OnPrivateToggleChanged(bool isPrivate)
        {
            if (m_PasswordRow) m_PasswordRow.SetActive(isPrivate);
            if (!isPrivate && m_PasswordField) m_PasswordField.text = string.Empty;
        }

        public void OnCreateClick()
        {
            bool isPrivate = m_IsPrivate != null && m_IsPrivate.isOn;
            // Dedicated is only an option when the build enables it; otherwise every room is
            // relay-hosted regardless of the (hidden) toggle's state.
            bool dedicatedAllowed = m_SessionUIMediator != null && m_SessionUIMediator.DedicatedServersEnabled;
            bool useDedicated = dedicatedAllowed && m_UseDedicatedServer != null && m_UseDedicatedServer.isOn;
            string password = (isPrivate && m_PasswordField != null) ? m_PasswordField.text : null;
            // Left blank on purpose when there's no field / no text: the mediator generates a
            // unique "Room-XXXX" name, since a fixed default collides with the master server's
            // duplicate-name check (409) once two players create an unnamed room.
            string name = m_SessionNameInputField != null ? m_SessionNameInputField.text : string.Empty;

            // Answered here rather than by the server. A private room with no password comes
            // back as HTTP 400 "Private lobbies require a password" — an error the player can do
            // nothing with, arriving after the request, phrased for whoever wrote the API. Asking
            // for it in the form is the same rule enforced where it can still be fixed.
            if (isPrivate && string.IsNullOrWhiteSpace(password))
            {
                m_SessionUIMediator?.ShowToast("Una sala privada necesita una contraseña.", 4f);
                if (m_PasswordField != null)
                {
                    m_PasswordField.Select();
                    m_PasswordField.ActivateInputField();
                }

                return;
            }

            if (useDedicated)
                m_SessionUIMediator?.CreateDedicatedSessionRequest(name, isPrivate, password);
            else
                // Relay: player hosts, the VPS relay forwards traffic (no port-forwarding).
                m_SessionUIMediator?.CreateSessionRequest(name, isPrivate, password);
        }

        // Hide the dedicated-server toggle when the build has dedicated servers disabled, so
        // Create Room only offers relay hosting. Called from Show() (after the mediator is set).
        void ApplyDedicatedToggleVisibility()
        {
            if (m_UseDedicatedServer == null) return;
            bool enabled = m_SessionUIMediator != null && m_SessionUIMediator.DedicatedServersEnabled;
            m_UseDedicatedServer.gameObject.SetActive(enabled);
        }

        public void Show()
        {
            if (m_CanvasGroup) { m_CanvasGroup.alpha = 1f; m_CanvasGroup.blocksRaycasts = true; m_CanvasGroup.interactable = true; }
            ApplyDedicatedToggleVisibility();
        }

        public void Hide()
        {
            if (m_CanvasGroup) { m_CanvasGroup.alpha = 0f; m_CanvasGroup.blocksRaycasts = false; m_CanvasGroup.interactable = false; }
        }

        // ── Runtime injection (prefab path) ──────────────────────────────────

        // Builds a "Dedicated server" checkbox at the top of the panel and wires
        // it to m_UseDedicatedServer, so checking it routes OnCreateClick() through
        // CreateDedicatedSessionRequest (docker run on the VPS) instead of P2P.
        /// <summary>
        /// Builds the password field the private-room flow needs.
        /// </summary>
        /// <remarks>
        /// <para><c>m_PasswordField</c> and <c>m_PasswordRow</c> are serialized fields that were
        /// never wired in the prefab. Every use of them is null-guarded, so nothing complained —
        /// the toggle worked, the row it was supposed to reveal did not exist, and the password
        /// went to the server as null. The master server then answered
        /// <c>HTTP 400: Private lobbies require a password</c> and the room was never created,
        /// with no way for the player to supply the thing it was asking for.</para>
        ///
        /// <para>Built here rather than added to the prefab for the same reason
        /// <see cref="InjectDedicatedToggle"/> is: an on-disk asset edit can be replaced by the
        /// Editor's cached copy when the dedicated-server build is made, and a control that exists
        /// in the Editor but not in the build is exactly the failure this is fixing.</para>
        /// </remarks>
        void InjectPasswordRow(Transform panelRoot)
        {
            if (panelRoot.Find("__PasswordRow") is Transform existing)
            {
                m_PasswordRow = existing.gameObject;
                m_PasswordField = existing.GetComponentInChildren<InputField>(true);
                m_PasswordRow.SetActive(m_IsPrivate != null && m_IsPrivate.isOn);
                return;
            }

            var row = new GameObject("__PasswordRow");
            row.transform.SetParent(panelRoot, false);

            var bar = row.AddComponent<Image>();
            bar.color = new Color(0.12f, 0.16f, 0.24f, 0.92f);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            // Directly under the dedicated-server toggle, which sits at -34 and is 30 tall.
            rowRect.anchoredPosition = new Vector2(0f, -68f);
            rowRect.sizeDelta = new Vector2(-40f, 32f);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(row.transform, false);
            var label = labelGO.AddComponent<Text>();
            label.text = "Contraseña";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.78f, 0.85f, 0.95f);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(8f, 0f);
            labelRect.sizeDelta = new Vector2(96f, 0f);

            var fieldGO = new GameObject("Field");
            fieldGO.transform.SetParent(row.transform, false);
            var fieldBg = fieldGO.AddComponent<Image>();
            fieldBg.color = new Color(0.04f, 0.06f, 0.11f, 0.95f);
            var fieldRect = fieldGO.GetComponent<RectTransform>();
            fieldRect.anchorMin = new Vector2(0f, 0f);
            fieldRect.anchorMax = new Vector2(1f, 1f);
            fieldRect.offsetMin = new Vector2(110f, 3f);
            fieldRect.offsetMax = new Vector2(-8f, -3f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(fieldGO.transform, false);
            var text = textGO.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.supportRichText = false;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);

            var field = fieldGO.AddComponent<InputField>();
            field.textComponent = text;
            field.contentType = InputField.ContentType.Password;
            field.lineType = InputField.LineType.SingleLine;

            m_PasswordRow = row;
            m_PasswordField = field;
            m_PasswordRow.SetActive(m_IsPrivate != null && m_IsPrivate.isOn);
        }

        void InjectDedicatedToggle(Transform panelRoot)
        {
            if (panelRoot.Find("__DedicatedToggle") is Transform existing)
            {
                m_UseDedicatedServer = existing.GetComponent<Toggle>();
                return;
            }

            var go = new GameObject("__DedicatedToggle");
            go.transform.SetParent(panelRoot, false);

            // A subtle background bar so the control reads as distinct from the
            // panel and stays legible even if it overlaps the prefab's title.
            var bar = go.AddComponent<Image>();
            bar.color = new Color(0.12f, 0.16f, 0.24f, 0.92f);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -34f);
            r.sizeDelta = new Vector2(-40f, 30f); // 20px inset each side

            // Checkbox background.
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            bgGO.AddComponent<Image>().color = new Color(0.28f, 0.28f, 0.38f);
            var bgR = bgGO.GetComponent<RectTransform>();
            bgR.anchorMin = bgR.anchorMax = new Vector2(0f, 0.5f);
            bgR.pivot = new Vector2(0f, 0.5f);
            bgR.sizeDelta = new Vector2(20f, 20f);
            bgR.anchoredPosition = new Vector2(8f, 0f);

            // Checkmark.
            var ckGO = new GameObject("Checkmark");
            ckGO.transform.SetParent(bgGO.transform, false);
            var ck = ckGO.AddComponent<Image>();
            ck.color = new Color(0.3f, 0.85f, 0.4f);
            var ckR = ckGO.GetComponent<RectTransform>();
            ckR.anchorMin = new Vector2(0.15f, 0.15f);
            ckR.anchorMax = new Vector2(0.85f, 0.85f);
            ckR.sizeDelta = Vector2.zero;

            // Label.
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var lt = labelGO.AddComponent<Text>();
            lt.font = GetFont();
            lt.text = "Servidor dedicado  (contenedor en el VPS; si no, se hospeda por relay)";
            lt.fontSize = 12;
            lt.color = new Color(0.88f, 0.88f, 0.88f);
            lt.alignment = TextAnchor.MiddleLeft;
            var lr = labelGO.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(34f, 0f); lr.offsetMax = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgGO.GetComponent<Image>();
            toggle.graphic = ck;
            // Default OFF: rooms are relay-hosted (player hosts, VPS just forwards) unless the
            // player opts into a dedicated VPS container here.
            toggle.isOn = false;
            m_UseDedicatedServer = toggle;
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
            bg.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.97f);
            var bgR = bg.GetComponent<RectTransform>();
            bgR.anchorMin = bgR.anchorMax = new Vector2(0.5f, 0.5f);
            bgR.sizeDelta = new Vector2(500f, 500f);
            bgR.anchoredPosition = Vector2.zero;

            float y = 205f;

            // ── Header ────────────────────────────────────────────────────────
            PlaceText(bg.transform, "Crear sala", y, 480f, 40f, 26, FontStyle.Bold, Color.white);
            y -= 44f;
            PlaceText(bg.transform, "Vas a ser el anfitrión: los demás se conectan a ti", y, 480f, 24f, 12,
                FontStyle.Italic, new Color(0.6f, 0.7f, 0.85f));
            y -= 32f;

            // ── Divider ───────────────────────────────────────────────────────
            MakeDivider(bg.transform, y);
            y -= 20f;

            // ── Room name ─────────────────────────────────────────────────────
            PlaceText(bg.transform, "Nombre de la sala", y, 450f, 22f, 12, FontStyle.Normal, new Color(0.75f, 0.75f, 0.75f));
            y -= 26f;
            m_SessionNameInputField = MakeInputField(bg.transform,
                "Mi sala  (déjalo vacío para un nombre al azar)", ref y, false, 450f);

            // ── Private toggle ────────────────────────────────────────────────
            y -= 10f;
            m_IsPrivate = MakeToggle(bg.transform, "Sala privada  (comparte el código para invitar)", y);
            y -= 40f;

            // ── Password row (hidden until private is on) ─────────────────────
            var pwRow = new GameObject("PasswordRow");
            pwRow.transform.SetParent(bg.transform, false);
            var pwRT = pwRow.GetComponent<RectTransform>();
            if (pwRT == null) pwRT = pwRow.AddComponent<RectTransform>();
            pwRT.anchorMin = pwRT.anchorMax = new Vector2(0.5f, 0.5f);
            pwRT.sizeDelta = new Vector2(450f, 58f);
            pwRT.anchoredPosition = new Vector2(0f, y - 29f);
            m_PasswordRow = pwRow;

            PlaceText(pwRow.transform, "Contraseña", 29f, 450f, 22f, 12, FontStyle.Normal, new Color(0.75f, 0.75f, 0.75f));
            float innerY = 4f;
            m_PasswordField = MakeInputField(pwRow.transform, "room password…", ref innerY, true, 450f);
            y -= 66f;

            // ── Dedicated server toggle ───────────────────────────────────────
            m_UseDedicatedServer = MakeToggle(bg.transform,
                "Servidor dedicado  (contenedor en el VPS; si no, se hospeda por relay)", y);
            // Default off — relay hosting is the primary (cheap) path; dedicated is opt-in.
            m_UseDedicatedServer.isOn = false;
            y -= 40f;

            MakeDivider(bg.transform, y);
            y -= 24f;

            // ── Create button ─────────────────────────────────────────────────
            MakeButton(bg.transform, "Crear sala", new Vector2(0f, y), OnCreateClick,
                new Color(0.14f, 0.48f, 0.18f), 220f, 46f);
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
            t.font = GetFont();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(0f, y);
        }

        static void MakeDivider(Transform parent, float y)
        {
            var go = new GameObject("Divider");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(460f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
        }

        static InputField MakeInputField(Transform parent, string placeholder, ref float y, bool password, float width)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.30f);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, 42f);
            r.anchoredPosition = new Vector2(0f, y - 21f);
            y -= 48f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var it = textGO.AddComponent<Text>();
            it.font = GetFont(); it.fontSize = 14; it.color = Color.white;
            it.alignment = TextAnchor.MiddleLeft; it.supportRichText = false;
            var itr = textGO.GetComponent<RectTransform>();
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = new Vector2(10f, 2f); itr.offsetMax = new Vector2(-10f, -2f);

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var ph = phGO.AddComponent<Text>();
            ph.font = GetFont(); ph.text = placeholder; ph.fontSize = 14;
            ph.fontStyle = FontStyle.Italic; ph.color = new Color(0.45f, 0.45f, 0.45f);
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

        static Toggle MakeToggle(Transform parent, string label, float y)
        {
            var go = new GameObject("Toggle_" + label);
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            if (r == null) r = go.AddComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(450f, 30f);
            r.anchoredPosition = new Vector2(0f, y);

            // Background checkbox
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            bgGO.AddComponent<Image>().color = new Color(0.28f, 0.28f, 0.38f);
            var bgR = bgGO.GetComponent<RectTransform>();
            bgR.anchorMin = bgR.anchorMax = new Vector2(0f, 0.5f);
            bgR.pivot = new Vector2(0f, 0.5f);
            bgR.sizeDelta = new Vector2(22f, 22f);
            bgR.anchoredPosition = new Vector2(0f, 0f);

            // Checkmark
            var ckGO = new GameObject("Checkmark");
            ckGO.transform.SetParent(bgGO.transform, false);
            var ck = ckGO.AddComponent<Image>();
            ck.color = new Color(0.3f, 0.85f, 0.4f);
            var ckR = ckGO.GetComponent<RectTransform>();
            ckR.anchorMin = new Vector2(0.15f, 0.15f);
            ckR.anchorMax = new Vector2(0.85f, 0.85f);
            ckR.sizeDelta = Vector2.zero;

            // Label
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var lt = labelGO.AddComponent<Text>();
            lt.font = GetFont(); lt.text = label; lt.fontSize = 13;
            lt.color = new Color(0.88f, 0.88f, 0.88f);
            lt.alignment = TextAnchor.MiddleLeft;
            var lr = labelGO.GetComponent<RectTransform>();
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(30f, 0f); lr.offsetMax = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgGO.GetComponent<Image>();
            toggle.graphic = ck;
            toggle.isOn = false;
            return toggle;
        }

        static void MakeButton(Transform parent, string label, Vector2 pos,
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
            t.font = GetFont(); t.text = label; t.fontSize = 15;
            t.fontStyle = FontStyle.Bold; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            var tr = tGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.onClick.AddListener(action);
        }
    }
}
