# -*- coding: utf-8 -*-
"""
Extractor de siluetas agnostico al estilo del icono de origen.

El set de VersusD mezcla tres convenciones distintas:
  a) glifo blanco sobre cuadrado de color      (ui_archer_atk viejo)
  b) glifo oscuro sobre transparente           (ui_revive, ui_checkmark)
  c) glifo oscuro dentro de un chip blanco     (ui_tank_atk)
Un umbral fijo de "brillante y poco saturado" solo resuelve (a) y devolvia
mascaras vacias para (b) y el cuadrado entero para (c).

Aca se estima el color de fondo y se marca lo que se aparta de el; si lo
marcado resulta ser otro bloque plano (caso c), se repite el proceso adentro.
"""
from PIL import Image, ImageFilter
from collections import Counter


def _dominant(pixels):
    """Color mas frecuente, cuantizado a 5 bits por canal."""
    c = Counter((r >> 3, g >> 3, b >> 3) for r, g, b in pixels)
    (r, g, b), _ = c.most_common(1)[0]
    return (r << 3, g << 3, b << 3)


def _far_from(img, sel, color, dist=62):
    """Mascara de los pixeles de `sel` que se alejan de `color`."""
    w, h = img.size
    px = img.load()
    out = Image.new('L', (w, h), 0)
    op = out.load()
    sp = sel.load()
    cr, cg, cb = color
    d2 = dist * dist
    for y in range(h):
        for x in range(w):
            if sp[x, y] < 128:
                continue
            r, g, b, _ = px[x, y]
            if (r - cr) ** 2 + (g - cg) ** 2 + (b - cb) ** 2 > d2:
                op[x, y] = 255
    return out


def _coverage(mask):
    """Fraccion del bbox de la mascara que esta efectivamente rellena."""
    bb = mask.getbbox()
    if not bb:
        return 0.0, 0.0
    area = (bb[2] - bb[0]) * (bb[3] - bb[1])
    if area == 0:
        return 0.0, 0.0
    filled = sum(1 for p in mask.crop(bb).getdata() if p > 127) / area
    frac = area / float(mask.size[0] * mask.size[1])
    return filled, frac


def extract_glyph(path, dist=62):
    im = Image.open(path).convert('RGBA')
    if max(im.size) > 256:                      # acelera el conteo
        im = im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
    w, h = im.size
    alpha = im.split()[3].point(lambda p: 255 if p > 40 else 0)
    ap = alpha.load()

    inside = [(x, y) for y in range(h) for x in range(w) if ap[x, y] > 127]
    if not inside:
        return Image.new('L', (w, h), 0)

    px = im.load()
    bg = _dominant([px[x, y][:3] for x, y in inside])
    mask = _clean(_far_from(im, alpha, bg, dist))

    a_area = len(inside)
    m_area = sum(1 for p in mask.getdata() if p > 127)

    if m_area < 0.02 * a_area:
        # nada se aparta del fondo: la forma opaca ya ES el glifo
        return _clean(alpha.copy()).filter(ImageFilter.GaussianBlur(0.4))

    if m_area < 0.22 * a_area and a_area < 0.90 * w * h:
        # solo quedo el contorno de una figura recortada (flecha del desplegable):
        # la silueta opaca describe mejor el simbolo que su filete
        return _clean(alpha.copy()).filter(ImageFilter.GaussianBlur(0.4))

    hole = _enclosed_hole(mask)
    if hole is not None:
        return hole.filter(ImageFilter.GaussianBlur(0.4))

    filled, frac = _coverage(mask)
    if filled > 0.90:
        mp = mask.load()
        sel = [(x, y) for y in range(h) for x in range(w) if mp[x, y] > 127]
        inner_bg = _dominant([px[x, y][:3] for x, y in sel])
        inner = _clean(_far_from(im, mask, inner_bg, dist))
        if sum(1 for p in inner.getdata() if p > 127) > 0.02 * m_area:
            mask = inner

    return mask.filter(ImageFilter.GaussianBlur(0.4))


def _clean(mask, border_frac=0.03, keep_frac=0.12):
    """Quita la franja de sombra del borde y las motas sueltas.

    Los iconos viejos traen una sombra en el borde inferior que se aparta del
    color de fondo tanto como el glifo, asi que entraba en la mascara y
    estiraba el bbox hasta ocupar toda la imagen.
    """
    w, h = mask.size
    px = mask.load()
    b = max(1, int(min(w, h) * border_frac))
    for y in range(h):
        for x in range(w):
            if x < b or y < b or x >= w - b or y >= h - b:
                px[x, y] = 0

    # etiquetado de componentes conexas (4-vecinos)
    lab = [[0] * w for _ in range(h)]
    areas, cur = {}, 0
    for y0 in range(h):
        for x0 in range(w):
            if px[x0, y0] < 128 or lab[y0][x0]:
                continue
            cur += 1
            stack, n = [(x0, y0)], 0
            lab[y0][x0] = cur
            while stack:
                x, y = stack.pop()
                n += 1
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and not lab[ny][nx] \
                            and px[nx, ny] >= 128:
                        lab[ny][nx] = cur
                        stack.append((nx, ny))
            areas[cur] = n

    if not areas:
        return mask
    top = max(areas.values())
    keep = {k for k, v in areas.items() if v >= keep_frac * top}
    for y in range(h):
        for x in range(w):
            if lab[y][x] and lab[y][x] not in keep:
                px[x, y] = 0
    return mask


def _enclosed_hole(mask, min_frac=0.10):
    """Devuelve el hueco cerrado mas grande dentro de la mascara, o None.

    Un "chip" (cuadrado claro con el simbolo recortado adentro) se reconoce
    justo por esto: el glifo queda como un agujero que no toca el borde.
    Medir cuan macizo es el bbox no alcanzaba, porque el chip con el hueco
    restado da 0.78 y un corazon solido da 0.65: demasiado cerca.
    """
    bb = mask.getbbox()
    if not bb:
        return None
    sub = mask.crop(bb)
    w, h = sub.size
    if w < 8 or h < 8:
        return None
    px = sub.load()
    seen = [[False] * w for _ in range(h)]
    best, best_n = None, 0
    for y0 in range(h):
        for x0 in range(w):
            if px[x0, y0] >= 128 or seen[y0][x0]:
                continue
            stack, comp, touches = [(x0, y0)], [], False
            seen[y0][x0] = True
            while stack:
                x, y = stack.pop()
                comp.append((x, y))
                if x == 0 or y == 0 or x == w - 1 or y == h - 1:
                    touches = True
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] \
                            and px[nx, ny] < 128:
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            if not touches and len(comp) > best_n:
                best, best_n = comp, len(comp)

    if not best or best_n < min_frac * w * h:
        return None
    out = Image.new('L', mask.size, 0)
    op = out.load()
    for x, y in best:
        op[x + bb[0], y + bb[1]] = 255
    return out
