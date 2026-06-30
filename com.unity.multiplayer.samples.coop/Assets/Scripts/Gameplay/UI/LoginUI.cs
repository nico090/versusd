using System;
using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Login / Register panel. If serialized UI fields are not wired in the inspector,
    /// it self-builds a functional Canvas panel at runtime.
    /// </summary>
    public class LoginUI : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] InputField m_UsernameField;
        [SerializeField] InputField m_PasswordField;
        [SerializeField] Text m_StatusLabel;
        [SerializeField] Button m_LoginButton;
        [SerializeField] Button m_RegisterButton;
        [SerializeField] Button m_GuestButton;

        [Inject] MasterServerFacade m_MasterServerFacade;

        public event Action<string> OnAuthSuccess;

        void Awake()
        {
            if (m_CanvasGroup == null)
                BuildUI();
            SetStatus(string.Empty);
            Hide();
        }

        // ── Self-build ────────────────────────────────────────────────────────

        void BuildUI()
        {
            // Put the Canvas on the root so the pre-existing CanvasGroup (alpha=0)
            // directly controls visibility via Show()/Hide().
            if (!GetComponent<Canvas>())
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
            }
            if (!GetComponent<CanvasScaler>()) gameObject.AddComponent<CanvasScaler>();
            if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();

            m_CanvasGroup = GetComponent<CanvasGroup>();
            if (m_CanvasGroup == null) m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.12f, 0.96f);
            var bgR = bg.GetComponent<RectTransform>();
            bgR.anchorMin = bgR.anchorMax = new Vector2(0.5f, 0.5f);
            bgR.sizeDelta = new Vector2(480f, 460f);
            bgR.anchoredPosition = Vector2.zero;

            float y = 170f;

            MakeLabel(bg.transform, "Login / Register", ref y, 36, FontStyle.Bold, 30f);
            y -= 10f;

            MakeLabel(bg.transform, "Username", ref y, 13, FontStyle.Normal, 20f);
            m_UsernameField = MakeInputField(bg.transform, "username...", ref y, false);

            MakeLabel(bg.transform, "Password", ref y, 13, FontStyle.Normal, 20f);
            m_PasswordField = MakeInputField(bg.transform, "password...", ref y, true);

            var statusGO = MakeRawText(bg.transform, string.Empty, y, 380f, 28f, 13, FontStyle.Italic);
            m_StatusLabel = statusGO.GetComponent<Text>();
            m_StatusLabel.color = new Color(1f, 0.4f, 0.4f);
            y -= 36f;

            y -= 14f;
            m_LoginButton = MakeButton(bg.transform, "Login", new Vector2(-155f, y), OnLoginClicked);
            m_RegisterButton = MakeButton(bg.transform, "Register", new Vector2(0f, y), OnRegisterClicked);
            m_GuestButton = MakeButton(bg.transform, "Guest", new Vector2(155f, y), OnGuestClicked);
        }

        void MakeLabel(Transform parent, string text, ref float y, int fontSize, FontStyle style, float height)
        {
            MakeRawText(parent, text, y, 400f, height, fontSize, style);
            y -= height + 4f;
        }

        static Font GetDefaultFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject MakeRawText(Transform parent, string text, float y, float width, float height, int fontSize, FontStyle style)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = GetDefaultFont();
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, height);
            r.anchoredPosition = new Vector2(0, y);
            return go;
        }

        InputField MakeInputField(Transform parent, string placeholder, ref float y, bool password)
        {
            y -= 4f;
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = Color.white;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(380f, 42f);
            r.anchoredPosition = new Vector2(0, y);
            y -= 50f;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(go.transform, false);
            var it = inputTextGO.AddComponent<Text>();
            it.font = GetDefaultFont();
            it.fontSize = 14; it.color = Color.black; it.supportRichText = false;
            it.alignment = TextAnchor.MiddleLeft;
            var itr = inputTextGO.GetComponent<RectTransform>();
            itr.anchorMin = Vector2.zero; itr.anchorMax = Vector2.one;
            itr.offsetMin = new Vector2(8, 2); itr.offsetMax = new Vector2(-8, -2);

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var ph = phGO.AddComponent<Text>();
            ph.font = GetDefaultFont();
            ph.text = placeholder; ph.fontSize = 14; ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.5f, 0.5f, 0.5f);
            ph.alignment = TextAnchor.MiddleLeft;
            var phr = phGO.GetComponent<RectTransform>();
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(8, 2); phr.offsetMax = new Vector2(-8, -2);

            var field = go.AddComponent<InputField>();
            field.textComponent = it;
            field.placeholder = ph;
            if (password) field.contentType = InputField.ContentType.Password;
            return field;
        }

        Button MakeButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.38f, 0.76f);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(140f, 42f);
            r.anchoredPosition = pos;

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(go.transform, false);
            var t = tGO.AddComponent<Text>();
            t.font = GetDefaultFont();
            t.text = label; t.fontSize = 14; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            var tr = tGO.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(action);
            return btn;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show()
        {
            m_CanvasGroup.alpha = 1f;
            m_CanvasGroup.blocksRaycasts = true;
            m_CanvasGroup.interactable = true;
        }

        public void Hide()
        {
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.blocksRaycasts = false;
            m_CanvasGroup.interactable = false;
        }

        // ── Button handlers ───────────────────────────────────────────────────

        public async void OnLoginClicked()
        {
            var user = m_UsernameField.text.Trim();
            var pass = m_PasswordField.text;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                SetStatus("Username and password are required.");
                return;
            }
            SetBusy(true);
            bool ok = await m_MasterServerFacade.LoginAsync(user, pass);
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("Login failed. Check credentials.");
        }

        public async void OnRegisterClicked()
        {
            var user = m_UsernameField.text.Trim();
            var pass = m_PasswordField.text;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                SetStatus("Username and password are required.");
                return;
            }
            SetBusy(true);
            bool ok = await m_MasterServerFacade.RegisterAsync(user, pass);
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("Registration failed. Username may already exist.");
        }

        public async void OnGuestClicked()
        {
            SetBusy(true);
            bool ok = await m_MasterServerFacade.LoginAnonymouslyAsync();
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("Guest login failed. Check server connection.");
        }

        void SetBusy(bool busy)
        {
            if (m_LoginButton) m_LoginButton.interactable = !busy;
            if (m_RegisterButton) m_RegisterButton.interactable = !busy;
            if (m_GuestButton) m_GuestButton.interactable = !busy;
        }

        void SetStatus(string msg)
        {
            if (m_StatusLabel) m_StatusLabel.text = msg;
        }
    }
}
