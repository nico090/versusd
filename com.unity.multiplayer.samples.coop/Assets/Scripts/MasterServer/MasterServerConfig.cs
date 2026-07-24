using UnityEngine;

namespace Unity.BossRoom.MasterServer
{
    [CreateAssetMenu(fileName = "MasterServerConfig", menuName = "BossRoom/MasterServerConfig")]
    public class MasterServerConfig : ScriptableObject
    {
        [Tooltip("Base URL of the Master Server, e.g. http://localhost:8000")]
        public string baseUrl = "http://localhost:8000";

        // NOTE: the LRM relay endpoint (host IP + UDP port) is NOT configured here. It lives on the
        // LightReflectiveMirrorTransport component in Startup.unity (serverIP / serverPort), which is
        // what the transport actually connects to on Awake. Former relayHost/relayPort fields here
        // were never read by any code and were removed to avoid the illusion of being configurable.

        [Header("Modes")]
        [Tooltip("When off, the 'Dedicated server' option is hidden in Create Room and every room is created via the relay. The /lobby/dedicated endpoint still exists server-side.")]
        public bool enableDedicatedServers = true;
    }
}
