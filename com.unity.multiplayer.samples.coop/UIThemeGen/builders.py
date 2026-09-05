# -*- coding: utf-8 -*-
"""Constructores de piezas de UI para el tema azul-violeta de VersusD."""
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math
from theme_core import (
    SS, VOID, INK, INK_HI, EDGE_DIM, VIOLET, VIOLET_HI, BLUE, BLUE_HI,
    LILAC, LAPIS, AMETHYST, GLOW_V, GLOW_B, DANGER,
    lerp, rgba, canvas, finish, add_glow, noise_layer, vertical_gradient,
    radial_falloff, diamond_pts, clipped_corner_rect, filigree_corner,
    ragged_banner,
)


# ------------------------------------------------------------ UTILIDADES
def _clip_to(layer, body):
    """Recorta `layer` al alpha de `body`."""
    layer.putalpha(ImageChops.multiply(layer.split()[3], body.split()[3]))
    return layer


def _textured(body, size, base, accent, seed, grad_a=130):
    """Aplica degradado vertical + grano dentro de la silueta de `body`."""
    grad = vertical_gradient(size, lerp(base, accent, 0.20), VOID, grad_a, 0)
    body = Image.alpha_composite(body, _clip_to(grad, body))
    n = noise_layer(size, 20, seed)
    body = Image.alpha_composite(body, _clip_to(n, body))
    return body


# ------------------------------------------------------------ GLIFOS
def extract_glyph(path, thresh=150):
    """Extrae la silueta clara (blanca) de un icono plano viejo como mascara alpha."""
    im = Image.open(path).convert('RGBA')
    r, g, b, a = im.split()
    lum = Image.merge('RGB', (r, g, b)).convert('L')
    mx = ImageChops.lighter(ImageChops.lighter(r, g), b)
    mn = ImageChops.darker(ImageChops.darker(r, g), b)
    sat = ImageChops.subtract(mx, mn)                    # saturacion aproximada
    bright = lum.point(lambda p: 255 if p > thresh else 0)
    flat = sat.point(lambda p: 255 if p < 90 else 0)     # poco saturado => blanco
    mask = ImageChops.multiply(bright, flat)
    mask = ImageChops.multiply(mask, a.point(lambda p: 255 if p > 40 else 0))
    return mask.filter(ImageFilter.GaussianBlur(0.4))


def trim_mask(mask, pad=0.06):
    bbox = mask.getbbox()
    if not bbox:
        return mask
    m = mask.crop(bbox)
    p = int(max(m.size) * pad) + 1
    out = Image.new('L', (m.width + p * 2, m.height + p * 2), 0)
    out.paste(m, (p, p))
    return out


def tint(mask, color_top, color_bot):
    """Convierte una mascara en silueta con degradado vertical."""
    g = vertical_gradient(mask.size, color_top, color_bot)
    g.putalpha(mask)
    return g


# ------------------------------------------------------------ PANELES
def make_panel(w, h, border=48, fill_a=232, ornate=True, accent=VIOLET,
               cut_ratio=0.0, seed=5, double_rule=True):
    """Panel oscuro con doble filete, grano y filigrana en las esquinas.

    `border` marca la zona 9-slice: todo el ornamento vive dentro de ella,
    asi el estirado central nunca deforma la decoracion.
    """
    img = canvas(w, h)
    W, H = w * SS, h * SS
    m = 3 * SS                                   # margen para que respire el glow
    cut = int(min(W, H) * cut_ratio) if cut_ratio else 0
    box = (m, m, W - m, H - m)

    body = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    if cut:
        clipped_corner_rect(bd, box, cut, fill=rgba(INK, fill_a))
    else:
        bd.rounded_rectangle(box, radius=4 * SS, fill=rgba(INK, fill_a))
    body = _textured(body, (W, H), INK_HI, accent, seed, 90)
    img = Image.alpha_composite(img, body)

    stroke = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stroke)
    lw = max(2, int(1.6 * SS))
    if cut:
        clipped_corner_rect(sd, box, cut, outline=rgba(accent, 255), width=lw)
    else:
        sd.rounded_rectangle(box, radius=4 * SS, outline=rgba(accent, 255), width=lw)

    if double_rule:
        i = int(5 * SS)
        ibox = (m + i, m + i, W - m - i, H - m - i)
        if cut:
            clipped_corner_rect(sd, ibox, max(1, cut - i),
                                outline=rgba(accent, 105), width=max(1, lw // 2))
        else:
            sd.rounded_rectangle(ibox, radius=3 * SS,
                                 outline=rgba(accent, 105), width=max(1, lw // 2))

    if ornate:
        fs = min(border * SS * 0.82, min(W, H) * 0.30)
        fw = max(2, int(1.3 * SS))
        off = m + int(3 * SS)
        filigree_corner(sd, off,     off,     fs, rgba(accent, 210), fw, False, False)
        filigree_corner(sd, W - off, off,     fs, rgba(accent, 210), fw, True,  False)
        filigree_corner(sd, off,     H - off, fs, rgba(accent, 210), fw, False, True)
        filigree_corner(sd, W - off, H - off, fs, rgba(accent, 210), fw, True,  True)

    glow_col = GLOW_B if accent == BLUE else GLOW_V
    img = add_glow(Image.alpha_composite(img, stroke), stroke.split()[3],
                   glow_col, 3.8, 0.85, 3)
    return finish(img, w, h)


def make_button(w, h, state='normal', accent=VIOLET):
    """Boton achaflanado. state: normal | hover | pressed | disabled."""
    a = {'normal': accent, 'hover': VIOLET_HI, 'pressed': BLUE,
         'disabled': EDGE_DIM}[state]
    fill_a = {'normal': 230, 'hover': 240, 'pressed': 246, 'disabled': 150}[state]
    img = canvas(w, h)
    W, H = w * SS, h * SS
    m = 3 * SS
    cut = int(H * 0.30)
    box = (m, m, W - m, H - m)

    body = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    base = INK_HI if state in ('hover', 'pressed') else INK
    clipped_corner_rect(bd, box, cut, fill=rgba(base, fill_a))
    body = _textured(body, (W, H), base, a, 11, 130)

    stroke = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stroke)
    lw = max(2, int(1.7 * SS))
    clipped_corner_rect(sd, box, cut, outline=rgba(a, 255), width=lw)
    i = int(4 * SS)
    clipped_corner_rect(sd, (m + i, m + i, W - m - i, H - m - i), max(1, cut - i),
                        outline=rgba(a, 90), width=max(1, lw // 2))
    # acentos rombicos a los lados
    r = H * 0.10
    for cx in (m + cut * 0.5, W - m - cut * 0.5):
        sd.polygon(diamond_pts(cx, H / 2, r * 0.62, r), fill=rgba(a, 235))

    img = Image.alpha_composite(Image.alpha_composite(img, body), stroke)
    if state != 'disabled':
        gi = {'normal': 0.85, 'hover': 1.35, 'pressed': 1.1}[state]
        glow_col = GLOW_B if a == BLUE else GLOW_V
        img = add_glow(img, stroke.split()[3], glow_col, 4.2, gi, 3)
    return finish(img, w, h)


# ------------------------------------------------------------ ICONOS ROMBICOS
def make_diamond_icon(w, h, glyph_mask=None, state='normal', accent=VIOLET, glow=True):
    """Marco rombico con silueta: el motivo central de la referencia 1."""
    a = {'normal': accent, 'active': VIOLET_HI, 'inactive': EDGE_DIM,
         'blue': BLUE, 'danger': DANGER}.get(state, accent)
    dim = (state == 'inactive')
    img = canvas(w, h)
    W, H = w * SS, h * SS
    cx, cy = W / 2.0, H / 2.0
    pad = min(W, H) * 0.085
    rx, ry = (W - pad * 2) / 2.0, (H - pad * 2) / 2.0

    body = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    bd.polygon(diamond_pts(cx, cy, rx, ry), fill=rgba(INK, 238))
    body = _textured(body, (W, H), INK_HI, a, 17, 150)

    stroke = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stroke)
    lw = max(2, int(1.7 * SS))
    pts = diamond_pts(cx, cy, rx, ry)
    sd.line(pts + [pts[0]], fill=rgba(a, 255), width=lw, joint='curve')
    ip = min(W, H) * 0.055
    pts2 = diamond_pts(cx, cy, rx - ip, ry - ip)
    sd.line(pts2 + [pts2[0]], fill=rgba(a, 95), width=max(1, lw // 2), joint='curve')
    # remate superior (la "pestana" metalica de la referencia)
    tw = W * 0.055
    sd.line([(cx - tw, pad * 0.55), (cx, pad * 0.06), (cx + tw, pad * 0.55)],
            fill=rgba(a, 240), width=lw, joint='curve')

    out = Image.alpha_composite(Image.alpha_composite(img, body), stroke)

    if glyph_mask is not None:
        gm = trim_mask(glyph_mask)
        # El cuadrado inscrito en un rombo mide la mitad de su diagonal, asi que
        # 0.46 es el maximo que entra sin pisar los filos. thumbnail() no servia:
        # solo reduce, y los glifos de origen son mas chicos que el objetivo.
        target = min(W, H) * 0.46
        k = target / max(gm.width, gm.height)
        gm = gm.resize((max(1, int(gm.width * k)), max(1, int(gm.height * k))),
                       Image.LANCZOS)
        gcol = tint(gm, EDGE_DIM if dim else VIOLET_HI, EDGE_DIM if dim else BLUE_HI)
        layer = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        layer.paste(gcol, (int(cx - gm.width / 2), int(cy - gm.height / 2)), gcol)
        if glow and not dim:
            out = add_glow(out, layer.split()[3], GLOW_V, 3.0, 0.95, 2)
        out = Image.alpha_composite(out, layer)

    if glow and not dim:
        glow_col = GLOW_B if a == BLUE else GLOW_V
        out = add_glow(out, stroke.split()[3], glow_col, 5.0, 1.25, 3)
    return finish(out, w, h)


# ------------------------------------------------------------ BARRAS
def make_bar(w, h, kind='fill'):
    """kind: fill (relleno de vida, violeta->azul) | bg (riel vacio)."""
    img = canvas(w, h)
    W, H = w * SS, h * SS
    m = 2 * SS
    cut = int(H * 0.42)
    box = (m, m, W - m, H - m)

    if kind == 'bg':
        d = ImageDraw.Draw(img)
        clipped_corner_rect(d, box, cut, fill=rgba(VOID, 235))
        clipped_corner_rect(d, box, cut, outline=rgba(VIOLET, 235),
                            width=max(2, int(1.5 * SS)))
        inner = int(3.5 * SS)
        clipped_corner_rect(d, (m + inner, m + inner, W - m - inner, H - m - inner),
                            max(1, cut - inner), outline=rgba(EDGE_DIM, 170),
                            width=max(1, int(0.8 * SS)))
        return finish(img, w, h)

    body = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    clipped_corner_rect(bd, box, cut, fill=rgba(VIOLET, 255))

    grad = Image.new('RGBA', (W, H))
    gd = ImageDraw.Draw(grad)
    for x in range(W):                                   # violeta -> azul
        gd.line([(x, 0), (x, H)], fill=rgba(lerp(VIOLET, BLUE, x / max(1, W - 1)), 255))
    grad.putalpha(body.split()[3])
    sh = vertical_gradient((W, H), (255, 255, 255), (255, 255, 255), 110, 0)
    grad = Image.alpha_composite(grad, _clip_to(sh, body))

    img = add_glow(Image.alpha_composite(img, grad), body.split()[3], GLOW_V, 3.4, 1.1, 3)
    return finish(img, w, h)


# ------------------------------------------------------------ ESTANDARTE
def make_banner(w, h, seed=3):
    """Cartel de tela rasgada con filete de acento (referencia 1)."""
    img = canvas(w, h)
    W, H = w * SS, h * SS
    ban, pts = ragged_banner(w, h, seed)
    ban = Image.alpha_composite(ban, _clip_to(noise_layer((W, H), 26, seed + 2), ban))
    shade = vertical_gradient((W, H), lerp(INK_HI, VIOLET, 0.18), VOID, 200, 235)
    ban = Image.alpha_composite(ban, _clip_to(shade, ban))

    stroke = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stroke)
    half = len(pts) // 2
    sd.line(pts[:half], fill=rgba(VIOLET, 220), width=max(2, int(1.1 * SS)), joint='curve')
    sd.line(pts[half:], fill=rgba(VIOLET, 160), width=max(2, int(0.9 * SS)), joint='curve')

    out = Image.alpha_composite(Image.alpha_composite(img, ban), stroke)
    out = add_glow(out, stroke.split()[3], GLOW_V, 3.0, 0.5, 2)
    return finish(out, w, h)


# ------------------------------------------------------------ FONDOS / GLOW
def make_vignette_bg(w, h, accent=VIOLET, strength=0.55):
    img = Image.new('RGBA', (w, h), rgba(VOID, 255))
    halo = Image.new('RGBA', (w, h), rgba(lerp(VOID, accent, strength * 0.5), 255))
    halo.putalpha(radial_falloff((w, h), 0.0, 1.0, 2.2))
    img = Image.alpha_composite(img, halo)
    return Image.alpha_composite(img, noise_layer((w, h), 16, 23, 2))


def make_glow_orb(w, h, accent=VIOLET, power=1.5):
    solid = Image.new('RGBA', (w, h), rgba(accent, 255))
    solid.putalpha(radial_falloff((w, h), 0.0, 1.0, power))
    return solid
