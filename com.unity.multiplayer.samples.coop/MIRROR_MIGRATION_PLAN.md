# Plan de migración NGO → Mirror — Trabajo restante

> Rama: `feature/mirror-migration`
> Fecha del diagnóstico: 2026-06-13
> Editor: Unity 6000.0.52f1

## Diagnóstico: los errores visibles están obsoletos

El `Editor.log` (última compilación 17:10) muestra ~80 errores, pero **casi todos ya
están arreglados en disco y no se han recompilado**. Verificado archivo por archivo:

| Lo que dice el log | Estado real en disco |
|---|---|
| `NetworkNameState` usa `NetworkVariable<>` | ✅ Ya usa `[SyncVar(hook=...)]` |
| `NetworkObjectPool` usa `INetworkPrefabInstanceHandler` | ✅ Ya usa `NetworkIdentity` + `ObjectPool` |
| `NetworkStats` usa `[Rpc]/RpcParams` | ✅ Ya usa `[Command]/[TargetRpc]` + `NetworkTime.rtt` |
| `NetworkedMessageChannel` usa `FastBufferReader` | ✅ Ya usa `NetworkMessage` + handlers de Mirror |
| `ConnectionManagement` referencia `UnityServices` | ✅ Sin referencias |
| `ConnectionPayload` no existe | ✅ Definido en `ConnectionManager.cs:44` |
| `ConnectionMethodMasterServer.cs` con errores | ✅ El archivo ya no existe |
| `NetworkGuid` usa `INetworkSerializeByMemcpy` | ✅ Migrado |

Las asmdefs también fueron actualizadas (referencian `Mirror`, `kcp2k`, `Mirror.Components`).
El Editor está **abierto** (lockfile 11:08), así que sigue mostrando errores viejos en caché.

**Conclusión:** la migración de código fuente está prácticamente terminada en el árbol de
trabajo; falta recompilar para obtener la lista real, completar assets y validar en runtime.

---

## Fase 1 — Recompilar para obtener la lista REAL de errores ✅ HECHO (2026-06-13)

Recompilado. El `Editor.log` arrojó **39 errores reales** (todos en el assembly
`Unity.BossRoom.Gameplay`), mucho menos que los ~80 del log viejo. Lista completa abordada en
la Fase 2 (lote 2) abajo. **Falta volver a recompilar tras este lote 2 para confirmar 0 errores
(y detectar posibles cascadas).**

## Fase 2 — Arreglar errores de código confirmados (pocos) ✅ HECHO (2026-06-13)

Detectados estáticamente y reales hoy:

1. ✅ **`Mirror.Transports` faltaba en la asmdef de ConnectionManagement.**
   `ConnectionMethod.cs` usa `KcpTransport`, que vive en el ensamblado `Mirror.Transports`
   (el subfolder `kcp2k` solo contiene el protocolo de bajo nivel). Referencia añadida a
   `Assets/Scripts/ConnectionManagement/Unity.BossRoom.ConnectionManagement.asmdef`.
2. ✅ **`connectionId` sobre `NetworkConnectionToServer`** — confirmado bug de compilación:
   en el Mirror vendido `connectionId` solo existe en `NetworkConnectionToClient`, no en la
   clase base ni en `NetworkConnectionToServer` (tipo de `NetworkClient.connection`). Corregido:
   - `ConnectionManager.cs:128` (`OnClientConnected`): usa `NetworkConnection.LocalConnectionId`
     (el id se ignora aguas abajo en los estados de cliente).
   - `HostingState.cs:58,77`: id local del host vía `NetworkServer.localConnection.connectionId`.
   - `conn.connectionId` en `OnServerClientConnected/Disconnected` ya era válido (`conn` es
     `NetworkConnectionToClient`).
3. ✅ **`ClientCharSelectState.cs` (no estaba en el plan, 3 usos del mismo bug).** Migrado al
   idioma de Mirror:
   - `CmdChangeSeat` ahora es autoritativo en servidor: toma el id del `sender` del `[Command]`
     (mismo patrón ya usado en `DebugCheatsManager.cs`) en vez de confiar en un id enviado por
     el cliente. Firma nueva: `CmdChangeSeat(int seatIdx, bool lockedIn, NetworkConnectionToClient sender = null)`.
   - Lectura ("¿cuál es mi asiento?") usa el helper `GetLocalClientId()` (correcto en host = 0).
   - ⚠️ **Gap conocido para Fase 4:** Mirror no expone al cliente puro su `connectionId` asignado
     por el servidor; además `ServerCharSelectState.SeatNewPlayer` aún **no está cableado** (nadie
     lo llama). La identificación del asiento en build cliente-dedicado necesita sincronizar el
     owner id a los clientes (p.ej. SyncVar en PersistentPlayer o netId en SessionPlayerState).

### Fase 2 — Lote 2: los 39 errores reales del recompilado ✅ HECHO (2026-06-13)

Todos en `Unity.BossRoom.Gameplay`. Agrupados por causa:

- **`Mirror.Components` faltaba en la asmdef de Gameplay** → resolvía `NetworkAnimator` y
  `NetworkTransform`. Referencia añadida a `Unity.BossRoom.Gameplay.asmdef`.
- **No existe clase concreta `NetworkTransform` en Mirror** (solo `NetworkTransformBase` abstracta
  + `Reliable`/`Unreliable`). `ServerDisplacerOnParentChange` ahora usa `NetworkTransformBase`.
- **`NetworkBehaviour` de Mirror no tiene `OnDestroy` virtual** → en `PersistentPlayer`,
  `ClientPlayerAvatar`, `PhysicsWrapper`, `ClientClickFeedback`, `UIStateDisplayHandler` se cambió
  `public override void OnDestroy()` + `base.OnDestroy()` por un mensaje Unity `void OnDestroy()`.
  (Las subclases de `GameStateBehaviour` sí tienen `OnDestroy` virtual y se dejaron intactas.)
- **`OwnerClientId`** (no existe en Mirror): `PersistentPlayer.OwnerClientId` pasó a `public`;
  se añadió `ServerCharacter.OwnerClientId` (`connectionToClient?.connectionId`); `PlayerServerCharacter`
  delega en `m_CachedServerCharacter.OwnerClientId`.
- **`.Value` sobre SyncVars planos**: `NetLifeState.IsGodMode.Value` y
  `NetworkNameState.Name.Value` → sin `.Value`. (Los `.Value` de `Nullable<>` e `IntVariable`/SO
  son legítimos y se conservaron.)
- **`using` faltantes**: `UIName` → `Unity.BossRoom.Utils`; `UIHealth` →
  `Unity.BossRoom.Gameplay.GameplayObjects` (resolvía `NetworkNameState`/`NetworkHealthState` y el
  mismatch de tipos en `UIStateDisplay`).
- **`isServer`/`isClient` en clases MonoBehaviour** (`ServerBossRoomState`, `ServerCharSelectState`,
  `ClientCharSelectState`): no son NetworkBehaviour → ahora vía `m_NetcodeHooks.isServer/isClient`.
- **`persistentPlayer` posiblemente sin asignar** (short-circuit del `&&` en `ServerBossRoomState`):
  declarada `= null` antes del `out`.
- **`IPUIMediator`**: quitado `using Unity.Networking.Transport`; `m_ConnectionManager.NetworkManager`
  → `if (m_ConnectionManager)`; `NetworkEndpoint.TryParse` → `System.Net.IPAddress.TryParse`.

## Fase 3 — Migración de assets (la pieza grande que NO es código) 🔧

Los prefabs/escenas referencian scripts por GUID, así que esto **requiere el Editor a mano**:

- Sustituir `NetworkObject` → `NetworkIdentity` en cada prefab en red (ConnectionManager,
  NetworkObjectPool, StaticNetworkObjects, proyectiles, personajes, etc.).
- Configurar `BossRoomMirrorNetworkManager`: lista `spawnPrefabs`, `playerPrefab`,
  `KcpTransport`, el `MirrorNetworkAuthenticator`, y escenas offline/online.
- Cambiar componentes `NetworkTransform`/`NetworkAnimator` de NGO por los de Mirror
  (`ClientNetworkTransform` ahora extiende los de Mirror).

Prefabs/escenas a revisar:
- `Assets/Prefabs/ConnectionManager.prefab`
- `Assets/Prefabs/Game/NetworkObjectSpawner.prefab`
- `Assets/Prefabs/Game/StaticNetworkObjects/*.prefab`
- `Assets/Prefabs/NetworkObjectPool.prefab`
- `Assets/Scenes/BossRoom.unity` y `Assets/Scenes/BossRoom/*.unity`

## Fase 4 — Validación en runtime

- Flujo de conexión: host / join / reconexión.
- Carga de escenas vía `ServerChangeScene` (semántica distinta a NGO `SceneManager`).
- Hooks de `[SyncVar]`, RPCs, y spawn/pool con `NetworkServer.Spawn`.
- Tests `HostAndDisconnectTest` y `ConnectionManagementTests` fueron estubeados →
  decidir si reescribir.

---

## Estado de referencia (verificado 2026-06-13)

- Sin `using Unity.Netcode` restantes salvo `Assets/Scripts/Mirror/MIGRATION_REFERENCE.cs`
  (doc, no se compila).
- Librería Mirror vendida en `Assets/Mirror/` con sus asmdefs
  (`Mirror`, `Mirror.Components`, `Mirror.Transports`, `kcp2k`, etc.).
- Shim de compatibilidad `NetcodeHooks` (paquete Utilities) traduce
  `OnStartServer/Client` → eventos `OnNetworkSpawnHook/OnNetworkDespawnHook`.
- Guía de equivalencias de API en `Assets/Scripts/Mirror/MIGRATION_REFERENCE.cs`.
