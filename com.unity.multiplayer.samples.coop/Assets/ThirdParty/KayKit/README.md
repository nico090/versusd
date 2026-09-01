# KayKit — cabezas

Mallas de cabeza extraídas de dos packs de Kay Lousberg (<www.kaylousberg.com>), ambos con
licencia **CC0**: uso comercial libre, sin atribución obligatoria.

| Pack | Se usa para | Licencia original |
|---|---|---|
| [Adventurers](https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0) | Los 4 héroes | `LICENSE.txt` |
| [Skeletons](https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0) | Imp, Vandal Imp y Boss | `LICENSE-Skeletons.txt` |

## Qué hay acá

| Carpeta | Contenido |
|---|---|
| `Heads/*.json` | Geometría de la cabeza (posiciones, normales, UVs, triángulos) |
| `Textures/*.png` | Atlas de paleta 1024×1024 del personaje donante |

No están los FBX del pack (20 MB cada uno, con 76 clips de animación que no usamos). Solo se
extrajo la malla de la cabeza de cada personaje, que es lo único que este proyecto necesita.

## Por qué JSON y no FBX

Las cabezas de KayKit vienen como *skinned meshes*, pero cada vértice pesa 1.0 contra un único
hueso `head` y 0.0 contra el root — o sea que son rígidas en la práctica. La extracción ya
multiplicó los vértices por la `inverseBindMatrix` de ese hueso, así que quedan en espacio local
del hueso: exactamente lo que `Head_Parent_*` espera. Por eso el pase de Editor solo tiene que
parentear y escalar, nunca re-skinnear.

También se convirtió de glTF (right-handed) a Unity (left-handed) negando X e invirtiendo el
winding de los triángulos, y se dio vuelta el origen de las UVs.

## Cómo se usa

`Boss Room/Style/8. Swap Heads To KayKit Models` — ver `Assets/Scripts/Editor/KayKitHeadPass.cs`.
El pase es re-ejecutable e idempotente. Para deshacer: `Boss Room/Style/Revert KayKit Heads`.

## Mapeo

| Personaje | Donante | Tris |
|---|---|---|
| Tank | Knight | 956 |
| Archer | Barbarian | 1033 |
| Mage | Mage | 1027 |
| Rogue | Rogue | 1115 |
| Imp | Skeleton_Minion | 442 |
| Vandal Imp | Skeleton_Rogue | 414 |
| Boss | Skeleton_Warrior | 394 |

Los enemigos van todos con calavera, escaladas por rango. Las tres comparten
`kaykit_skeleton.png`, así que el swap de los tres enemigos agrega una sola textura.

`BossGraphics` lleva **dos** rigs de cabeza completos (`Head_Parent_Boss` y un
`Head_Parent_Imp` de sobra), por eso el pase apunta al `Head_Parent` por nombre explícito en vez
de buscar el primero que encuentre.

## Regenerar

El script de extracción vive fuera del proyecto (fue una pasada única). Si hace falta rehacerlo:
bajar los `.glb` de `Characters/gltf/` del repo de arriba, buscar el nodo que termina en `_Head`,
aplicar la `inverseBindMatrix` de su joint dominante y volcar posiciones/normales/UVs/índices.
