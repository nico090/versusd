# VersusD — Próximos pasos: cerrar el relay LRM + desplegar

> Documento de retoma. Estado al 2026-07-12. Detalle técnico completo en la memoria
> `relay-lrm-migration` y el plan `~/.claude/plans/shiny-crunching-widget.md`.

## Qué hay pendiente (resumen)

Hay **dos tandas de cambios** esperando build/deploy:

- **A) Fixes de combate/spawns** (ya cerrados en código): bolt del mago (velocidad 64→20,
  skillshot al cursor, splash en el punto de impacto) + spawns más suaves (proximidad 60→35,
  detección 60→30, `SpawnsPerWave` 2→1, `TimeBetweenWaves` 5→8, stagger inicial random).
  - Server-side (splash, tuning, stagger) → necesita **rebuild del DS**.
  - Client-side (skillshot input) → va en el **build del cliente**.
- **B) Relay LRM** (código escrito, **sin compilar en Editor**): host en la PC del user, el VPS
  reenvía (sin port-forwarding). Master server + Docker ya verificados; falta el gate del Editor.

---

## Paso 0 — GATE en el Editor (bloqueante, va PRIMERO)

La Fase 3 del relay no se compiló en Unity. Antes de cualquier deploy:

- [ ] Abrir el proyecto en Unity y **confirmar que compila** (import de LRM en
      `Assets/ThirdParty/LightReflectiveMirror/` + código nuevo en ConnectionManagement / MasterServer / UI).
      Si salta algún error de firmas LRM↔Mirror, anotarlo y avisar.
- [ ] **Wirear el componente** `LightReflectiveMirrorTransport` en el prefab del **NetworkManager**:
  - `clientToServerTransport` = el `KcpTransport` que ya está en el objeto
  - `serverIP` = IP pública del VPS · `serverPort` = `7777`
  - `authenticationKey` = **igual** a `RELAY_AUTH_KEY` del `.env`
  - `connectOnAwake` = `true`
  - (opcional NAT punch: agregar `LRMDirectConnectModule` y `useNATPunch = true`)
  - ⚠️ El código usa el `serverIP` **del componente**, NO `MasterServerConfig.relayHost/relayPort`
    (esos quedaron sin uso). El valor que manda es el del componente.
- [ ] En `MasterServerConfig`: setear `enableDedicatedServers` (true = se ofrece dedicado en Create Room;
      false = todo se crea por relay y el toggle se oculta).

---

## Paso 1 — Desplegar (son 4 componentes, no uno)

Desde `master-server/`. Asegurar primero en `.env`:
- [ ] `RELAY_AUTH_KEY=<algo-real>` (mismo valor que el `authenticationKey` del componente)

### 1. Master server (cambió código Python: endpoint `/lobby/relay`, modelos)
```bash
docker compose up -d --build master-server
```

### 2. Relay (container nuevo — server .NET de LRM, con su propio Dockerfile)
```bash
# clonar LRM si no está
git clone https://github.com/Derek-R-S/Light-Reflective-Mirror.git
docker build -t versused-relay:latest Light-Reflective-Mirror
docker compose up -d relay
```
- [ ] Abrir en el firewall del VPS: **`7777/udp`** (transporte) y **`7776/udp`** (NAT punch).

### 3. Dedicated server (fixes server-side de combate/spawns)
> Solo relevante si se sigue usando el modo dedicado.
```bash
# tras un build Dedicated Server / Linux del cliente a com.unity.multiplayer.samples.coop/Builds
docker build -f master-server/Dockerfile.gameserver -t versused-game-server:latest com.unity.multiplayer.samples.coop/Builds
docker rm -f gs-9000   # que el master respawnee la imagen nueva
```

### 4. Cliente (skillshot del mago + UI/conexión relay + LRM wireado)
- [ ] Build/distribuir el cliente (o correr desde el Editor para probar).

> Prod con TLS: `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`

---

## Paso 2 — Probar

- [ ] **Combate:** el bolt del mago se ve volar y va hacia el cursor; el splash pega aunque tires al piso
      cerca de un enemigo. Al entrar a una sala no aparece un enjambre de mobs.
- [ ] **Relay (prueba clave):** crear sala → que un cliente entre **desde otra red sin port-forwarding**
      (ej. celular en datos móviles). Confirmar que se ven, se mueven y se pegan.
- [ ] **Listado:** la sala relay aparece en "Find & Join" (si no es privada).
- [ ] **Dedicado** (si `enableDedicatedServers=true`): sigue funcionando.
- [ ] Master server: `.venv-test/Scripts/python.exe -m pytest` sigue verde (27 tests).

---

## Cabos sueltos / riesgos conocidos

- **Fase 3 sin compilar en Editor** — puede haber ajustes de firmas al abrir (API `Transport` de
  Mirror es estable y los 4 GUIDs del `LRM.asmdef` matchean, buena señal).
- **`MasterServerConfig.relayHost/relayPort`** quedaron sin uso (el componente manda). Se pueden borrar
  o cablear si se quiere config sin tocar el prefab.
- **`SessionUIMediator.GetLocalIpAddress`** quedó como código muerto (inofensivo).
- **Join relay muy rápido al arrancar:** con `connectOnAwake=true` el cliente ya está conectado al
  relay al momento de unirse; si alguien clickea Join en el primer segundo podría necesitar reintentar.
- **Trade-offs del relay (por diseño):** host = PC del user (peor anti-cheat; si el host cierra, muere la
  partida) y el VPS gasta ancho de banda (pero barato en CPU vs. container dedicado).

## Cómo desactivar el relay / volver a solo-dedicado

- El endpoint `/lobby/dedicated` y todo el flujo dedicado siguen intactos.
- Poner `MasterServerConfig.enableDedicatedServers = true` para ofrecer dedicado en la UI.
- Para forzar todo por relay: `enableDedicatedServers = false` (oculta el toggle).
- El relay se puede apagar con `docker compose stop relay` (las salas dedicadas siguen andando).
