# Revisión online (cliente-servidor) — cambios y pasos de Editor

Resumen del pase de revisión de la estructura online (Mirror dedicated-server). Detalle completo en
`~/.claude/plans/revisemos-la-estructura-de-reflective-moth.md`.

## Veredicto

La estructura cliente-servidor está **sana**. Los grandes peligros del puerto NGO→Mirror ya estaban
resueltos en el código (replicación de escenas aditivas, `loadOnNetworkSpawn`, replay en `OnServerReady`,
authenticator auto-attach, identificación por `PlayerId`, guards de re-entrada, validación de spawnPrefabs).

## Cambios ya aplicados (código + escenas)

1. **Loading-progress multi-cliente implementado** (antes era un stub: las barras de los otros jugadores
   nunca aparecían). Archivos:
   - `Packages/.../SceneManagement/NetworkedLoadingProgressTracker.cs` → ahora `NetworkBehaviour` con
     `[SyncVar]` de progreso; el cliente dueño lo empuja por `Command`, el servidor lo difunde a todos.
   - `Packages/.../SceneManagement/LoadingProgressManager.cs` → singleton `Instance`, registro de trackers
     y spawn/despawn server-side por conexión.
   - `Packages/.../SceneManagement/ClientLoadingScreen.cs` → identifica el tracker propio por
     `isOwned` (en vez de comparar connectionId/netId, que fallaba en clientes dedicados).
   - `Assets/Scripts/Mirror/BossRoomMirrorNetworkManager.cs` → spawnea el tracker en `OnServerReady` y lo
     despawnea en `OnServerDisconnect`.
   - El prefab `Assets/Prefabs/LoadingProgressTracker.prefab` ya tenía `NetworkIdentity` + el componente,
     ya está en `NetworkManager.spawnPrefabs` y ya está referenciado por `LoadingProgressManager` en
     `SceneLoader.prefab` — **no requiere acción de Editor**.

2. **Cruft NGO eliminado**: quitadas las 115 entradas muertas `GlobalObjectIdHash` /
   `InScenePlacedSourceGlobalObjectIdHash` de `m_Modifications` en las 8 escenas (los `sceneId` de Mirror
   quedaron intactos). Backup en `%TEMP%/versusd-scene-backup/` por si hace falta revertir.

## Pendiente en el Editor de Unity (no hacible de forma segura desde fuera)

- [ ] **Borrar el PrefabInstance corrupto de `MainMenu.unity`**: aparece como "Missing Prefab" en la
      jerarquía (era `PrefabInstance &9111111111`, apuntaba a un prefab inexistente + script faltante con
      guids placeholder). Seleccionarlo y borrarlo; guardar la escena.
- [ ] **Verificar `ServerBossRoomState.m_PlayerSpawnPoints`** en `BossRoom.unity`: que el array esté
      poblado (si está vacío, en build DS release los `Assert` se eliminan y los jugadores spawnean en el
      origen).
- [ ] **Correr `Boss Room/Mirror Audit/Run Full Audit`** y resolver lo que reporte.
- [ ] **Recompilar**: confirmar que el Weaver de Mirror no se queja del nuevo SyncVar.

## Validación end-to-end (cuando quieras probarlo en vivo)

Requiere rebuild + redeploy del DS (ver memoria `versusd-architecture`):

```
docker build -f master-server/Dockerfile.gameserver -t versused-game-server:latest com.unity.multiplayer.samples.coop/Builds
docker rm -f gs-9000
```

Luego: host + un cliente remoto/dedicado; al pasar CharSelect→BossRoom cada lado debe ver su barra local
**y** la barra del otro jugador avanzando.
