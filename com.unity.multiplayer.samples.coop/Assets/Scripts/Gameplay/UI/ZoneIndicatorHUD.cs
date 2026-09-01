using System.Collections.Generic;
using Mirror;
using TMPro;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Gameplay.GameState;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Tells the player what the zone they are standing in does.
    /// </summary>
    /// <remarks>
    /// <para>The discs and motes say <i>where</i> a zone is and, by colour, that the four are
    /// different — but colour alone does not say which is which, and the one that matters most is
    /// the one you want to leave. A player should never have to learn that red hurts by dying in
    /// it.</para>
    ///
    /// <para>Entirely client-side and read-only. The effect is the server's; this reports it. The
    /// membership test is the same one <c>ServerZoneSpawner</c> uses — a flat distance against the
    /// zone radius — so the banner cannot disagree with what is actually being applied.</para>
    ///
    /// <para>Self-bootstrapping, like the rest of this project's runtime HUD, so it needs no scene
    /// or prefab edit.</para>
    /// </remarks>
    public class ZoneIndicatorHUD : MonoBehaviour
    {
        /// <summary>Above the HUD, below the pause menu's modal layer.</summary>
        const int k_SortingOrder = 210;

        /// <summary>Seconds the banner takes to appear and disappear.</summary>
        const float k_FadeSeconds = 0.18f;

        static ZoneIndicatorHUD s_Instance;

        Canvas m_Canvas;
        CanvasGroup m_Group;
        RectTransform m_Card;
        Image m_Swatch;
        TextMeshProUGUI m_Label;
        TextMeshProUGUI m_Detail;

        NetworkGameState m_GameState;
        ServerCharacter m_Local;
        bool m_Shown;

        static readonly Dictionary<ZoneKind, (string Title, string Detail)> k_Copy = new()
        {
            [ZoneKind.Heal] = ("ZONA DE CURACIÓN", "Recuperás vida mientras estés adentro"),
            [ZoneKind.Speed] = ("ZONA DE VELOCIDAD", "Velocidad aumentada por 30 s"),
            [ZoneKind.Damage] = ("ZONA DE PODER", "Daño doble por 30 s"),
            [ZoneKind.Hazard] = ("ZONA TÓXICA", "Perdés vida mientras estés adentro"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            var host = new GameObject(nameof(ZoneIndicatorHUD));
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<ZoneIndicatorHUD>();
        }

        void Awake()
        {
            BuildChrome();
        }

        void BuildChrome()
        {
            m_Canvas = UIKit.Root(gameObject, nameof(ZoneIndicatorHUD), k_SortingOrder);

            m_Card = UIKit.Card(transform, "ZoneBanner", new Vector2(460f, 0f), UIKit.Unit * 2f, UIKit.Unit);
            // Bottom-centre: out of the way of the action bar's corner and of the reticle, and the
            // one place a player already looks for state about themselves.
            m_Card.anchorMin = new Vector2(0.5f, 0f);
            m_Card.anchorMax = new Vector2(0.5f, 0f);
            m_Card.pivot = new Vector2(0.5f, 0f);
            m_Card.anchoredPosition = new Vector2(0f, 220f);

            var row = UIKit.Row(m_Card, "Row", UIKit.Unit * 1.5f, 0f, TextAnchor.MiddleLeft);
            UIKit.Flexible(row, 54f, expandWidth: true);

            // A colour chip rather than an icon: it is the same colour as the ground under their
            // feet, which is the whole association the banner is there to teach.
            var swatchHost = UIKit.NewRect(row, "Swatch");
            m_Swatch = swatchHost.gameObject.AddComponent<Image>();
            var swatchElement = swatchHost.gameObject.AddComponent<LayoutElement>();
            swatchElement.preferredWidth = 14f;
            swatchElement.flexibleWidth = 0f;
            swatchElement.preferredHeight = 44f;

            var column = UIKit.Column(row, "Text", 0f, 0f, TextAnchor.MiddleLeft);
            UIKit.Flexible(column, 48f, expandWidth: true);

            m_Label = UIKit.Text(column, string.Empty, UIKit.TextStyle.Label, TextAlignmentOptions.Left);
            m_Label.enableWordWrapping = false;

            m_Detail = UIKit.Text(column, string.Empty, UIKit.TextStyle.Caption, TextAlignmentOptions.Left);
            m_Detail.enableWordWrapping = false;

            m_Group = m_Card.gameObject.AddComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            m_Group.blocksRaycasts = false;
            m_Group.interactable = false;
        }

        void Update()
        {
            var zone = CurrentZone();

            if (zone.HasValue)
            {
                Show(zone.Value.Kind);
            }
            else
            {
                m_Shown = false;
            }

            // Faded rather than switched off, so walking the edge of a zone does not strobe.
            float target = m_Shown ? 1f : 0f;
            m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, target, Time.deltaTime / k_FadeSeconds);
        }

        /// <summary>The zone the local player is standing in, if any.</summary>
        /// <remarks>
        /// When zones overlap the first one wins, which matches the server: its sweep applies every
        /// zone in list order, so the banner names the same one the player would guess from where
        /// they are standing rather than inventing a priority the effects do not have.
        /// </remarks>
        ZoneState? CurrentZone()
        {
            if (m_GameState == null)
            {
                m_GameState = FindAnyObjectByType<NetworkGameState>();
                if (m_GameState == null)
                {
                    return null;
                }
            }

            if (m_Local == null || m_Local.physicsWrapper == null)
            {
                m_Local = LocalCharacter();
                if (m_Local == null || m_Local.physicsWrapper == null)
                {
                    return null;
                }
            }

            Vector3 position = m_Local.physicsWrapper.Transform.position;

            foreach (var zone in m_GameState.Zones)
            {
                var offset = position - zone.Position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= zone.Radius * zone.Radius)
                {
                    return zone;
                }
            }

            return null;
        }

        static ServerCharacter LocalCharacter()
        {
            var player = NetworkClient.localPlayer;
            if (player == null)
            {
                return null;
            }

            // The avatar is not the connection's player object — that is the PersistentPlayer — so
            // the character is found among the spawned objects this client owns.
            foreach (var identity in NetworkClient.spawned.Values)
            {
                if (identity != null && identity.isOwned
                    && identity.TryGetComponent(out ServerCharacter character)
                    && !character.IsNpc)
                {
                    return character;
                }
            }

            return null;
        }

        void Show(ZoneKind kind)
        {
            if (!k_Copy.TryGetValue(kind, out var copy))
            {
                return;
            }

            Color colour = ZoneRules.ColorFor(kind);

            m_Swatch.color = colour;
            m_Label.text = copy.Title;
            m_Label.color = colour;
            m_Detail.text = copy.Detail;
            m_Shown = true;
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}
