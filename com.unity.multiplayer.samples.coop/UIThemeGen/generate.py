# -*- coding: utf-8 -*-
"""
Regenera el set de UI de VersusD con la estetica azul-violeta.

Estrategia: se reescriben los PNG EN SU LUGAR, conservando nombre y tamano
exactos. Unity mantiene los GUID, asi que los 364 prefabs y las 14 escenas
toman el look nuevo sin tocar una sola linea de YAML.

Los archivos previos se copian a Textures/UI/_PreVioleta/ antes de escribir.
"""
import os, sys, shutil
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from theme_core import *
import builders as B
import glyphx

ROOT = r'D:\VersusD\com.unity.multiplayer.samples.coop'
UI = os.path.join(ROOT, 'Assets', 'Textures', 'UI')
BACKUP = os.path.join(UI, '_PreVioleta')
FONT = os.path.join(ROOT, 'Assets', 'UI Toolkit', 'TextMesh Pro', 'Fonts',
                    'Bungee-Regular.ttf')

written = []


def size_of(name):
    return Image.open(os.path.join(UI, name)).size


def backup_all(names):
    os.makedirs(BACKUP, exist_ok=True)
    for n in names:
        src, dst = os.path.join(UI, n), os.path.join(BACKUP, n)
        if os.path.exists(src) and not os.path.exists(dst):
            shutil.copy2(src, dst)


ORIGINAL = os.path.join(UI, '_Original')


def glyph(name):
    """Silueta del icono. Se prefiere _Original (arte limpio, glifo blanco
    sobre color); si no esta, se usa el backup previo a este tema."""
    for folder in (ORIGINAL, BACKUP, UI):
        p = os.path.join(folder, name)
        if os.path.exists(p):
            m = glyphx.extract_glyph(p)
            if m.getbbox():
                return m
    return None


def save(img, name):
    w, h = size_of(name)
    if img.size != (w, h):
        img = img.resize((w, h), Image.LANCZOS)
    img.save(os.path.join(UI, name))
    written.append(name)


# ---------------------------------------------------------------- INVENTARIO
ICONS_181 = [
    'ui_archer_atk', 'ui_archer_skill1', 'ui_archer_skill2', 'ui_archer_skill3',
    'ui_mage_atk', 'ui_mage_skill1', 'ui_mage_skill2',
    'ui_rogue_atk', 'ui_rogue_skill1', 'ui_rogue_skill2',
    'ui_tank_atk', 'ui_tank_skill1', 'ui_tank_skill2',
    'ui_action_pickup', 'ui_action_putdown', 'ui_revive',
    'ui_emote_cheer', 'ui_emote_dance', 'ui_emote_sit', 'ui_emote_wave',
]
SYMBOLS = ['ui_archer_symbol', 'ui_mage_symbol', 'ui_rogue_symbol', 'ui_tank_symbol']
PANELS = ['ui_dialog', 'ui_char_info_frame', 'ui_scroll_frame', 'ui_hero_bg']
BUTTONS = ['ui_btn_blank', 'ui_btn_disabled', 'button_Disabled',
           'inputfield_Blank', 'ui_btn_ready_up', 'ui_btn_ready_dwn']
CHARBOX = ['ui_char_box_bg_selected', 'ui_char_box_ovr_avail', 'ui_char_box_ovr_selected']
SMALL = ['ui_btn_exit', 'ui_btn_randomize', 'ui_sound_settings',
         'ui_dropdown_arrow', 'ui_checkmark', 'ui_connecting']
BARS = ['ui_healthbar', 'ui_healthbar_bg']
PTAGS = ['ui_ptag_%d' % i for i in range(1, 9)] + ['ui_ptag_glow']
BGS = ['ui_bg_gradient', 'ui_bg_gradient2', 'ui_blurred_square', 'ui_char_box_glow']
TITLES = ['ui_char_select_title', 'ui_char_select_title2']

ALL = ([n + '.png' for n in ICONS_181] +
       [n + '_active.png' for n in SYMBOLS] + [n + '_inactive.png' for n in SYMBOLS] +
       [n + '.png' for n in PANELS + BUTTONS + CHARBOX + SMALL + BARS + PTAGS + BGS + TITLES])
ALL = [n for n in ALL if os.path.exists(os.path.join(UI, n))]


# ---------------------------------------------------------------- GENERACION
def build_icons():
    """Iconos de habilidad/emote como rombos con silueta y bloom."""
    blue_set = {'ui_tank_atk', 'ui_tank_skill1', 'ui_tank_skill2', 'ui_action_pickup',
                'ui_action_putdown'}
    for n in ICONS_181:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        st = 'blue' if n in blue_set else ('danger' if n == 'ui_revive' else 'normal')
        save(B.make_diamond_icon(w, h, glyph(f), st), f)


def build_symbols():
    """Simbolos de clase: rombo ancho, version activa e inactiva."""
    for n in SYMBOLS:
        for suf, st in (('_active', 'active'), ('_inactive', 'inactive')):
            f = n + suf + '.png'
            if f not in ALL:
                continue
            w, h = size_of(f)
            save(B.make_diamond_icon(w, h, glyph(f), st), f)


def build_panels():
    borders = {'ui_dialog': 64, 'ui_char_info_frame': 56, 'ui_scroll_frame': 40,
               'ui_hero_bg': 34}
    for n in PANELS:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        save(B.make_panel(w, h, border=borders.get(n, 48),
                          ornate=(min(w, h) >= 150), seed=hash(n) % 999), f)


def build_buttons():
    states = {'ui_btn_blank': 'normal', 'ui_btn_disabled': 'disabled',
              'button_Disabled': 'disabled', 'inputfield_Blank': 'pressed',
              'ui_btn_ready_up': 'normal', 'ui_btn_ready_dwn': 'hover'}
    for n in BUTTONS:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        save(B.make_button(w, h, states.get(n, 'normal')), f)


def build_charbox():
    """Tarjetas de seleccion de personaje: marco achaflanado alto."""
    cfg = {'ui_char_box_bg_selected': (VIOLET, 235, True),
           'ui_char_box_ovr_avail': (EDGE_DIM, 120, False),
           'ui_char_box_ovr_selected': (VIOLET_HI, 200, True)}
    for n in CHARBOX:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        acc, alpha, orn = cfg[n]
        save(B.make_panel(w, h, border=44, fill_a=alpha, ornate=orn,
                          accent=acc, cut_ratio=0.07, seed=hash(n) % 999), f)


def build_small():
    """Botones chicos: rombo con la silueta original dentro."""
    st = {'ui_btn_exit': 'danger', 'ui_checkmark': 'active',
          'ui_connecting': 'blue', 'ui_dropdown_arrow': 'normal',
          'ui_btn_randomize': 'blue', 'ui_sound_settings': 'normal'}
    for n in SMALL:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        save(B.make_diamond_icon(w, h, glyph(f), st.get(n, 'normal')), f)


def build_bars():
    if 'ui_healthbar.png' in ALL:
        w, h = size_of('ui_healthbar.png')
        save(B.make_bar(w, h, 'fill'), 'ui_healthbar.png')
    if 'ui_healthbar_bg.png' in ALL:
        w, h = size_of('ui_healthbar_bg.png')
        save(B.make_bar(w, h, 'bg'), 'ui_healthbar_bg.png')


def build_ptags():
    """Chapas de jugador: rombo violeta con el numeral."""
    for i in range(1, 9):
        f = 'ui_ptag_%d.png' % i
        if f not in ALL:
            continue
        w, h = size_of(f)
        base = B.make_diamond_icon(w, h, None, 'normal' if i % 2 else 'blue')
        # numeral centrado
        lay = Image.new('RGBA', (w * SS, h * SS), (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        try:
            fnt = ImageFont.truetype(FONT, int(h * SS * 0.40))
        except Exception:
            fnt = ImageFont.load_default()
        txt = str(i)
        bb = d.textbbox((0, 0), txt, font=fnt)
        d.text(((w * SS - (bb[2] - bb[0])) / 2 - bb[0],
                (h * SS - (bb[3] - bb[1])) / 2 - bb[1]),
               txt, font=fnt, fill=rgba(VIOLET_HI, 255))
        lay = add_glow(lay, lay.split()[3], GLOW_V, 3.0, 1.0, 2)
        save(Image.alpha_composite(base, finish(lay, w, h)), f)

    if 'ui_ptag_glow.png' in ALL:
        w, h = size_of('ui_ptag_glow.png')
        save(B.make_glow_orb(w, h, VIOLET, 1.3), 'ui_ptag_glow.png')


def build_bgs():
    cfg = {'ui_bg_gradient': (VIOLET, 0.55), 'ui_bg_gradient2': (BLUE, 0.50),
           'ui_blurred_square': (VIOLET, 0.35), 'ui_char_box_glow': (VIOLET, 0.85)}
    for n in BGS:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        acc, stg = cfg[n]
        if n in ('ui_char_box_glow', 'ui_blurred_square'):
            save(B.make_glow_orb(w, h, acc, 1.5), f)
        else:
            save(B.make_vignette_bg(w, h, acc, stg), f)


def build_titles():
    for n in TITLES:
        f = n + '.png'
        if f not in ALL:
            continue
        w, h = size_of(f)
        save(B.make_banner(w, h, seed=hash(n) % 97), f)


def main():
    print('Assets a regenerar:', len(ALL))
    backup_all(ALL)
    print('Backup en', BACKUP)
    for fn in (build_icons, build_symbols, build_panels, build_buttons,
               build_charbox, build_small, build_bars, build_ptags,
               build_bgs, build_titles):
        fn()
        print('  ok %-16s (%d escritos)' % (fn.__name__, len(written)))
    print('TOTAL escritos:', len(written))


if __name__ == '__main__':
    main()
