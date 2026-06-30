using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.BossRoom.Utils
{
    /// This utility help showing Network statistics at runtime.
    ///
    /// This component attaches to any networked object.
    /// It'll spawn all the needed text and canvas.
    ///
    /// NOTE: This class will be removed once Unity provides support for this.
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkStats : NetworkBehaviour
    {
        // For a value like RTT an exponential moving average is a better indication of the current rtt and fluctuates less.
        struct ExponentialMovingAverageCalculator
        {
            readonly float m_Alpha;
            float m_Average;

            public float Average => m_Average;

            public ExponentialMovingAverageCalculator(float average)
            {
                m_Alpha = 2f / (k_MaxWindowSize + 1);
                m_Average = average;
            }

            public float NextValue(float value) => m_Average = (value - m_Average) * m_Alpha + m_Average;
        }

        const int k_MaxWindowSizeSeconds = 3;
        const float k_PingIntervalSeconds = 0.1f;
        const float k_MaxWindowSize = k_MaxWindowSizeSeconds / k_PingIntervalSeconds;

        // Some games are less sensitive to latency than others.
        const float k_StrugglingNetworkConditionsRTTThreshold = 130;
        const float k_BadNetworkConditionsRTTThreshold = 200;

        ExponentialMovingAverageCalculator m_BossRoomRTT = new ExponentialMovingAverageCalculator(0);

        float m_LastPingTime;
        TextMeshProUGUI m_TextStat;
        TextMeshProUGUI m_TextHostType;
        TextMeshProUGUI m_TextBadNetworkConditions;

        // When receiving pong client RPCs, we need to know when the initiating ping sent it so we can calculate its individual RTT
        int m_CurrentRTTPingId;

        Dictionary<int, float> m_PingHistoryStartTimes = new Dictionary<int, float>();

        string m_TextToDisplay;

        public override void OnStartClient()
        {
            bool isClientOnly = isClient && !isServer;
            if (!isLocalPlayer && isClientOnly) // we don't want to track player ghost stats, only our own
            {
                enabled = false;
                return;
            }

            if (isLocalPlayer)
            {
                CreateNetworkStatsText();
            }
        }

        // Creating a UI text object and add it to NetworkOverlay canvas
        void CreateNetworkStatsText()
        {
            Assert.IsNotNull(Editor.NetworkOverlay.Instance,
                "No NetworkOverlay object part of scene. Add NetworkOverlay prefab to bootstrap scene!");

            string hostType = NetworkServer.active && NetworkClient.active ? "Host" : NetworkClient.active ? "Client" : "Unknown";
            Editor.NetworkOverlay.Instance.AddTextToUI("UI Host Type Text", $"Type: {hostType}", out m_TextHostType);
            Editor.NetworkOverlay.Instance.AddTextToUI("UI Stat Text", "No Stat", out m_TextStat);
            Editor.NetworkOverlay.Instance.AddTextToUI("UI Bad Conditions Text", "", out m_TextBadNetworkConditions);
        }

        void FixedUpdate()
        {
            if (!isServer)
            {
                if (isLocalPlayer && Time.realtimeSinceStartup - m_LastPingTime > k_PingIntervalSeconds)
                {
                    CmdPing(m_CurrentRTTPingId);
                    m_PingHistoryStartTimes[m_CurrentRTTPingId] = Time.realtimeSinceStartup;
                    m_CurrentRTTPingId++;
                    m_LastPingTime = Time.realtimeSinceStartup;
                }

                // Use Mirror's built-in RTT (in seconds, convert to ms)
                float mirrorRttMs = (float)(NetworkTime.rtt * 1000.0);

                if (m_TextStat != null)
                {
                    m_TextToDisplay = $"RTT: {m_BossRoomRTT.Average.ToString("0")} ms;\nMirror RTT {mirrorRttMs.ToString("0")} ms";
                    if (mirrorRttMs > k_BadNetworkConditionsRTTThreshold)
                    {
                        m_TextStat.color = Color.red;
                    }
                    else if (mirrorRttMs > k_StrugglingNetworkConditionsRTTThreshold)
                    {
                        m_TextStat.color = Color.yellow;
                    }
                    else
                    {
                        m_TextStat.color = Color.white;
                    }
                }

                if (m_TextBadNetworkConditions != null)
                {
                    m_TextBadNetworkConditions.text = mirrorRttMs > k_BadNetworkConditionsRTTThreshold ? "Bad Network Conditions Detected!" : "";
                    var color = Color.red;
                    color.a = Mathf.PingPong(Time.time, 1f);
                    m_TextBadNetworkConditions.color = color;
                }
            }
            else
            {
                m_TextToDisplay = $"Connected players: {NetworkServer.connections.Count.ToString()}";
            }

            if (m_TextStat)
            {
                m_TextStat.text = m_TextToDisplay;
            }
        }

        [Command]
        void CmdPing(int pingId)
        {
            RpcPong(pingId);
        }

        [TargetRpc]
        void RpcPong(int pingId)
        {
            if (m_PingHistoryStartTimes.TryGetValue(pingId, out float startTime))
            {
                m_PingHistoryStartTimes.Remove(pingId);
                m_BossRoomRTT.NextValue(Time.realtimeSinceStartup - startTime);
            }
        }

        public override void OnStopClient()
        {
            if (m_TextStat != null)
            {
                Destroy(m_TextStat.gameObject);
            }

            if (m_TextHostType != null)
            {
                Destroy(m_TextHostType.gameObject);
            }

            if (m_TextBadNetworkConditions != null)
            {
                Destroy(m_TextBadNetworkConditions.gameObject);
            }
        }
    }
}
