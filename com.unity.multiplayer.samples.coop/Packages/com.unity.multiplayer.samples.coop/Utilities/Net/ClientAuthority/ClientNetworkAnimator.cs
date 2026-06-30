using Mirror;
using UnityEngine;

namespace Unity.Multiplayer.Samples.Utilities.ClientAuthority
{
    /// <summary>
    /// Client-authority animator. Set clientAuthority = true on the attached NetworkAnimator component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkAnimator))]
    public class ClientNetworkAnimator : NetworkBehaviour { }
}
