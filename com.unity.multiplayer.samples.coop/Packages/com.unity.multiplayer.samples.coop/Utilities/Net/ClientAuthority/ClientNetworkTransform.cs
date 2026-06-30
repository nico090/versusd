using Mirror;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities.ClientAuthority
{
    /// <summary>
    /// Client-authority transform. Set syncDirection = ClientToServer on the attached NetworkTransform component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkTransformReliable))]
    public class ClientNetworkTransform : NetworkBehaviour { }
}
