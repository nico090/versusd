# -*- coding: utf-8 -*-
"""
Ajusta spriteBorder en los .meta de los sprites 9-slice.

El ornamento (filigrana de esquina, chaflan, rombos laterales) se dibuja
dentro de una franja de borde; si el .meta declara una franja menor, Unity
estira esa decoracion al redimensionar. Aca se declara la franja real.

Formato de Unity: spriteBorder {x: izq, y: abajo, z: der, w: arriba}
"""
import os, re
from PIL import Image

UI = r'D:\VersusD\com.unity.multiplayer.samples.coop\Assets\Textures\UI'

# nombre -> franja de borde usada al dibujar (en px del sprite)
BORDERS = {
    'ui_dialog':                 60,
    'ui_char_info_frame':        56,
    'ui_scroll_frame':           40,
    'ui_hero_bg':                34,
    'ui_char_box_bg_selected':   46,
    'ui_char_box_ovr_avail':     46,
    'ui_char_box_ovr_selected':  46,
    'ui_btn_blank':              58,
    'ui_btn_disabled':           58,
    'button_Disabled':           58,
    'inputfield_Blank':          58,
    'ui_btn_ready_up':           52,
    'ui_btn_ready_dwn':          52,
    'ui_healthbar':              26,
    'ui_healthbar_bg':           30,
}

PAT = re.compile(r'spriteBorder:\s*\{x:\s*[\d.]+,\s*y:\s*[\d.]+,\s*z:\s*[\d.]+,\s*w:\s*[\d.]+\}')


def main():
    changed = []
    for name, b in BORDERS.items():
        png = os.path.join(UI, name + '.png')
        meta = png + '.meta'
        if not (os.path.exists(png) and os.path.exists(meta)):
            print('  falta', name)
            continue

        w, h = Image.open(png).size
        # una franja nunca puede pasar la mitad del sprite, o Unity la rechaza
        bx = min(b, (w - 2) // 2)
        by = min(b, (h - 2) // 2)

        with open(meta, 'r', encoding='utf-8') as f:
            s = f.read()
        new = 'spriteBorder: {x: %d, y: %d, z: %d, w: %d}' % (bx, by, bx, by)
        s2, n = PAT.subn(new, s)
        if n and s2 != s:
            with open(meta, 'w', encoding='utf-8') as f:
                f.write(s2)
            changed.append('%-28s %dx%d -> borde %d/%d' % (name, w, h, bx, by))

    print('metas actualizados:', len(changed))
    for c in changed:
        print('   ', c)


if __name__ == '__main__':
    main()
