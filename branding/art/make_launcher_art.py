"""Backdrop of the launcher window (src/ClassicUO.Bootstrap/src/IEX/Launcher/background.png, 960x640).

Stone, the bevelled gold frame, the banner (a dusk scene with the logo and the shard name) and
the empty boxes of the two panels. The window draws everything that changes on top. The banner
picture is launcher-banner.webp (924x220) next to this script; without it a drawn dusk scene is
used. The logo and title go on its left over a dark fade, the tabs on its bottom-left edge and the
link icons on its top-right.
Box positions here must match LauncherForm.cs.

    python make_launcher_art.py
"""
import math, os, random
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(REPO, 'src', 'ClassicUO.Bootstrap', 'src', 'IEX', 'Launcher', 'background.png')
LOGO = os.path.join(REPO, 'branding', 'logo-source.png')
PICTURE = os.path.join(HERE, 'launcher-banner.webp')
FONT = os.path.join(REPO, 'src', 'ClassicUO.Bootstrap', 'src', 'IEX', 'Launcher', 'Grenze.ttf')
W, H = 924, 220


def gold_text(im, x, y, s, font):
    dr = ImageDraw.Draw(im)
    bbox = dr.textbbox((x, y), s, font=font, anchor='lm')
    w, h = bbox[2] - bbox[0] + 8, bbox[3] - bbox[1] + 8
    mask = Image.new('L', (w, h), 0)
    ImageDraw.Draw(mask).text((x - bbox[0] + 4, y - bbox[1] + 4), s, font=font, fill=255, anchor='lm')
    grad = Image.new('RGB', (w, h))
    gp = grad.load()
    hi, mid, lo = (250, 222, 128), (226, 170, 52), (130, 84, 18)
    for yy in range(h):
        t = yy / max(1, h - 1)
        c = tuple(int(hi[i] + (mid[i] - hi[i]) * t * 2) if t < .5 else int(mid[i] + (lo[i] - mid[i]) * (t - .5) * 2) for i in range(3))
        for xx in range(w):
            gp[xx, yy] = c
    im.paste((8, 5, 4), (bbox[0] - 4, bbox[1] - 4), mask.filter(ImageFilter.MaxFilter(3)))
    im.paste(grad, (bbox[0] - 4, bbox[1] - 4), mask)


def dusk_scene():
    b = Image.new('RGB', (W, H))
    bp = b.load()
    top, mid, low = (18, 16, 34), (92, 46, 40), (210, 128, 52)
    for yy in range(H):
        t = yy / H
        c = tuple(int(top[i] + (mid[i] - top[i]) * t / .6) if t < .6 else int(mid[i] + (low[i] - mid[i]) * (t - .6) / .4) for i in range(3))
        for xx in range(W):
            bp[xx, yy] = c
    bd = ImageDraw.Draw(b)
    random.seed(9)
    for _ in range(80):
        sx, sy = random.randint(0, W), random.randint(0, int(H * .45))
        v = random.randint(140, 230)
        bd.point((sx, sy), fill=(v, v, v - 20))
    glow = Image.new('RGB', (W, H))
    ImageDraw.Draw(glow).ellipse((int(W * .62) - 90, H - 70, int(W * .62) + 90, H + 110), fill=(150, 90, 30))
    b = ImageChops.add(b, glow.filter(ImageFilter.GaussianBlur(30)))
    bd = ImageDraw.Draw(b)

    def ridge(base, amp, freq, seed, col):
        random.seed(seed)
        ph = [random.random() * 6 for _ in range(3)]
        pts = [(0, H)]
        for xx in range(0, W + 4, 4):
            pts.append((xx, base - amp * (0.6 * math.sin(xx * freq + ph[0]) + 0.3 * math.sin(xx * freq * 2.7 + ph[1]) + 0.15 * math.sin(xx * freq * 7 + ph[2]))))
        pts.append((W, H))
        bd.polygon(pts, fill=col)

    ridge(H * .62, 26, .006, 1, (54, 34, 42))
    ridge(H * .74, 20, .01, 2, (32, 22, 30))
    kx, ky = int(W * .78), int(H * .58)
    for dx, tw, th in [(0, 16, 46), (20, 26, 30), (50, 14, 40), (-18, 20, 26)]:
        bd.rectangle((kx + dx, ky - th, kx + dx + tw, ky + 20), fill=(26, 18, 26))
        for m in range(0, tw, 5):
            bd.rectangle((kx + dx + m, ky - th - 4, kx + dx + m + 2, ky - th), fill=(26, 18, 26))
    bd.rectangle((kx + 4, ky - 34, kx + 6, ky - 30), fill=(250, 190, 90))
    ridge(H * .9, 14, .014, 3, (14, 11, 15))
    random.seed(4)
    for _ in range(28):
        tx = random.randint(0, W)
        ty = int(H * .88) + random.randint(-6, 6)
        th = random.randint(14, 30)
        bd.polygon([(tx, ty - th), (tx - th // 4, ty), (tx + th // 4, ty)], fill=(12, 10, 13))

    return b


def banner():
    if os.path.exists(PICTURE):
        b = Image.open(PICTURE).convert('RGB')
        if b.size != (W, H):
            # cover the banner, cropping the overflow evenly
            k = max(W / b.width, H / b.height)
            b = b.resize((round(b.width * k), round(b.height * k)), Image.LANCZOS)
            x, y = (b.width - W) // 2, (b.height - H) // 2
            b = b.crop((x, y, x + W, y + H))
        # darken behind the logo and title, and a little behind the tabs, so they read on a busy picture
        shade = Image.new('L', (W, H), 0)
        sp = shade.load()
        for y in range(H):
            bottom = max(0.0, (y - (H - 60)) / 60) * 120
            for x in range(W):
                left = max(0.0, 1 - x / 470) ** 1.6 * 205
                sp[x, y] = int(min(225, left + bottom * max(0.0, 1 - x / 420)))
        b.paste((10, 8, 8), (0, 0), shade)
    else:
        b = dusk_scene()

    logo = Image.open(LOGO).convert('RGB')
    lw = 92
    logo = logo.resize((lw, int(logo.height * lw / logo.width)), Image.LANCZOS)
    lx, ly = 22, (H - 34 - logo.height) // 2
    b.paste(logo, (lx, ly), logo.convert('L').point(lambda v: min(255, v * 3)))
    gold_text(b, lx + lw + 18, ly + logo.height // 2, 'In Ex Xen', ImageFont.truetype(FONT, 58))
    return b


WW, WH = 960, 640
GOLD, GOLDLO, GOLDHI = (196, 148, 52), (112, 76, 22), (240, 204, 110)


def main():
    random.seed(3)
    im = Image.new('RGB', (WW, WH), (22, 21, 24))
    px = im.load()
    for y in range(WH):
        for x in range(WW):
            n = random.randint(-5, 5)
            px[x, y] = (22 + n, 21 + n, 25 + n)
    im = im.filter(ImageFilter.GaussianBlur(0.8))
    dr = ImageDraw.Draw(im)
    for i, c in enumerate([(10, 10, 12), (88, 86, 92), (64, 62, 68), (52, 50, 56), (40, 38, 44), (30, 29, 33)]):
        dr.rectangle((i, i, WW - 1 - i, WH - 1 - i), outline=c)
    for i, c in enumerate((GOLDLO, GOLDHI, GOLD, (46, 32, 10))):
        dr.rectangle((6 + i, 6 + i, WW - 7 - i, WH - 7 - i), outline=c)

    im.paste(banner(), (18, 18))
    dr.rectangle((18, 18, 941, 237), outline=GOLDLO)
    dr.line((18, 238, 941, 238), fill=GOLD)

    def panel(x0, y0, x1, y1, fill):
        dr.rectangle((x0, y0, x1, y1), fill=fill, outline=GOLDLO)
        dr.rectangle((x0 + 1, y0 + 1, x1 - 1, y1 - 1), outline=(48, 38, 20))

    panel(18, 288, 656, 621, (9, 9, 11))      # patch notes / shard list
    panel(670, 252, 941, 621, (12, 11, 13))   # shard, account, update, play
    im.save(OUT)
    print(OUT, im.size, os.path.getsize(OUT))


if __name__ == '__main__':
    main()
