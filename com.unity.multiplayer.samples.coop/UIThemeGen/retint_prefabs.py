# -*- coding: utf-8 -*-
"""
Lleva a la gama azul-violeta los colores sueltos de los prefabs y escenas de UI.

Quedaban dorados y ambares (hue 36-51) cableados en los prefabs: al teñir un
Image, esos valores pisan el sprite por mas que el PNG ya sea violeta.

Se rota SOLO el tono y se conservan luminosidad y saturacion, asi la jerarquia
de contraste que el diseno ya tenia (un dorado brillante seguia siendo el
elemento mas brillante) se mantiene tal cual.
"""
import os, re, glob, colorsys

ROOT = r'D:\VersusD\com.unity.multiplayer.samples.coop'

KEYS = ('m_Color', 'm_fontColor', 'm_EffectColor', 'm_OutlineColor',
        'topLeft', 'topRight', 'bottomLeft', 'bottomRight')

HUE_OK = (200, 295)     # franja aceptada: azul -> violeta
HUE_TARGET_LIGHT = 268 / 360.0
HUE_TARGET_DARK = 238 / 360.0
MIN_SAT = 0.25


def remap(r, g, b):
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    if s < MIN_SAT or l < 0.04:
        return None                       # gris, blanco o negro: no tine
    hue = h * 360
    if HUE_OK[0] <= hue <= HUE_OK[1]:
        return None                       # ya esta en gama
    nh = HUE_TARGET_LIGHT if l >= 0.45 else HUE_TARGET_DARK
    return colorsys.hls_to_rgb(nh, l, s)


def fmt(v):
    return '%g' % round(v, 3)


def patch(path):
    with open(path, 'r', encoding='utf-8') as f:
        s = f.read()
    n = [0]

    pat = re.compile(
        r'(?P<key>' + '|'.join(KEYS) + r'):\s*\{r:\s*(?P<r>[-\d.e]+),\s*'
        r'g:\s*(?P<g>[-\d.e]+),\s*b:\s*(?P<b>[-\d.e]+),\s*a:\s*(?P<a>[-\d.e]+)\}')

    def sub(m):
        r, g, b = float(m.group('r')), float(m.group('g')), float(m.group('b'))
        a = m.group('a')
        if float(a) < 0.02:
            return m.group(0)
        out = remap(r, g, b)
        if out is None:
            return m.group(0)
        n[0] += 1
        return '%s: {r: %s, g: %s, b: %s, a: %s}' % (
            m.group('key'), fmt(out[0]), fmt(out[1]), fmt(out[2]), a)

    s2 = pat.sub(sub, s)
    if n[0]:
        with open(path, 'w', encoding='utf-8') as f:
            f.write(s2)
    return n[0]


def main():
    files = (glob.glob(os.path.join(ROOT, 'Assets', 'Prefabs', 'UI', '*.prefab')) +
             glob.glob(os.path.join(ROOT, 'Assets', 'Scenes', '*.unity')) +
             glob.glob(os.path.join(ROOT, 'Assets', 'Scenes', '**', '*.unity')))
    total, touched = 0, 0
    for p in sorted(set(files)):
        c = patch(p)
        if c:
            touched += 1
            total += c
            print('  %-40s %d colores' % (os.path.basename(p), c))
    print('archivos tocados: %d | colores remapeados: %d' % (touched, total))


if __name__ == '__main__':
    main()
