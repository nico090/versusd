# Plan: giro automático de cámara según la dirección de marcha

Estado: **implementado (fases 0 a 4) y playtesteado → queda APAGADO por default.** Archivos:
`CameraUtils/CameraAutoRotate.cs` y `Gameplay/UserInput/CameraAutoRotateToggle.cs` (nuevos),
`ClientInputSender.cs` y `ClientPrefs.cs` (tocados). Cero prefabs, cero escenas, cero netcode.

## ⚠️ Resultado del playtest (2026-07-25): la disyuntiva que el plan no vio

El plan asumía que cancelar `AppliedYaw` resolvía el problema. Resuelve **uno** y crea otro:

- **Con compensación** (lo implementado): la caminata sale recta, pero mientras la cámara gira el
  stick deja de coincidir con la pantalla. Peor todavía: la base se rebasa recién cuando **todo**
  queda quieto (giro terminado *y* sin movimiento), porque rebasarla con el stick apretado haría
  volantazo. O sea que caminando continuo y cambiando de dirección, la desalineación **se acumula**:
  dos giros de 90° sin soltar y "adelante" apunta para atrás.
- **Sin compensación** (base viva de la cámara): el stick queda siempre pegado a la pantalla, pero
  la caminata se curva mientras gira, y hay que acotar cuánto puede girar por tramo o espiralás.

**No hay setting que evite las dos cosas** — es inherente al movimiento relativo a cámara, no un
problema de tuning. Reportado en playtest como "cuando gira la cámara me cambian los controles" y
"cada vez anda peor" (eso último es la acumulación).

**Decisión: default OFF** (`ClientPrefs.k_DefaultCameraAutoRotate = false`). El código queda entero y
se prende con el botón de la esquina. Si alguna vez se retoma, la bifurcación es elegir cuál de los
dos males se banca; la rama "sin compensación" está sin escribir. Investigación hecha el 2026-07-25; los
anclajes de línea corresponden a esa fecha (verificar antes de editar).

### Hallazgos al implementar la Fase 0 (leídos del prefab y del paquete)

`Assets/Prefabs/GameCam/CMCameraPrefab.prefab` (nodo `CMFreeLook`, tag `CMCamera`):

1. **No existe control manual de yaw en ninguna plataforma.** El controller `"Look Orbit X"` del
   `CinemachineInputAxisController` tiene `InputAction: {fileID: 0}` — sin bindear, y la instancia
   en `BossRoom.unity` no lo overridea. Consecuencias: el gate "1.5s tras rotación manual" (§3.3) y
   la detección de input manual (§3.1) **sobran**; el auto-giro es el único que escribe el eje, así
   que la cancelación de `AppliedYaw` es exacta; y el default "OFF en desktop" (§3.4) hay que
   repensarlo, porque en PC el yaw también está clavado en 40°.
2. **`TrackerSettings.BindingMode: 4` = `WorldSpace`**, y `HorizontalAxis` tiene `Range -180..180`,
   `Wrap: 1`, `Recentering.Enabled: 0`. En `CinemachineOrbitalFollow.cs:212` la posición sale de
   `Quaternion.AngleAxis(HorizontalAxis.Value, Vector3.up)`, o sea **el valor del eje = yaw de mundo
   de la cámara** (mismo signo, offset ~0, sin depender de la rotación del pj). Escribir el eje es
   por lo tanto directo: `axis.Value = yawDeseado`, sin conversiones.
3. **Ojo con los asmdef.** `Unity.BossRoom.CameraUtils` solo referencia `Unity.Cinemachine`, así que
   `CameraAutoRotate` **no puede ver `ClientPrefs`** (`Unity.BossRoom.Utils`). Por eso la preferencia
   se lee y se escribe desde `CameraAutoRotateToggle`, que vive en `Unity.BossRoom.Gameplay` (que sí
   referencia a los dos), en vez de ensanchar el assembly-hoja de la cámara.
4. La cámara nunca queda casi vertical (`Orbits` van de altura 5/radio 8 a altura 35/radio 15 →
   pitch ~32°..67°), así que proyectar el forward al piso para medir el yaw es estable.

Objetivo: que la cámara acompañe hacia dónde camina el personaje, sin romper el control
ni la legibilidad del combate PvP.

---

## 1. Estado actual del sistema (lo que ya existe)

| Pieza | Dónde | Qué hace hoy |
|---|---|---|
| Cámara | `Assets/Scripts/CameraUtils/CameraController.cs:31-39` | `CinemachineOrbitalFollow`; `HorizontalAxis` (yaw) fijo en 40°, `VerticalAxis` en 0.5 |
| Zoom móvil | `Assets/Scripts/Gameplay/UserInput/MobileZoomBar.cs:310-320` | Maneja **`VerticalAxis`** (no el yaw) |
| Bindings táctiles de cámara | `MobileZoomBar.DisableTouchCameraBindings()` (`:275-308`) | Anula todo binding táctil de los ejes de cámara → **en móvil no hay control manual de yaw** |
| Movimiento | `Assets/Scripts/Gameplay/UserInput/ClientInputSender.cs:307-337` | Direccional (WASD / stick / joystick táctil), sin click-to-move |
| Base de input | `ClientInputSender.CameraRelativeMove()` (`:592-608`) | **Relativa a la cámara**: "arriba" = alejarse de la cámara |
| Aim | `ClientInputSender.GetAimDirection()` (`:655+`), `AimMode` (`:140-147`) | `Pointer` (mouse → raycast del cursor al piso) vs `Movement` (gamepad/móvil) |
| Preferencias locales | `Assets/Scripts/Utils/ClientPrefs.cs:20-38` | Wrapper de `PlayerPrefs` |

### Hallazgo clave (define la arquitectura)

El `forward` del personaje **no** es solo la dirección de marcha. Además de
`ServerCharacterMovement.cs:279` (`transform.rotation = Quaternion.LookRotation(movementVector)`),
lo pisan las acciones:

- `Action/ConcreteActions/TargetAction.cs:92` — mientras haya enemigo seleccionado, el pj
  queda **encarado al enemigo de forma continua**.
- `MeleeAction.cs:69`, `LaunchProjectileAction.cs:21`, `PickUpAction.cs:86` — snap del
  `forward` al golpear / disparar / levantar.

**Por eso se descarta el recentering nativo de Cinemachine**
(`CinemachineOrbitalFollow.RecenteringTarget = TrackingTarget` + `HorizontalAxis.Recentering`).
Sería la solución de 5 líneas, pero seguiría el `forward` del pj → la cámara pegaría un
volantazo cada vez que el auto-target cambia de víctima o cada vez que se dispara.

**Decisión:** manejamos el yaw nosotros, alimentados por la **intención de movimiento**
(el vector que `ClientInputSender` ya calcula), no por `transform.forward`.

### Corrección post-playtest (2026-07-25): la base sale del eje, no del transform

El prefab tiene `TrackerSettings.PositionDamping: {1,1,1}`, así que **el transform de la cámara va
atrasado respecto del eje** mientras se mueve. La versión original de §2 restaba `AppliedYaw`
(instantáneo) del `m_MainCamera.transform.forward` (atrasado): dos magnitudes desfasadas, así que la
resta no daba constante durante el giro → la base se corría y volvía, y la caminata se curvaba.
Síntoma reportado: "cuando gira la cámara me cambian los controles".

Ahora `CameraAutoRotate` expone **`BasisYaw` = yaw del eje − `AppliedYaw`** y `CameraRelativeMove`
arma el `forward` desde ahí (con fallback al transform si todavía no resolvió la cámara). Ambos
términos vienen del eje, así que la diferencia es exactamente constante mientras dura el giro.

El offset eje→mundo se mide **una sola vez** (dos lecturas estables seguidas, para no cazar el
transitorio del arranque) y se latchea: el binding es `WorldSpace`, o sea que el mapeo es constante.
Medirlo por frame metía en la base el bamboleo del damping siguiendo al personaje.

---

## 2. El problema central: bucle de realimentación

El movimiento es relativo a la cámara. Si además la cámara sigue a la dirección de marcha,
se retroalimentan:

- Stick **arriba** sostenido → caminás hacia el forward de cámara → la cámara ya está
  alineada → **equilibrio estable, se siente bien**.
- Stick **al costado** sostenido → caminás hacia la derecha de cámara → la cámara rota hacia
  ahí → ahora "derecha" apunta a otro lado → **el pj camina en círculo**, indefinidamente.

En un Zelda/GTA se tolera. En un PvP top-down donde strafeás alrededor del rival, no: no
podrías circular a un enemigo sin que la cámara gire sola.

### Solución: cancelar el aporte propio

```
yawBaseDeInput = yawActualDeCámara − CameraAutoRotate.AppliedYaw
```

`AppliedYaw` acumula **solo** los grados que metió el auto-giro. ~~Se resetea al soltar el stick.~~
**Revisado post-playtest:** se resetea cuando **todo quedó quieto** (giro terminado *y* sin
movimiento), no al soltar. Resetear al soltar dejaba la cámara a mitad de camino en cualquier ángulo
arbitrario cada vez que dabas un toquecito, y la base se corría esa cantidad arbitraria. Ahora un
giro empezado **se termina** aunque sueltes, y encima no arranca hasta que sostenés un rumbo
**0.45s** (`k_CommitSeconds`), así que los toques cortos y los cambios rápidos de dirección no mueven
la cámara en absoluto. Resultado:

- Stick lateral sostenido → **línea recta**, mientras la cámara acompaña por detrás.
- Rotación **manual** del jugador → sigue afectando la base en vivo (no se cancela, porque
  no entra en `AppliedYaw`).

Es superior a "latchear el yaw al empezar a moverse", porque ese enfoque también congelaría
la rotación manual mientras caminás.

---

## 3. Componentes a implementar

### 3.1 `Assets/Scripts/CameraUtils/CameraAutoRotate.cs` (nuevo, ~150 líneas)

MonoBehaviour que **se auto-bootstrapea** con `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
y resuelve la cámara por tag `"CMCamera"` — mismo patrón que `MobileZoomBar.cs:56-67`. Así no
hay que tocar escenas ni prefabs (importante: con el Editor abierto, editar assets en disco no
llega al build; los fixes van en C#).

API estática que consume `ClientInputSender`:

```csharp
public static void ReportMoveIntent(Vector3 worldDir); // cada frame con movimiento
public static void Suspend();                          // pointer aim / target lockeado
public static void BeginMoveLatch();                   // transición neutro → activo
public static void EndMoveLatch();                     // al soltar
public static float AppliedYaw { get; }                // grados aportados por el auto-giro
```

Lógica en `LateUpdate()`:

- **Deadzone con histéresis**: empieza a girar si el desvío entre el yaw actual y el deseado
  supera **~25°**; frena por debajo de **~5°**. Sin histéresis vibra permanentemente.
- **Damping**: `Mathf.SmoothDampAngle`, velocidad tope **~90°/s**. Respetar
  `HorizontalAxis.Range` y `HorizontalAxis.Wrap` (usar `Mathf.DeltaAngle`).
- **Detección de input manual**: guardar el valor escrito el frame anterior; si el valor
  actual difiere más que un epsilon, lo movió el jugador (vía `CinemachineInputAxisController`)
  → suspenderse **1.5s**. Mismo patrón que `m_ManualTargetUntil` (`ClientInputSender.cs:107`).
- **Escritura del eje**: usar copy-modify-assign como `MobileZoomBar.ApplyZoom()` (`:310-320`):

```csharp
var axis = m_OrbitalFollow.HorizontalAxis;
axis.Value = nuevoYaw;
m_OrbitalFollow.HorizontalAxis = axis;
```

- **No tocar `VerticalAxis`** → cero conflicto con la barra de zoom móvil.
- Acumular en `AppliedYaw` exactamente el delta aplicado cada frame (y solo ese).

### 3.2 Cambios en `ClientInputSender.cs` (~30 líneas)

1. En el bloque de movimiento (`:307-337`):
   - transición neutro → activo: `CameraAutoRotate.BeginMoveLatch()`;
   - cada frame con movimiento: `CameraAutoRotate.ReportMoveIntent(dirMundo)`;
   - donde ya se manda el stop (`:324-331`): `CameraAutoRotate.EndMoveLatch()`.
2. En `CameraRelativeMove()` (`:592-608`): construir `forward`/`right` a partir de
   `yawCámara − CameraAutoRotate.AppliedYaw` en vez del `m_MainCamera.transform.forward` vivo.
   Mantener el fallback actual para cuando la cámara mira casi vertical.

### 3.3 Gates — dónde NO debe girar

| Condición | Motivo |
|---|---|
| `m_AimMode == AimMode.Pointer` | con mouse la mira sale de raycastear el cursor al piso (`:659-666`); si la cámara barre con el cursor quieto, cambia solo el objetivo auto-seleccionado |
| Target seleccionado (`m_ServerCharacter.TargetId != 0`) | girar en pleno duelo = perdés de vista al rival |
| ~~1.5s tras rotación manual~~ | **descartado**: no hay binding de yaw manual (hallazgo 1) |
| Preferencia apagada | ver 3.4 |

**Desvío al implementar — el gate de target se acotó.** `TargetId != 0` a secas apagaba el
auto-giro casi siempre: el soft-lock continuo agarra cualquier imp dentro de 14 m y un cono de 80°,
y el mapa está lleno. Suspende solo si además: hay un pick **deliberado** vigente
(`Time.time < m_ManualTargetUntil`) **o** el target es un **jugador** (`!IsNpc`) — que son los dos
casos de "duelo" que el gate quería proteger. Si en el playtest igual molesta girar con un imp
lockeado, la versión estricta es borrar esas dos condiciones internas.

**Efecto lateral útil del gate de pointer:** en PC el `AimMode` es `Pointer` salvo que estés usando
WASD/stick. O sea que quien toca el mouse se apaga el auto-giro solo, y quien juega a teclado lo
tiene. Eso cubre casi todo lo que iba a hacer el default por plataforma de §3.4.

### 3.4 `Assets/Scripts/Utils/ClientPrefs.cs`

Agregar `GetCameraAutoRotate()` / `SetCameraAutoRotate()` siguiendo el patrón de `:20-38`.

~~**Defaults: ON en táctil, OFF en desktop.**~~ **Revisado: ON en todas.** El default por plataforma
ya no hace falta, porque el gate de `AimMode.Pointer` apaga el auto-giro solo mientras el jugador
usa el mouse (y en PC el `AimMode` es `Pointer` salvo que estés con WASD/stick). Quien juega a
mouse no lo ve; quien juega a teclado lo tiene.

~~Sin UI nueva en esta pasada~~ → sí hay UI, pero **sin tocar prefabs**: ver Fase 4. Tampoco hace
falta la tecla en `DebugCheatsManager`, el botón en pantalla sirve para A/B testear.

---

## 4. Fases

| # | Qué | Riesgo |
|---|---|---|
| **0** | ✅ **Hecho.** `CameraAutoRotate.cs` mínimo, **sin** gates ni cancelación de `AppliedYaw`, solo para sentir el círculo y decidir si el resto vale la pena. Sí lleva deadzone/histéresis + damping desde el arranque (sin eso el giro es un snap y no se puede juzgar nada). `ClientInputSender.cs:315-327` reporta la intención. `CameraAutoRotate.Enabled` es el switch en runtime | nulo |
| **1** | ✅ **Hecho.** Cancelación de `AppliedYaw` (anti-círculo) + deadzone/histéresis + damping. `AppliedYaw` acumula el delta real (post wrap/clamp) y se resetea en `BeginMoveLatch`/`EndMoveLatch`; `CameraRelativeMove` rota su `forward` en `-AppliedYaw` y deriva el `right` de ahí (antes lo leía del transform de la cámara, que quedaría desfasado). Umbrales: 25°/5°, `SmoothTime` 0.35s, tope 90°/s | umbrales a ojo: contar con 2-3 iteraciones de ajuste |
| **2** | ✅ **Hecho.** Gates en `ClientInputSender.ShouldSuspendCameraAutoRotate()` (dueño del aim mode y del target) → `CameraAutoRotate.Suspend()`. El de "input manual" se descartó: no existe binding de yaw (ver hallazgo 1). El de target se acotó a **picks deliberados o enemigos jugadores** — ver abajo. Al cerrarse un gate en pleno giro la cámara frena por fricción (no por `SmoothDamp` con error 0, que es un resorte y haría rebote) | bajo |
| **3** | ✅ **Hecho.** `ClientPrefs.Get/SetCameraAutoRotate()`; `CameraAutoRotate.Bootstrap` siembra `Enabled` desde ahí. Default **ON en todas las plataformas** (ver §3.4 revisado). Sin tecla de debug: el toggle en pantalla la reemplaza | bajo |
| **4** | ✅ **Hecho sin tocar prefabs.** `CameraAutoRotateToggle.cs`: botón redondo arriba a la derecha, se auto-bootstrapea y se dibuja solo (sprites generados en runtime, **sin texto** → sin dependencia de TMP/fuentes). **No usa `Button` ni `GraphicRaycaster`**: pollea mouse y touches por su cuenta, así que no depende del `EventSystem` de la escena. Guardado en `ClientInputSender` vía `CameraAutoRotateToggle.IsActive` para que el tap no seleccione target. También se prende/apaga con la tecla **Y** (libre: no hay binding en `PlayerActions`) | ninguno (cero prefabs) |

Si en la Fase 0 el círculo resulta tolerable, el plan se achica a la mitad.

---

## 5. Validación

- **Círculo**: stick lateral sostenido 5s → trayectoria recta, no espiral.
- **Combate**: seleccionar enemigo y strafearlo → cámara quieta.
- **Mouse (PC)**: mover el mouse → auto-giro apagado y el target no cambia solo.
- **Zoom móvil**: usar la barra mientras camina → sin interferencia con el yaw.
- **Manual**: rotar a mano en PC → el auto-giro cede 1.5s y retoma sin saltos.
- Device Simulator para probar el joystick en el Editor + prueba real con 2 clientes contra
  el dedicated server.

---

## 6. Costo y alcance

~200 líneas. Archivos: **1 nuevo** (`CameraAutoRotate.cs`) + 3 tocados (`ClientInputSender.cs`,
`ClientPrefs.cs`, `DebugCheatsManager.cs`).

Todo cliente y todo C#: **cero impacto en netcode, servidor o assets**. Reversible con el flag
de `ClientPrefs`.
