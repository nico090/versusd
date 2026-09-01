using System;
using TMPro;
using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The first screen of the game: the wordmark, and a card that logs a player in, registers
    /// them, or lets them in as a guest.
    /// </summary>
    /// <remarks>
    /// <para><b>Built from code.</b> The prefab this lives on carries no wired UI at all — the
    /// screen has always assembled itself at run time, which is the arrangement this project
    /// trusts (see <see cref="ToonMenuRestyler"/> for why prefab-authored UI is a liability
    /// here). What changed is that it is now assembled out of <see cref="UIKit"/> instead of
    /// hand-placed rectangles, so it agrees with the rest of the game and survives a phone
    /// aspect ratio.</para>
    ///
    /// <para><b>Three ways in, ranked.</b> Guest is the fastest path into a match and login is
    /// the one that keeps your name, so login is the primary action, register and guest are
    /// quieter alternatives beneath it. That ranking is the whole reason
    /// <see cref="UIKit.Role"/> exists.</para>
    /// </remarks>
    public class LoginUI : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_CanvasGroup;
        [SerializeField] TMP_InputField m_UsernameField;
        [SerializeField] TMP_InputField m_PasswordField;
        [SerializeField] TextMeshProUGUI m_StatusLabel;
        [SerializeField] Button m_LoginButton;
        [SerializeField] Button m_RegisterButton;
        [SerializeField] Button m_GuestButton;

        [Inject] MasterServerFacade m_MasterServerFacade;

        public event Action<string> OnAuthSuccess;

        /// <summary>Height reserved for the wordmark above the card.</summary>
        const float k_BrandHeight = 190f;

        const float k_CardWidth = 620f;

        void Awake()
        {
            if (m_CanvasGroup == null)
            {
                BuildUI();
            }

            SetStatus(string.Empty);
            Hide();
        }

        // ── Self-build ────────────────────────────────────────────────────────

        void BuildUI()
        {
            // The canvas goes on the root so the CanvasGroup that Show()/Hide() drive covers the
            // whole screen, dimmed backdrop included.
            UIKit.Root(gameObject, gameObject.name, 100);

            m_CanvasGroup = GetComponent<CanvasGroup>();
            if (m_CanvasGroup == null)
            {
                m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            UIKit.Scrim(transform, 0.82f);

            // One column holding the mark and the card, so the pair stays centred together
            // whatever the window shape is.
            var column = UIKit.Column(transform, "Login", UIKit.Unit * 3f, 0f, TextAnchor.MiddleCenter);
            UIKit.Stretch(column);

            var brand = UIKit.NewRect(column, "Brand");
            UIKit.Flexible(brand, k_BrandHeight, k_CardWidth);
            BrandMark.Build(brand);

            var card = UIKit.Card(column, "LoginCard", new Vector2(k_CardWidth, 0f), UIKit.Unit * 4f, UIKit.Unit * 2f);

            UIKit.Text(card, "Entrar a la arena", UIKit.TextStyle.Heading);
            UIKit.Divider(card);
            UIKit.Spacer(card, UIKit.Unit * 0.5f);

            m_UsernameField = UIKit.Input(card, "Tu nombre de usuario", UIIcons.Icon.User);
            m_PasswordField = UIKit.Input(card, "Tu contraseña", UIIcons.Icon.Lock, password: true);

            m_StatusLabel = UIKit.Text(card, string.Empty, UIKit.TextStyle.Caption, TextAlignmentOptions.Center,
                UIKit.Danger);

            m_LoginButton = UIKit.Button(card, "Entrar", UIKit.Role.Primary, OnLoginClicked, UIIcons.Icon.Key);

            var alternatives = UIKit.Row(card, "Alternatives", UIKit.Unit * 1.5f, 0f, TextAnchor.MiddleCenter);
            UIKit.Flexible(alternatives, UIKit.ControlHeight, expandWidth: true);

            m_RegisterButton = UIKit.Button(alternatives, "Crear cuenta", UIKit.Role.Secondary, OnRegisterClicked,
                UIIcons.Icon.Plus);
            m_GuestButton = UIKit.Button(alternatives, "Entrar como invitado", UIKit.Role.Ghost, OnGuestClicked,
                UIIcons.Icon.User);

            UIKit.Spacer(card, UIKit.Unit * 0.5f);
            UIKit.Text(card, "Como invitado no se guarda tu progreso ni tu nombre.", UIKit.TextStyle.Caption);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Moves the caret from one field to the next when Tab is pressed.
        /// </summary>
        /// <remarks>
        /// <para>Written by hand because a text field eats the key. TMP_InputField consumes Tab as
        /// input rather than letting the EventSystem treat it as a navigation request, so the
        /// built-in Selectable navigation never sees it and the caret stays where it is — which on
        /// a username-and-password form is the one keystroke everybody reaches for without
        /// looking.</para>
        ///
        /// <para>Shift+Tab goes back, and Tab from the password field submits, so the whole form
        /// can be filled without touching the mouse.</para>
        /// </remarks>
        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame)
            {
                return;
            }

            // Two fields, so Tab just alternates and Shift+Tab needs no special case: going back
            // from the password IS going to the username either way.
            bool onPassword = m_PasswordField != null && m_PasswordField.isFocused;
            Focus(onPassword ? m_UsernameField : m_PasswordField);
        }

        static void Focus(TMP_InputField field)
        {
            if (field == null)
            {
                return;
            }

            field.Select();
            field.ActivateInputField();
            // Caret to the end rather than selecting the text: Tab means "carry on here", and a
            // field that arrives fully selected loses whatever was already typed to the next key.
            field.caretPosition = field.text.Length;
            field.selectionAnchorPosition = field.caretPosition;
            field.selectionFocusPosition = field.caretPosition;
        }

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
                SetStatus("Escribe tu usuario y tu contraseña.");
                return;
            }
            SetBusy(true);
            bool ok = await m_MasterServerFacade.LoginAsync(user, pass);
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("No se pudo entrar. Revisa usuario y contraseña.");
        }

        public async void OnRegisterClicked()
        {
            var user = m_UsernameField.text.Trim();
            var pass = m_PasswordField.text;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                SetStatus("Escribe tu usuario y tu contraseña.");
                return;
            }
            SetBusy(true);
            bool ok = await m_MasterServerFacade.RegisterAsync(user, pass);
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("No se pudo crear la cuenta. Ese usuario ya existe.");
        }

        public async void OnGuestClicked()
        {
            SetBusy(true);
            bool ok = await m_MasterServerFacade.LoginAnonymouslyAsync();
            SetBusy(false);
            if (ok) OnAuthSuccess?.Invoke(m_MasterServerFacade.Username);
            else SetStatus("No se pudo entrar como invitado. Revisa la conexión.");
        }

        void SetBusy(bool busy)
        {
            if (m_LoginButton) m_LoginButton.interactable = !busy;
            if (m_RegisterButton) m_RegisterButton.interactable = !busy;
            if (m_GuestButton) m_GuestButton.interactable = !busy;

            if (busy)
            {
                SetStatus("Conectando...");
            }
        }

        void SetStatus(string msg)
        {
            if (m_StatusLabel == null)
            {
                return;
            }

            m_StatusLabel.text = msg;
            // "Connecting…" is progress, not a problem, and colouring it like a failure is what
            // made the old screen feel like it was breaking every time it worked.
            m_StatusLabel.color = msg == "Conectando..." ? HudSkin.TextDim : UIKit.Danger;
        }
    }
}
