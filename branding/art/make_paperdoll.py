"""Builds the dark paperdoll frame from the Midjourney art (paperdoll-mj.webp) on the stock
paperdoll layout, which the client's code places things on (opening for the character, item
slots either side, button column at x=185, nameplate text at y=262, virtue button at 80,4).

Outputs (next to this script):
  paperdoll.rgba.gz  - 262x340 frame (gumps 0x07D0 and 0x07D1; stock is 262x324, the extra
                       16px make the nameplate hold three lines of name and title)
  virtue.rgba.gz     - 32x32 pentagram medallion (gump 0x0071, the virtue button)
  paperdoll-preview.png
Raw RGBA rows, gzipped; colors reduced to UO's 15-bit range.

    python make_paperdoll.py
"""
import gzip
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = Image.open(os.path.join(HERE, 'paperdoll-mj.webp')).convert('RGB')
W, H = 262, 340  # 16px taller than stock: a three-line nameplate (IEX)
random.seed(3)

GOLD_HI = (248, 216, 112)
GOLD = (224, 168, 48)
GOLD_LO = (136, 88, 16)
EDGE = (6, 6, 8)
VOID = (2, 2, 5)

# --- source pieces (coordinates in the 960x1200 art) -----------------------------------
STONE = SRC.crop((748, 120, 832, 1000))          # plain stone of the pillar
NAMEPLATE = SRC.crop((198, 1008, 728, 1140))     # gold-trimmed nameplate
GARGOYLE_L = SRC.crop((132, 18, 292, 150))
GARGOYLE_R = SRC.crop((634, 18, 744, 142))
MEDALLION = SRC.crop((388, 30, 532, 172))


def keyed(img, threshold=16):
    """Pure-black background of the art becomes transparent."""
    img = img.convert('RGBA')
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, _ = px[x, y]
            if max(r, g, b) < threshold:
                px[x, y] = (0, 0, 0, 0)
    return img


def stone_tile(scale):
    t = STONE.resize((max(1, int(STONE.width * scale)), max(1, int(STONE.height * scale))), Image.LANCZOS)
    return t


def fill_stone(canvas, mask, scale, ox=0, oy=0, gain=1.0):
    tile = stone_tile(scale)
    tp = tile.load()
    cp = canvas.load()
    mp = mask.load()
    for y in range(H):
        for x in range(W):
            if mp[x, y]:
                tx, ty = (x + ox) % (2 * tile.width), (y + oy) % (2 * tile.height)
                tx = tx if tx < tile.width else 2 * tile.width - 1 - tx  # mirrored tiling: no seams
                ty = ty if ty < tile.height else 2 * tile.height - 1 - ty
                r, g, b = tp[tx, ty]
                cp[x, y] = (min(255, int(r * gain)), min(255, int(g * gain)), min(255, int(b * gain)), 255)


def arch_mask(x0, y0, x1, y1, grow=0):
    """Round-topped arch: a semicircle on top of a rectangle."""
    m = Image.new('1', (W, H), 0)
    d = ImageDraw.Draw(m)
    x0 -= grow; y0 -= grow; x1 += grow; y1 += grow
    r = (x1 - x0) / 2
    d.ellipse((x0, y0, x1, y0 + 2 * r), fill=1)
    d.rectangle((x0, y0 + r, x1, y1), fill=1)
    return m


def outline(canvas, mask, color_lit, color_shade):
    """1px line on the inside edge of a mask: lit on top/left, shaded bottom/right."""
    mp = mask.load()
    cp = canvas.load()
    for y in range(H):
        for x in range(W):
            if not mp[x, y]:
                continue
            up = y == 0 or not mp[x, y - 1]
            left = x == 0 or not mp[x - 1, y]
            down = y == H - 1 or not mp[x, y + 1]
            right = x == W - 1 or not mp[x + 1, y]
            if up or left:
                cp[x, y] = color_lit + (255,)
            elif down or right:
                cp[x, y] = color_shade + (255,)


def ring(canvas, mask_outer, mask_inner, color_lit, color_shade):
    """Gold line between two masks (outer minus inner)."""
    mo, mi = mask_outer.load(), mask_inner.load()
    band = Image.new('1', (W, H), 0)
    bp = band.load()
    for y in range(H):
        for x in range(W):
            bp[x, y] = 1 if mo[x, y] and not mi[x, y] else 0
    outline(canvas, band, color_lit, color_shade)
    cp = canvas.load()
    for y in range(H):
        for x in range(W):
            if bp[x, y] and cp[x, y][:3] not in (color_lit, color_shade):
                cp[x, y] = GOLD + (255,)


def quant15(img):
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a < 128:
                px[x, y] = (0, 0, 0, 0)
            else:
                px[x, y] = ((r >> 3) * 255 // 31, (g >> 3) * 255 // 31, (b >> 3) * 255 // 31, 255)
    return img


def frame():
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    # Silhouette: body below y=28, plus the arch hood over the opening.
    body = Image.new('1', (W, H), 0)
    d = ImageDraw.Draw(body)
    d.rounded_rectangle((1, 26, W - 2, H - 2), radius=10, fill=1)
    hood = arch_mask(4, 6, 190, 260)
    bp, hp = body.load(), hood.load()
    for y in range(H):
        for x in range(W):
            if hp[x, y]:
                bp[x, y] = 1

    fill_stone(canvas, body, 0.45)

    # Button pillar on the right: slightly lighter stone, its own edge.
    pillar = Image.new('1', (W, H), 0)
    ImageDraw.Draw(pillar).rectangle((180, 30, W - 6, 252), fill=1)
    fill_stone(canvas, pillar, 0.45, ox=9, oy=60, gain=1.15)
    outline(canvas, pillar, (110, 110, 120), (10, 10, 12))

    # Opening for the character, with a stepped dark molding and the gold line around it.
    opening = arch_mask(22, 32, 172, 248)
    step = arch_mask(22, 32, 172, 248, grow=3)
    gold_out = arch_mask(22, 32, 172, 248, grow=7)
    gold_in = arch_mask(22, 32, 172, 248, grow=5)
    ring(canvas, gold_out, gold_in, GOLD_HI, GOLD_LO)
    sp, op = step.load(), opening.load()
    cp = canvas.load()
    for y in range(H):
        for x in range(W):
            if sp[x, y] and not op[x, y]:
                n = random.randint(-3, 3)
                cp[x, y] = (22 + n, 22 + n, 26 + n, 255)
            if op[x, y]:
                cp[x, y] = VOID + (255,)

    # Outer gold trim and dark edge on the whole silhouette.
    inner = Image.new('1', (W, H), 0)
    ip = inner.load()
    for y in range(H):
        for x in range(W):
            ip[x, y] = 1 if bp[x, y] and all(
                0 <= x + dx < W and 0 <= y + dy < H and bp[x + dx, y + dy]
                for dx, dy in ((-3, 0), (3, 0), (0, -3), (0, 3))) else 0
    inner2 = Image.new('1', (W, H), 0)
    i2 = inner2.load()
    for y in range(H):
        for x in range(W):
            i2[x, y] = 1 if bp[x, y] and all(
                0 <= x + dx < W and 0 <= y + dy < H and bp[x + dx, y + dy]
                for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1))) else 0
    for y in range(H):
        for x in range(W):
            if bp[x, y] and not i2[x, y]:
                cp[x, y] = EDGE + (255,)
    ring(canvas, inner2, inner, GOLD_HI, GOLD_LO)

    # Nameplate from the art, across the bottom (title text is drawn at 36,268, three lines).
    plate = NAMEPLATE.resize((234, 78), Image.LANCZOS).convert('RGBA')
    canvas.alpha_composite(plate, (14, 254))

    # Small gold diamond: the minimize button's spot (hit box 228,260 16x16).
    for dy in range(-6, 7):
        span = 6 - abs(dy)
        for dx in range(-span, span + 1):
            edge = abs(dx) == span
            cp[236 + dx, 268 + dy] = (GOLD_HI if dy < 0 else GOLD_LO) + (255,) if edge else (30, 30, 34, 255)

    # Gargoyles perched on the hood.
    gl = keyed(GARGOYLE_L).resize((60, 50), Image.LANCZOS)
    gr = keyed(GARGOYLE_R).resize((44, 50), Image.LANCZOS)
    canvas.alpha_composite(gl, (0, 0))
    canvas.alpha_composite(gr, (146, 0))
    return quant15(canvas)


def medallion():
    m = keyed(MEDALLION).resize((32, 32), Image.LANCZOS)
    mask = Image.new('L', (32, 32), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, 31, 31), fill=255)
    out = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    out.paste(m, (0, 0), mask)
    return quant15(out)


def save_raw(img, name):
    with gzip.open(os.path.join(HERE, name), 'wb') as f:
        f.write(img.tobytes())


if __name__ == '__main__':
    f = frame()
    v = medallion()
    save_raw(f, 'paperdoll.rgba.gz')
    save_raw(v, 'virtue.rgba.gz')
    prev = f.copy()
    prev.alpha_composite(v, (80, 4))
    bg = Image.new('RGBA', (W + 20, H + 20), (40, 60, 40, 255))
    bg.alpha_composite(prev, (10, 10))
    bg.resize((bg.width * 2, bg.height * 2), Image.NEAREST).save(os.path.join(HERE, 'paperdoll-preview.png'))
    print('ok')
