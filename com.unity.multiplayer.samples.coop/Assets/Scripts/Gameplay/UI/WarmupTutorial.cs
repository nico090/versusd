using System;
using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameState;
using Unity.BossRoom.Gameplay.UserInput;
using Unity.BossRoom.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// The floating control tutorial shown during <see cref="MatchPhase.Warmup"/>: one line at a
    /// time, in white over the game with nothing behind it, each line clearing itself the moment
    /// the player actually does the thing it asks for.
    /// </summary>
    /// <remarks>
    /// <para><b>Why one step at a time.</b> <see cref="ControlsHintPanel"/> already lists every
    /// binding in the corner, and a list is what you read once you have decided to look something
    /// up — it is not what teaches you. This asks for a single thing, waits, and only then asks for
    /// the next one, so the player learns each control by using it rather than by reading about
    /// it.</para>
    ///
    /// <para><b>Shown once per install.</b> The first match teaches; every one after it does not,
    /// because <see cref="ClientPrefs.GetWarmupTutorialSeen"/> is written the moment the lesson
    /// ends or the player presses Escape on it. Escape is the explicit "I know this already", and
    /// it is taken as final: a walkthrough that had to be waved off every match would be worse
    /// than no walkthrough at all.</para>
    ///
    /// <para>Even so the copy is one short line with no panel behind it, and a player who ignores
    /// it entirely still clears it — every step gives up after
    /// <see cref="k_StepTimeoutSeconds"/> — so at no point is it in the way of the fight it is
    /// describing.</para>
    ///
    /// <para><b>Steps can also time out.</b> The warm-up is
    /// <see cref="DeathmatchRules.WarmupDuration"/> seconds long and there are up to five steps, so
    /// a step nobody performs is left behind after <see cref="k_StepTimeoutSeconds"/> rather than
    /// holding the rest hostage. A player who never finds the camera drag should still be told
    /// where the attack is.</para>
    ///
    /// <para>Self-bootstrapping and code-built, like every other runtime widget in this project
    /// (<see cref="ControlsHintPanel"/>, <see cref="UserInput.MobileMovementJoystick"/>), so there
    /// is no scene or prefab wiring an Editor re-import could quietly drop. It carries no
    /// GraphicRaycaster, so it can never swallow a click meant for the world behind it.</para>
    /// </remarks>
    public class WarmupTutorial : MonoBehaviour
    {
        /// <summary>Above the match HUD, below the on-screen input widgets.</summary>
        const int k_SortingOrder = 15000;

        /// <summary>Design resolution the layout numbers below are expressed in.</summary>
        static readonly Vector2 k_ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>Clearance kept for the action bar, which owns the bottom of the screen.</summary>
        const float k_BottomOffset = 210f;

        /// <summary>How long the confirmation stays up before the next step replaces it.</summary>
        const float k_ConfirmSeconds = 0.55f;

        /// <summary>
        /// How long a step waits before giving up and moving on. Five steps at this rate still fit
        /// inside the warm-up, with room to spare for the ones the player actually performs.
        /// </summary>
        const float k_StepTimeoutSeconds = 4.5f;

        /// <summary>How long the closing line stays up once every step is done.</summary>
        const float k_DoneSeconds = 2f;

        const float k_FadeSpeed = 4f;

        /// <summary>Cumulative seconds of movement input that count as "you have moved".</summary>
        const float k_MoveHoldSeconds = 0.6f;

        /// <summary>Cumulative seconds of camera drag that count as "you have looked around".</summary>
        const float k_LookHoldSeconds = 0.45f;

        /// <summary>
        /// One instruction, plus the test for whether the player has carried it out.
        /// </summary>
        /// <remarks>
        /// Two shapes of test, because the controls come in two shapes. A press step is satisfied
        /// by a single <see cref="ClientInputSender.LocalActionRequested"/> matching its action; a
        /// hold step needs its predicate to stay true for a while, so that brushing a key does not
        /// count as having learned to walk.
        /// </remarks>
        readonly struct Step
        {
            public readonly string Text;
            public readonly Func<bool> Doing;
            public readonly float HoldSeconds;
            public readonly ActionID Action;

            Step(string text, Func<bool> doing, float holdSeconds, ActionID action)
            {
                Text = text;
                Doing = doing;
                HoldSeconds = holdSeconds;
                Action = action;
            }

            public static Step Hold(string text, Func<bool> doing, float seconds) =>
                new Step(text, doing, seconds, default);

            public static Step Press(string text, ActionID action) =>
                new Step(text, null, 0f, action);

            public bool IsPress => Doing == null;
        }

        static WarmupTutorial s_Instance;

        CanvasGroup m_Group;
        Text m_Tip;
        Text m_Progress;

        NetworkGameState m_GameState;
        List<Step> m_Steps;

        /// <summary>
        /// The "press Escape to skip" half of the progress line, or empty on a phone.
        /// </summary>
        /// <remarks>
        /// A way out that nobody is told about is not a way out, and this only shows up on the one
        /// run where it is needed. Blank on touch for the obvious reason, and because those players
        /// have the whole sequence in front of them as buttons anyway.
        /// </remarks>
        string m_SkipHint = string.Empty;

        int m_Index;
        float m_Held;
        bool m_Pressed;
        float m_StepStartedAt;
        float m_ConfirmUntil;
        float m_DoneUntil;
        bool m_Retired;
        float m_TargetAlpha;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // Nothing to teach on a headless dedicated server, and nothing to draw it with.
            if (Application.isBatchMode || s_Instance != null)
            {
                return;
            }

            // Already taught on this install, so there is nothing for this object to do all
            // session. Checked again in Retire for the match after the one that taught it: the
            // preference is written mid-session, long after this ran.
            if (ClientPrefs.GetWarmupTutorialSeen())
            {
                return;
            }

            Create();
        }

        static void Create()
        {
            var host = new GameObject(nameof(WarmupTutorial));
            DontDestroyOnLoad(host);
            s_Instance = host.AddComponent<WarmupTutorial>();
        }

        /// <summary>Whether the walkthrough is going to run in the next warm-up.</summary>
        public static bool Armed => !ClientPrefs.GetWarmupTutorialSeen();

        /// <summary>
        /// Turns the walkthrough back on (or off again) from outside — the menu switch a player
        /// who dismissed it, or who simply wants it again, has to have.
        /// </summary>
        /// <remarks>
        /// Arming has to build the object as well as clear the preference: <see cref="Bootstrap"/>
        /// runs once at launch and skips the object entirely on an install that had already been
        /// taught, so on that session there is nothing alive to un-retire.
        /// </remarks>
        public static void SetArmed(bool armed)
        {
            if (!armed)
            {
                if (s_Instance != null)
                {
                    s_Instance.Retire();
                }
                else
                {
                    ClientPrefs.SetWarmupTutorialSeen(true);
                }

                return;
            }

            ClientPrefs.SetWarmupTutorialSeen(false);

            if (s_Instance == null)
            {
                if (!Application.isBatchMode)
                {
                    Create();
                }

                return;
            }

            s_Instance.Rearm();
        }

        /// <summary>Puts a finished or dismissed lesson back at its first step.</summary>
        void Rearm()
        {
            m_Retired = false;
            m_Steps = null;
            m_Index = 0;
            m_Held = 0f;
            m_Pressed = false;
            m_ConfirmUntil = 0f;
            m_DoneUntil = 0f;
            m_TargetAlpha = 0f;
        }

        void Awake()
        {
            BuildUI();
            ClientInputSender.LocalActionRequested += OnActionRequested;
        }

        void OnDestroy()
        {
            ClientInputSender.LocalActionRequested -= OnActionRequested;
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        // -- Driving ---------------------------------------------------------------------------

        /// <summary>
        /// Takes the Escape key if the walkthrough is on screen, and retires it for good.
        /// </summary>
        /// <returns>
        /// True if the key was spent here, so the caller must not also act on it. False whenever
        /// there is nothing showing, which is what lets Escape fall through to the pause menu.
        /// </returns>
        /// <remarks>
        /// Offered to <see cref="PauseMenuUI"/> rather than read from the keyboard here, because
        /// that is where the ordering between the things Escape can close already lives. Two
        /// components both watching the key would mean one press closing two of them.
        /// </remarks>
        public static bool TryDismiss()
        {
            if (s_Instance == null || s_Instance.m_Retired || s_Instance.m_Steps == null)
            {
                return false;
            }

            s_Instance.Retire();
            return true;
        }

        /// <summary>
        /// Ends the walkthrough for good: it fades out, never comes back this session, and never
        /// comes back on this install.
        /// </summary>
        void Retire()
        {
            m_Retired = true;
            m_Steps = null;
            ClientPrefs.SetWarmupTutorialSeen(true);
        }

        void Update()
        {
            if (m_Retired)
            {
                // Nothing left but to let the fade land, after which this costs one branch a frame
                // for the rest of the session.
                if (m_Group.alpha > 0f)
                {
                    m_TargetAlpha = 0f;
                    Fade();
                }

                return;
            }

            if (m_GameState == null)
            {
                // Looked up again rather than cached for good: the state object belongs to the
                // gameplay scene, so it goes away between matches and comes back as a new instance.
                m_GameState = FindAnyObjectByType<NetworkGameState>();
            }

            var sender = ClientInputSender.LocalInstance;
            bool running = m_GameState != null
                           && m_GameState.Phase == MatchPhase.Warmup
                           && sender != null;

            if (!running)
            {
                // Torn down rather than paused, so the next match starts its lesson from the top
                // instead of resuming a half-finished one against a possibly different class.
                m_Steps = null;
                m_TargetAlpha = 0f;
                Fade();
                return;
            }

            if (m_Steps == null)
            {
                BuildSteps(sender);
            }

            Advance();

            // Advance can retire the lesson on the very frame it finishes, and retiring drops the
            // step list — so the draw only happens if there is still something to draw.
            if (m_Steps != null)
            {
                Draw();
            }

            // Decided after Advance, never before it: the frame the lesson finishes is the frame
            // the fade-out has to start, and a target set ahead of it would be overwritten here.
            m_TargetAlpha = m_Retired ? 0f : 1f;
            Fade();
        }

        void Advance()
        {
            // Past the end of the list: the closing line is up, and then we are done for this match.
            if (m_Index >= m_Steps.Count)
            {
                if (m_DoneUntil > 0f && Time.time >= m_DoneUntil)
                {
                    // Ran to the end, which counts as taught whether or not every step was
                    // actually performed — the player has now been shown all of them.
                    Retire();
                }

                return;
            }

            // Holding on the confirmation for the step just cleared.
            if (m_ConfirmUntil > 0f)
            {
                if (Time.time >= m_ConfirmUntil)
                {
                    m_ConfirmUntil = 0f;
                    StartStep(m_Index + 1);
                }

                return;
            }

            var step = m_Steps[m_Index];

            bool done;
            if (step.IsPress)
            {
                done = m_Pressed;
            }
            else
            {
                if (step.Doing())
                {
                    m_Held += Time.deltaTime;
                }

                done = m_Held >= step.HoldSeconds;
            }

            if (done)
            {
                m_ConfirmUntil = Time.time + k_ConfirmSeconds;
                return;
            }

            if (Time.time - m_StepStartedAt >= k_StepTimeoutSeconds)
            {
                // Skipped, not cleared, so no praise for something that was never done.
                StartStep(m_Index + 1);
            }
        }

        void StartStep(int index)
        {
            m_Index = index;
            m_Held = 0f;
            m_Pressed = false;
            m_StepStartedAt = Time.time;

            if (m_Index >= m_Steps.Count)
            {
                m_DoneUntil = Time.time + k_DoneSeconds;
            }
        }

        void OnActionRequested(ActionID action)
        {
            if (m_Steps == null || m_ConfirmUntil > 0f || m_Index >= m_Steps.Count)
            {
                return;
            }

            var step = m_Steps[m_Index];
            if (step.IsPress && step.Action == action)
            {
                m_Pressed = true;
            }
        }

        // -- Content ---------------------------------------------------------------------------

        /// <summary>
        /// Builds the lesson for the player in front of us: the touch bindings or the desktop ones,
        /// and only the skills this class actually has.
        /// </summary>
        /// <remarks>
        /// Built here rather than in <c>Awake</c> because it needs the local character: not every
        /// class defines a Skill2 or a Skill3, and telling somebody to press a button that does
        /// nothing for them is worse than saying nothing at all.
        /// </remarks>
        void BuildSteps(ClientInputSender sender)
        {
            bool touch = Application.isMobilePlatform;
            m_Steps = new List<Step>(5);
            m_SkipHint = touch ? string.Empty : "     Esc para saltear";
            m_DoneUntil = 0f;

            m_Steps.Add(Step.Hold(
                touch ? "Movete con el <b>joystick</b> de la izquierda"
                      : "Movete con <b>W A S D</b>",
                () => ClientInputSender.LocalMoveInputActive,
                k_MoveHoldSeconds));

            m_Steps.Add(Step.Hold(
                touch ? "Girá la cámara <b>arrastrando</b> con un dedo"
                      : "Girá la cámara: mantené la <b>rueda</b> del mouse y movelo",
                touch ? (Func<bool>)(() => TouchCameraOrbit.IsActive)
                      : () => MouseCameraOrbit.IsActive,
                k_LookHoldSeconds));

            if (sender.actionState1 != null)
            {
                m_Steps.Add(Step.Press(
                    touch ? "Atacá con el botón <b>1</b>"
                          : "Atacá con <b>click izquierdo</b>",
                    sender.actionState1.actionID));
            }

            if (sender.actionState2 != null)
            {
                m_Steps.Add(Step.Press(
                    touch ? "Tu poder es el botón <b>2</b>"
                          : "Tu poder va con <b>click derecho</b>",
                    sender.actionState2.actionID));
            }

            if (sender.actionState3 != null)
            {
                m_Steps.Add(Step.Press(
                    touch ? "Y tu habilidad, el botón <b>3</b>"
                          : "Y tu habilidad, la tecla <b>3</b>",
                    sender.actionState3.actionID));
            }

            StartStep(0);
        }

        void Draw()
        {
            if (m_Index >= m_Steps.Count)
            {
                m_Tip.text = "¡Listo! Ya sabés lo básico.";
                m_Progress.text = string.Empty;
                return;
            }

            m_Tip.text = m_ConfirmUntil > 0f ? "¡Bien!" : m_Steps[m_Index].Text;
            m_Progress.text = $"{m_Index + 1} / {m_Steps.Count}{m_SkipHint}";
        }

        // -- Widgets ---------------------------------------------------------------------------

        void Fade()
        {
            float alpha = Mathf.MoveTowards(m_Group.alpha, m_TargetAlpha, k_FadeSpeed * Time.deltaTime);
            m_Group.alpha = alpha;

            bool visible = alpha > 0.001f;
            if (m_Group.gameObject.activeSelf != visible)
            {
                m_Group.gameObject.SetActive(visible);
            }
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = k_SortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = k_ReferenceResolution;
            // The same split the rest of the kit uses: a phone held upright keeps its width, and an
            // ultrawide window does not blow the type up.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster on purpose - this is a caption, not a control.

            var groupGO = new GameObject("Tutorial", typeof(RectTransform));
            groupGO.transform.SetParent(canvasGO.transform, false);
            m_Group = groupGO.AddComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            m_Group.interactable = false;
            m_Group.blocksRaycasts = false;

            var groupRT = (RectTransform)groupGO.transform;
            groupRT.anchorMin = new Vector2(0.5f, 0f);
            groupRT.anchorMax = new Vector2(0.5f, 0f);
            groupRT.pivot = new Vector2(0.5f, 0f);
            groupRT.anchoredPosition = new Vector2(0f, k_BottomOffset);
            groupRT.sizeDelta = new Vector2(1200f, 110f);

            m_Tip = CreateText(groupRT, "Tip", 38, Color.white,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), new Vector2(0f, 0f));

            // Dim, small, and directly under the line it counts: it is there to say "there are a
            // couple more of these and then it stops", which is the one thing a caption with no
            // dismiss button owes the reader.
            m_Progress = CreateText(groupRT, "Progress", 22, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 36f));

            groupGO.SetActive(false);
        }

        static Text CreateText(RectTransform parent, string name, int fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // The background is the game itself, so readability has to come from the type. A shadow
            // rather than a full outline: an outline on white at this size reads as a sticker, and
            // the point is for this to sit lightly over the fight.
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);

            return text;
        }
    }
}
