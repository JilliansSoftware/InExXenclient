"""Builds the paperdoll frames from the Midjourney art: dark (black stone, fine gold lines,
winged gargoyles: paperdoll-dark-mj.webp) and light (sandstone, griffins:
paperdoll-light-mj.webp). The dark art is a retexture of the light one with the griffins
repainted as gargoyles, so both have the same layout.

The art already has the paperdoll's layout (arched opening on the left, plain block column for
the buttons on the right, nameplate across the bottom), so it is scaled whole. The scaling is
piecewise, to land its parts where the client's code puts things:
  opening for the character   light about 27..172 x 36..248, dark about 32..172 x 60..256
  button column               from x=188 (the buttons are drawn at x=185)
  nameplate panel             about 14..248 x 262..328 (the name is drawn at 36,268, three lines)
  minimize button             228,260 (16x16)
  virtue button               the art's own pentagram medallion: the button is that 32x32
                              piece of the frame, drawn again on top of it. Stock puts it at
                              80,4; the themes move it onto the medallion ('virtue' below, and
                              IexDarkUi.VirtuePosition in the client - keep them the same)
The dark art's gargoyles rise above the top ledge and past the left wall. It is fitted from
their tips, so they are whole; that puts its arch lower than the light one's.

Outputs (next to this script; copy the .gz files to src/ClassicUO.Client/IEX/Art):
  paperdoll.rgba.gz, paperdoll-light.rgba.gz   262x340 frame (gumps 0x07D0 and 0x07D1; stock
                                               is 262x324, the extra 16px hold a three-line name)
  virtue.rgba.gz, virtue-light.rgba.gz         32x32 (gump 0x0071)
  paperdoll-preview.png, paperdoll-light-preview.png
Raw RGBA rows, gzipped; colors reduced to UO's 15-bit range.

    python make_paperdoll.py [dir with the themes' dumped buttons (dark/, light/), for the previews]
"""
import gzip
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
W, H = 262, 340

GOLD_LO, GOLD, GOLD_HI = (136, 88, 16), (224, 168, 48), (248, 216, 112)

# Per theme: the art and its (source, target) control points; the rest is used by its builder.
THEMES = {
    'dark': dict(
        art='paperdoll-dark-mj.webp', suffix='',  # 1125x1543
        #   wing tip, opening,    opening,    column,     right wall
        xs=[(65, 0), (201, 32), (714, 172), (810, 188), (1077, W)],
        #   horn tips, arch springing, nameplate top, its panel,        bottom
        ys=[(84, 0), (585, 123), (1309, 256), (1332, 262), (1445, 328), (1483, H)],
        virtue=(87, 30),
        # in the art; the third box is the left one's snout and horn, which reach past x=400
        # beside the medallion (without it their right edge was traced as trim and went gold)
        gargoyles=[(60, 80, 400, 440), (522, 80, 850, 440), (400, 90, 415, 216)],
        arch_line=(457.5, 585, 278, 306),  # centre, and the radius range the line round the arch lies in
        plate=((126, 1309, 1049, 1458), (148, 1332, 1024, 1445)),  # nameplate: outer box, dark panel
        opening=(2, 2, 5),
        diamond=(GOLD_HI, GOLD_LO, (30, 30, 34), 270)),
    'light': dict(
        art='paperdoll-light-mj.webp', suffix='-light',  # 928x1232
        #   left edge, opening,   opening,    column,     right edge
        xs=[(88, 0), (170, 27), (557, 172), (630, 188), (836, W)],
        #   under the cornice top, arch springing, foot of the opening, nameplate panel, bottom
        ys=[(150, 0), (462, 107), (1003, 248), (1021, 262), (1112, 328), (1136, H)],
        virtue=(84, 10),
        opening=(22, 15, 10),
        diamond=((204, 150, 68), (104, 64, 22), (236, 216, 170), 270)),
}


# ------------------------------------------------------------------ scaling

def edges(points, n):
    """Source coordinate of each target pixel boundary 0..n."""
    out = []
    for t in range(n + 1):
        for (s0, t0), (s1, t1) in zip(points, points[1:]):
            if t <= t1:
                out.append(s0 + (s1 - s0) * (t - t0) / (t1 - t0))
                break
    return out


def target(points, s):
    """Where a source coordinate lands."""
    for (s0, t0), (s1, t1) in zip(points, points[1:]):
        if s <= s1:
            return t0 + (t1 - t0) * (s - s0) / (s1 - s0)
    return points[-1][1]


def scaled(src, xs, ys):
    """The art on the 262x340 layout: each pixel is the average of the source box it covers."""
    ex, ey = edges(xs, W), edges(ys, H)
    # Sum table, so any box average is four lookups.
    table = np.zeros((src.shape[0] + 1, src.shape[1] + 1, src.shape[2]))
    table[1:, 1:] = src.cumsum(0).cumsum(1)
    out = np.zeros((H, W, src.shape[2]))
    for y in range(H):
        y0, y1 = int(round(ey[y])), max(int(round(ey[y])) + 1, int(round(ey[y + 1])))
        for x in range(W):
            x0, x1 = int(round(ex[x])), max(int(round(ex[x])) + 1, int(round(ex[x + 1])))
            box = table[y1, x1] - table[y0, x1] - table[y1, x0] + table[y0, x0]
            out[y, x] = box / ((y1 - y0) * (x1 - x0))
    return out


def luma(rgb):
    return (rgb[..., 0] * 3 + rgb[..., 1] * 6 + rgb[..., 2]) / 10


def gold_ramp(t):
    """t 0..1 (array) -> dark to bright gold."""
    t = np.clip(t, 0, 1)[..., None]
    lo, mid, hi = (np.array(c, float) for c in (GOLD_LO, GOLD, GOLD_HI))
    return np.where(t < 0.6, lo + (mid - lo) * t / 0.6, mid + (hi - mid) * (t - 0.6) / 0.4)


# ------------------------------------------------------------------ light

def light(theme, src):
    raw = scaled(src, theme['xs'], theme['ys'])
    # The art is a greyish tan; lift and warm it to sit with the Light theme's sandstone.
    v = (raw / 255.0) ** 0.62
    v = v * (1.12 - 0.2 * v)  # hold the bright nameplate panel back from washing out
    rgb = np.clip(v * np.array([1.12, 1.06, 0.95]) * 255, 0, 255)
    rgb[luma(raw) < 16] = theme['opening']  # the frame fills the picture: black is the opening
    return rgb, np.full((H, W), 255)


# ------------------------------------------------------------------ dark

def dark(theme, src):
    lum = luma(src)

    # The art's trim is thin pale lines, nearly lost at game size. Find them where they are
    # painted (thin strokes much brighter than what surrounds them, outside the gargoyles),
    # and carry them through the scaling on their own so they can be drawn in gold.
    around = np.asarray(Image.fromarray(lum.astype(np.uint8)).filter(ImageFilter.BoxBlur(10))).astype(float)
    lines = (lum - around > 40) & (lum > 125)  # trim peaks at 140-200; block joints stay well under
    for x0, y0, x1, y1 in theme['gargoyles']:  # their highlights are as bright as the trim
        lines[y0:y1, x0:x1] = False

    # The line round the arch is painted faint and fades toward the springing. Trace it: at
    # each angle find the brightest point across the ring's edge, keep the points that lie on
    # one smooth curve (that drops whatever a gargoyle covers), and mark the line along it.
    cx, cy, lo, hi = theme['arch_line']
    angles = np.radians(np.arange(-92, 92.01, 0.25))
    radii = np.arange(lo, hi + 1)
    px = np.clip((cx + np.outer(np.sin(angles), radii)).round().astype(int), 0, src.shape[1] - 1)
    py = np.clip((cy - np.outer(np.cos(angles), radii)).round().astype(int), 0, src.shape[0] - 1)
    across = lum[py, px]
    peak = radii[across.argmax(1)].astype(float)
    smooth = np.array([np.median(peak[max(0, k - 30):k + 31]) for k in range(len(peak))])
    good = (np.abs(peak - smooth) <= 3) & (across.max(1) > 32) & (np.abs(np.degrees(angles)) > 13)  # not under the medallion
    # Short breaks are bridged along the smooth curve; long ones (a gargoyle) stay open.
    near = np.array([good[max(0, k - 16):k + 17].mean() for k in range(len(good))]) >= 0.5
    near &= np.abs(np.degrees(angles)) > 13
    for a, r in zip(angles[near], smooth[near]):
        x, y = cx + r * np.sin(a), cy - r * np.cos(a)
        lines[int(y - 1):int(y + 2.5), int(x - 1):int(x + 2.5)] = True

    # Gargoyles: darker, so they sit back into the stone.
    src = src.copy()
    for x0, y0, x1, y1 in theme['gargoyles']:
        k = 1 - 0.42 * np.clip((lum[y0:y1, x0:x1] - 30) / 25, 0, 1)
        src[y0:y1, x0:x1] *= k[..., None]

    raw = scaled(src, theme['xs'], theme['ys'])
    trim = scaled(lines[..., None].astype(float), theme['xs'], theme['ys'])[..., 0]
    lum = luma(raw)

    rgb = np.clip(raw * 1.3, 0, 255)          # the stone is very dark; lift it a little
    rgb[lum < 10] = theme['opening']

    # Nameplate: its border in gold (shaded as the art shades it), its panel left dark for
    # the gold lettering.
    xs, ys = theme['xs'], theme['ys']
    (ox0, oy0, ox1, oy1), (px0, py0, px1, py1) = theme['plate']
    yy, xx = np.mgrid[0:H, 0:W]

    def inside(x0, y0, x1, y1):
        return ((xx >= round(target(xs, x0))) & (xx < round(target(xs, x1)))
                & (yy >= round(target(ys, y0))) & (yy < round(target(ys, y1))))

    border = inside(ox0, oy0, ox1, oy1) & ~inside(px0, py0, px1, py1)
    rgb = np.where(border[..., None], gold_ramp(0.12 + (lum - 8) / 75), rgb)

    # The thin lines, in gold.
    k = np.clip(trim * 4.0, 0, 1)[..., None]
    rgb = rgb + (np.array(GOLD, float) - rgb) * k * ~border[..., None]

    # The gargoyles reach above the ledge and past the left wall: the black beyond them (from
    # the picture's edge to the first lit pixel) is the surround.
    alpha = np.full((H, W), 255)
    wall, ledge = int(target(xs, 101)), int(target(ys, 148))
    for y in range(H):
        for x in range(wall):
            if lum[y, x] > 5:
                break
            alpha[y, x] = 0
    for x in range(W):
        for y in range(ledge):
            if lum[y, x] > 5:
                break
            alpha[y, x] = 0
    return rgb, alpha


BUILD = {'dark': dark, 'light': light}


# ------------------------------------------------------------------ output

def frame(name):
    theme = THEMES[name]
    src = np.asarray(Image.open(os.path.join(HERE, theme['art'])).convert('RGB')).astype(np.float64)
    rgb, alpha = BUILD[name](theme, src)
    canvas = Image.fromarray(np.dstack([rgb, alpha]).astype(np.uint8), 'RGBA').copy()
    cp = canvas.load()

    # Small diamond: the minimize button's spot (hit box 228,260 16x16).
    lit, shade, fill, cy = theme['diamond']
    for dy in range(-6, 7):
        span = 6 - abs(dy)
        for dx in range(-span, span + 1):
            cp[236 + dx, cy + dy] = ((lit if dy < 0 else shade) if abs(dx) == span else fill) + (255,)
    return quant15(canvas)


def quant15(img):
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            px[x, y] = (0, 0, 0, 0) if a < 128 else ((r >> 3) * 255 // 31, (g >> 3) * 255 // 31, (b >> 3) * 255 // 31, 255)
    return img


def save_raw(img, name):
    with gzip.open(os.path.join(HERE, name), 'wb') as f:
        f.write(img.tobytes())


def dumped(folder, gid):
    p = os.path.join(folder, '%04X.rgba' % gid)
    if not os.path.exists(p):
        return None
    with open(p, 'rb') as f:
        w, h = struct.unpack('<ii', f.read(8))
        return Image.frombytes('RGBA', (w, h), f.read(w * h * 4))


if __name__ == '__main__':
    for name, theme in THEMES.items():
        f = frame(name)
        vx, vy = theme['virtue']
        v = f.crop((vx, vy, vx + 32, vy + 32))  # the virtue button: the same pixels as the frame under it
        save_raw(f, 'paperdoll%s.rgba.gz' % theme['suffix'])
        save_raw(v, 'virtue%s.rgba.gz' % theme['suffix'])

        prev = Image.new('RGBA', (2 * W + 30, H + 20), (44, 66, 44, 255))
        prev.alpha_composite(f, (10, 10))
        full = f.copy()
        if len(sys.argv) > 1:  # buttons as the client places them
            for i, b in enumerate((0x7EF, 0x7D6, 0x7D9, 0x7DC, 0x7DF, 0x7E5, 0x7EB)):
                im = dumped(os.path.join(sys.argv[1], name), b)
                if im is not None:
                    full.alpha_composite(im, (185, 44 + i * 27))
        prev.alpha_composite(full, (W + 20, 10))
        prev.resize((prev.width * 2, prev.height * 2), Image.NEAREST).save(
            os.path.join(HERE, 'paperdoll%s-preview.png' % theme['suffix']))
    print('ok')
