using System;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Live network readout, so an online failure can be described instead of guessed at.
    /// </summary>
    /// <remarks>
    /// Every online problem looks the same from the outside — "it dropped" — and the causes worth
    /// telling apart (a saturated link, a timeout, a handler exception) leave very different
    /// traces. This keeps those traces where they can actually be read:
    ///
    /// - An on-screen panel (F3, or a three-finger tap) showing rtt, Mirror's own connection
    ///   quality verdict, the live snapshot buffer multiplier and the object count. On a phone
    ///   there is no other way to see any of it — Player.log sits in app-private storage.
    /// - A compact line into the log every few seconds while connected, so a PC session leaves a
    ///   timeline of what the link was doing before it died, not just the death itself.
    /// - The last handful of network-relevant log lines, captured off the Unity log callback and
    ///   kept on screen. Mirror, kcp2k and LRM all explain themselves on the way down
    ///   ("Connection timed out after not receiving any message for...", "Could not spawn
    ///   assetId...", "Disconnecting connection ... exception"), and that sentence is normally the
    ///   whole answer.
    ///
    /// Reading the log rather than intercepting Mirror's events is deliberate: NetworkManager
    /// *assigns* NetworkClient.OnErrorEvent in SetupClient, so a subscriber added here would be
    /// silently dropped on the next StartClient. The log callback survives that, and also catches
    /// kcp2k's own messages, which never pass through NetworkManager at all.
    /// </remarks>
    public class OnlineDiagnostics : MonoBehaviour
    {
        /// <summary>Seconds between the periodic "here is the state of the link" log lines.</summary>
        const float k_LogInterval = 5f;

        /// <summary>Seconds between overlay samples. Fast enough to see a spike, slow enough to read.</summary>
        const float k_SampleInterval = 0.5f;

        /// <summary>
        /// How many captured lines to keep. Deep enough that a burst of registration warnings
        /// during a scene load cannot push the line that explains the drop out of the window.
        /// </summary>
        const int k_MaxCapturedLines = 16;

        /// <summary>
        /// Substrings that mark a log line as worth keeping. Deliberately broad — a missed line is
        /// a lost diagnosis, an extra one is a wasted row on the panel.
        /// </summary>
        static readonly string[] k_InterestingLogMarkers =
        {
            "[Mirror]", "[LRM]", "[Relay]", "[OnlineTuning]", "Kcp", "kcp",
            "isconnect", "imed out", "Could not spawn", "assetId", "dead_link", "relay",
            // Mirror rejects a message it cannot read by logging one of these as a *warning* and
            // then disconnecting. The error that follows ("failed to unpack and invoke message")
            // says only that something was rejected; these say what, and without them a drop of
            // this kind is indistinguishable from a timeout.
            "Unknown message id", "Invalid message header", "failed to unpack",
            "caused an Exception", "without an active client",
        };

        /// <summary>
        /// Lines that match a marker but are never the reason for anything. Registering the spawn
        /// list logs one per prefab, which is enough to fill the whole capture window during the
        /// scene load — precisely when the interesting line arrives.
        /// </summary>
        static readonly string[] k_NoiseLogMarkers =
        {
            "Replacing existing prefab",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject(nameof(OnlineDiagnostics));
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineDiagnostics>();
        }

        readonly Queue<string> m_CapturedLines = new Queue<string>(k_MaxCapturedLines);
        readonly StringBuilder m_Builder = new StringBuilder();

        bool m_Visible;
        float m_NextLogTime;
        float m_NextSampleTime;
        GUIStyle m_Style;

        // Sampled once per k_SampleInterval rather than rebuilt per OnGUI call: OnGUI runs several
        // times a frame, and a readout that changes between the layout and repaint passes flickers.
        string m_Readout = "";

        // Connection lifetime, for the "it survived N seconds" half of a drop report.
        bool m_WasConnected;
        float m_ConnectedSince;

        void OnEnable()
        {
            Application.logMessageReceived += OnLogMessage;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLogMessage;
        }

        void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            // Never log from in here — Debug.Log would re-enter this callback.
            bool interesting = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;

            foreach (var noise in k_NoiseLogMarkers)
            {
                if (condition.Contains(noise, StringComparison.Ordinal))
                {
                    return;
                }
            }

            if (!interesting)
            {
                foreach (var marker in k_InterestingLogMarkers)
                {
                    if (condition.Contains(marker, StringComparison.Ordinal))
                    {
                        interesting = true;
                        break;
                    }
                }
            }

            if (!interesting)
            {
                return;
            }

            // One line each: the panel has to stay readable on a phone screen.
            var firstLine = condition;
            int newline = firstLine.IndexOf('\n');
            if (newline >= 0)
            {
                firstLine = firstLine.Substring(0, newline);
            }
            if (firstLine.Length > 110)
            {
                firstLine = firstLine.Substring(0, 110) + "...";
            }

            if (m_CapturedLines.Count >= k_MaxCapturedLines)
            {
                m_CapturedLines.Dequeue();
            }
            m_CapturedLines.Enqueue($"{DateTime.Now:HH:mm:ss} {firstLine}");
        }

        void Update()
        {
            PollToggle();
            TrackConnectionLifetime();

            if (Time.unscaledTime >= m_NextSampleTime)
            {
                m_NextSampleTime = Time.unscaledTime + k_SampleInterval;
                m_Readout = BuildReadout();
            }

            // The periodic log only makes sense while there is a link to describe.
            if (NetworkClient.isConnected || NetworkServer.active)
            {
                if (Time.unscaledTime >= m_NextLogTime)
                {
                    m_NextLogTime = Time.unscaledTime + k_LogInterval;
                    Debug.Log("[NetDiag] " + BuildReadout().Replace("\n", " | "));
                }
            }
            else
            {
                // Restart the cadence so the first line after connecting lands immediately.
                m_NextLogTime = 0f;
            }
        }

        void PollToggle()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                m_Visible = !m_Visible;
                return;
            }

            // Three fingers down at once: not something the game's controls can produce by
            // accident (the joystick and the power buttons are one finger each, zoom is two).
            var touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            int pressed = 0;
            bool anyPressedThisFrame = false;
            foreach (var touch in touchscreen.touches)
            {
                if (!touch.press.isPressed)
                {
                    continue;
                }
                pressed++;
                anyPressedThisFrame = anyPressedThisFrame || touch.press.wasPressedThisFrame;
            }

            if (pressed == 3 && anyPressedThisFrame)
            {
                m_Visible = !m_Visible;
            }
        }

        void TrackConnectionLifetime()
        {
            bool connected = NetworkClient.isConnected;

            if (connected && !m_WasConnected)
            {
                m_ConnectedSince = Time.unscaledTime;
            }
            else if (!connected && m_WasConnected)
            {
                // The moment worth having in the log: how long it lasted, and what the transport
                // was saying just before it went. The captured lines hold the reason.
                Debug.LogWarning($"[NetDiag] CLIENTE DESCONECTADO tras {Time.unscaledTime - m_ConnectedSince:F1}s. " +
                                 $"Ultimas lineas de red:\n{string.Join("\n", m_CapturedLines)}");
            }

            m_WasConnected = connected;
        }

        string BuildReadout()
        {
            m_Builder.Clear();

            string role = NetworkServer.active
                ? (NetworkClient.active ? "HOST" : "SERVER")
                : NetworkClient.isConnected ? "CLIENT"
                : NetworkClient.isConnecting ? "CONNECTING"
                : "OFFLINE";

            string transportName = Transport.active != null ? Transport.active.GetType().Name : "<none>";

            m_Builder.Append(role).Append("  transport=").Append(transportName);
            m_Builder.Append("  fps=").Append(Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)));
            m_Builder.Append("  sendRate=").Append(NetworkServer.sendRate);

            if (NetworkClient.isConnected)
            {
                m_Builder.Append('\n');
                m_Builder.Append("rtt=").Append(Mathf.RoundToInt((float)(NetworkTime.rtt * 1000))).Append("ms");
                m_Builder.Append("  jitter=").Append(Mathf.RoundToInt((float)(NetworkTime.rttVariance * 1000))).Append("ms");
                m_Builder.Append("  quality=").Append(NetworkClient.connectionQuality);
                // The live, dynamically adjusted buffer. If this keeps climbing, the link is
                // jittery and the interpolator is compensating — that is what stutter looks like
                // in numbers, before it looks like anything on screen.
                m_Builder.Append("  buffer=").Append(NetworkClient.bufferTimeMultiplier.ToString("F1")).Append('x');
                m_Builder.Append("  objects=").Append(NetworkClient.spawned.Count);
            }

            if (NetworkServer.active)
            {
                m_Builder.Append('\n');
                m_Builder.Append("connections=").Append(NetworkServer.connections.Count);
                m_Builder.Append("  spawned=").Append(NetworkServer.spawned.Count);
            }

            return m_Builder.ToString();
        }

        void OnGUI()
        {
            if (!m_Visible)
            {
                return;
            }

            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(12, Screen.height / 55),
                    richText = false,
                    wordWrap = false,
                };
            }

            m_Builder.Clear();
            m_Builder.Append(m_Readout);
            if (m_CapturedLines.Count > 0)
            {
                m_Builder.Append("\n-- log de red --\n");
                m_Builder.Append(string.Join("\n", m_CapturedLines));
            }
            var text = m_Builder.ToString();

            float width = Mathf.Min(Screen.width - 20f, 900f);
            float height = m_Style.CalcHeight(new GUIContent(text), width) + 16f;
            var rect = new Rect(10f, 10f, width, height);

            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f), text, m_Style);
        }
    }
}
