# PLAN 1.0 — Revisión de VersusD (bugs, seguridad y mejoras)

> Revisión del código de VersusD: un deathmatch PvP construido sobre BossRoom
> portado a **Mirror**, con **servidores dedicados** on-demand y un **Master Server**
> en FastAPI/MongoDB (auth, lobbies, stats y spawn de contenedores Docker).
>
> Fecha: 2026-07-02 · Alcance: `master-server/` (Python) + `Assets/Scripts/` (C#/Unity).
> Estado: propuesta priorizada. Nada de esto está aplicado todavía.

---

## Cómo leer este documento

Cada hallazgo lleva una **severidad** (🔴 Alta / 🟠 Media / 🟢 Baja) y una etiqueta
de tipo (SEG = seguridad, BUG = corrección, MEJ = mejora/calidad). La sección final
es un roadmap por fases con el orden recomendado de ejecución.

---

## 1. Seguridad — Servidor de juego (Unity / C#)

### 🔴 SEG-1 — Crash del servidor dedicado vía `ActionID` fuera de rango (DoS)
**Archivo:** `Gameplay/GameplayObjects/Character/ServerCharacter.cs` → `CmdPlayAction`
(línea ~319) y `GameDataSource.GetActionPrototypeByID` (línea ~89).

`CmdPlayAction` es un `[Command]` que recibe `ActionRequestData` **directamente del
cliente**, y hace:

```csharp
GameDataSource.Instance.GetActionPrototypeByID(data1.ActionID).Config.IsFriendly
```

`GetActionPrototypeByID` indexa una lista sin validar:

```csharp
public Action GetActionPrototypeByID(ActionID index) => m_AllActions[index.ID];
```

Un cliente malicioso (o un paquete corrupto) puede enviar un `ActionID.ID` negativo o
fuera de rango → `ArgumentOutOfRangeException` sin capturar en el hilo del servidor →
**el servidor dedicado headless cae y tumba la partida para todos los conectados**. Es
un DoS remoto trivial y no autenticado más allá de estar en la sala.

**Arreglo:** usar el `TryGetActionPrototypeByID` que ya existe (devuelve `false` en vez
de crashear) en toda ruta que consuma un `ActionID` de origen cliente. Rechazar
silenciosamente la acción si el ID no existe. Considerar validar también que el
personaje realmente posee esa acción.

```csharp
[Command]
public void CmdPlayAction(ActionRequestData data)
{
    if (!GameDataSource.Instance.TryGetActionPrototypeByID(data.ActionID, out var proto))
        return; // ID inválido → ignorar, no crashear
    if (!proto.Config.IsFriendly)
        ActionPlayer.OnGameplayActivity(Action.GameplayActivity.UsingAttackAction);
    var data1 = data;
    PlayAction(ref data1);
}
```

### 🟠 SEG-2 — Falta de validación/saneo de inputs de los `[Command]`
**Archivo:** `ServerCharacter.cs` (`CmdSendCharacterInput`, `CmdSetMovementDirection`).

Los comandos aceptan `Vector3` arbitrarios del cliente. El riesgo de teleport está
parcialmente mitigado porque el destino pasa por `NavMesh.SamplePosition` /
pathfinding, pero conviene validar rangos (NaN/Infinity, distancia máxima por tick)
para evitar movimientos imposibles o valores que envenenen la simulación física.
Mirror ya exige autoridad por defecto en `[Command]`, así que la propiedad de
"solo el dueño llama" está cubierta; el gap es el saneo de contenido.

### 🟢 SEG-3 — El servidor confía en `isDebug` que envía el cliente
**Archivo:** `Mirror/MirrorNetworkAuthenticator.cs`, `HostingState.GetConnectStatus`.

El chequeo de build-type se salta en batch mode (intencional para el DS), y `isDebug`
viene del payload del cliente. Impacto bajo hoy, pero si en el futuro `isDebug`
habilita rutas privilegiadas (god mode ya está `#if`-gated, bien), debe dejar de ser
declarado por el cliente.

---

## 2. Seguridad — Master Server (Python / FastAPI)

### 🔴 SEG-4 — Tráfico en claro (HTTP): credenciales y JWT expuestos
**Archivos:** `MasterServerConfig.cs` (`baseUrl = "http://localhost:8000"`),
`docker-compose.yml` (expone `8000` plano), `MirrorNetworkAuthenticator.masterServerUrl`.

Login/registro mandan usuario+contraseña y reciben un JWT sobre **HTTP sin TLS**.
En producción cualquiera en la ruta de red puede capturar contraseñas y robar tokens
(replay hasta 24 h, `access_token_expire_minutes=1440`). **Requisito de producción:**
terminar TLS delante del master server (reverse proxy: Caddy/nginx/Traefik) y que el
cliente use `https://`. Documentarlo como bloqueante de release público.

### 🔴 SEG-5 — `POST /lobby/dedicated` permite agotamiento de recursos (DoS)
**Archivo:** `app/routers/lobby.py` → `create_dedicated_lobby`.

Cualquier usuario autenticado —incluidos guests, que se crean sin fricción vía
`/auth/guest`— puede pedir el spawn de un contenedor Docker. **No hay rate limit ni
cuota por usuario** en `/lobby/*` (el limitador solo cubre `/auth`). Un atacante:
1. crea guests en masa,
2. llama `/lobby/dedicated` repetidamente,
3. agota el rango de puertos `9000-9999` y/o lanza cientos de contenedores → cae el host.

**Arreglo:** rate-limit por usuario/IP en creación de lobbies + cuota de "N lobbies
dedicados activos por jugador" + tope global de contenedores concurrentes.

### 🟠 SEG-6 — Cuentas guest sin expiración: crecimiento ilimitado de la DB
**Archivos:** `app/routers/auth.py` (`guest`), `app/models.py` (`User`).

Cada `/auth/guest` inserta un `User` permanente que **nunca se purga**. Con IPs
rotativas (el rate limit es por IP) se puede inflar `users` sin límite. Añadir un TTL
index sobre guests (p. ej. borrar guests sin actividad > 24-48 h) o un job de limpieza.

### 🟠 SEG-7 — Rate limiter en memoria no funciona con múltiples workers
**Archivo:** `app/ratelimit.py`.

`RateLimiter` guarda estado en un `dict` por proceso. Con >1 worker de uvicorn/gunicorn
(lo normal en prod) cada worker cuenta por separado → el límite efectivo se multiplica
por el nº de workers y se puede evadir. Migrar a un store compartido (Redis) o fijar
`--workers 1` y documentarlo.

### 🟠 SEG-8 — `/var/run/docker.sock` montado = root en el host si hay RCE
**Archivo:** `docker-compose.yml`.

Montar el socket de Docker da al contenedor del master server control total del daemon
(equivale a root en el host). Es inherente al diseño de spawning on-demand, pero eleva
mucho el impacto de cualquier RCE en el master server. Mitigaciones: usar un proxy de
socket con allow-list de comandos (p. ej. `docker-socket-proxy`), o un runtime rootless,
o mover el spawn a un microservicio aislado con superficie mínima.

### 🟢 SEG-9 — `.env` local con secretos por defecto presente en disco
**Archivos:** `master-server/.env`, `app/config.py`, `app/security.py`.

Bien: `.env` está en `.gitignore` (no se commitea) y `assert_secrets_configured`
aborta el arranque con placeholders cuando `REQUIRE_SECURE_SECRETS=true`; el
`docker-compose.yml` fuerza `true` por defecto. Riesgo residual: el `.env` local trae
`REQUIRE_SECURE_SECRETS=false` y secretos `dev-insecure-change-me`; si ese archivo se
copia a un servidor, arranca inseguro. Acción: documentar claramente y añadir un check
de despliegue que rechace `REQUIRE_SECURE_SECRETS=false` fuera de local.

### 🟢 SEG-10 — `validate-join-token` es un oráculo no autenticado
**Archivo:** `app/routers/servers.py` → `validate_join_token`.

Intencionalmente sin auth (los hosts P2P no tienen server-secret). El token es un
uuid4 no adivinable, de un solo uso y TTL corto, así que el riesgo es bajo. Punto fino:
`consume` lo controla el llamador sin secreto, de modo que quien conozca un token
ajeno podría quemarlo. Considerar exigir el server-secret para `consume=true`, o atar
el consumo al server_id asignado a esa sesión.

---

## 3. Bugs / Corrección

### 🟠 BUG-1 — Al desconectarse, el jugador desaparece del marcador (¿incluido el líder?)
**Archivos:** `ServerBossRoomState.OnServerClientDisconnected`,
`NetworkGameState.RemovePlayer`.

`RemovePlayer` borra la fila del que se va. El comentario dice que es para que un
ausente no "gane", pero en un deathmatch esto significa que **si el líder pierde
conexión un instante (o crashea), pierde todo su score** y es eliminado del resultado
final. Decisión de diseño a confirmar: alternativas son marcarlo como "desconectado"
conservando el score durante una ventana de gracia (reconexión), o congelar su score en
el scoreboard final. Ligado a la lógica de reconexión que ya existe (join token
"peek").

### 🟢 BUG-2 — Verificar el poblado de `KillerClientId` en muertes
**Archivos:** `ServerScoreTracker.OnDeath`, `LifeStateChangedEventMessage` (publisher).

La nueva regla de suicidio (`killerClientId == victimClientId → -3`) depende de que el
mensaje de muerte traiga el `KillerClientId` correcto (el `LastLethalInflicter`). Hay
que confirmar que el publisher rellena killer/victim de forma consistente para PCs que
entran en `Fainted` (no `Dead`) en deathmatch, y que `killerClientId == 0` realmente
significa "entorno/sin atacante" y no colisiona con el connectionId 0 del host. En una
partida hosteada, el host tiene connectionId 0 → un kill legítimo del host podría
confundirse con "muerte por entorno". **Revisar este caso concreto.**

### 🟢 BUG-3 — Doble suscripción a `OnDisconnectedEvent`
**Archivos:** `ConnectionManager` y `ServerBossRoomState` ambos hacen
`NetworkServer.OnDisconnectedEvent += ...`.

Funciona (delegado multicast), pero conviene documentar el orden y asegurar que ambos
se desuscriben en su teardown (ConnectionManager lo hace en `OnDestroy`;
ServerBossRoomState en `OnNetworkDespawn`). Bajo riesgo, solo higiene.

---

## 4. Mejoras / Calidad

- **🟠 MEJ-1 — Tests del master server:** ya existe una suite (`tests/`) con pytest y
  mongomock. Integrarla en CI y ejecutarla en cada cambio. Añadir casos para SEG-5
  (cuota de lobbies dedicados) y SEG-1 (ActionID inválido, si se testea del lado C#).
- **🟠 MEJ-2 — Observabilidad:** el DS y el master server logean con `Debug.Log`/prints;
  añadir logs estructurados y métricas (nº de contenedores vivos, puertos libres,
  tasa de auth fallida) para detectar el abuso de SEG-5/SEG-6.
- **🟢 MEJ-3 — Timeout/errores de red en `MasterServerClient`:** las `UnityWebRequest`
  no fijan `timeout`; una petición colgada bloquea flujos async del DS. Añadir timeouts
  y reintentos con backoff en heartbeats.
- **🟢 MEJ-4 — `AoeActionInput`:** el `TODO` ya se resolvió (input system). Confirmar
  que no quedan otros `TODO`/rutas del input viejo (memoria del proyecto menciona el
  módulo de input legacy que rompía el menú Android en PostGame).
- **🟢 MEJ-5 — Config de JWT:** expiración de 24 h es amplia para un juego; considerar
  tokens más cortos + refresh, o al menos reducir a unas horas.
- **🟢 MEJ-6 — Normalización de `connectionId → ulong`:** el patrón
  `(ulong)(uint)conn.connectionId` se repite por todo el código. Extraer un helper
  único evita errores de conversión y hace explícita la semántica.

---

## 5. Roadmap por fases

### Fase 1 — Correcciones críticas (antes de cualquier despliegue público)
1. **SEG-1** — Blindar `CmdPlayAction` con `TryGetActionPrototypeByID` (crash/DoS). *(rápido, alto impacto)*
2. **SEG-5** — Rate limit + cuota en `/lobby/dedicated` (agotamiento de recursos).
3. **SEG-4** — TLS delante del master server + cliente en `https://` (credenciales en claro).

### Fase 2 — Endurecimiento
4. **SEG-2** — Saneo de inputs de movimiento (NaN/rango).
5. **SEG-6** — TTL/purga de cuentas guest.
6. **SEG-7** — Rate limiter compartido (Redis) o fijar `--workers 1` documentado.
7. **SEG-8** — Aislar el acceso al socket de Docker (proxy con allow-list).
8. **BUG-2** — Verificar killer/victim y el caso `connectionId == 0` del host.

### Fase 3 — Diseño y calidad
9. **BUG-1** — Definir política de score en desconexión/reconexión.
10. **MEJ-1/MEJ-2** — CI con la suite de tests + observabilidad/métricas.
11. **SEG-9/SEG-10, MEJ-3..6** — Higiene: despliegue seguro, timeouts, JWT, helpers.

---

## Estado de implementación (v1.0)

Implementado en este commit. Los 24 tests del master server pasan.

| Ítem | Estado | Detalle |
|---|---|---|
| SEG-1 | ✅ Hecho | `CmdPlayAction` usa `TryGetActionPrototypeByID` (no más crash por ActionID). |
| SEG-2 | ✅ Hecho | Comandos de movimiento rechazan vectores no finitos (`IsFiniteVector`). |
| SEG-3 | 📝 Documentado | Riesgo aceptado y anotado (`SECURITY.md`); no gatear privilegios en `isDebug`. |
| SEG-4 | ✅ Hecho (infra) | `docker-compose.prod.yml` con Caddy/TLS; cliente debe usar `https://`. |
| SEG-5 | ✅ Hecho | Rate limit de lobby + cuota por jugador + tope global de contenedores (+tests). |
| SEG-6 | ✅ Hecho | Índice TTL sobre `guest_expires_at`; guests con expiración (+tests). |
| SEG-7 | ✅ Hecho | Límites configurables + aviso de `--workers 1` en código y docs. |
| SEG-8 | ✅ Hecho (infra) | `docker-socket-proxy` con allow-list en el overlay de prod. |
| SEG-9 | ✅ Hecho | `.env.example` y `SECURITY.md` con checklist; guard ya existente. |
| SEG-10 | 📝 Aceptado | Documentado en `SECURITY.md` (token uuid4 single-use + TTL corto). |
| BUG-1 | 📝 Documentado | Decisión de diseño anotada; fix real (re-key por PlayerId) como follow-up. |
| BUG-2 | ✅ Verificado | El orden de ramas ya maneja el host id 0; comentario aclaratorio añadido. |
| BUG-3 | ✅ Verificado | Ambos handlers se suscriben/desuscriben correctamente; sin cambio. |
| MEJ-1 | ✅ Hecho | Workflow de CI (`.github/workflows/master-server-tests.yml`). |
| MEJ-2 | ✅ Hecho | Logging estructurado de spawns/cuotas en `lobby.py`. |
| MEJ-3 | ✅ Hecho | Timeout de 15s en peticiones del cliente y del authenticator. |
| MEJ-4 | ✅ Verificado | Sin `TODO`/input legacy pendientes en `Scripts/`. |
| MEJ-5 | ✅ Hecho | Default de JWT bajado a 12h en `.env.example` (configurable). |
| MEJ-6 | ⏭️ Diferido | Refactor cosmético del cast `connectionId→ulong`; sin compilador Unity, el barrido no verificado añade más riesgo que valor. Adoptar de forma incremental. |

Leyenda: ✅ hecho/verificado · 📝 documentado (riesgo aceptado o decisión de diseño) · ⏭️ diferido.

---

## Notas de contexto (arquitectura observada)

- **Cliente Unity** ⇄ **Master Server** (FastAPI): auth (JWT), browser de lobbies,
  stats, y spawn on-demand de contenedores de servidor dedicado vía Docker CLI.
- **Servidor dedicado** (headless, batch mode): se registra en el master, hace polling
  de allocation, arranca Mirror, exige **join token** validado. P2P/LAN acepta sin token.
- **Autoridad:** la resolución de daño y el scoring son server-authoritative (correcto);
  la selección de acción llega del cliente y por eso SEG-1 es crítico.
- Coincide con lo registrado en memoria del proyecto (port a Mirror, DS, hooks de
  SyncVar/NetworkAnimator que no disparaban en el DS headless — ya mitigados en el diff
  actual).
