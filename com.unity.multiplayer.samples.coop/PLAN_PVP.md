# Plan: Convertir BossRoom (co-op PvE) en Deathmatch PvP por puntos

## Context

El proyecto es el sample co-op de Unity BossRoom, ya migrado de NGO a **Mirror** y con un
cliente de **Master Server REST** a medio cablear (`MasterServerClient`/`MasterServerFacade`
apuntan a un `baseUrl` cuyo backend **no existe todavía**). El objetivo es convertirlo en un
**deathmatch PvP free-for-all de 5 minutos** con:

- Scoring: matar **jugador = +3**, matar **NPC = +1**, **NPC mata jugador = -3**.
- **Respawn** continuo durante la partida (deathmatch); los **NPCs (imps) siguen spawneando** (PvP + PvE).
- Al agotarse el tiempo: pantalla de fin con título **Ganaste/Perdiste** y **ranking** por puntos.
- **Salas** vía Master Server: crear pública (aparece en lista del lobby) o privada con **password** (solo amigos con la clave).
- **Registro y login** de usuarios + persistencia de **partidas jugadas / ganadas** por jugador.

Decisiones del usuario: backend nuevo en **FastAPI**; muerte = **respawn**; NPCs **siguen** apareciendo; ganador = **free-for-all por puntos**.

El trabajo se divide en tres frentes: **(A) Lógica de gameplay**, **(B) UI**, **(C) Master Server (FastAPI)**.

---

## Estado actual relevante (lo que ya existe y reutilizamos)

- **Daño / muerte**: `ServerCharacter.ReceiveHP(inflicter, HP)` (`Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacter.cs`) ya recibe **quién** causó el daño (`inflicter`) y decide muerte (PC→`Fainted`, NPC→`Dead`). **No** propaga el inflicter al cambio de `LifeState`.
- **Detección de facción (friendly fire)**: `ActionUtils.DetectNearbyEntities/UseSphere` filtra por capas `"PCs"` / `"NPCs"`. `MeleeAction.GetIdealMeleeFoe` y demás acciones ofensivas (`AOEAction`, `DashAttackAction`, `FXProjectileTargetedAction`, `PhysicsProjectile`, `TrampleAction`) usan `isNPC = Config.IsFriendly ^ parent.IsNpc` → hoy **un jugador solo detecta NPCs**.
- **Estados de juego**: `ServerBossRoomState` (spawn de players, `CheckForGameOver`/`BossDefeated` → `PostGame`), `PersistentGameState` (solo `WinState`), `ServerPostGameState` + `NetworkPostGame` (solo sincroniza `WinState`).
- **Muertes notificadas**: `PublishMessageOnLifeChange` publica `LifeStateChangedEventMessage` (sin inflicter).
- **Spawns**: `ServerBossRoomState.SpawnPlayer`, spawners de NPC (`ServerWaveSpawner`, `EnemyPortal`).
- **Master Server cliente**: `MasterServerClient` (HTTP) + `MasterServerFacade` (fachada) ya implementan auth (register/login/guest), lobby (query/create con `is_private`/join con `join_token`/leave/heartbeat) y registro de server. `ConnectionMethodMasterServer` resuelve IP/puerto/token. **Falta**: password en salas y endpoints de stats. **El backend que sirve estas rutas no existe.**
- **UI menú**: `ClientMainMenuState` hoy solo abre Direct-IP; `SessionUIMediator`/`SessionCreationUI` están **stubbeados** (métodos vacíos) — son los huecos donde montar login + browser de salas.
- **CharSelect**: ya funciona (fix reciente de doble-conteo en `ServerCharSelectState.SeatNewPlayer`).

---

## A. Lógica de Gameplay (servidor + sincronización)

### ✅ A1. Habilitar PvP (PC daña PC)
- Introducir un flag de modo **PvP** accesible en runtime (campo en `GameDataSource` o un `GameModeConfig` ScriptableObject, leído server-side).
- En `ActionUtils.DetectNearbyEntities` / `DetectNearbyEntitiesUseSphere`: cuando el atacante es un PC y PvP está activo, **incluir también la capa `"PCs"`** en el mask.
- En `MeleeAction.GetIdealMeleeFoe` y el resto de acciones ofensivas: permitir que un PC seleccione objetivos PC, **excluyendo siempre el propio `netId`** del atacante (evitar auto-daño). Patrón aplicado a los archivos representativos:
  - `Assets/Scripts/Gameplay/Action/ActionUtils.cs`
  - `Assets/Scripts/Gameplay/Action/ConcreteActions/MeleeAction.cs`
  - `.../AOEAction.cs`, `.../DashAttackAction.cs`, `.../FXProjectileTargetedAction.cs`, `.../TrampleAction.cs`
  - `Assets/Scripts/Gameplay/GameplayObjects/Projectiles/PhysicsProjectile.cs`

### ✅ A2. Atribución de muertes (quién mató a quién)
- En `ServerCharacter`: guardar el último `inflicter` que aplicó daño letal y, al cruzar `HitPoints <= 0`, **publicar un evento de muerte con atribución** (víctima + inflicter + si la víctima/inflicter es NPC). Extender `LifeStateChangedEventMessage` (o crear `CharacterDiedMessage`) con `KillerClientId` / `KillerIsNpc` / `VictimClientId` / `VictimIsNpc`.
- Reutilizar la infra de PubSub existente (`IPublisher`/`ISubscriber`, `PublishMessageOnLifeChange`).

### ✅ A3. ScoreTracker + estado de partida en red
- **`ServerScoreTracker`** (server-only): se suscribe al evento de muerte y aplica:
  - víctima PC + killer PC → killer **+3**
  - víctima NPC + killer PC → killer **+1**
  - víctima PC + killer NPC (o sin killer) → víctima **-3**
  - Mantiene `Dictionary<clientId, score>`.
- **`NetworkGameState`** (NetworkBehaviour, vive en la escena de juego): `SyncVar` con **tiempo restante** (cuenta regresiva desde 300 s) y una **`SyncList<ScoreEntry>`** (clientId, playerName, playerNumber, score) para que los clientes muestren el marcador en vivo. Patrón calcado de `NetworkCharSelection.sessionPlayers` (SyncList) y `NetworkPostGame` (SyncVar+hook).

### ✅ A4. Timer de 5 min, respawn y fin de partida
- En `ServerBossRoomState` (o un nuevo `ServerDeathmatchState` paralelo para no romper el modo co-op): arrancar el timer al iniciar; al llegar a 0 → congelar input, calcular **ranking ordenado** por score y transicionar a `PostGame`.
- **Respawn**: cuando un PC entra en `Fainted`, en vez de `CheckForGameOver`, programar una corrutina que lo revive/reposiciona en un spawn point tras N segundos (reutilizar `SpawnPlayer`/`Revive` y `m_PlayerSpawnPoints`). Eliminar/parametrizar la condición de "todos fainted = derrota".
- Mantener `ServerWaveSpawner`/`EnemyPortal` activos durante la partida.

### ✅ A5. Resultados → PostGame y persistencia
- Extender `PersistentGameState` para llevar el **scoreboard final ordenado** (no solo `WinState`).
- `NetworkPostGame`: añadir `SyncList<ScoreEntry>` final + el `WinState` por jugador (gana el #1).
- En `ServerPostGameState.OnNetworkSpawn`: **reportar el resultado al Master Server** (POST stats: cada jugador suma 1 partida jugada; el #1 suma 1 ganada) usando `MasterServerFacade`.

---

## B. UI

### ✅ B1. Login / Registro
- Nueva pantalla (en `Startup`/`MainMenu`) con campos usuario/clave y botones **Registrarse / Iniciar sesión / Invitado**, cableada a `MasterServerFacade.RegisterAsync/LoginAsync/LoginAnonymouslyAsync` (ya existen).
- Bloquear el acceso al lobby hasta autenticarse; mostrar el `Username`.

### ✅ B2. Browser de salas + creación
- Rellenar los stubs de `SessionUIMediator` / `SessionCreationUI` (`Assets/Scripts/Gameplay/UI/Session/`):
  - **Lista de salas públicas**: `QueryLobbiesAsync()` → lista con nombre, jugadores `current/max`, candado si es privada. Botón **Unirse** → `JoinLobbyAsync` → `ConnectionMethodMasterServer` → conectar por KCP.
  - **Crear sala**: nombre + toggle **Privada** + campo **password** (visible si privada) + maxPlayers. Llama a `CreateLobbyAsync(...)` extendido con password.
  - Unirse a privada: pedir password y enviarla en el join.
- Patrón visual de referencia disponible en `Assets/Mirror/Examples/EdgegapLobby/Scripts/UILobbyList/UILobbyEntry/UILobbyCreate.cs`.

### ✅ B3. HUD de partida (timer + marcador en vivo)
- Widget que lee `NetworkGameState`: **cronómetro** (mm:ss) y **tabla de puntajes** ordenada que se actualiza con el callback de la `SyncList`. Reutilizar patrón de `ClientCharSelectState.OnSessionPlayerStateChanged` (suscripción a `SyncList.Callback`).

### ✅ B4. Pantalla de fin de partida
- En el cliente de PostGame: título grande **"¡GANASTE!" / "Perdiste"** según la posición del jugador local, y **tabla de ranking final** (posición, nombre, puntos). Lee el `SyncList<ScoreEntry>` de `NetworkPostGame`. Reutilizar el flujo existente de `NetworkPostGame.OnWinStateChangedEvent`.

---

## C. Master Server (FastAPI) — proyecto nuevo

Servicio REST (carpeta `master-server/` en el repo, o repo aparte) con persistencia (SQLite para dev / Postgres para prod) y JWT. Debe cumplir el contrato que el cliente **ya** consume (ver `MasterServerClient`) más las extensiones:

- **Auth**: `POST /auth/register`, `POST /auth/login`, `POST /auth/guest` → `{access_token, player_id, username}` (JWT). Hash de password (bcrypt).
- **Lobby**:
  - `GET /lobby` → lista de salas **públicas** (incluye `current_players`, `is_private`).
  - `POST /lobby` → crear; body extendido con **`password`** (opcional) y `is_private`. Guardar hash de la password.
  - `POST /lobby/{id}/join` → si es privada, **validar password**; devuelve `{host_ip, host_port, join_token}`. Generar y registrar `join_token`.
  - `DELETE /lobby/{id}/leave`, `POST /lobby/{id}/heartbeat` (TTL para limpiar salas muertas).
  - `POST /servers/validate-join-token` (lo valida el game server en el handshake).
- **Stats** (nuevo):
  - `POST /stats/match-result` → registra resultado: por cada jugador `games_played += 1`; al ganador `games_won += 1`.
  - `GET /stats/{player_id}` → `{games_played, games_won}` para mostrar en perfil/lobby.
- **Modelo de datos**: `users(player_id, username, password_hash, games_played, games_won)`, `lobbies(session_id, name, host_ip, host_port, max_players, is_private, password_hash, last_heartbeat)`.

### Extensiones del cliente para C
- `MasterServerModels.cs`: añadir `password` a `CreateLobbyRequest`; añadir request/response de stats.
- `MasterServerClient.cs` / `MasterServerFacade.cs`: métodos `JoinLobbyAsync(sessionId, password)`, `SubmitMatchResultAsync(...)`, `GetStatsAsync(playerId)`.
- Cablear el `join_token` en la validación server-side del handshake (`HostingState.HandleApproval` / `BossRoomMirrorNetworkManager`) — hoy el token viaja en `ConnectionPayload` pero no se valida.

---

## Orden de implementación sugerido

1. **C** (FastAPI auth + lobby + stats) — desbloquea todo lo online; testeable con curl independiente del juego.
2. **A1–A2** (PvP + atribución de kills) — núcleo jugable.
3. **A3–A4** (score tracker, timer, respawn, fin por tiempo).
4. **A5 + B4** (resultados, PostGame con ranking, reporte de stats).
5. **B1–B3** (login, browser de salas, HUD en vivo).

Cada fase es compilable/testeable de forma incremental.

---

## Verificación

- **Backend**: levantar FastAPI (`uvicorn`), probar con `curl`/Swagger: registro, login, crear sala pública/privada, listar, join con/sin password correcta, submit de stats, get stats.
- **PvP local**: dos instancias (host + cliente, vía Direct-IP o Master Server). Confirmar que un jugador puede dañar y matar a otro, que el marcador suma **+3**, que matar un imp suma **+1**, y que morir ante un imp resta **-3**.
- **Respawn**: verificar que el jugador muerto reaparece y sigue puntuando.
- **Timer/Fin**: reducir temporalmente el límite a ~30 s; confirmar transición a PostGame al expirar, título Ganaste/Perdiste correcto según posición, y ranking ordenado.
- **Salas**: crear sala privada con password desde una instancia; desde otra, listar (no debe revelar la clave), unirse con password incorrecta (rechazo) y correcta (éxito).
- **Stats**: tras una partida, `GET /stats/{player_id}` refleja +1 partida jugada a todos y +1 ganada al #1.
- **No-regresión**: el flujo Direct-IP y CharSelect siguen funcionando (incluido el fix de doble-conteo).
