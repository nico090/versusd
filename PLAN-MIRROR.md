# Plan — Dejar el flujo de Unity Mirror sólido (P2P + dedicado)

Limpieza/endurecimiento del port NGO→Mirror. Objetivo: que funcionen bien **ambos**
modos (cliente Editor → game-server Linux dedicado, y P2P host+cliente).

> **Regla de oro del deploy:** cada vez que cambia código C# o escenas, rebuildear el
> server Linux **y** la imagen Docker. Cliente (Editor) y DS tienen que salir del mismo
> build o las `SyncList`/escenas se desincronizan. Esto aplica especialmente a cambios
> en cualquier struct sincronizado (ej. `ScoreEntry`).

---

## Hallazgos (estado del port)

El grueso ya está resuelto: auth por join-token (`MirrorNetworkAuthenticator`), bridge de
lifecycle (`BossRoomMirrorNetworkManager`), máquina de estados de conexión intacta, y
shims tipo-NGO (`NetcodeHooks`, `NetworkIdentityUtils.FindByNetId`). Lo que quedaba eran
residuos y dos huecos de flujo:

1. **[BUG] Win/loss del PostGame por ClientId.** `PostGameUI` se identificaba con
   `NetworkConnection.LocalConnectionId` (= 0 en todo cliente remoto) → en cliente
   dedicado el resultado salía mal. *(Arreglado — Fase 3.)*
2. **[BUG] Reconexión al dedicado con token quemado.** La reconexión reusaba el
   join-token single-use ya consumido; un DS (`m_RequireToken=true`) la rechazaba siempre.
   *(Arreglado — Fase 4.)*
3. **Stubs muertos de NGO.** `NetworkLatencyWarning` y `NetworkSimulatorUIMediator` son
   `MonoBehaviour` vacíos. *(Pendiente — Editor.)*
4. **`spawnPrefabs` solo se auto-registra en Editor.** `AutoRegisterSpawnablePrefabs()`
   está bajo `#if UNITY_EDITOR`; el build del DS depende 100% de la lista serializada.
   Riesgo de "Could not resolve prefab". *(Pendiente — Editor.)*
5. **Identidades vestigiales.** En escenas son property-mods muertos (inocuos); confirmar
   los prefabs fuente requiere abrirlos. *(Pendiente — Editor.)*
6. **SyncVars gateados con `#if`.** *(Auditado — PASS, ninguno.)*
7. **[BUG] El mapa (dungeon) no carga en cliente remoto/dedicado.** `ServerAdditiveSceneLoader`
   cargaba la sub-escena con `SceneManager.LoadSceneAsync(..., Additive)` solo en el server;
   Mirror no replica additive loads como NGO → host P2P ve el mapa, cliente remoto lo ve vacío.
   *(Arreglado — broadcast de `SceneMessage` Load/UnloadAdditive a clientes ready.)* Limitación:
   late-joiners no reciben el additive ya cargado.

---

## Fase 0 — Baseline reproducible  ⏳ requiere Editor + Docker
- `docker compose up -d` (master) + build DS Linux + rebuild imagen.
- Documentar el happy path actual de P2P y de dedicado con logs limpios.
- Sirve para saber qué anda antes de seguir tocando.

## Fase 1 — Barrido de residuos NGO  ◑ parcial
- ✅ **SyncVars gateados: PASS.** Ningún `SyncVar`/`SyncList` queda tras `#if`. Los `#if`
  de `ServerCharacter`/`SwitchedDoor` son uso de cheat / campos no-sincronizados.
- ✅ **Herramienta de auditoría:** `Boss Room/Mirror Audit/Run Full Audit`
  (`Scripts/Editor/MirrorPortAudit.cs`) escanea escenas+prefabs del proyecto (salta
  `Assets/Mirror/`) y reporta sin tocar nada: identidades vestigiales (`NetworkIdentity`
  sin `NetworkBehaviour`), scripts faltantes, stubs muertos y cobertura de `spawnPrefabs`.
- ⏳ **Aplicar fixes en Editor:** correr la auditoría y, por cada hallazgo, quitar el
  componente/identidad en el Editor (con undo). Refs conocidas de stubs:
  `NetworkLatencyWarning` → `Prefabs/NetworkOverlay.prefab`;
  `NetworkSimulatorUIMediator` → `Prefabs/UI/NetworkSimulator.prefab`. Tras quitar el
  componente, borrar `NetworkLatencyWarning.cs` / `NetworkSimulatorUIMediator.cs`.

## Fase 2 — Robustez de `spawnPrefabs` en build  ◑ parcial
- ✅ **Validación en runtime:** `BossRoomMirrorNetworkManager.ValidateSpawnPrefabs()` corre
  en `OnStartServer` (también en el build del DS, no solo Editor) y loguea un `LogError` por
  cada nombre de `k_SpawnablePrefabNames` ausente de la lista serializada `spawnPrefabs`.
  Convierte el "Could not resolve prefab" opaco en un error explícito al arrancar el server.
- ⏳ **Editor:** confirmar que la lista `spawnPrefabs` del NetworkManager (y `playerPrefab`)
  esté **completa en el asset/escena Startup**; el auto-registro sigue siendo `#if UNITY_EDITOR`.

## Fase 3 — Identidad ClientId vs PlayerId  ✅ hecho
Regla: cliente machea por **PlayerId**, servidor por **connectionId**.
- `NetworkGameState.cs` — `PlayerId` agregado a `ScoreEntry` (+ `Equals`) y a `RegisterPlayer`.
- `ServerBossRoomState.cs:218` — server pasa `SessionManager.GetPlayerId(clientId)`.
- `PostGameUI.cs` — compara `sorted[0].PlayerId == ClientAuthPayload.Current.PlayerId`.
- Resto del código auditado: server usa connectionId correctamente (CharSelect, scoring).

## Fase 4 — Reconexión al dedicado (re-pedir join-token)  ✅ hecho
- `ConnectionManager.cs` — expone `MasterServerFacade` (resuelto opcional vía try/catch,
  porque su registración es condicional a tener `MasterServerConfig`).
- `ConnectionMethod.cs` — `SetupClientReconnectionAsync` re-pide token fresco vía
  `JoinLobbyAsync(sessionId)` y refresca token + endpoint antes del reintento. P2P/LAN sin cambios.
- `OfflineState.cs:44` — pasa el facade al `ConnectionMethodIP`.
- **Limitación conocida:** lobbies privados con password no se re-piden (no guardamos el
  password); habría que threadearlo si se necesita.

## Fase 5 — Verificación E2E  ⏳ requiere Editor + Docker
Matriz en ambos modos (P2P y dedicado): conexión, CharSelect, transición a BossRoom,
desconexión, **reconexión**, fin de partida (win/loss correcto por jugador).

---

## Próximo paso sugerido
1. Abrir Unity → `Boss Room/Mirror Audit/Run Full Audit`, revisar la Consola y aplicar los
   fixes (quitar identidades vestigiales/stubs, completar `spawnPrefabs`). Cierra Fases 1 y 2.
2. Rebuild cliente+DS y correr Fase 0/5 para verificar Fases 3, 4 y el guard de spawnPrefabs
   en vivo. Matriz E2E en P2P y dedicado.
