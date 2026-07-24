//#if MIRROR <- commented out because MIRROR isn't defined on first import yet
using System;
using System.IO;
using System.Net;
using Mirror;
using Newtonsoft.Json;

namespace kcp2k
{
    // NOTE: ported to the modern (cookie-based) kcp2k that ships with Mirror
    // @ebf5d41bb3b0. The wire protocol MUST match the Unity client's KcpTransport,
    // otherwise the relay can't parse the client's handshake (the old kcp2k had no
    // security cookie -> "Input failed with error=-3" + client-side 10s timeout).
    // The public surface below still matches LRM's OLD Mirror.Transport base class
    // (ServerStart(ushort), ClientSend(channelId, segment), Update(), Action<int,Exception>
    // error callbacks, ...) because KcpWebCombined.cs and the LRM server depend on it.
    // Only the internals were rewritten to drive the new kcp2k API.
    public class KcpTransport : Transport
    {
        // scheme used by this transport
        public const string Scheme = "kcp";

        // common
        public static int ConnectionTimeout = 10000;

        public bool NoDelay = true;

        public uint Interval = 10;

        public int FastResend = 2;

        public bool CongestionWindow = false; // KCP 'NoCongestionWindow' is false by default. here we negate it for ease of use.

        public uint SendWindowSize = 4096; //Kcp.WND_SND; 32 by default. Mirror sends a lot, so we need a lot more.

        public uint ReceiveWindowSize = 4096; //Kcp.WND_RCV; 128 by default. Mirror sends a lot, so we need a lot more.

        // config is built from the serialized/env settings in Awake().
        KcpConfig config;

        // server & client
        KcpServer server;
        KcpClient client;

        // debugging
        public bool debugLog;

        // translate Kcp <-> Mirror channels.
        // LRM's stripped Mirror uses channel 0 = Reliable, 1 = Unreliable.
        static int FromKcpChannel(KcpChannel channel) =>
            channel == KcpChannel.Reliable ? 0 : 1;

        static KcpChannel ToKcpChannel(int channelId) =>
            channelId == 1 ? KcpChannel.Unreliable : KcpChannel.Reliable;

        public override void Awake()
        {
            KCPConfig conf = new KCPConfig();

            bool noConfig = bool.Parse(Environment.GetEnvironmentVariable("NO_CONFIG") ?? "false");

            if (!File.Exists("KCPConfig.json") && !noConfig)
            {
                File.WriteAllText("KCPConfig.json", JsonConvert.SerializeObject(conf, Formatting.Indented));
            }
            else
            {
                if (noConfig)
                {
                    conf = new KCPConfig();
                    conf.NoDelay = bool.Parse(Environment.GetEnvironmentVariable("KCP_NODELAY") ?? "true");
                    conf.Interval = uint.Parse(Environment.GetEnvironmentVariable("KCP_INTERVAL") ?? "10");
                    conf.FastResend = int.Parse(Environment.GetEnvironmentVariable("KCP_FAST_RESEND") ?? "2");
                    conf.CongestionWindow = bool.Parse(Environment.GetEnvironmentVariable("KCP_CONGESTION_WINDOW") ?? "false");
                    conf.SendWindowSize = uint.Parse(Environment.GetEnvironmentVariable("KCP_SEND_WINDOW_SIZE") ?? "4096");
                    conf.ReceiveWindowSize = uint.Parse(Environment.GetEnvironmentVariable("KCP_RECEIVE_WINDOW_SIZE") ?? "4096");
                    conf.ConnectionTimeout = int.Parse(Environment.GetEnvironmentVariable("KCP_CONNECTION_TIMEOUT") ?? "10000");
                }
                else
                    conf = JsonConvert.DeserializeObject<KCPConfig>(File.ReadAllText("KCPConfig.json"));
            }

            NoDelay = conf.NoDelay;
            Interval = conf.Interval;
            FastResend = conf.FastResend;
            CongestionWindow = conf.CongestionWindow;
            SendWindowSize = conf.SendWindowSize;
            ReceiveWindowSize = conf.ReceiveWindowSize;
            ConnectionTimeout = conf.ConnectionTimeout;

            // logging
            if (debugLog)
                Log.Info = Console.WriteLine;
            else
                Log.Info = _ => { };
            Log.Warning = Console.WriteLine;
            Log.Error = Console.WriteLine;

            // build the modern kcp2k config.
            // DualMode = false: bind IPv4 only. The relay runs in a container and
            // clients reach it via the VPS public IPv4, so IPv6 dual-mode just risks
            // bind failures inside Docker. Buffer sizes / MTU keep their defaults.
            config = new KcpConfig(
                DualMode: false,
                NoDelay: NoDelay,
                Interval: Interval,
                FastResend: FastResend,
                CongestionWindow: CongestionWindow,
                SendWindowSize: SendWindowSize,
                ReceiveWindowSize: ReceiveWindowSize,
                Timeout: ConnectionTimeout,
                MaxRetransmits: Kcp.DEADLINK * 2);

            // client (relay never actually connects as a client, but the object is
            // kept so ClientConnected()/ClientSend() stay valid no-ops)
            client = new KcpClient(
                () => OnClientConnected.Invoke(),
                (message, channel) => OnClientDataReceived.Invoke(message, FromKcpChannel(channel)),
                () => OnClientDisconnected.Invoke(),
                (error, reason) => OnClientError.Invoke(new Exception($"{error}: {reason}")),
                config
            );

            // server
            server = new KcpServer(
                (connectionId, endPoint) => OnServerConnected.Invoke(connectionId),
                (connectionId, message, channel) => OnServerDataReceived.Invoke(connectionId, message, FromKcpChannel(channel)),
                (connectionId) => OnServerDisconnected.Invoke(connectionId),
                (connectionId, error, reason) => OnServerError.Invoke(connectionId, new Exception($"{error}: {reason}")),
                config
            );

            Console.WriteLine("KcpTransport initialized!");
        }

        // all except WebGL
        public override bool Available() => true;

        // client
        public override bool ClientConnected() => client.connected;
        public override void ClientConnect(string address) { }
        public override void ClientSend(int channelId, ArraySegment<byte> segment)
        {
            client.Send(segment, ToKcpChannel(channelId));
        }
        public override void ClientDisconnect() => client.Disconnect();

        // server
        public override Uri ServerUri()
        {
            UriBuilder builder = new UriBuilder();
            builder.Scheme = Scheme;
            builder.Host = Dns.GetHostName();
            return builder.Uri;
        }
        public override bool ServerActive() => server.IsActive();
        public override void ServerStart(ushort requestedPort) => server.Start(requestedPort);
        public override void ServerSend(int connectionId, int channelId, ArraySegment<byte> segment)
        {
            server.Send(connectionId, segment, ToKcpChannel(channelId));
        }
        public override bool ServerDisconnect(int connectionId)
        {
            server.Disconnect(connectionId);
            return true;
        }
        public override string ServerGetClientAddress(int connectionId)
        {
            IPEndPoint endPoint = server.GetClientEndPoint(connectionId);
            return endPoint != null ? endPoint.Address.ToString() : "";
        }
        public override void ServerStop() => server.Stop();

        public override void Update()
        {
            server.TickIncoming();
            server.TickOutgoing();
        }

        // common
        public override void Shutdown() { }

        // max message size
        public override int GetMaxPacketSize(int channelId = 0)
        {
            switch (channelId)
            {
                case 1:
                    return KcpPeer.UnreliableMaxMessageSize(config.Mtu);
                default:
                    return KcpPeer.ReliableMaxMessageSize(config.Mtu, ReceiveWindowSize);
            }
        }

        public override string ToString() => "KCP";
    }
}
//#endif MIRROR <- commented out because MIRROR isn't defined on first import yet
