# -*- coding: utf-8 -*-
"""
Deja el violeta solo en los titulos y pone el resto del texto en blanco.

El pase anterior habia llevado a violeta todo el texto de la UI, y a esa
densidad el color deja de acentuar: si todo es violeta, nada resalta y el
cuerpo se lee peor sobre paneles oscuros.

El corte sale de los propios prefabs, no a ojo: los titulos viven en 70pt o
mas (YOU WON!, Loading..., Debug cheats) y todo lo demas -- etiquetas de campo,
botones, filas de lista, cuerpo -- esta en 50pt o menos. No hay nada en el medio.

El violeta entra por dos vias y hay que cortar las dos: m_fontColor solido, y
el gradiente de vertices, que cuando esta activo gana sobre el color de fuente.
"""
import os, re, glob

ROOT = r'D:\VersusD\com.unity.multiplayer.samples.coop'
TITLE_PT = 70.0

# Miden como titulo pero no lo son: botones dentro de una tarjeta grande.
NOT_TITLES = {'OK'}

WHITE = (1.0, 1.0, 1.0)


def pack(a):
    """Color32 blanco con el alfa dado, como lo serializa Unity."""
    return 0xFF | (0xFF << 8) | (0xFF << 16) | (int(round(a * 255)) << 24)


def is_title(block):
    m = re.search(r'm_fontSize:\s*([\d.]+)', block)
    if not m or float(m.group(1)) < TITLE_PT:
        return False
    t = re.search(r'm_text:\s*(.*)', block)
    return not (t and t.group(1).strip().strip("'\"") in NOT_TITLES)


def whiten(block):
    """Pone el texto en blanco y apaga el gradiente."""
    changed = False

    m = re.search(r'm_fontColor:\s*\{r:\s*([\d.]+),\s*g:\s*([\d.]+),\s*'
                  r'b:\s*([\d.]+),\s*a:\s*([\d.]+)\}', block)
    if m:
        r, g, b, a = (float(x) for x in m.groups())
        if (r, g, b) != WHITE:
            block = block[:m.start()] + \
                'm_fontColor: {r: 1, g: 1, b: 1, a: %g}' % a + block[m.end():]
            changed = True
            # el Color32 serializado tiene que acompanar, o TMP usa el viejo
            block = re.sub(r'(m_fontColor32:\s*\n\s*serializedVersion:\s*2\s*\n\s*rgba:\s*)\d+',
                           lambda mm: mm.group(1) + str(pack(a)), block, count=1)

    m = re.search(r'(m_enableVertexGradient:\s*)1', block)
    if m:
        block = block[:m.start()] + m.group(1) + '0' + block[m.end():]
        changed = True

    return block, changed


def main():
    total_w, total_k, touched = 0, 0, 0
    for f in sorted(glob.glob(os.path.join(ROOT, 'Assets', 'Prefabs', 'UI', '*.prefab'))):
        with open(f, encoding='utf-8', errors='ignore') as fh:
            src = fh.read()

        parts = src.split('--- !u!')
        out, nw, nk = [parts[0]], 0, 0
        for b in parts[1:]:
            if 'm_text:' in b:
                if is_title(b):
                    nk += 1
                else:
                    b, ch = whiten(b)
                    if ch:
                        nw += 1
            out.append(b)

        if nw:
            with open(f, 'w', encoding='utf-8') as fh:
                fh.write('--- !u!'.join(out))
            touched += 1
            print('  %-30s %2d a blanco, %d titulo(s) en violeta'
                  % (os.path.basename(f), nw, nk))
        total_w += nw
        total_k += nk

    print('archivos: %d | textos a blanco: %d | titulos que siguen violeta: %d'
          % (touched, total_w, total_k))


if __name__ == '__main__':
    main()
