"""Generates the textures for every gas and delivery method.

    <python with Pillow + numpy> tools/ChemicalWarfare/gen_sprites.py

Everything is drawn here, in one flat style (dark outline, brushed metal, coloured band with a glyph per gas):
- canisters, gas traps and vents;
- hand grenades, 40x46mm grenades, 81mm shells (each in three stack sizes, CE's _a/_b/_c naming),
  their in-flight projectiles and the landed, venting casing.

Writes HSK Chemical Warfare/Common/Textures/Things/PGR/...
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from gas_data import GASES  # noqa: E402

OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "HSK Chemical Warfare", "Common", "Textures", "Things", "PGR"))

SS = 4                      # supersampling factor
OUTLINE = (24, 24, 26, 255)


def save(img, rel):
    path = os.path.join(OUT, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def rgb(c, k=1.0):
    return tuple(int(max(0, min(255, round(v * 255 * k)))) for v in c)


# ---------------------------------------------------------------- drawing helpers

def canvas(px=128):
    return Image.new("RGBA", (px * SS, px * SS), (0, 0, 0, 0))


def finish(img, px=128):
    return img.resize((px, px), Image.LANCZOS)


def outlined(layer, width=6):
    """Puts a dark outline under everything drawn on layer."""
    alpha = layer.split()[3]
    grown = alpha.filter(ImageFilter.MaxFilter(width * 2 + 1))
    base = Image.new("RGBA", layer.size, OUTLINE)
    base.putalpha(grown)
    base.alpha_composite(layer)
    return base


def shaded_fill(layer, mask, color, x0, x1, highlight=0.3):
    """Fills mask with color, lit from the upper left like the Gas Pack art."""
    w, h = layer.size
    xs = np.arange(w, dtype=np.float32)
    t = np.clip((xs - x0) / max(x1 - x0, 1), 0, 1)
    shade = 0.62 + 0.55 * np.exp(-((t - highlight) / 0.22) ** 2) - 0.18 * t
    shade = np.tile(shade, (h, 1))
    m = np.array(mask, dtype=np.float32) / 255.0
    arr = np.array(layer).astype(np.float32)
    for ch in range(3):
        arr[..., ch] = arr[..., ch] * (1 - m) + np.clip(color[ch] * shade, 0, 255) * m
    arr[..., 3] = np.maximum(arr[..., 3], m * 255)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def shape_mask(size, draw_fn):
    m = Image.new("L", size, 0)
    draw_fn(ImageDraw.Draw(m))
    return m


def draw_glyph(d, glyph, cx, cy, s, fill):
    w = max(2, int(s * 0.16))
    if glyph == "zigzag":
        pts = [(cx - s, cy + s * 0.4), (cx - s * 0.5, cy - s * 0.5), (cx, cy + s * 0.4),
               (cx + s * 0.5, cy - s * 0.5), (cx + s, cy + s * 0.4)]
        d.line(pts, fill=fill, width=w, joint="curve")
    elif glyph == "spores":
        for ox, oy in ((-0.5, 0.35), (0.5, 0.35), (0, -0.45)):
            r = s * 0.32
            d.ellipse([cx + ox * s - r, cy + oy * s - r, cx + ox * s + r, cy + oy * s + r], fill=fill)
    elif glyph == "gear":
        r = s * 0.62
        for k in range(8):
            a = k * math.pi / 4
            tx, ty = cx + math.cos(a) * r * 1.25, cy + math.sin(a) * r * 1.25
            d.ellipse([tx - s * 0.2, ty - s * 0.2, tx + s * 0.2, ty + s * 0.2], fill=fill)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)
        h = s * 0.25
        d.ellipse([cx - h, cy - h, cx + h, cy + h], fill=(0, 0, 0, 0))
    elif glyph == "drop":
        r = s * 0.6
        d.ellipse([cx - r, cy - r * 0.2, cx + r, cy + r * 1.0], fill=fill)
        d.polygon([(cx - r * 0.92, cy + r * 0.25), (cx, cy - s * 1.0), (cx + r * 0.92, cy + r * 0.25)], fill=fill)
    elif glyph == "moon":
        r = s * 0.9
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)
        d.ellipse([cx - r * 0.45, cy - r * 1.05, cx + r * 1.4, cy + r * 0.75], fill=(0, 0, 0, 0))
    elif glyph == "hex":
        pts = [(cx + math.cos(math.pi / 3 * k) * s, cy + math.sin(math.pi / 3 * k) * s) for k in range(6)]
        d.polygon(pts, outline=fill, width=w)
        d.line([(cx, cy - s * 0.5), (cx, cy + s * 0.5)], fill=fill, width=w)
    elif glyph == "cross":
        d.line([(cx - s * 0.8, cy - s * 0.8), (cx + s * 0.8, cy + s * 0.8)], fill=fill, width=int(w * 1.4))
        d.line([(cx - s * 0.8, cy + s * 0.8), (cx + s * 0.8, cy - s * 0.8)], fill=fill, width=int(w * 1.4))
    elif glyph == "shield":
        pts = [(cx - s * 0.8, cy - s * 0.85), (cx + s * 0.8, cy - s * 0.85), (cx + s * 0.8, cy),
               (cx, cy + s * 0.95), (cx - s * 0.8, cy)]
        d.polygon(pts, outline=fill, width=w)
        d.line([(cx, cy - s * 0.85), (cx, cy + s * 0.9)], fill=fill, width=w)
    elif glyph == "chip":
        r = s * 0.55
        d.rectangle([cx - r, cy - r, cx + r, cy + r], outline=fill, width=w)
        for k in (-0.5, 0, 0.5):
            for side in (-1, 1):
                d.line([(cx + k * s, cy + side * r), (cx + k * s, cy + side * s)], fill=fill, width=max(1, w // 2))
                d.line([(cx + side * r, cy + k * s), (cx + side * s, cy + k * s)], fill=fill, width=max(1, w // 2))
        d.ellipse([cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3], fill=fill)
    elif glyph == "leaf":
        # pointed leaf on the diagonal, its midrib cut out
        ang = math.radians(-45)
        ca, sa = math.cos(ang), math.sin(ang)
        pts = []
        for k in range(25):
            u = -1 + 2 * k / 24
            pts.append((u, 0.42 * (1 - u * u)))
        pts += [(u, -v) for u, v in reversed(pts)]
        d.polygon([(cx + (u * ca - v * sa) * s, cy + (u * sa + v * ca) * s) for u, v in pts], fill=fill)
        d.line([(cx - ca * s * 0.75, cy - sa * s * 0.75), (cx + ca * s * 0.95, cy + sa * s * 0.95)],
               fill=(0, 0, 0, 0), width=max(1, w // 2))
    elif glyph == "bug":
        d.ellipse([cx - s * 0.42, cy - s * 0.35, cx + s * 0.42, cy + s * 0.95], fill=fill)      # abdomen
        d.ellipse([cx - s * 0.26, cy - s * 0.78, cx + s * 0.26, cy - s * 0.26], fill=fill)      # head
        lw = max(1, int(w * 0.75))
        for side in (-1, 1):
            for k, (y0, y1) in enumerate(((-0.05, -0.45), (0.25, 0.25), (0.55, 0.95))):
                d.line([(cx + side * s * 0.3, cy + y0 * s), (cx + side * s * 0.95, cy + y1 * s)], fill=fill, width=lw)
            d.line([(cx + side * s * 0.1, cy - s * 0.72), (cx + side * s * 0.45, cy - s * 1.05)], fill=fill, width=lw)


def band_color(g):
    """Colour of the marking band: the gas colour, or a stripe of its own (agent orange's orange drums)."""
    return g.stripe or g.color


def glyph_layer(size, glyph, cx, cy, s, color):
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw_glyph(ImageDraw.Draw(layer), glyph, cx, cy, s, color)
    return layer


METAL = (150, 154, 160)
METAL_DARK = (92, 95, 100)
BRASS = (196, 158, 72)


# ---------------------------------------------------------------- single sprites (512 px canvas)

def grenade_single(g):
    img = canvas()
    size = img.size
    x0, x1, y0, y1 = 176, 336, 190, 470
    body_col = rgb(g.color, 0.55)
    body = shape_mask(size, lambda d: d.rounded_rectangle([x0, y0, x1, y1], radius=46, fill=255))
    img = shaded_fill(img, body, body_col, x0, x1)
    band = shape_mask(size, lambda d: d.rectangle([x0, 268, x1, 388], fill=255))
    band = Image.fromarray(np.minimum(np.array(band), np.array(body)))
    img = shaded_fill(img, band, rgb(band_color(g), 1.0), x0, x1)
    img.alpha_composite(glyph_layer(size, g.glyph, (x0 + x1) / 2, 328, 40, (20, 20, 22, 230)))
    # fuse head and spoon
    cap = shape_mask(size, lambda d: d.polygon([(206, 192), (306, 192), (290, 128), (222, 128)], fill=255))
    img = shaded_fill(img, cap, METAL, 206, 306)
    spoon = shape_mask(size, lambda d: d.polygon([(286, 128), (318, 120), (356, 300), (338, 306)], fill=255))
    img = shaded_fill(img, spoon, METAL_DARK, 286, 356, 0.5)
    ring = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(ring).ellipse([150, 96, 222, 168], outline=METAL + (255,), width=14)
    img.alpha_composite(ring)
    return outlined(img, 7)


def launcher_single(g):
    img = canvas()
    size = img.size
    x0, x1 = 186, 326
    case = shape_mask(size, lambda d: d.rectangle([x0, 290, x1, 452], fill=255))
    img = shaded_fill(img, case, BRASS, x0, x1)
    rim = shape_mask(size, lambda d: d.rectangle([x0 - 10, 440, x1 + 10, 470], fill=255))
    img = shaded_fill(img, rim, (165, 128, 55), x0 - 10, x1 + 10)
    head = shape_mask(size, lambda d: (d.rectangle([x0 + 4, 200, x1 - 4, 296], fill=255),
                                       d.ellipse([x0 + 4, 120, x1 - 4, 290], fill=255)))
    img = shaded_fill(img, head, rgb(g.color, 0.95), x0, x1)
    band = shape_mask(size, lambda d: d.rectangle([x0 + 4, 262, x1 - 4, 290], fill=255))
    img = shaded_fill(img, band, rgb(g.stripe, 1.0) if g.stripe else rgb(g.color, 0.5), x0, x1)
    img.alpha_composite(glyph_layer(size, g.glyph, (x0 + x1) / 2, 210, 34, (20, 20, 22, 230)))
    return outlined(img, 7)


def mortar_single(g):
    img = canvas()
    size = img.size
    cx = 256
    fins = shape_mask(size, lambda d: (d.polygon([(cx - 74, 470), (cx - 30, 392), (cx + 30, 392), (cx + 74, 470)], fill=255),
                                       d.rectangle([cx - 26, 360, cx + 26, 470], fill=255)))
    img = shaded_fill(img, fins, METAL_DARK, cx - 74, cx + 74)
    body = shape_mask(size, lambda d: (d.ellipse([cx - 88, 70, cx + 88, 300], fill=255),
                                       d.polygon([(cx - 88, 190), (cx + 88, 190), (cx + 30, 388), (cx - 30, 388)], fill=255)))
    img = shaded_fill(img, body, rgb(g.color, 0.6), cx - 88, cx + 88)
    band = Image.fromarray(np.minimum(np.array(shape_mask(size, lambda d: d.rectangle([0, 132, 512, 168], fill=255))),
                                      np.array(body)))
    img = shaded_fill(img, band, rgb(g.stripe) if g.stripe else (225, 200, 60), cx - 88, cx + 88)
    img.alpha_composite(glyph_layer(size, g.glyph, cx, 236, 46, rgb(band_color(g), 1.15) + (255,)))
    fuze = shape_mask(size, lambda d: d.polygon([(cx - 22, 82), (cx + 22, 82), (cx + 12, 46), (cx - 12, 46)], fill=255))
    img = shaded_fill(img, fuze, METAL, cx - 22, cx + 22)
    return outlined(img, 7)


def landed_single(g):
    """The casing lying on the ground, venting: tinted mist under a tipped-over canister."""
    img = canvas()
    size = img.size
    mist = Image.new("RGBA", size, (0, 0, 0, 0))
    md = ImageDraw.Draw(mist)
    for r, a in ((230, 50), (170, 70), (110, 90)):
        md.ellipse([256 - r, 256 - r * 0.8, 256 + r, 256 + r * 0.8], fill=rgb(g.color, 1.0) + (a,))
    mist = mist.filter(ImageFilter.GaussianBlur(28))
    body = canvas()
    b = shape_mask(size, lambda d: d.rounded_rectangle([150, 220, 362, 300], radius=36, fill=255))
    body = shaded_fill(body, b, rgb(g.color, 0.5), 150, 362)
    cap = shape_mask(size, lambda d: d.rectangle([350, 228, 384, 292], fill=255))
    body = shaded_fill(body, cap, METAL, 350, 384)
    body = outlined(body, 6)
    mist.alpha_composite(body)
    return mist



# ---------------------------------------------------------------- canister, trap, vent

STEEL_LABEL_DARK = (20, 20, 22, 230)
SOIL = (96, 80, 62)


def canister_single(g):
    """A compressed-gas cylinder: steel body, shoulder painted in the gas colour, marked label band, valve."""
    img = canvas()
    size = img.size
    x0, x1 = 176, 336
    body = shape_mask(size, lambda d: d.rounded_rectangle([x0, 150, x1, 466], radius=54, fill=255))
    img = shaded_fill(img, body, METAL, x0, x1)
    shoulder = Image.fromarray(np.minimum(np.array(shape_mask(size, lambda d: d.rectangle([0, 140, 512, 228], fill=255))),
                                          np.array(body)))
    img = shaded_fill(img, shoulder, rgb(g.color, 0.9), x0, x1)
    band = Image.fromarray(np.minimum(np.array(shape_mask(size, lambda d: d.rectangle([0, 262, 512, 396], fill=255))),
                                      np.array(body)))
    img = shaded_fill(img, band, rgb(band_color(g), 1.0), x0, x1)
    d = ImageDraw.Draw(img)
    for y in (262, 396):
        d.line([(x0 + 6, y), (x1 - 6, y)], fill=OUTLINE, width=6)
    img.alpha_composite(glyph_layer(size, g.glyph, (x0 + x1) / 2, 329, 42, STEEL_LABEL_DARK))
    foot = shape_mask(size, lambda d: d.rounded_rectangle([x0 - 8, 446, x1 + 8, 474], radius=8, fill=255))
    img = shaded_fill(img, foot, METAL_DARK, x0 - 8, x1 + 8)
    neck = shape_mask(size, lambda d: d.rectangle([236, 112, 276, 156], fill=255))
    img = shaded_fill(img, neck, METAL_DARK, 236, 276)
    wheel = Image.new("RGBA", size, (0, 0, 0, 0))
    wd = ImageDraw.Draw(wheel)
    wd.ellipse([200, 86, 312, 124], outline=METAL + (255,), width=12)
    wd.line([(256, 88), (256, 122)], fill=METAL + (255,), width=10)
    img.alpha_composite(wheel)
    return outlined(img, 7)


def trap_single(g):
    """A canister rig half-buried in a mound, wired to a pressure plate."""
    img = canvas()
    size = img.size
    mound = shape_mask(size, lambda d: d.ellipse([40, 268, 472, 476], fill=255))
    img = shaded_fill(img, mound, SOIL, 40, 472, 0.35)
    d = ImageDraw.Draw(img)
    for cx, cy, r in ((110, 420, 10), (380, 440, 8), (300, 300, 7), (170, 330, 6)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(72, 60, 46, 255))
    plate = shape_mask(size, lambda d: d.rounded_rectangle([70, 352, 166, 412], radius=10, fill=255))
    img = shaded_fill(img, plate, METAL_DARK, 70, 166)
    d = ImageDraw.Draw(img)
    d.line([(166, 382), (204, 360), (226, 336)], fill=OUTLINE, width=8, joint="curve")
    can = shape_mask(size, lambda d: d.rounded_rectangle([206, 262, 434, 368], radius=42, fill=255))
    img = shaded_fill(img, can, METAL, 206, 434)
    band = Image.fromarray(np.minimum(np.array(shape_mask(size, lambda d: d.rectangle([278, 0, 362, 512], fill=255))),
                                      np.array(can)))
    img = shaded_fill(img, band, rgb(band_color(g), 1.0), 206, 434)
    img.alpha_composite(glyph_layer(size, g.glyph, 320, 315, 28, STEEL_LABEL_DARK))
    nozzle = shape_mask(size, lambda d: d.rectangle([428, 292, 462, 338], fill=255))
    img = shaded_fill(img, nozzle, METAL_DARK, 428, 462)
    d = ImageDraw.Draw(img)
    d.ellipse([224, 236, 252, 264], fill=rgb(g.color, 1.15) + (255,))
    d.line([(238, 262), (238, 280)], fill=OUTLINE, width=6)
    return outlined(img, 7)


def vent_single(g):
    """A powered floor vent: steel frame, slats, and a status panel in the gas colour."""
    img = canvas()
    size = img.size
    frame = shape_mask(size, lambda d: d.rounded_rectangle([28, 28, 484, 484], radius=36, fill=255))
    img = shaded_fill(img, frame, METAL_DARK, 28, 484)
    plate = shape_mask(size, lambda d: d.rounded_rectangle([64, 64, 448, 448], radius=20, fill=255))
    img = shaded_fill(img, plate, METAL, 64, 448)
    d = ImageDraw.Draw(img)
    for k in range(5):
        y = 90 + k * 46
        d.rounded_rectangle([96, y, 416, y + 24], radius=8, fill=(38, 40, 44, 255))
    panel = shape_mask(size, lambda d: d.rounded_rectangle([96, 338, 416, 432], radius=14, fill=255))
    img = shaded_fill(img, panel, rgb(band_color(g), 1.0), 96, 416)
    img.alpha_composite(glyph_layer(size, g.glyph, 256, 385, 34, STEEL_LABEL_DARK))
    d = ImageDraw.Draw(img)
    for cx, cy in ((46, 46), (466, 46), (46, 466), (466, 466)):
        d.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], fill=METAL + (255,), outline=OUTLINE, width=3)
    return outlined(img, 6)


def draw_pack_items():
    for g in GASES:
        save(finish(canister_single(g)), f"Canisters/{g.key}")
        save(finish(trap_single(g)), f"Traps/{g.key}")
        save(finish(vent_single(g)), f"Vents/{g.key}")

# ---------------------------------------------------------------- stacks and projectiles

def place(dst, sprite, scale, cx, cy, angle=0):
    s = sprite
    if angle:
        s = s.rotate(angle, resample=Image.BICUBIC, expand=False)
    w = int(s.width * scale)
    s = s.resize((w, w), Image.LANCZOS)
    dst.alpha_composite(s, (int(cx - w / 2), int(cy - w / 2)))


def stacks(single, layouts):
    out = {}
    for suffix, items in layouts.items():
        img = canvas()
        for scale, cx, cy, angle in items:
            place(img, single, scale, cx, cy, angle)
        out[suffix] = finish(img)
    return out


GRENADE_STACKS = {
    "a": [(0.9, 256, 256, 0)],
    "b": [(0.72, 190, 270, 8), (0.72, 322, 250, -6)],
    "c": [(0.62, 160, 300, 10), (0.62, 352, 300, -8), (0.62, 256, 210, 2)],
}
LAUNCHER_STACKS = {
    "a": [(0.8, 256, 256, 0)],
    "b": [(0.6, 170, 270, 0), (0.6, 256, 250, 0), (0.6, 342, 270, 0)],
    "c": [(0.5, 150, 320, 0), (0.5, 256, 320, 0), (0.5, 362, 320, 0),
          (0.5, 203, 200, 0), (0.5, 309, 200, 0), (0.5, 256, 110, 0)],
}
MORTAR_STACKS = {
    "a": [(0.95, 256, 256, 0)],
    "b": [(0.78, 186, 262, 6), (0.78, 326, 250, -4)],
    "c": [(0.66, 150, 290, 8), (0.66, 362, 290, -8), (0.66, 256, 220, 0)],
}


def draw_delivery():
    for g in GASES:
        gren, launch, mortar = grenade_single(g), launcher_single(g), mortar_single(g)
        for kind, single, layouts in (("Grenades", gren, GRENADE_STACKS), ("Ammo40mm", launch, LAUNCHER_STACKS),
                                      ("Shells81", mortar, MORTAR_STACKS)):
            for suffix, img in stacks(single, layouts).items():
                save(img, f"{kind}/{g.key}/{g.key}_{suffix}")
        # In-flight projectiles point north, like vanilla's.
        for name, single, scale in (("Grenade", gren, 0.55), ("Launcher", launch, 0.5), ("Mortar", mortar, 0.7)):
            img = canvas()
            place(img, single, scale, 256, 256)
            save(finish(img), f"Projectiles/{name}_{g.key}")
        save(finish(landed_single(g)), f"Landed/{g.key}")


def contact_sheet():
    tiles = []
    for g in GASES:
        row = [f"Canisters/{g.key}", f"Traps/{g.key}", f"Vents/{g.key}",
               f"Grenades/{g.key}/{g.key}_a", f"Grenades/{g.key}/{g.key}_c",
               f"Ammo40mm/{g.key}/{g.key}_a", f"Ammo40mm/{g.key}/{g.key}_c",
               f"Shells81/{g.key}/{g.key}_a", f"Shells81/{g.key}/{g.key}_c",
               f"Projectiles/Mortar_{g.key}", f"Landed/{g.key}"]
        tiles.append(row)
    cell = 96
    sheet = Image.new("RGBA", (cell * len(tiles[0]), cell * len(tiles)), (58, 60, 62, 255))
    for r, row in enumerate(tiles):
        for c, rel in enumerate(row):
            im = Image.open(os.path.join(OUT, rel + ".png")).convert("RGBA")
            im.thumbnail((cell - 6, cell - 6), Image.LANCZOS)
            sheet.alpha_composite(im, (c * cell + (cell - im.width) // 2, r * cell + (cell - im.height) // 2))
    path = os.path.join(os.environ.get("TEMP", os.path.dirname(__file__)), "pgr_sprite_sheet.png")
    sheet.save(path)
    print("contact sheet:", path)


if __name__ == "__main__":
    draw_pack_items()
    draw_delivery()
    contact_sheet()
    print("textures in", OUT)
