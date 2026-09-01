# Plan de implementación: Deathmatch PvPvE — Boss Room modificado

## 1. Resumen del modo

Partida de **5 minutos**, free-for-all, gana el que más puntos tiene al terminar.

| Evento | Momento | Puntos |
|---|---|---|
| Aparece el boss en el centro del mapa | Inicio de partida (5:00 restantes) | — |
| Matar un imp / NPC menor | Toda la partida | **1 punto** |
| Matar a otro jugador | Fase normal (5:00 → 2:00) | **5 puntos** |
| Matar a otro jugador | Fase final (últimos 2:00) | **10 puntos** (doble) |
| Dar el golpe final al boss | Cuando ocurra | **20 puntos** |

Nota de diseño: el boss vale 4 kills de jugador. Como aparece desde el inicio, genera una decisión inmediata: ir por él temprano (riesgo de que te caigan por atrás mientras lo peleás) o dejarlo para robar el last-hit. Si en las pruebas ves que todos lo ignoran, subilo a 25–30; si todos se amontonan ahí, bajalo a 15.

---

## 2. Arquitectura general

Todo el estado de la partida es **server-authoritative**. Los clientes solo leen NetworkVariables y muestran UI.

Componentes nuevos (todos en el assembly de Gameplay/Server):

```
MatchTimerManager      → tiempo restante, fases de partida
ScoreManager           → puntaje por jugador, tabla de posiciones
BossSpawnManager       → spawn del boss al inicio, en el centro
KillAttributionSystem  → quién mató a quién/qué, asignación de puntos
```

---

## 3. Timer y fases de partida

### 3.1 NetworkVariables

```csharp
public class MatchTimerManager : NetworkBehaviour
{
    public NetworkVariable<float> TimeRemaining = new(300f); // 5 min
    public NetworkVariable<MatchPhase> CurrentPhase = new(MatchPhase.Normal);

    public enum MatchPhase { PreGame, Normal, DoubleKills, Ended }
}
```

### 3.2 Lógica server-side

- En `OnNetworkSpawn`, si `IsServer`: arrancar el countdown.
- Actualizar `TimeRemaining` una vez por segundo (no cada frame — evitás tráfico de red innecesario; el cliente interpola localmente el segundo intermedio).
- Al llegar a **120 segundos restantes** → `CurrentPhase = DoubleKills`. Disparar un ClientRpc para efecto visual/sonoro ("¡Kills dobles!").
- Al llegar a **0** → `CurrentPhase = Ended`, congelar input de todos los jugadores (podés reusar el mecanismo de stun/"Stunned" del `ServerCharacter` para congelarlos limpio), mostrar tabla final.

### 3.3 UI cliente

- Suscribirse a `TimeRemaining.OnValueChanged` y `CurrentPhase.OnValueChanged`.
- Timer visible siempre. En fase DoubleKills, cambiarle el color (rojo/dorado) y agregar el multiplicador "x2" al lado.

---

## 4. Sistema de puntaje

### 4.1 Estructura

Usá una `NetworkList<PlayerScore>` en el ScoreManager:

```csharp
public struct PlayerScore : INetworkSerializable, IEquatable<PlayerScore>
{
    public ulong ClientId;
    public int Score;
    public int Kills;       // solo jugadores
    public int NpcKills;    // imps
    public bool KilledBoss;
    // implementar NetworkSerialize e IEquatable
}
```

### 4.2 Atribución de kills

Boss Room ya trackea el atacante en `ReceiveHitPoints` / el sistema de daño de `ServerCharacter` (el parámetro `inflicter`). Enganchate ahí:

- En el punto donde la vida llega a 0 (el estado `LifeState` pasa a `Fainted`/`Dead`), capturar quién fue el último `inflicter`.
- Clasificar la víctima:
  - Es jugador → 5 pts (o 10 si `CurrentPhase == DoubleKills`)
  - Es imp/NPC menor → 1 pt
  - Es el boss → 20 pts
- Sumar al `PlayerScore` del inflicter.

**Caso borde a resolver:** muertes sin atacante (caída, daño de entorno si lo hubiera) → no dan puntos a nadie. Muerte por NPC → tampoco. Guardá el último jugador que te pegó en los últimos ~3 segundos si querés dar "kill asistida" cuando un imp remata a alguien que vos dejaste al borde (opcional, fase 2).

### 4.3 UI

- Tabla de posiciones (tecla Tab o siempre visible en esquina): nombre + puntos, ordenada.
- Kill feed: "X mató a Y (+10)" — un ClientRpc con los datos, el cliente lo muestra 3-4 segundos.

---

## 5. Boss al inicio de partida

### 5.1 Spawn

- El `BossSpawnManager` (server) spawnea el prefab del boss en el punto central del mapa apenas arranca la fase Normal.
- Marcar la posición con un Transform vacío en la escena ("BossSpawnPoint") en la sala central.
- El boss usa su IA original (`AIBrain` / los action states que ya trae), con una diferencia: **su lista de objetivos incluye a todos los jugadores por igual** — esto ya funciona así en el original, no deberías tener que tocar nada.

### 5.2 Ajustes al boss para PvPvE

El boss original está balanceado para 8 jugadores coordinados pegándole a la vez. Acá le van a pegar de a uno o dos mientras se cuidan las espaldas:

- **Vida: bajarla a ~40-50%** del valor original (está en su CharacterClass ScriptableObject, campo BaseHP). Objetivo: que un jugador solo tarde ~30-40 segundos en bajarlo, tiempo suficiente para que sea riesgoso.
- **Daño: bajarlo ~30%** para que no borre a un jugador con un combo (recordá que acá nadie te revive... el revive no existe más).
- Si el boss tiene fase de "trampled"/vulnerable (el mecanismo de romperle la armadura), evaluá si lo dejás: agrega profundidad, pero complica la pelea en solitario. Recomendación: dejarlo pero acortar el tiempo necesario.
- **No respawnea.** Un boss por partida. El anuncio de su muerte va por kill feed global con fanfarria.

### 5.3 Imps

- Mantener 2-3 grupos chicos de imps en salas laterales (no en el centro, que es del boss).
- Respawn de imps cada ~30-45 segundos para que la fuente de "1 punto" nunca se seque del todo — le da algo que hacer al que viene perdiendo las peleas directas.
- Bajarles la vida si hace falta: matarlos tiene que ser rápido (2-3 golpes), son puntos de consolación, no un desafío.

---

## 6. Respawn de jugadores

- Al morir: pantalla de muerte 5 segundos → respawn automático.
- **Elección de spawn point server-side**: de la lista de spawn points, elegir el que maximice la distancia mínima a los demás jugadores vivos (loop simple sobre posiciones, no hace falta nada sofisticado).
- **Invulnerabilidad de 2 segundos** post-respawn que se corta si atacás. Evita el spawn-kill sin permitir abusos.
- El estado `Fainted` original (esperando revive) se elimina del flujo: muerte directa.

---

## 7. Balance PvP de las clases

Los stats viven en los **CharacterClass ScriptableObjects** (carpeta GameData) y los datos de cada habilidad en sus **Action configs** (daño, cooldown, rango, duración de efectos). Todo lo de abajo se ajusta ahí, sin tocar código.

Principio general: en PvE el balance es "daño por segundo contra bolsas de vida"; en PvP lo que rompe el juego es el **control** (stuns, slows) y el **burst** (matar antes de que reaccionen). Por eso los nerfs van ahí y no tanto al daño plano.

### 7.1 Tank

El problema: mucha vida + stun + daño decente = 1v1 imbatible.

- **Vida: -25 a -30%.** Sigue siendo el más gordo, pero matable en el tiempo de una pelea.
- **Stun (Shield Bash o equivalente): duración a la mitad** (si dura 2s, dejarlo en 1s) y **cooldown +50%**. Un stun corto sigue sirviendo para conectar un combo, pero no para encadenar.
- Identidad en este modo: el mejor peleando **contra el boss** (aguanta sus golpes) y disputando el centro. Su debilidad: lo kitean el Archer y el Mage.

### 7.2 Archer

El problema: en PvP el daño a distancia sin castigo domina, sobre todo el tiro cargado.

- **Tiro cargado: daño -20-25%** o tiempo de carga +30%. Que sea un premio por apuntar, no un one-shot.
- **Ataque básico: sin tocar.** Su chip damage constante está bien.
- **Vida: la más baja del juego** (si no lo es ya, bajarla ~10%). Es un cristal: si lo agarran, muere.
- Identidad: castiga desde afuera al que pelea con el boss o al que persigue a otro. El "buitre" del modo.

### 7.3 Mage

- **AoE: daño -15%** pero sin tocar el radio. El área es su rol y con imps + peleas amontonadas en el centro va a brillar solo.
- Si tiene proyectil con algún efecto de control, revisar que no permita perma-kiteo (cooldown +25% si hace falta).
- Identidad: el mejor farmeando imps (1 pt cada uno se acumula) y limpiando peleas grupales. Débil en 1v1 seco.

### 7.4 Rogue

El problema: stealth en PvP es frustrante si permite escapar gratis de toda pelea.

- **Stealth: al atacar desde stealth, cooldown completo antes de re-entrar** (que no pueda entrar y salir en la misma pelea). Si la duración es larga, recortarla ~30%.
- **Daño de dash/backstab: sin tocar o +10%.** Es su única moneda: si no mata rápido, no tiene nada.
- Identidad: el ladrón de kills — remata al que quedó débil peleando con el boss o con otro, y el mejor candidato a robar el last-hit del boss.

### 7.5 Regla de hierro para iterar

Cambiá **un stat por vez, 10-15% por vez**, y jugá 3-4 partidas antes del siguiente cambio. El instinto de tocar cinco cosas juntas te deja sin saber qué causó qué. Llevá una planilla simple: fecha, cambio, resultado observado.

Métrica objetivo: en un grupo parejo, ninguna clase debería ganar más del ~35% de las partidas de forma sostenida, y ninguna menos del ~15%.

---

## 8. Orden de implementación sugerido

1. **Timer + fases** (MatchTimerManager) — base de todo, testeable solo.
2. **ScoreManager + atribución de kills** de jugadores (5 pts fijos) — ya tenés deathmatch jugable con timer.
3. **Fase de kills dobles** — trivial una vez que existen 1 y 2.
4. **Respawn con invulnerabilidad y spawn alejado.**
5. **Boss al inicio + 20 pts por last-hit** — reusa el spawn/IA original.
6. **Imps con respawn + 1 pt.**
7. **UI completa**: tabla, kill feed, anuncios de fase.
8. **Pases de balance** (sección 7) — al final, con el modo completo, porque el boss y los imps cambian el valor relativo de cada clase.

Los pasos 1-4 te dan un juego funcional en sí mismo; 5-6 le ponen la identidad PvPvE.

---

## 9. Casos borde a tener en cuenta

- **Empate al final**: definir criterio (más kills de jugador desantepata; si persiste, muerte súbita de 30s o victoria compartida).
- **Jugador se desconecta**: su score queda congelado en la tabla o se elimina — decidir. Sus puntos no se redistribuyen.
- **Boss mata a un jugador**: nadie recibe puntos (o implementar la asistencia opcional de 4.2).
- **Dos jugadores le pegan al boss y muere**: el punto va solo al del golpe final — es intencional, genera la tensión del robo. Comunicarlo claro en el kill feed.
- **Jugador muerto cuando termina el tiempo**: cuenta igual, el score es lo único que importa.
