# -*- coding: utf-8 -*-
"""
Aplica la paleta azul-violeta a los materiales de TextMesh Pro.

Los SDF de Bungee venian con _GlowColor verde puro (0,1,0), resto del tema
anterior: era lo que tenia de verde la tipografia en pantalla.
"""
import os, re, glob

ROOT = r'D:\VersusD\com.unity.multiplayer.samples.coop'

# Colores TMP en floats 0..1
FACE     = (0.90, 0.91, 0.98, 1.00)   # HudSkin.TextPrimary
GLOW     = (0.60, 0.38, 0.92, 0.38)   # HudSkin.AccentViolet, tenue
OUTLINE  = (0.035, 0.030, 0.090, 1.00)  # casi negro azulado
UNDERLAY = (0.050, 0.030, 0.140, 0.65)  # sombra violeta profunda

COLORS = {
    '_FaceColor': FACE,
    '_GlowColor': GLOW,
    '_OutlineColor': OUTLINE,
    '_UnderlayColor': UNDERLAY,
}
FLOATS = {
    # Un halo ancho sobre cada letra tenia el efecto contrario al buscado: con
    # la cara blanca, el violeta del glow volvia a tenir todo el texto. Queda
    # apenas como atmosfera, no como color de la tipografia.
    '_GlowOuter': 0.06,
    '_GlowPower': 0.55,
    '_OutlineWidth': 0.18,
}

TARGETS = (
    glob.glob(os.path.join(ROOT, 'Assets', 'Fonts', '*.mat')) +
    glob.glob(os.path.join(ROOT, 'Assets', 'UI Toolkit', 'TextMesh Pro',
                           'Resources', 'Fonts & Materials', '*.mat'))
)


def fmt(v):
    return ('%g' % round(v, 4))


def patch(path):
    with open(path, 'r', encoding='utf-8') as f:
        s = f.read()
    orig = s

    for key, (r, g, b, a) in COLORS.items():
        pat = re.compile(r'(- ' + key + r':\s*\{r:\s*)[-\d.e]+(,\s*g:\s*)[-\d.e]+'
                         r'(,\s*b:\s*)[-\d.e]+(,\s*a:\s*)[-\d.e]+(\})')
        s = pat.sub(lambda m: '%s%s%s%s%s%s%s%s%s' % (
            m.group(1), fmt(r), m.group(2), fmt(g), m.group(3), fmt(b),
            m.group(4), fmt(a), m.group(5)), s)

    for key, v in FLOATS.items():
        s = re.sub(r'(- ' + key + r':\s*)[-\d.e]+', r'\g<1>' + fmt(v), s)

    if s != orig:
        with open(path, 'w', encoding='utf-8') as f:
            f.write(s)
        return True
    return False


def main():
    done = 0
    for p in TARGETS:
        if patch(p):
            done += 1
            print('  ', os.path.basename(p))
    print('materiales de texto actualizados: %d/%d' % (done, len(TARGETS)))


if __name__ == '__main__':
    main()
