using Unity.BossRoom.Utils;
using Mirror;

namespace Unity.BossRoom.Gameplay.Messages
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD

    public struct CheatUsedMessage : NetworkMessage
    {
        public string CheatUsed;
        public string CheaterName;
    }

#endif
}
