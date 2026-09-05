# -*- coding: utf-8 -*-
"""
VersusD - Nucleo del tema de UI  (estetica: Age of Darkness / arbol de habilidades gotico)
Paleta azul-violeta, marcos rombicos, glow, filigrana, estandartes rasgados.

Todo se dibuja con supersampling SS y se reduce al final -> bordes limpios.
"""
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math, random

SS = 4  # factor de supersampling

# ---------------------------------------------------------------- PALETA
# Alineada con Assets/Scripts/Gameplay/UI/HudSkin.cs, que ya definia la gama
# azul-violeta para el HUD construido por codigo. Se toman de alli AccentBlue,
# AccentViolet, Lapis, Amethyst y PanelColor tal cual, para que el HUD dibujado
# por script y estos sprites sean el mismo tema y no dos parecidos.

VOID      = (7,   6,  12)     # negro azulado mas profundo
INK       = (11,  10,  18)    # HudSkin.PanelColor
INK_HI    = (24,  21,  44)    # relleno elevado
EDGE_DIM  = (55,  70, 120)    # borde apagado / estado inactivo
VIOLET    = (153, 97, 235)    # HudSkin.AccentViolet
VIOLET_HI = (190, 150, 255)   # filo brillante
BLUE      = (92,  173, 240)   # HudSkin.AccentBlue
BLUE_HI   = (150, 205, 250)
LAPIS     = (71,  112, 219)   # HudSkin.Lapis
AMETHYST  = (143, 107, 224)   # HudSkin.Amethyst
LILAC     = (230, 232, 250)   # HudSkin.TextPrimary
GLOW_V    = (140, 85, 240)    # bloom violeta
GLOW_B    = (70,  140, 235)   # bloom azul
# HudSkin reserva el rojo para la alarma de los ultimos 30s y el dorado para el
# primer puesto. Para "salir" y "revivir" hace falta algo que se despegue del
# resto sin salirse de la gama pedida: violeta caliente, no magenta.
DANGER    = (176, 76, 220)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def rgba(c, a=255):
    return (c[0], c[1], c[2], int(a))


def canvas(w, h, fill=(0, 0, 0, 0)):
    """Lienzo supersampleado."""
    return Image.new('RGBA', (w * SS, h * SS), fill)


def finish(img, w, h):
    """Reduce el lienzo supersampleado al tamano final."""
    return img.resize((w, h), Image.LANCZOS)


# ---------------------------------------------------------------- GLOW
def glow_from(mask, color, radius, intensity=1.0, spread=1.0):
    """Crea una capa de resplandor exterior a partir de una mascara alpha."""
    m = mask.filter(ImageFilter.GaussianBlur(radius * SS))
    if spread != 1.0:
        m = m.point(lambda p: min(255, int(p * spread)))
    layer = Image.new('RGBA', mask.size, rgba(color, 0))
    layer.putalpha(m.point(lambda p: int(p * intensity)))
    solid = Image.new('RGBA', mask.size, rgba(color, 255))
    solid.putalpha(m.point(lambda p: int(p * intensity)))
    return solid


def add_glow(base, mask, color, radius, intensity=0.85, passes=2):
    """Compone un bloom multicapa por debajo del contenido nitido."""
    out = Image.new('RGBA', base.size, (0, 0, 0, 0))
    for i in range(passes):
        r = radius * (1.0 + i * 1.6)
        inten = intensity / (1.0 + i * 0.9)
        out = Image.alpha_composite(out, glow_from(mask, color, r, inten))
    return Image.alpha_composite(out, base)


# ---------------------------------------------------------------- TEXTURA
def noise_layer(size, opacity=14, seed=7, scale=3):
    """Grano sutil para que los rellenos planos no se vean muertos."""
    rnd = random.Random(seed)
    w, h = size
    sw, sh = max(1, w // scale), max(1, h // scale)
    n = Image.new('L', (sw, sh))
    n.putdata([rnd.randint(0, 255) for _ in range(sw * sh)])
    n = n.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(0.6))
    layer = Image.new('RGBA', (w, h), (255, 255, 255, 0))
    layer.putalpha(n.point(lambda p: int(abs(p - 128) / 128 * opacity)))
    return layer


def vertical_gradient(size, top, bottom, alpha_top=255, alpha_bottom=255):
    w, h = size
    g = Image.new('RGBA', (w, h))
    d = ImageDraw.Draw(g)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=rgba(lerp(top, bottom, t),
                                           alpha_top + (alpha_bottom - alpha_top) * t))
    return g


def radial_falloff(size, inner=0.0, outer=1.0, power=1.6):
    """Mascara radial: 255 en el centro -> 0 en el borde."""
    w, h = size
    m = Image.new('L', (w, h), 0)
    px = m.load()
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    mr = math.hypot(cx, cy)
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy) / mr
            t = (d - inner) / max(1e-6, outer - inner)
            t = min(1.0, max(0.0, t))
            px[x, y] = int((1.0 - t) ** power * 255)
    return m


# ---------------------------------------------------------------- FORMAS
def diamond_pts(cx, cy, rx, ry):
    return [(cx, cy - ry), (cx + rx, cy), (cx, cy + ry), (cx - rx, cy)]


def draw_diamond(d, cx, cy, rx, ry, fill=None, outline=None, width=1):
    d.polygon(diamond_pts(cx, cy, rx, ry), fill=fill, outline=outline)
    if outline and width > 1:
        pts = diamond_pts(cx, cy, rx, ry)
        d.line(pts + [pts[0]], fill=outline, width=width, joint='curve')


def clipped_corner_rect(d, box, cut, fill=None, outline=None, width=1):
    """Rectangulo con esquinas cortadas en diagonal (chaflan) - motivo del arbol de habilidades."""
    x0, y0, x1, y1 = box
    pts = [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
           (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]
    d.polygon(pts, fill=fill)
    if outline:
        d.line(pts + [pts[0]], fill=outline, width=width, joint='curve')
    return pts


def filigree_corner(d, x, y, size, color, width, flip_x=False, flip_y=False):
    """Voluta ornamental estilo gotico para las esquinas de los paneles."""
    sx = -1 if flip_x else 1
    sy = -1 if flip_y else 1

    def P(px, py):
        return (x + px * size * sx, y + py * size * sy)

    # barrido principal
    d.line([P(0.02, 0.60), P(0.06, 0.30), P(0.20, 0.10), P(0.48, 0.03), P(0.86, 0.02)],
           fill=color, width=width, joint='curve')
    # voluta interior que se enrosca
    d.line([P(0.10, 0.52), P(0.16, 0.30), P(0.32, 0.18), P(0.52, 0.15)],
           fill=color, width=max(1, int(width * 0.7)), joint='curve')
    # pequeno gancho
    d.line([P(0.30, 0.28), P(0.40, 0.34), P(0.44, 0.46)],
           fill=color, width=max(1, int(width * 0.6)), joint='curve')
    # acento rombico
    r = size * 0.055
    cx, cy = P(0.60, 0.10)
    d.polygon(diamond_pts(cx, cy, r, r * 1.5), fill=color)


def ragged_banner(w, h, seed=3, jag=0.055):
    """Estandarte de tela rasgada (el cartel de texto de la referencia 1)."""
    rnd = random.Random(seed)
    W, H = w * SS, h * SS
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    j = H * jag
    top, bot = [], []
    steps = 26
    for i in range(steps + 1):
        t = i / steps
        x = t * W
        top.append((x, j * 0.55 + rnd.uniform(-j, j) * 0.7))
        bot.append((x, H - j * 0.55 + rnd.uniform(-j, j) * 0.7))
    pts = top + bot[::-1]
    d.polygon(pts, fill=rgba(INK_HI, 252))
    return img, pts
