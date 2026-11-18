"""Frames for the compact health bars (src/ClassicUO.Client/IEX/IexHealthBar.cs), from the
same Midjourney art as the old-style bars (statusbar-mj.webp), at the old bars' scale.

Each frame is 125x61: the stone band with its gold trim, corner studs and the four centre
studs, around a plain dark stone middle. The client stretches it to any size: corners and
studs keep their shape, the stretches between them grow (IexHealthBar.FrameArt).
  middle (inside the inner gold line): x 9..115, y 7..52

  compactbar          your bar, black stone
  compactbar-war      your bar in war mode, red stone
  compactbar-other    others, grey stone (plus a copy per notoriety, stone coloured)

    python make_compactbar.py
"""
import gzip
import os
from collections import deque

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = Image.open(os.path.join(HERE, 'statusbar-mj.webp')).convert('RGB')
SIZE = (125, 61)
INNER = (9, 7, 116, 53)  # the middle, x0, y0, x1, y1 (exclusive)


def bar(y):
    return SRC.crop((54, y, 841, y + 385)).resize(SIZE, Image.LANCZOS)


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def is_gold(c):
    return max(c[:3]) - min(c[:3]) > 38 and c[0] > c[1] > c[2]


def key_outside(img, threshold=16):
    """Near-black reaching the edge (the rounded corners' background) becomes transparent."""
    img = img.convert('RGBA')
    p = img.load()
    w, h = img.size
    seen = set()
    q = deque((x, y) for x in range(w) for y in (0, h - 1))
    q.extend((x, y) for y in range(h) for x in (0, w - 1))
    while q:
        x, y = q.popleft()
        if (x, y) in seen or not (0 <= x < w and 0 <= y < h):
            continue
        seen.add((x, y))
        c = p[x, y]
        if lum(c) > threshold:
            continue
        p[x, y] = (0, 0, 0, 0)
        q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return img


def in_middle(x, y):
    return INNER[0] <= x < INNER[2] and INNER[1] <= y < INNER[3]


def recolor(img, color, gain=1.15):
    """The band's stone takes a colour (by its brightness); gold trim and the middle stay."""
    out = img.copy()
    p = out.load()
    for y in range(out.height):
        for x in range(out.width):
            c = p[x, y]
            if c[3] == 0 or is_gold(c) or in_middle(x, y):
                continue
            v = lum(c) / 255
            p[x, y] = tuple(min(255, int(k * v * gain)) for k in color) + (255,)
    return out


def quant15(img):
    p = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = p[x, y]
            p[x, y] = (r & 0xF8, g & 0xF8, b & 0xF8, a)
    return img


NOTORIETY = {  # as make_status.py
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
    dark = key_outside(bar(481))   # black stone, plain middle
    grey = key_outside(bar(930))   # grey stone (its middle has slots)

    # the grey frame gets the black frame's plain middle
    gp, dp = grey.load(), dark.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            if in_middle(x, y):
                gp[x, y] = dp[x, y]

    out = {
        'compactbar': dark,
        'compactbar-war': recolor(dark, (255, 72, 70), gain=1.3),
        'compactbar-other': grey,
    }
    for n, c in NOTORIETY.items():
        out['compactbar-' + n] = recolor(grey, c)

    for n, im in out.items():
        save_raw(quant15(im), n)
        im.resize((im.width * 4, im.height * 4), Image.NEAREST).save(os.path.join(HERE, n + '-raw.png'))
    print('ok')
