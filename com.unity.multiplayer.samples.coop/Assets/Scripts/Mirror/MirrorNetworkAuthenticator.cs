using System;
using System.Collections;
using System.Text;
using Mirror;
using Unity.BossRoom.ConnectionManagement;
using Unity.BossRoom.MasterServer;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.BossRoom.Mirror
{
    /// <summary>
    /// Mirror NetworkAuthenticator that validates connecting players via the Master Server.
    ///
    /// Flow:
    ///   1. Client sends AuthRequestMessage containing playerId + joinToken.
    ///   2. Server calls POST /servers/validate-join-token to verify the token.
    ///   3. Server sends AuthResponseMessage (success/fail) back to client.
    ///
    /// The joinToken is issued by the Master Server when a player calls POST /lobby/{id}/join.
    /// It is single-use and short-lived so it can't be replayed.
    ///
    /// On a dedicated server (headless batch mode) a valid token is REQUIRED. On a
    /// player-hosted (P2P) or LAN-direct host, an empty token is accepted so direct
    /// IP play works without the master server.
    /// </summary>
    [AddComponentMenu("BossRoom/Mirror Network Authenticator")]
    public class MirrorNetworkAuthenticator : NetworkAuthenticator
    {
        [Tooltip("Base URL of the Master Server, e.g. http://localhost:8000. On a dedicated " +
                 "server this is overridden by the MASTER_SERVER_URL environment variable.")]
        public string masterServerUrl = "http://localhost:8000";

        // Resolved in OnStartServer: dedicated servers require a valid join token.
        bool m_RequireToken;

        // ── Messages ──────────────────────────────────────────────────────────

        public struct AuthRequestMessage : NetworkMessage
        {
            public string playerId;
            public string playerName;
            public string joinToken;
            public string sessionId;
            public bool isDebug;
        }

        public struct AuthResponseMessage : NetworkMessage
        {
            public bool success;
            public string reason;
        }

        // ── Server side ───────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            // A dedicated game server runs headless (batch mode) and is reached via
            // the master server, so it must require a validated join token.
            m_RequireToken = Application.isBatchMode;

            var envUrl = Environment.GetEnvironmentVariable("MASTER_SERVER_URL");
            if (!string.IsNullOrEmpty(envUrl))
                masterServerUrl = envUrl;

            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            // The host's local connection bypasses the transport layer, so the
            // AuthRequestMessage arrives a few frames later via Mirror's local-
            // connection queue. Accept it immediately to avoid a race with
            // OnServerReady (which may fire before the queue is drained).
            if (conn is LocalConnectionToClient)
            {
                var payload = ClientAuthPayload.Current;
                AcceptConnection(conn,
                    payload?.PlayerId   ?? SystemInfo.deviceUniqueIdentifier,
                    payload?.PlayerName ?? "Host",
                    payload?.IsDebug    ?? Debug.isDebugBuild);
                return;
            }
            // Remote clients: wait for the client to send AuthRequestMessage.
        }

        void OnAuthRequestMessage(NetworkConnectionToClient conn, AuthRequestMessage msg)
        {
            // Local host connection was already accepted synchronously in
            // OnServerAuthenticate — skip to avoid double-acceptance.
            if (conn.isAuthenticated) return;

            if (string.IsNullOrEmpty(msg.joinToken))
            {
                if (m_RequireToken)
                {
                    RejectConnection(conn, "A join token is required to connect to this server.");
                    return;
                }
                // P2P/LAN host: accept without a master-server token.
                AcceptConnection(conn, msg.playerId, msg.playerName, msg.isDebug);
                return;
            }

            StartCoroutine(ValidateTokenCoroutine(conn, msg));
        }

        IEnumerator ValidateTokenCoroutine(NetworkConnectionToClient conn, AuthRequestMessage msg)
        {
            // Peek (consume=false): validate the token WITHOUT consuming it. The token
            // is single-use, so consuming it here would strand a client that later gets
            // bounced by the gameplay approval gate (it would have no token to reconnect
            // with). It's consumed in ConsumeJoinToken once the player is actually seated.
            var url = $"{masterServerUrl.TrimEnd('/')}/servers/validate-join-token";
            var requestBody = JsonUtility.ToJson(new ValidateJoinTokenRequest
            {
                join_token = msg.joinToken,
                session_id = msg.sessionId,
                consume = false,
            });
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            if (ok)
            {
                var resp = JsonUtility.FromJson<ValidateJoinTokenResponse>(req.downloadHandler.text);
                ok = resp != null && resp.valid;
                if (!ok)
                    Debug.LogWarning($"[Auth] Token invalid for {msg.playerId}: {req.downloadHandler.text}");
            }
            else
            {
                Debug.LogWarning($"[Auth] Token validation request failed: {req.error}");
            }

            if (ok)
                AcceptConnection(conn, msg.playerId, msg.playerName, msg.isDebug, msg.joinToken, msg.sessionId);
            else
                RejectConnection(conn, "Invalid or expired join token.");
        }

        void AcceptConnection(NetworkConnectionToClient conn, string playerId, string playerName, bool isDebug,
            string joinToken = "", string sessionId = "")
        {
            conn.authenticationData = new PlayerAuthData
            {
                PlayerId = playerId,
                PlayerName = playerName,
                IsDebug = isDebug,
                JoinToken = joinToken ?? string.Empty,
                SessionId = sessionId ?? string.Empty,
            };
            conn.Send(new AuthResponseMessage { success = true });
            ServerAccept(conn);
        }

        /// <summary>
        /// Consumes (burns) a peeked join token after the player has cleared the
        /// gameplay approval gate and is seated. Fire-and-forget: a failure here is
        /// non-fatal because the token expires on its own via its short TTL. No-op for
        /// host/LAN connections, which carry no token.
        /// </summary>
        public void ConsumeJoinToken(string joinToken, string sessionId)
        {
            if (string.IsNullOrEmpty(joinToken))
                return;
            StartCoroutine(ConsumeJoinTokenCoroutine(joinToken, sessionId));
        }

        IEnumerator ConsumeJoinTokenCoroutine(string joinToken, string sessionId)
        {
            var url = $"{masterServerUrl.TrimEnd('/')}/servers/validate-join-token";
            var requestBody = JsonUtility.ToJson(new ValidateJoinTokenRequest
            {
                join_token = joinToken,
                session_id = sessionId,
                consume = true,
            });
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestBody));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[Auth] Failed to consume join token (will expire via TTL): {req.error}");
        }

        void RejectConnection(NetworkConnectionToClient conn, string reason)
        {
            conn.Send(new AuthResponseMessage { success = false, reason = reason });
            // Give the message a moment to flush before disconnecting.
            conn.isAuthenticated = false;
            ServerReject(conn);
        }

        // ── Client side ───────────────────────────────────────────────────────

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        public override void OnClientAuthenticate()
        {
            var payload = ClientAuthPayload.Current;
            NetworkClient.Send(new AuthRequestMessage
            {
                playerId = payload?.PlayerId ?? SystemInfo.deviceUniqueIdentifier,
                playerName = payload?.PlayerName ?? "Unknown",
                joinToken = payload?.JoinToken ?? string.Empty,
                sessionId = payload?.SessionId ?? string.Empty,
                isDebug = payload?.IsDebug ?? Debug.isDebugBuild,
            });
        }

        void OnAuthResponseMessage(AuthResponseMessage msg)
        {
            if (msg.success)
            {
                ClientAccept();
            }
            else
            {
                Debug.LogError($"[Auth] Server rejected connection: {msg.reason}");
                ClientReject();
            }
        }
    }

    /// <summary>Carries auth data on the server after a player is accepted.</summary>
    public class PlayerAuthData
    {
        public string PlayerId;
        public string PlayerName;
        public bool IsDebug;
        // Peeked join token (and its lobby session id), consumed once the player
        // clears the gameplay approval gate. Empty for host/LAN connections.
        public string JoinToken;
        public string SessionId;
    }
}
