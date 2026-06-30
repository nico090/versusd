using UnityEngine;

namespace Unity.BossRoom.MasterServer
{
    [CreateAssetMenu(fileName = "MasterServerConfig", menuName = "BossRoom/MasterServerConfig")]
    public class MasterServerConfig : ScriptableObject
    {
        [Tooltip("Base URL of the Master Server, e.g. http://localhost:8000")]
        public string baseUrl = "http://localhost:8000";
    }
}
