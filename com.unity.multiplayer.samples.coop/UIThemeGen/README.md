# Tema de UI azul-violeta — VersusD

Generador del set de UI a partir de `REFERENCIAUI1.jpeg` (Age of Darkness) y
`REFERENCIAUI2.jpeg` (árbol de habilidades gótico), en gama azul-violeta.

## Cómo se aplica

Los PNG se reescriben **en su lugar**, con el mismo nombre y el mismo tamaño en
píxeles. Unity conserva los GUID, así que los 364 prefabs y las 14 escenas toman
el look nuevo sin editar una línea de YAML. No hay que re-vincular nada.

## Ejecutar

```bash
cd UIThemeGen
python generate.py        # regenera los 64 sprites
python fix_borders.py     # ajusta spriteBorder 9-slice en los .meta
python theme_text.py      # aplica la paleta a los materiales de TextMesh Pro
python retint_prefabs.py  # lleva a la gama los colores cableados en prefabs
```

Después, en Unity: `Assets → Reimport All` (o click derecho sobre
`Textures/UI → Reimport`) para que refresque las texturas.

## Paleta

Está en `theme_core.py` y **se toma de `Assets/Scripts/Gameplay/UI/HudSkin.cs`**,
que ya definía la gama para el HUD construido por código (`AccentBlue`,
`AccentViolet`, `Lapis`, `Amethyst`, `PanelColor`). Se respeta ese archivo como
fuente de verdad para que el HUD por script y estos sprites sean un solo tema.

Si cambiás un color, cambialo en `HudSkin.cs` y replicalo en `theme_core.py`.

Excepciones deliberadas que `HudSkin` documenta y este tema no pisa:
- **dorado** = primer puesto, nada más;
- **rojo** = alarma de los últimos 30 segundos.

## Archivos

| Archivo | Qué hace |
|---|---|
| `theme_core.py` | Paleta y primitivas: glow, grano, rombos, filigrana, estandarte rasgado |
| `builders.py` | Piezas: paneles, botones, iconos rómbicos, barras, fondos |
| `glyphx.py` | Extrae la silueta de los iconos viejos, sea cual sea su estilo |
| `generate.py` | Inventario y generación de los 64 sprites |
| `fix_borders.py` | `spriteBorder` de los 9-slice |
| `theme_text.py` | Materiales SDF de TextMesh Pro |
| `retint_prefabs.py` | Colores cableados en prefabs de UI |

## Volver atrás

- `Assets/Textures/UI/_PreVioleta/` — los PNG tal como estaban antes de este tema.
- `_Original/` — el arte original de Boss Room, anterior a todo reskin.
- `git checkout -- Assets/Textures/UI Assets/Prefabs/UI Assets/Fonts` revierte todo.

## Nota sobre `glyphx.py`

El set mezclaba tres convenciones de icono: glifo blanco sobre cuadrado de
color, glifo oscuro sobre transparente, y glifo oscuro recortado dentro de un
chip claro. Un umbral fijo de "brillante y poco saturado" solo resolvía la
primera. El extractor estima el color de fondo y marca lo que se aparta de él;
si lo marcado resulta tener un **hueco cerrado**, ese hueco es el glifo (ése era
el caso de `ui_tank_atk`, donde antes se tomaba el chip entero).
