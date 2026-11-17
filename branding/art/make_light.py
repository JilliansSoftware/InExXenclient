"""Light (sandstone) copies of the dark painted art, for the Light window theme.

Reads the dark art built by make_paperdoll.py and make_status.py (*.rgba.gz next to this
script) and recolors it: the black stone becomes sandstone by its brightness, the gold trim
and gold lettering become dark bronze. Left as they are: the stat icons in the status window
(with their dark outline). The light paperdoll and virtue button are not made here: they
have their own art (make_paperdoll.py).
The war bar and the notoriety bars are tinted from the light bars, as the dark ones are from
the dark bars.

Outputs <name>-light.rgba.gz (copy them to src/ClassicUO.Client/IEX/Art) and light-preview.png.

    python make_light.py
"""
import gzip
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))

SIZES = {
    'paperdoll': (262, 340),
    'virtue': (32, 32),
    'status': (427, 184),
    'statusbar': (154, 60),
    'statusbar-other': (154, 60),
}

# Sandstone by brightness of the dark stone (0 = its black edges, 1 = its highlights).
SAND = [(0.00, (62, 40, 22)), (0.16, (124, 90, 56)), (0.36, (184, 148, 102)),
        (0.58, (222, 196, 150)), (0.80, (238, 218, 178)), (1.00, (252, 242, 214))]
# Bronze by brightness of the gold.
BRONZE = [(0.00, (48, 26, 8)), (0.55, (94, 52, 16)), (1.00, (140, 88, 32))]
OPENING = (22, 15, 10)  # the paperdoll's dark doorway


def ramp(stops, t):
    t = max(0.0, min(1.0, t))
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        if t <= b:
            k = (t - a) / (b - a)
            return tuple(int(ca[i] + (cb[i] - ca[i]) * k) for i in range(3))
    return stops[-1][1]


def lum(c):
    return (c[0] * 3 + c[1] * 6 + c[2]) // 10


def is_gold(c):
    return max(c[:3]) - min(c[:3]) > 38 and c[0] > c[1] > c[2]


def load(name):
    with gzip.open(os.path.join(HERE, name + '.rgba.gz'), 'rb') as f:
        return Image.frombytes('RGBA', SIZES[name], f.read())


def sandstone(img, keep=None, white=96, gamma=0.5):
    """keep: set of (x, y) left untouched."""
    out = img.copy()
    p = out.load()
    for y in range(out.height):
        for x in range(out.width):
            c = p[x, y]
            if c[3] == 0 or (keep and (x, y) in keep):
                continue
            if is_gold(c):
                p[x, y] = ramp(BRONZE, (max(c[:3]) - 96) / 150) + (255,)
            else:
                p[x, y] = ramp(SAND, (max(0, lum(c) - 5) / white) ** gamma) + (255,)
    return out


def tint(img, color):
    """Sandstone takes a notoriety colour; bronze and the dark slots stay."""
    out = img.copy()
    p = out.load()
    for y in range(out.height):
        for x in range(out.width):
            c = p[x, y]
            if c[3] == 0:
                continue
            v = lum(c)
            if v < 60 or (c[0] - c[2] > 70 and v < 110):  # slots, edges, bronze
                continue
            p[x, y] = tuple(min(255, int(c[i] * 0.5 + color[i] * v / 205 * 0.5)) for i in range(3)) + (255,)
    return out


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


def status_icons(img):
    """The stock stat icons: bright or coloured pixels inside the cells, plus their dark outline."""
    p = img.load()
    cells = [(COLS[i], ROWS[j], COLS[i + 1], ROWS[j + 1]) for i in range(4) for j in range(3)]
    cells.append((RESIST[0], ROWS[0], RESIST[1], ROWS[3]))
    icon = set()
    for x0, y0, x1, y1 in cells:
        for y in range(y0 + 3, y1 - 2):
            for x in range(x0 + 3, x1 - 2):
                c = p[x, y]
                if max(c[:3]) - min(c[:3]) > 30 or lum(c) > 100:
                    icon.add((x, y))
    ring = set()
    for x, y in icon:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                q = (x + dx, y + dy)
                if q not in icon and lum(p[q]) < 14:
                    ring.add(q)
    return icon | ring


# ------------------------------------------------------------------ paperdoll

def paperdoll_opening():
    """The arch the character stands in (make_paperdoll.py: arch_mask(22, 32, 172, 248))."""
    w, h = SIZES['paperdoll']
    m = Image.new('1', (w, h), 0)
    d = ImageDraw.Draw(m)
    x0, y0, x1, y1 = 22, 32, 172, 248
    r = (x1 - x0) / 2
    d.ellipse((x0, y0, x1, y0 + 2 * r), fill=1)
    d.rectangle((x0, y0 + r, x1, y1), fill=1)
    mp = m.load()
    return {(x, y) for y in range(h) for x in range(w) if mp[x, y]}


NOTORIETY = {
    'innocent': (90, 140, 255),
    'ally': (90, 220, 110),
    'enemy': (255, 150, 60),
    'murderer': (255, 70, 60),
    'invulnerable': (255, 225, 70),
}


if __name__ == '__main__':
    out = {}

    st = load('status')
    out['status'] = sandstone(st, keep=status_icons(st), white=70)

    # Your bar is dark all over (keep its slots darker than the stone); others' bars are grey.
    own = sandstone(load('statusbar'), gamma=0.7)
    other = sandstone(load('statusbar-other'), white=170, gamma=0.8)
    out['statusbar'] = own
    out['statusbar-war'] = tint(own, (255, 72, 70))
    out['statusbar-other'] = other
    for n, c in NOTORIETY.items():
        out['statusbar-' + n] = tint(other, c)

    for n, im in out.items():
        quant15(im)
        with gzip.open(os.path.join(HERE, n + '-light.rgba.gz'), 'wb') as f:
            f.write(im.tobytes())

    prev = Image.new('RGBA', (720, 560), (44, 66, 44, 255))
    prev.alpha_composite(out['status'], (285, 0))
    for i, n in enumerate(['statusbar', 'statusbar-war', 'statusbar-other'] + ['statusbar-' + k for k in NOTORIETY]):
        prev.alpha_composite(out[n], (290 + (i % 2) * 170, 200 + (i // 2) * 70))
    prev.resize((1440, 1120), Image.NEAREST).save(os.path.join(HERE, 'light-preview.png'))
    print('ok')
