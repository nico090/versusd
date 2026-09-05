using System;
using System.Collections.Generic;
using TMPro;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The how-to-play wizard: a few pages explaining the match, shown once and reachable after.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a wizard and not one screen.</b> The game asks a new player to hold several
    /// unrelated rules at once — a scoring system, four classes, a boss that only shows up at the
    /// end, and ground zones that do four different things. Put on one page that is a wall of text
    /// nobody reads. One idea per page, with the player pressing to continue, is the difference
    /// between reading it and skipping it.</para>
    ///
    /// <para><b>Shown once, then on demand.</b> First run opens it automatically; after that the
    /// "?" button in the corner of the menu brings it back. The flag lives in
    /// <see cref="ClientPrefs"/>, per install, so it does not follow an account around.</para>
    ///
    /// <para>The numbers are read from <see cref="DeathmatchRules"/>, <see cref="ZoneRules"/> and
    /// <see cref="HeroBalance"/> rather than written into the copy. A tutorial that disagrees with
    /// the game is worse than no tutorial, and this way retuning a value updates what the player is
    /// told at the same moment it changes what happens.</para>
    ///
    /// <para>Self-bootstrapping, so it needs no scene or prefab edit.</para>
    /// </remarks>
    public class HowToPlayWizard : MonoBehaviour
    {
        /// <summary>Above the settings canvas, which is the highest thing a menu draws.</summary>
        const int k_SortingOrder = 400;

        /// <summary>Scenes the "?" button belongs in. In a match the pause menu owns the chrome.</summary>
        static readonly string[] k_MenuScenes = { "MainMenu", "CharSelect", "PostGame" };

        static HowToPlayWizard s_Instance;

        readonly struct Page
        {
            public readonly string Title;
            public readonly string Body;
            public readonly UIIcons.Icon Icon;

            public Page(string title, string body, UIIcons.Icon icon)
            {
                Title = title;
                Body = body;
                Icon = icon;
            }
        }

        Canvas m_Canvas;
        GameObject m_Modal;
        GameObject m_HelpButton;
        RectTransform m_Dots;
        TextMeshProUGUI m_Title;
        TextMeshProUGUI m_Body;
        Image m_Glyph;
        Button m_Back;
        Button m_Next;
        TextMeshProUGUI m_NextLabel;

        Page[] m_Pages;
        int m_Index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(HowToPlayWizard));
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<HowToPlayWizard>();
        }

        void Awake()
        {
            m_Pages = BuildPages();
            m_Canvas = UIKit.Root(gameObject, nameof(HowToPlayWizard), k_SortingOrder);

            BuildHelpButton();
            BuildModal();

            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply(SceneManager.GetActiveScene());
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply(scene);

        void Apply(Scene scene)
        {
            bool inMenu = Array.IndexOf(k_MenuScenes, scene.name) >= 0;
            m_HelpButton.SetActive(inMenu);

            // Only ever offered from a menu: interrupting somebody mid-match to explain the match
            // is worse than not explaining it.
            if (inMenu && scene.name == "MainMenu" && !ClientPrefs.GetTutorialSeen())
            {
                Open();
            }
            else if (!inMenu)
            {
                Close();
            }
        }

        // ── Content ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A number of seconds written the way a person would say it.
        /// </summary>
        /// <remarks>
        /// These pages used to divide by 60 and print "minutos" unconditionally, which was fine
        /// while every duration on them was a whole number of minutes. The endgame window is now
        /// 45 seconds, and rounding that to "1 minutos" would be wrong twice over. Retuning a value
        /// must not be able to make the tutorial lie — that is the whole reason these strings read
        /// the constants instead of repeating them.
        /// </remarks>
        static string Duration(float seconds)
        {
            if (seconds < 60f)
            {
                return $"{Mathf.RoundToInt(seconds)} segundos";
            }

            int minutes = Mathf.RoundToInt(seconds / 60f);
            return minutes == 1 ? "1 minuto" : $"{minutes} minutos";
        }

        /// <summary>
        /// The endgame page, which has to describe whatever order the two special events are
        /// currently tuned to fire in.
        /// </summary>
        /// <remarks>
        /// The boss is meant to land before the doubling, and the gap between them is the point of
        /// the whole phase — so the copy names that gap rather than describing two things that
        /// happen "at the end". Both branches are kept because the constants are a tuning knob:
        /// set them to the same number again and the page must go back to saying so instead of
        /// promising a window that no longer exists.
        /// </remarks>
        static string EndgameCopy()
        {
            float gap = DeathmatchRules.BossSpawnTimeRemaining - DeathmatchRules.DoubleKillsThreshold;
            string doubleAt = Duration(DeathmatchRules.DoubleKillsThreshold);

            if (gap < 1f)
            {
                return $"En los últimos {doubleAt} los kills de jugador valen el doble, " +
                       "y justo ahí aparece el boss.\n\n" +
                       "Las dos cosas pasan a la vez a propósito: no te alcanza el tiempo para " +
                       "las dos, así que hay que elegir.";
            }

            return $"El boss aparece cuando queda {Duration(DeathmatchRules.BossSpawnTimeRemaining)}. " +
                   $"En los últimos {doubleAt}, además, los kills de jugador valen el doble.\n\n" +
                   $"Esos {Duration(gap)} de diferencia son la ventana para empezar el boss antes " +
                   "de que se abra la caza. Si seguís ahí cuando empieza, sos el blanco más caro " +
                   "del mapa.";
        }

        static Page[] BuildPages()
        {
            string matchLength = Duration(DeathmatchRules.MatchDuration);
            int boonSeconds = Mathf.RoundToInt(ZoneRules.BoonSeconds);
            string endgame = EndgameCopy();

            return new[]
            {
                new Page(
                    "TODOS CONTRA TODOS",
                    $"Una partida dura {matchLength} y gana el que más puntos junte.\n\n" +
                    "No hay equipos: cada jugador va por su cuenta, y el mapa además está lleno " +
                    "de imps que no son de nadie.",
                    UIIcons.Icon.Swords),

                new Page(
                    "CÓMO SE PUNTÚA",
                    $"Matar a otro jugador: {DeathmatchRules.PointsPerPlayerKill} puntos.\n" +
                    $"Matar un imp: {DeathmatchRules.PointsPerNpcKill} punto.\n" +
                    $"Rematar al boss: {DeathmatchRules.PointsPerBossKill} puntos.\n\n" +
                    "Morir no te resta. Lo que perdés es el tiempo que tardás en volver: " +
                    $"{Mathf.RoundToInt(DeathmatchRules.RespawnDelay)} segundos.",
                    UIIcons.Icon.Trophy),

                new Page("EL FINAL", endgame, UIIcons.Icon.Clock),

                new Page(
                    "ZONAS DEL MAPA",
                    "Cada tanto aparecen círculos de colores en el piso:\n\n" +
                    "Verde: te cura mientras estés adentro.\n" +
                    "Rojo: te lastima mientras estés adentro.\n" +
                    $"Azul: velocidad aumentada por {boonSeconds} s.\n" +
                    $"Violeta: daño doble por {boonSeconds} s.\n\n" +
                    "El azul y el violeta te los llevás puestos al salir. El verde y el rojo, no.",
                    UIIcons.Icon.Flag),

                new Page(
                    "CONTROLES",
                    "Moverte: WASD o el joystick.\n" +
                    "Click izquierdo: ataque básico.\n" +
                    "Click derecho: el poder de tu clase.\n" +
                    "1, 2, 3: habilidades.\n" +
                    "Escape: pausa y ajustes.\n\n" +
                    "La mira se ajusta sola hacia el enemigo al que estés apuntando.",
                    UIIcons.Icon.Bolt),

                new Page(
                    "LAS CUATRO CLASES",
                    "Tank: aguanta y controla. Congela y embiste.\n" +
                    "Rogue: rápido y frágil. Sigilo y golpes de apertura.\n" +
                    "Archer: pega de lejos, pero es el que menos vida tiene.\n" +
                    "Mage: daño en área, cura y un meteorito telegrafiado.\n\n" +
                    "Ninguna gana sola: la que te conviene depende de quién más esté jugando.",
                    UIIcons.Icon.Shield),
            };
        }

        // ── Chrome ────────────────────────────────────────────────────────────────────────────

        void BuildHelpButton()
        {
            m_HelpButton = new GameObject("HelpButton");
            var rect = m_HelpButton.AddComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // Below the settings prefab's own two corner icons, which sit at -32 and -160.
            rect.anchoredPosition = new Vector2(-20f, -288f);
            rect.sizeDelta = new Vector2(72f, 72f);

            var button = UIKit.IconButton(rect, UIIcons.Icon.Warning, UIKit.Role.Secondary, Open, 72f);
            button.transform.SetParent(rect, false);
        }

        void BuildModal()
        {
            m_Modal = new GameObject("Modal");
            var modalRect = m_Modal.AddComponent<RectTransform>();
            modalRect.SetParent(transform, false);
            UIKit.Stretch(modalRect);

            UIKit.Scrim(modalRect, 0.86f);

            var card = UIKit.Card(modalRect, "Card", new Vector2(720f, 520f), UIKit.Unit * 4f, UIKit.Unit * 2f);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;

            var header = UIKit.Row(card, "Header", UIKit.Unit * 2f, 0f, TextAnchor.MiddleLeft);
            UIKit.Flexible(header, 56f, expandWidth: true);
            m_Glyph = UIKit.Icon(header, UIIcons.Icon.Swords, ToonMenuSkin.Accent, 44f);
            m_Title = UIKit.Text(header, string.Empty, UIKit.TextStyle.Title, TextAlignmentOptions.Left);
            m_Title.enableWordWrapping = false;

            UIKit.Divider(card);

            m_Body = UIKit.Text(card, string.Empty, UIKit.TextStyle.Body, TextAlignmentOptions.TopLeft);
            UIKit.Flexible(m_Body.rectTransform, -1f, expandWidth: true, flexibleHeight: 1f);

            m_Dots = UIKit.Row(card, "Dots", UIKit.Unit, 0f, TextAnchor.MiddleCenter);
            UIKit.Flexible(m_Dots, 18f, expandWidth: true);
            for (int i = 0; i < m_Pages.Length; i++)
            {
                var dot = UIKit.NewRect(m_Dots, "Dot");
                dot.gameObject.AddComponent<Image>();
                var element = dot.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = 10f;
                element.preferredHeight = 10f;
                element.flexibleWidth = 0f;
            }

            var footer = UIKit.Row(card, "Footer", UIKit.Unit * 1.5f, 0f, TextAnchor.MiddleCenter);
            UIKit.Flexible(footer, UIKit.ControlHeight, expandWidth: true);

            m_Back = UIKit.Button(footer, "Atrás", UIKit.Role.Ghost, () => Step(-1), UIIcons.Icon.Back);
            UIKit.Button(footer, "Saltar", UIKit.Role.Ghost, Dismiss);
            m_Next = UIKit.Button(footer, "Siguiente", UIKit.Role.Primary, () => Step(1), UIIcons.Icon.Forward);
            m_NextLabel = m_Next.GetComponentInChildren<TextMeshProUGUI>();

            m_Modal.SetActive(false);
        }

        // ── Flow ──────────────────────────────────────────────────────────────────────────────

        public void Open()
        {
            m_Index = 0;
            m_Modal.SetActive(true);
            Refresh();
        }

        void Close() => m_Modal.SetActive(false);

        /// <summary>Closes and records that the player has seen it.</summary>
        void Dismiss()
        {
            ClientPrefs.SetTutorialSeen(true);
            Close();
        }

        void Step(int delta)
        {
            // Past the last page, "Siguiente" is "Empezar" and finishes the wizard.
            if (m_Index + delta >= m_Pages.Length)
            {
                Dismiss();
                return;
            }

            m_Index = Mathf.Clamp(m_Index + delta, 0, m_Pages.Length - 1);
            Refresh();
        }

        void Refresh()
        {
            var page = m_Pages[m_Index];

            m_Title.text = page.Title;
            m_Body.text = page.Body;
            m_Glyph.sprite = UIIcons.Get(page.Icon);

            bool last = m_Index == m_Pages.Length - 1;
            m_NextLabel.text = last ? "Empezar" : "Siguiente";
            m_Back.gameObject.SetActive(m_Index > 0);

            for (int i = 0; i < m_Dots.childCount; i++)
            {
                var image = m_Dots.GetChild(i).GetComponent<Image>();
                if (image != null)
                {
                    image.color = i == m_Index ? ToonMenuSkin.Accent : ToonMenuSkin.AccentSoft;
                }
            }
        }
    }
}
