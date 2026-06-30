using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Mirror;

namespace Unity.BossRoom.Gameplay.Messages
{
    public struct LifeStateChangedEventMessage : NetworkMessage
    {
        public LifeState NewLifeState;
        public CharacterTypeEnum CharacterType;
        public string CharacterName;

        // Death attribution — populated when NewLifeState is Dead or Fainted
        public ulong KillerClientId;
        public bool KillerIsNpc;
        public ulong VictimClientId;
        public bool VictimIsNpc;
    }
}
