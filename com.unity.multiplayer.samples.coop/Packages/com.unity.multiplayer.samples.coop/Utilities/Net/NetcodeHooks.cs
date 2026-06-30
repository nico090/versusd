using System;
using Mirror;

namespace Unity.Multiplayer.Samples.Utilities
{
    // useful for classes that can't be NetworkBehaviours themselves (for example, with dedicated servers, you can't have a NetworkBehaviour that exists
    // on clients but gets stripped on the server, this will mess with your NetworkBehaviour indexing.
    public class NetcodeHooks : NetworkBehaviour
    {
        public event Action OnNetworkSpawnHook;

        public event Action OnNetworkDespawnHook;

        public override void OnStartServer()
        {
            base.OnStartServer();
            OnNetworkSpawnHook?.Invoke();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            OnNetworkSpawnHook?.Invoke();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            OnNetworkDespawnHook?.Invoke();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            OnNetworkDespawnHook?.Invoke();
        }
    }
}
