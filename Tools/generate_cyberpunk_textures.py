"""
Cyberpunk-fantasy repaint of the character texture set.

Reads every source texture in Assets/Textures/Characters and writes, into one folder per
character, a recoloured "Cyber" albedo plus a matching emission map.

The originals are left untouched: nothing here overwrites or moves them, so the existing
materials keep working and the new maps are opt-in.

Usage:  python Tools/generate_cyberpunk_textures.py [--preview]

The look is built in three passes:
  1. Duotone-by-luminance. The original shading is preserved (it carries all the hand-painted
     form) but its colours are remapped onto a dark tech ramp that ends in the character's neon
     accent. A slice of the untouched original is blended back so each character still reads as
     itself instead of a flat neon silhouette.
  2. Detail boost. A light unsharp on luminance keeps the toon-shaded edges crisp after the
     tonal squash.
  3. Neon decals. Circuit traces / panel cuts / vias, masked to *flat* areas of the texture
     (low local variance) so they never scribble over faces, straps or other painted detail.
     The same decals, and only they, drive the emission map.
"""

import argparse
import os
import random
import zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "com.unity.multiplayer.samples.coop", "Assets", "Textures", "Characters",
)

# Per-character duotone ramp + neon accents.
#   ramp:   (position 0-1, rgb) stops applied to the original luminance
#   accent: the main neon, used for traces and the hottest highlights
#   accent2: secondary neon, used for pads/vias and a hint of rim
PALETTES = {
    "Archer": {
        "ramp": [(0.00, (14, 20, 36)), (0.28, (30, 54, 86)), (0.55, (58, 116, 148)),
                 (0.80, (64, 206, 226)), (1.00, (214, 252, 255))],
        "accent": (0, 229, 255), "accent2": (124, 255, 63),
    },
    "Mage": {
        "ramp": [(0.00, (22, 14, 38)), (0.28, (56, 30, 86)), (0.55, (112, 60, 150)),
                 (0.80, (226, 86, 206)), (1.00, (255, 220, 250))],
        "accent": (255, 43, 214), "accent2": (123, 92, 255),
    },
    "Rogue": {
        "ramp": [(0.00, (12, 24, 24)), (0.28, (26, 58, 56)), (0.55, (50, 116, 100)),
                 (0.80, (86, 224, 150)), (1.00, (222, 255, 236))],
        "accent": (57, 255, 122), "accent2": (0, 210, 199),
    },
    "Tank": {
        "ramp": [(0.00, (26, 18, 14)), (0.28, (62, 42, 28)), (0.55, (130, 90, 46)),
                 (0.80, (255, 178, 60)), (1.00, (255, 240, 210))],
        "accent": (255, 176, 32), "accent2": (255, 77, 28),
    },
    "Boss": {
        "ramp": [(0.00, (24, 10, 22)), (0.28, (62, 20, 46)), (0.55, (128, 34, 72)),
                 (0.80, (240, 60, 100)), (1.00, (255, 216, 226))],
        "accent": (255, 23, 68), "accent2": (176, 38, 255),
    },
    "Imp": {
        "ramp": [(0.00, (14, 22, 12)), (0.28, (34, 58, 26)), (0.55, (72, 120, 42)),
                 (0.80, (176, 238, 50)), (1.00, (240, 255, 208))],
        "accent": (166, 255, 0), "accent2": (0, 255, 200),
    },
    "VandalImp": {
        "ramp": [(0.00, (24, 10, 24)), (0.28, (60, 20, 52)), (0.55, (126, 38, 84)),
                 (0.80, (246, 72, 152)), (1.00, (255, 220, 238))],
        "accent": (255, 46, 136), "accent2": (255, 138, 0),
    },
    "Hero": {
        "ramp": [(0.00, (14, 20, 36)), (0.28, (30, 54, 86)), (0.55, (58, 116, 148)),
                 (0.80, (64, 206, 226)), (1.00, (214, 252, 255))],
        "accent": (0, 229, 255), "accent2": (124, 255, 63),
    },
    "Enemy": {
        "ramp": [(0.00, (24, 10, 22)), (0.28, (62, 20, 46)), (0.55, (128, 34, 72)),
                 (0.80, (240, 60, 100)), (1.00, (255, 216, 226))],
        "accent": (255, 40, 70), "accent2": (176, 38, 255),
    },
}

# Per-part tuning. "traces" is the density multiplier of the circuitry, "keep" how much of the
# untouched original survives the duotone (the higher, the more it still looks like the source
# character), "glow" scales the emission.
PART_STYLE = {
    "torso":   {"traces": 1.00, "keep": 0.34, "glow": 1.00, "gamma": 1.10, "tint": 0.00},
    "helmet":  {"traces": 0.85, "keep": 0.32, "glow": 1.00, "gamma": 1.10, "tint": 0.00},
    "weapon":  {"traces": 0.70, "keep": 0.30, "glow": 1.10, "gamma": 1.10, "tint": 0.05},
    # skin: a few implants, not a whole PCB
    "head":    {"traces": 0.20, "keep": 0.48, "glow": 0.60, "gamma": 1.00, "tint": 0.05},
    # hair is a neon dye job: gamma < 1 pulls dark hair up into the bright end of the ramp,
    # otherwise brown/black hair just grades to near-black and the character loses a silhouette
    "hair":    {"traces": 0.00, "keep": 0.22, "glow": 1.15, "gamma": 0.58, "tint": 0.28},
    "sheet":   {"traces": 0.00, "keep": 0.40, "glow": 1.20, "gamma": 0.90, "tint": 0.10},
}


def classify(name):
    low = name.lower()
    if "eyes" in low or "mouths" in low:
        return "sheet"
    for key in ("torso", "helmet", "weapon", "head", "hair"):
        if key in low:
            return key
    return "torso"


def character_of(name):
    stem = name.split("_")[0]
    return stem if stem in PALETTES else "Archer"


# --------------------------------------------------------------------------------------
# numpy helpers
# --------------------------------------------------------------------------------------

def box_mean(a, radius):
    """Mean over a (2r+1)^2 window, via summed-area table. Edges use edge padding."""
    # float64 throughout: a 1024x1024 summed-area table overflows float32's precision long
    # before the last row, which shows up as banding in the flatness mask.
    pad = np.pad(a.astype(np.float64), radius + 1, mode="edge")
    integral = pad.cumsum(0).cumsum(1)
    size = 2 * radius + 1
    h, w = a.shape
    y0, x0 = 0, 0
    total = (integral[y0 + size:y0 + size + h, x0 + size:x0 + size + w]
             - integral[y0:y0 + h, x0 + size:x0 + size + w]
             - integral[y0 + size:y0 + size + h, x0:x0 + w]
             + integral[y0:y0 + h, x0:x0 + w])
    return total / float(size * size)


def flatness_mask(lum, radius):
    """1 where the texture is a flat expanse (safe to decorate), 0 over painted detail."""
    mean = box_mean(lum, radius)
    var = np.maximum(box_mean(lum * lum, radius) - mean * mean, 0.0)
    std = np.sqrt(var)
    # std of ~0.02 and below is "flat"; by 0.09 we consider it detail and back off entirely.
    return np.clip(1.0 - (std - 0.02) / 0.07, 0.0, 1.0)


def erode(mask, radius):
    """Shrink a 0/1 mask by `radius` pixels (PIL's MinFilter needs an odd kernel)."""
    size = max(3, int(radius) * 2 + 1)
    img = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.MinFilter(size)), dtype=np.float32) / 255.0


def background_mask(rgb):
    """
    1 on the atlas's unused backdrop, 0 on the packed UV islands.

    These textures pack their islands onto a single flat fill colour. Painting decals out there
    is not just wasted — mipmapping and bilinear filtering pull those texels into the island
    edges, so a neon line sitting next to an island bleeds onto the model's silhouette.
    """
    quantised = (rgb * 8.0).astype(np.int32)
    codes = quantised[..., 0] * 81 + quantised[..., 1] * 9 + quantised[..., 2]
    counts = np.bincount(codes.ravel(), minlength=9 * 81 + 9 * 9 + 9)
    dominant = int(counts.argmax())
    if counts[dominant] < 0.12 * codes.size:
        return np.zeros(codes.shape, dtype=np.float32)  # no single fill colour: nothing to avoid

    # Average the bucket's actual pixels rather than using the bucket's corner: a 1/8-wide bucket
    # puts its corner up to 0.125 away from the real fill colour, which is further than the
    # tolerance below and made this mask silently match nothing.
    fill = rgb[codes == dominant].mean(axis=0)
    distance = np.abs(rgb - fill[None, None, :]).max(axis=2)
    return (distance < 0.08).astype(np.float32)


def decal_mask(rgb, lum, alpha, scale):
    """Where circuitry is allowed: flat, opaque, on-island areas, pulled in from the edges."""
    allowed = flatness_mask(lum, max(2, int(round(3 * scale))))
    allowed *= 1.0 - background_mask(rgb)
    if alpha is not None:
        allowed *= (alpha > 0.5).astype(np.float32)
    # Keep a margin from every island border so filtering can't smear the neon off the model.
    return allowed * erode((allowed > 0.15).astype(np.float32), max(2, int(round(3 * scale))))


def ramp_lookup(lum, stops):
    positions = np.array([s[0] for s in stops], dtype=np.float32)
    out = np.empty(lum.shape + (3,), dtype=np.float32)
    for c in range(3):
        values = np.array([s[1][c] / 255.0 for s in stops], dtype=np.float32)
        out[..., c] = np.interp(lum, positions, values)
    return out


def luminance(rgb):
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114


def screen(base, top):
    return 1.0 - (1.0 - base) * (1.0 - top)


# --------------------------------------------------------------------------------------
# decals
# --------------------------------------------------------------------------------------

def draw_circuitry(size, seed, density):
    """A layer of orthogonal traces, panel cuts and vias. Returns (core, pads) as float masks."""
    w, h = size
    scale = max(w, h) / 512.0
    core = Image.new("L", size, 0)
    pads = Image.new("L", size, 0)
    dc, dp = ImageDraw.Draw(core), ImageDraw.Draw(pads)
    rng = random.Random(seed)

    line_w = max(1, int(round(3 * scale)))
    step = max(8, int(round(18 * scale)))

    # Panel cuts: a few large rounded rectangles, outline only.
    for _ in range(int(round(5 * density))):
        pw = rng.randint(int(60 * scale), int(190 * scale))
        ph = rng.randint(int(50 * scale), int(170 * scale))
        x = rng.randint(0, max(1, w - pw))
        y = rng.randint(0, max(1, h - ph))
        dc.rounded_rectangle([x, y, x + pw, y + ph],
                             radius=int(10 * scale), outline=255, width=line_w)

    # Traces: orthogonal random walks snapped to a grid, with a via at each end.
    for _ in range(int(round(16 * density))):
        x = rng.randrange(0, w, step)
        y = rng.randrange(0, h, step)
        dp.ellipse([x - 3 * scale, y - 3 * scale, x + 3 * scale, y + 3 * scale], fill=255)
        horizontal = rng.random() < 0.5
        for _ in range(rng.randint(2, 6)):
            run = rng.randint(1, 5) * step * rng.choice((-1, 1))
            nx = x + (run if horizontal else 0)
            ny = y + (0 if horizontal else run)
            nx = int(np.clip(nx, 0, w - 1))
            ny = int(np.clip(ny, 0, h - 1))
            dc.line([x, y, nx, ny], fill=255, width=line_w)
            x, y, horizontal = nx, ny, not horizontal
        dp.ellipse([x - 4 * scale, y - 4 * scale, x + 4 * scale, y + 4 * scale], fill=255)

    # Data strips: short dashed runs, the small "readout" detail.
    for _ in range(int(round(10 * density))):
        x = rng.randrange(0, w)
        y = rng.randrange(0, h)
        horizontal = rng.random() < 0.5
        for i in range(rng.randint(4, 10)):
            off = i * int(7 * scale)
            x0 = x + (off if horizontal else 0)
            y0 = y + (0 if horizontal else off)
            x1 = x0 + (int(4 * scale) if horizontal else line_w)
            y1 = y0 + (line_w if horizontal else int(4 * scale))
            dp.rectangle([x0, y0, x1, y1], fill=200)

    return (np.asarray(core, dtype=np.float32) / 255.0,
            np.asarray(pads, dtype=np.float32) / 255.0)


def blurred(mask, radius):
    img = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32) / 255.0


# --------------------------------------------------------------------------------------
# main conversion
# --------------------------------------------------------------------------------------

def convert(path, out_albedo, out_emissive):
    name = os.path.basename(path)
    part = classify(name)
    style = PART_STYLE[part]
    palette = PALETTES[character_of(name)]
    accent = np.array(palette["accent"], dtype=np.float32) / 255.0
    accent2 = np.array(palette["accent2"], dtype=np.float32) / 255.0

    src = Image.open(path)
    alpha = None
    if src.mode in ("RGBA", "LA") or "transparency" in src.info:
        src = src.convert("RGBA")
        alpha = np.asarray(src.split()[-1], dtype=np.float32) / 255.0
    rgb = np.asarray(src.convert("RGB"), dtype=np.float32) / 255.0
    h, w = rgb.shape[:2]
    scale = max(w, h) / 512.0

    lum = luminance(rgb)

    # 1. duotone ------------------------------------------------------------------------
    # Normalise into the ramp's range first: the source art is bright and evenly lit, so fed in
    # raw it all lands in the top third of the ramp.
    graded = np.clip((lum - 0.06) / 0.86, 0.0, 1.0) ** style["gamma"]
    duotone = ramp_lookup(graded, palette["ramp"])

    # Blend back toward the untouched original. A full repaint turned every character into a
    # flat neon silhouette; keeping a third of the source is what makes it read as *this*
    # character wearing cyberpunk colours rather than a coloured blob.
    out = duotone * (1.0 - style["keep"]) + rgb * style["keep"]
    if style["tint"] > 0.0:
        out = screen(out, accent[None, None, :] * style["tint"])

    # 2. detail boost -------------------------------------------------------------------
    detail = lum - blurred(lum, 2.0 * scale)
    out += detail[..., None] * 0.35

    # Highlight lift: screen (not replace) the accent into the brightest tones, so painted
    # highlights read as lit-from-inside instead of being flooded with one flat colour.
    rim = np.clip((graded - 0.86) / 0.14, 0.0, 1.0)
    out = screen(np.clip(out, 0.0, 1.0), accent[None, None, :] * rim[..., None] * 0.45)

    # Blending duotone with the source averages two different hues and the result goes muddy;
    # a saturation push afterwards puts the colour back.
    out_lum = luminance(out)[..., None]
    out = np.clip(out_lum + (out - out_lum) * 1.35, 0.0, 1.0)

    # 3. decals -------------------------------------------------------------------------
    emissive = np.zeros_like(out)
    if style["traces"] > 0.0:
        # crc32, not hash(): string hashing is salted per process, and the decal layout has to
        # be identical every time the script is run or every re-run repaints the whole set.
        core, pads = draw_circuitry((w, h), seed=zlib.crc32(name.encode()), density=style["traces"])
        mask = decal_mask(rgb, lum, alpha, scale)
        core *= mask
        pads *= mask

        lines = np.maximum(core, pads)
        glow = blurred(lines, 4.0 * scale) * style["glow"]

        out = screen(out, glow[..., None] * accent[None, None, :] * 0.5)
        out = screen(out, blurred(pads, 2.0 * scale)[..., None] * accent2[None, None, :] * 0.45)
        out = out * (1.0 - lines[..., None] * 0.55) + \
            np.minimum(accent * 1.5, 1.0)[None, None, :] * lines[..., None] * 0.55
        out = np.clip(out, 0.0, 1.0)

        emissive = (lines[..., None] * np.minimum(accent * 1.3, 1.0)[None, None, :]
                    + glow[..., None] * accent[None, None, :] * 0.4)

    # Highlights emit a little on every map, decals or not — that's what keeps the hair,
    # eyes and mouth sheets glowing.
    emissive += rim[..., None] * accent[None, None, :] * 0.45 * style["glow"]
    emissive = np.clip(emissive, 0.0, 1.0)

    def save(array, path_out, with_alpha):
        data = (np.clip(array, 0, 1) * 255).astype(np.uint8)
        image = Image.fromarray(data, "RGB")
        if with_alpha and alpha is not None:
            image.putalpha(Image.fromarray((alpha * 255).astype(np.uint8), "L"))
        os.makedirs(os.path.dirname(path_out), exist_ok=True)
        image.save(path_out)

    save(out, out_albedo, True)
    save(emissive, out_emissive, True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", action="store_true",
                        help="also write a contact sheet next to this script")
    args = parser.parse_args()

    sources = sorted(f for f in os.listdir(SRC_DIR)
                     if f.lower().endswith((".tga", ".png")) and not f.endswith(".meta"))

    written = []
    for filename in sources:
        character = character_of(filename)
        # "_CLR" is not always the suffix (Mage_Torso_CLR_Girl), so strip it wherever it sits
        # instead of only at the end, or the output ends up named ..._CLR_Girl_Cyber_CLR.
        stem = os.path.splitext(filename)[0].replace("_CLR", "")
        out_dir = os.path.join(SRC_DIR, character)
        albedo = os.path.join(out_dir, f"{stem}_Cyber_CLR.png")
        emissive = os.path.join(out_dir, f"{stem}_Cyber_EMIS.png")
        convert(os.path.join(SRC_DIR, filename), albedo, emissive)
        written.append(albedo)
        print(f"{filename:32s} -> {character}/{os.path.basename(albedo)}")

    print(f"\n{len(written)} albedo + {len(written)} emission maps written under {SRC_DIR}")

    if args.preview:
        cols = 8
        cell = 128
        rows = (len(written) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * cell, rows * cell), (12, 12, 16))
        for i, path in enumerate(written):
            thumb = Image.open(path).convert("RGBA").resize((cell, cell), Image.LANCZOS)
            # composite over grey: judging the sheets on black hides everything dark
            backdrop = Image.new("RGBA", thumb.size, (90, 90, 96, 255))
            thumb = Image.alpha_composite(backdrop, thumb).convert("RGB")
            sheet.paste(thumb, ((i % cols) * cell, (i // cols) * cell))
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "cyberpunk_preview.png")
        sheet.save(out)
        print(f"preview -> {out}")


if __name__ == "__main__":
    main()
