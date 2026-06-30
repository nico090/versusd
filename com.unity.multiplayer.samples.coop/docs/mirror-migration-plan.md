# Plan de migración: NGO + Unity Services → Mirror + Lobby self-hosted + Master Server (FastAPI/MongoDB)

> Documento de planificación para migrar Boss Room desde Netcode for GameObjects (NGO) y
> Unity Multiplayer Services hacia **Mirror**, con un **lobby self-hosted** (VPS o local) y un
> **Master Server** propio basado en **FastAPI + MongoDB**.

## Estado actual (punto de partida)

El proyecto (Boss Room) usa hoy:

- **Netcode for GameObject
s (NGO) 2.4.3** como capa de red.
  - 31 archivos con `NetworkBehaviour`, 34 con `NetworkObject`, 15 con `NetworkVariable`.
  - RPCs nuevos `[Rpc(...)]` en 7 archivos.
- **Unity Services**: Authentication 3.5.1 + Multiplayer Services 1.1.4 (Relay + Lobby + Sessions).
- Máquina de estados de conexión encapsulada en `Assets/Scripts/ConnectionManagement`.
- Fachada de servicios en `Assets/Scripts/UnityServices`.

Ventaja: el acoplamiento a la red está aislado en esas dos carpetas, lo que facilita la migración.

---

## Visión general de la arquitectura objetivo

```
┌─────────────────┐         HTTPS/REST          ┌──────────────────────┐
│  Cliente Unity  │ ──────────────────────────► │   Master Server      │
│   (Mirror)      │   login, lista de salas,    │   FastAPI + MongoDB   │
│                 │   crear/unirse, perfil      │  (auth, lobby, stats) │
└───────┬─────────┘ ◄────────────────────────── └──────────┬───────────┘
        │  KCP/Telepathy (game traffic)                     │ registra/heartbeat
        ▼                                                   ▼
┌─────────────────┐                              ┌──────────────────────┐
│ Game Server     │ ───── heartbeat/registro ──► │  Lista de servidores │
│ (Mirror host    │                              │  activos (MongoDB)   │
│  headless / VPS │                              └──────────────────────┘
│  o host local)  │
└─────────────────┘
```

Tres piezas independientes que se pueden desarrollar y probar por separado:

1. **Capa de red del juego** → Mirror (reemplaza NGO).
2. **Master Server** → FastAPI + MongoDB (reemplaza Authentication + Lobby de Unity).
3. **Servidor de juego dedicado/host** → build headless de Mirror desplegable en VPS o como host local.

---

## Fase 0 — Preparación y decisiones

**Decisiones técnicas a cerrar:**

- **Transport de Mirror:** KCP (UDP, recomendado, bueno para acción) vs Telepathy (TCP).
  Para Boss Room → **KCP**.
- **Modelo de hosting:** "host" cliente-servidor (un jugador hostea) y/o servidor dedicado
  headless en VPS. El plan soporta ambos.
- **NAT/conectividad:** Mirror NO trae Relay como Unity. Para juego sobre internet sin abrir
  puertos necesitarás: servidor dedicado con IP pública (VPS) **o** una solución de
  relay/punch-through. Para LAN/local no hay problema.

**Acciones:**

- Crear rama `feature/mirror-migration`.
- Documentar el flujo de conexión actual (host, client, reconnect) como referencia de paridad.
- Tag del estado actual: `git tag pre-mirror`.

---

## Fase 1 — Master Server (FastAPI + MongoDB)

Se construye primero porque es independiente de Unity y desbloquea el lobby.

**Stack:** Python 3.11+, FastAPI, Uvicorn, Motor (driver async MongoDB) o Beanie (ODM),
Pydantic v2, JWT (python-jose) + passlib para auth.

**Estructura del repo del backend** (nuevo, en `/masterserver` o repo aparte):

```
masterserver/
├── app/
│   ├── main.py
│   ├── config.py
│   ├── db.py                 # conexión Motor/MongoDB
│   ├── models/               # Pydantic + documentos Mongo
│   │   ├── user.py
│   │   ├── session.py        # sala de lobby
│   │   └── server.py         # game server registrado
│   ├── routers/
│   │   ├── auth.py           # /auth/register, /auth/login, /auth/guest
│   │   ├── lobby.py          # /lobby (CRUD salas), join/leave
│   │   └── servers.py        # /servers register/heartbeat/list
│   └── security.py           # JWT, hashing
├── tests/
├── requirements.txt
└── docker-compose.yml        # FastAPI + MongoDB
```

**Endpoints mínimos** (mapean lo que hoy hace Unity Lobby/Auth):

| Endpoint | Reemplaza a | Función |
|---|---|---|
| `POST /auth/guest` / `POST /auth/login` | `AuthenticationServiceFacade` | Devuelve JWT + playerId |
| `GET /lobby` | `MultiplayerServicesFacade.QuerySessions` | Lista salas activas |
| `POST /lobby` | crear sesión/host | Crea sala, devuelve IP:puerto del host |
| `POST /lobby/{id}/join` | unirse a sesión | Reserva slot, devuelve datos de conexión |
| `DELETE /lobby/{id}/leave` | salir | Libera slot |
| `POST /servers/register` + `PUT /servers/{id}/heartbeat` | registro de servidor dedicado | El game server headless se anuncia al master |

**Colecciones MongoDB:** `users`, `lobbies` (con TTL index para limpiar salas muertas),
`servers` (con TTL/heartbeat).

**Entregable Fase 1:** API corriendo en Docker, testeable con `curl`/pytest, sin Unity todavía.

---

## Fase 2 — Capa de red en Unity: NGO → Mirror

La fase más grande. Mapeo de equivalencias:

| NGO | Mirror |
|---|---|
| `NetworkManager` | `NetworkManager` (de Mirror) |
| `NetworkBehaviour` | `NetworkBehaviour` (de Mirror, distinto namespace) |
| `NetworkObject` | `NetworkIdentity` |
| `NetworkVariable<T>` | `SyncVar` (atributo) / `SyncList` |
| `[Rpc(SendTo.Server)]` | `[Command]` |
| `[Rpc(SendTo.ClientsAndHost)]` | `[ClientRpc]` / `[TargetRpc]` |
| `OnNetworkSpawn` / `OnNetworkDespawn` | `OnStartServer/OnStartClient/OnStopServer/...` |
| `IsServer/IsClient/IsHost/IsOwner` | `isServer/isClient/isLocalPlayer/...` |
| `NetworkManager.SpawnManager.InstantiateAndSpawn` | `NetworkServer.Spawn` |
| `UnityTransport` (UTP/Relay) | `KcpTransport` |
| Connection approval / payload | `OnServerConnect` + `NetworkAuthenticator` |

**Pasos:**

1. **Instalar Mirror** (Asset Store/GitHub UPM) y **quitar** `com.unity.netcode.gameobjects`,
   `com.unity.services.multiplayer`, `com.unity.services.authentication` del `manifest.json`.
2. **Reescribir `Assets/Scripts/ConnectionManagement`**: conservar la máquina de estados
   (Offline/StartingHost/Hosting/ClientConnecting/ClientConnected/ClientReconnecting), pero
   `ConnectionMethod` ya no usa `UnityTransport`/Relay sino `KcpTransport` con IP:puerto que
   entrega el Master Server. El `ConnectionPayload` (playerId, playerName) se migra a un
   `NetworkAuthenticator` de Mirror.
3. **Migrar gameplay**: recorrer los archivos con `NetworkObject` / `NetworkVariable` / RPCs.
   Empezar por los core (`ServerCharacter`, spawners, vida/daño): `NetworkVariable<T>` →
   `SyncVar`/`SyncList`, RPCs → `[Command]`/`[ClientRpc]`.
4. **Prefabs/escenas**: cada prefab con `NetworkObject` necesita `NetworkIdentity` de Mirror y
   registrarse en `NetworkManager.spawnPrefabs`. Trabajo manual en editor (los GUID cambian).
5. **Reemplazar `MultiplayerServicesFacade`/`AuthenticationServiceFacade`** por un cliente REST
   (UnityWebRequest) que hable con el Master Server.

**Riesgo principal:** la conversión de prefabs y `NetworkVariable`→`SyncVar` es tediosa y
propensa a errores silenciosos. Recomendación: migrar **vertical** — que UN personaje pueda
hostear + unirse + moverse antes de migrar todo el gameplay.

---

## Fase 3 — Lobby self-hosted (VPS o local)

**Flujo cliente:**

1. Cliente hace login contra Master Server → JWT.
2. Cliente pide `GET /lobby` → muestra lista de salas en UI.
3. **Crear sala (host local):** cliente arranca como Mirror host, registra la sala en el Master
   con su IP pública/local y puerto.
4. **Crear sala (VPS dedicado):** el Master ordena/registra un game server headless; devuelve
   IP:puerto.
5. **Unirse:** cliente recibe IP:puerto del Master y conecta por KCP directo al host/servidor.

**Build de servidor dedicado headless:**

- Target de build "Dedicated Server" de Unity (o build estándar con `-batchmode -nographics`).
- Al arrancar lee config (puerto, master URL) por args/env, hace `POST /servers/register` y
  `heartbeat` periódico.
- Desplegable en VPS vía Docker o systemd.

---

## Fase 4 — Integración, pruebas y despliegue

- **Pruebas locales:** dos instancias del juego + Master Server en `localhost` (Docker compose).
- **Pruebas LAN.**
- **Pruebas VPS:** Master Server + 1 game server dedicado en el VPS; clientes externos conectan.
- **Hardening:** HTTPS/TLS en el Master (reverse proxy nginx/Caddy), rate limiting, validación de
  JWT en cada request, índices TTL para limpiar salas/servidores zombie.
- **CI:** tests de la API (pytest) + tests de Unity (`Unity.BossRoom.Tests.Runtime`).

---

## Estimación de esfuerzo (orientativa)

| Fase | Complejidad | Comentario |
|---|---|---|
| 1 — Master Server | Media | Independiente, rápido de prototipar |
| 2 — NGO→Mirror | **Alta** | El grueso del trabajo; ~30-40 archivos + prefabs |
| 3 — Lobby/servidor dedicado | Media | Depende de Fase 1 y 2 |
| 4 — Integración/VPS | Media | NAT/conectividad es el riesgo real sin Relay |

---

## Advertencias importantes

1. **Pérdida de Relay:** Unity daba conectividad NAT "gratis" vía Relay. Mirror no. Para internet
   necesitarás servidor dedicado en VPS con IP pública o montar tu propio relay/punch-through.
   Es el cambio arquitectónico más impactante.
2. **No es un port mecánico:** los modelos de autoridad de NGO y Mirror difieren en detalles.
   Espera bugs de sincronización a depurar.
3. **Trabajo en prefabs/escenas no automatizable** por script con seguridad — requiere el editor
   de Unity abierto.

---

## Orden de ejecución recomendado

1. **Fase 0** — preparación y rama.
2. **Fase 1** — Master Server (autónomo, desbloquea el resto).
3. **Fase 2 (spike)** — Mirror + flujo mínimo de conexión (host + join + 1 personaje).
4. **Fase 2 (completa)** — migrar todo el gameplay.
5. **Fase 3** — lobby self-hosted + build dedicado.
6. **Fase 4** — integración, VPS y hardening.
