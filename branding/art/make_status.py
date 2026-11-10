"""Dark status window and status bars from the Midjourney art (status-mj.webp,
statusbar-mj.webp), fitted onto the stock layouts, which the client places its numbers,
lock arrows, buttons and bars on by pixel.

  status window  gump 0x2A6C, 427x184: frame x 32..395, y 21..161; grid rows 67/95/123/151,
                 columns 53/119/184/255/329, resist column to 383; minimize hit box 389,152
  your bar       0x0803 peace, 0x0807 war (red copy), 154x60: bars at x=34, y=12/25/38
  others' bars   0x0804 (grey) plus coloured copies for each notoriety, 154x60:
                 name at 16,14 (120x15), one bar at 34,38

The stat icons are lifted from the stock status art (its grey stone flood-filled away).
The art is stretched piecewise: frame bands keep their scale, the parts between them
stretch, so corners and studs stay in shape.

    python make_status.py <stock-dump-dir> <text-dir>
  stock-dump-dir: s2A6C.png (the stock status art, dumped from the UO files)
  text-dir: title.rgba (ASCII 4 "Character Status"), L_H/L_M/L_S.rgba (ASCII 1 letters)
"""
import gzip
import os
import struct
import sys
from collections import deque

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
DUMP, TXT = sys.argv[1], sys.argv[2]

GOLD_HI = (248, 216, 112)
GOLD = (224, 168, 48)
GOLD_LO = (136, 88, 16)
EDGE = (6, 6, 8)


# ------------------------------------------------------------------ helpers

def remap(src, xs, ys, size):
    """Separable piecewise-linear resample: control points xs/ys are (source, target)
    pairs; each target pixel averages the source box it covers."""
    def build(points, n):
        edges = []
        for t in range(n + 1):
            for (s0, t0), (s1, t1) in zip(points, points[1:]):
                if t0 <= t <= t1:
                    edges.append(s0 + (s1 - s0) * (t - t0) / max(1e-9, t1 - t0))
                    break
            else:
                edges.append(points[-1][0])
        return edges

    ex, ey = build(xs, size[0]), build(ys, size[1])
    out = Image.new('RGB', size)
    op = out.load()
    sp = src.load()
    for ty in range(size[1]):
        y0, y1 = int(ey[ty]), max(int(ey[ty]) + 1, int(round(ey[ty + 1])))
        for tx in range(size[0]):
            x0, x1 = int(ex[tx]), max(int(ex[tx]) + 1, int(round(ex[tx + 1])))
            r = g = b = n = 0
            for y in range(y0, min(y1, src.height)):
                for x in range(x0, min(x1, src.width)):
                    c = sp[x, y]
                    r += c[0]; g += c[1]; b += c[2]; n += 1
            op[tx, ty] = (r // n, g // n, b // n) if n else (0, 0, 0)
    return out


def nine(src, box, border, size, tborder):
    """9-slice a source box to size: corners scaled to tborder, edges/middle stretched."""
    x0, y0, x1, y1 = box
    return remap(src.crop(box), [(0, 0), (border, tborder), (x1 - x0 - border, size[0] - tborder), (x1 - x0, size[0])],
                 [(0, 0), (border, tborder), (y1 - y0 - border, size[1] - tborder), (y1 - y0, size[1])], size)


def lum(c):
    return (c[0] * 3 + c[1] * 6 + c[2]) // 10


def key_outside(img, threshold=16):
    """RGB -> RGBA with the black around the art (connected to the border) transparent."""
    img = img.convert('RGBA')
    p = img.load()
    w, h = img.size
    q = deque([(x, y) for x in range(w) for y in (0, h - 1)] + [(x, y) for y in range(h) for x in (0, w - 1)])
    seen = set()
    while q:
        x, y = q.popleft()
        if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
            continue
        seen.add((x, y))
        if lum(p[x, y]) < threshold:
            p[x, y] = (0, 0, 0, 0)
            q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return img


def is_gold(c):
    return max(c[:3]) - min(c[:3]) > 38 and c[0] > c[1] > c[2]


def recolor(img, color, gain=1.15):
    """Stone takes a colour (by its brightness); the gold trim is left alone."""
    out = img.copy()
    p = out.load()
    for y in range(out.height):
        for x in range(out.width):
            c = p[x, y]
            if c[3] == 0 or is_gold(c):
                continue
            v = lum(c) / 255
            p[x, y] = tuple(min(255, int(k * v * gain)) for k in color) + (255,)
    return out


def text(name):
    with open(os.path.join(TXT, name + '.rgba'), 'rb') as f:
        w, h = struct.unpack('<ii', f.read(8))
        data = struct.unpack('<%dI' % (w * h), f.read(4 * w * h))
    return w, h, data


def gold_ramp(t):
    t = max(0.0, min(1.0, t))
    stops = [(0, GOLD_LO), (0.55, GOLD), (1, GOLD_HI)]
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if t <= b:
            k = (t - a) / (b - a)
            return tuple(int(ca[i] + (cb[i] - ca[i]) * k) for i in range(3))
    return GOLD_HI


def blit_text(img, name, x, y, center_w=None):
    """Draw a dumped text in gold."""
    w, h, data = text(name)
    if center_w is not None:
        x += (center_w - w) // 2
    p = img.load()
    for j in range(h):
        for i in range(w):
            c = data[j * w + i]
            if (c >> 24) == 0:
                continue
            v = max(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF) / 255
            p[x + i, y + j] = ((8, 6, 6) if v < 0.12 else gold_ramp((v - 0.12) / 0.6)) + (255,)


def quant15(img):
    p = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = p[x, y]
            p[x, y] = (0, 0, 0, 0) if a < 128 else (
                (r >> 3) * 255 // 31, (g >> 3) * 255 // 31, (b >> 3) * 255 // 31, 255)
    return img


# ------------------------------------------------------------------ status window

ROWS = [67, 95, 123, 151]
COLS = [53, 119, 184, 255, 329]
RESIST = (329, 383)


def stock_icons():
    """The stock status art with its grey stone flood-filled away, leaving only the icons."""
    im = Image.open(os.path.join(DUMP, 's2A6C.png')).convert('RGBA')
    p = im.load()
    keep = Image.new('RGBA', im.size, (0, 0, 0, 0))
    kp = keep.load()

    def stone(c):
        r, g, b = c[:3]
        return max(r, g, b) - min(r, g, b) <= 8 and 128 <= (r * 3 + g * 6 + b) // 10 <= 215

    cells = [(COLS[i], ROWS[j], COLS[i + 1], ROWS[j + 1]) for i in range(4) for j in range(3)]
    cells.append((RESIST[0], ROWS[0], RESIST[1], ROWS[3]))
    for x0, y0, x1, y1 in cells:
        x0, y0, x1, y1 = x0 + 2, y0 + 2, x1 - 1, y1 - 1
        seen = set()
        q = deque((x, y) for x in range(x0, x1) for y in (y0, y1 - 1))
        q.extend((x, y) for y in range(y0, y1) for x in (x0, x1 - 1))
        while q:
            x, y = q.popleft()
            if (x, y) in seen or not (x0 <= x < x1 and y0 <= y < y1):
                continue
            seen.add((x, y))
            if stone(p[x, y]) or (p[x, y][0] + p[x, y][1] + p[x, y][2]) < 120 and (
                    x in (x0, x1 - 1) or y in (y0, y1 - 1)):
                q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
        for y in range(y0, y1):
            for x in range(x0, x1):
                if (x, y) not in seen:
                    kp[x, y] = p[x, y]

    # Stone shut inside an icon (between the weights, in the ankh's loop, between the
    # stacked resist icons) is not reached from the cell edge: clear those pockets too.
    pockets = [(COLS[0], ROWS[1], COLS[1], ROWS[2]), (COLS[1], ROWS[2], COLS[2], ROWS[3]),
               (RESIST[0], 92, RESIST[1], ROWS[3])]
    for x0, y0, x1, y1 in pockets:
        done = set()
        for sy in range(y0, y1):
            for sx in range(x0, x1):
                if (sx, sy) in done or kp[sx, sy][3] == 0 or not stone(kp[sx, sy]):
                    continue
                comp, q = [], deque([(sx, sy)])
                while q:
                    x, y = q.popleft()
                    if (x, y) in done or not (x0 <= x < x1 and y0 <= y < y1):
                        continue
                    if kp[x, y][3] == 0 or not stone(kp[x, y]) or sum(kp[x, y][:3]) < 3 * 160:
                        continue
                    done.add((x, y))
                    comp.append((x, y))
                    q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
                if len(comp) >= 4:
                    for x, y in comp:
                        kp[x, y] = (0, 0, 0, 0)

    # The stock icons carry a grey drop shadow and grey anti-aliasing for the light stone,
    # which shows as a ragged halo on black: peel off light grey edge pixels, then give
    # each icon a thin dark outline so its edge reads clean.
    w, h = keep.size

    def edge(x, y):
        return any(0 <= x + dx < w and 0 <= y + dy < h and kp[x + dx, y + dy][3] == 0
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))

    for _ in range(2):
        peel = []
        for y in range(h):
            for x in range(w):
                c = kp[x, y]
                if c[3] and max(c[:3]) - min(c[:3]) <= 18 and 85 <= lum(c) <= 165 and edge(x, y):
                    peel.append((x, y))
        for x, y in peel:
            kp[x, y] = (0, 0, 0, 0)

    lone = [(x, y) for y in range(h) for x in range(w) if kp[x, y][3] and not any(
        0 <= x + dx < w and 0 <= y + dy < h and (dx or dy) and kp[x + dx, y + dy][3]
        for dx in (-1, 0, 1) for dy in (-1, 0, 1))]
    for x, y in lone:  # stray single pixels
        kp[x, y] = (0, 0, 0, 0)

    ring = []
    for y in range(h):
        for x in range(w):
            if kp[x, y][3] == 0 and any(0 <= x + dx < w and 0 <= y + dy < h and kp[x + dx, y + dy][3]
                                        for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                ring.append((x, y))
    for x, y in ring:
        kp[x, y] = (5, 4, 6, 255)
    return keep


def status():
    src = Image.open(os.path.join(HERE, 'status-mj.webp')).convert('RGB')
    W, H = 427, 184
    # Frame and title band: the outer bands keep their scale (~0.14), the rest stretches.
    base = remap(src,
                 [(0, 0), (99, 32), (136, 37), (1553, 390), (1593, 395), (1680, 427)],
                 [(0, 0), (49, 21), (86, 26), (201, 46), (628, 156), (668, 161), (720, 184)],
                 (W, H))
    img = key_outside(base)

    # Body: plain stone from inside the tall cell, tiled at the art's scale.
    t = src.crop((1375, 275, 1495, 565))
    t = t.resize((t.width * 3 // 10, t.height * 3 // 10), Image.LANCZOS)
    tile = Image.new('RGB', (t.width * 2, t.height * 2))  # mirrored, so no seams
    tile.paste(t, (0, 0))
    tile.paste(t.transpose(Image.FLIP_LEFT_RIGHT), (t.width, 0))
    tile.paste(t.transpose(Image.FLIP_TOP_BOTTOM), (0, t.height))
    tile.paste(t.transpose(Image.ROTATE_180), (t.width, t.height))
    for ty in range(47, 156, tile.height):
        for tx in range(38, 390, tile.width):
            img.paste(tile.crop((0, 0, min(tile.width, 390 - tx), min(tile.height, 156 - ty))), (tx, ty))
    d = ImageDraw.Draw(img)

    # The art's thin gold lines are lost when scaled down: redraw them where they map to.
    d.line((37, 26, 390, 26), fill=GOLD)
    d.line((37, 26, 37, 156), fill=GOLD)
    d.line((390, 26, 390, 156), fill=GOLD_LO)
    d.line((37, 156, 390, 156), fill=GOLD_LO)
    d.line((37, 45, 390, 45), fill=GOLD_HI)
    d.line((37, 46, 390, 46), fill=GOLD_LO)
    for x in range(60, 368):  # a gold rule under the name, fading toward the ends
        k = 1 - abs((x - 214) / 154) ** 2
        base = img.getpixel((x, 64))
        img.putpixel((x, 64), tuple(int(base[i] + (GOLD[i] - base[i]) * k) for i in range(3)) + (255,))

    # Stat cells: the art's first cell, 9-sliced to each stock cell; the tall cell for resists.
    for i in range(4):
        for j in range(3):
            x0, y0, x1, y1 = COLS[i], ROWS[j], COLS[i + 1], ROWS[j + 1]
            img.paste(nine(src, (148, 208, 380, 415), 30, (x1 - x0 - 1, y1 - y0 - 1), 3), (x0, y0))
    img.paste(nine(src, (1320, 208, 1550, 622), 30, (RESIST[1] - RESIST[0], ROWS[3] - ROWS[0] - 1), 3),
              (RESIST[0], ROWS[0]))

    img.alpha_composite(stock_icons())
    blit_text(img, 'title', 37, 25, center_w=353)

    # Minimize: a gold ring with a bar, where the stock "restore" knob was.
    cx, cy, r = 397, 160, 11
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=EDGE)
    d.ellipse((cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1), outline=GOLD_HI, width=2)
    d.ellipse((cx - r + 3, cy - r + 3, cx + r - 3, cy + r - 3), fill=(28, 26, 30), outline=GOLD_LO)
    d.rectangle((cx - 5, cy - 1, cx + 5, cy + 1), fill=GOLD_HI)
    return quant15(img)


# ------------------------------------------------------------------ status bars

BARS = Image.open(os.path.join(HERE, 'statusbar-mj.webp')).convert('RGB')
FRAME_X = [(0, 0), (56, 9)]
FRAME_X_END = [(729, 146), (787, 154)]


def own_bar():
    """Black bar with three slots (hits, mana, stamina) and a strip for the letters."""
    src = BARS.crop((54, 31, 841, 416))
    img = key_outside(remap(src,
                            FRAME_X + [(82, 12), (134, 31), (158, 34), (692, 143)] + FRAME_X_END,
                            [(0, 0), (52, 8), (73, 12), (142, 23), (157, 25), (226, 36), (241, 38),
                             (313, 49), (334, 52), (385, 60)],
                            (154, 60)))
    for name, y in (('L_H', 10), ('L_M', 23), ('L_S', 36)):
        w = text(name)[0]
        blit_text(img, name, 13 + (18 - w) // 2 + 1, y)
    return img


def other_bar():
    """Grey bar: the name slot on top, one bar below (with its H)."""
    src = BARS.crop((54, 930, 841, 1315))
    img = key_outside(remap(src,
                            FRAME_X + [(100, 13), (680, 144)] + FRAME_X_END,
                            [(0, 0), (52, 8), (80, 12), (150, 31), (225, 37), (295, 50), (334, 52),
                             (385, 60)],
                            (154, 60)))
    blit_text(img, 'L_H', 17, 36)
    return img


NOTORIETY = {  # file name: stone colour
    'innocent': (90, 140, 255),
    'ally': (90, 220, 110),
    'enemy': (255, 150, 60),
    'murderer': (255, 70, 60),
    'invulnerable': (255, 225, 70),
}


def save_raw(img, name):
    with gzip.open(os.path.join(HERE, name + '.rgba.gz'), 'wb') as f:
        f.write(img.tobytes())


if __name__ == '__main__':
    out = {'status': status()}
    own, other = own_bar(), other_bar()
    out['statusbar'] = quant15(own)
    out['statusbar-war'] = quant15(recolor(own, (255, 72, 70), gain=1.3))
    out['statusbar-other'] = quant15(other)
    for n, c in NOTORIETY.items():
        out['statusbar-' + n] = quant15(recolor(other, c))
    for n, im in out.items():
        save_raw(im, n)
        im.save(os.path.join(HERE, n + '-raw.png'))
    print('ok')
