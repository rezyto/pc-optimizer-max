"""Generates pcmax.ico (16..256 px) and pcmax.png: a lightning bolt on a cyan->violet gradient.
Run: python3 make-icon.py  (needs Pillow). The output files are committed; this is only for regeneration."""
import os
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
S = 1024  # draw large, downscale for quality

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def render(size):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    cyan, violet = (0, 229, 255), (124, 77, 255)
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * S)
            r, g, b = lerp(cyan, violet, t)
            px[x, y] = (r, g, b, 255)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=230, fill=255)
    img.paste(grad, (0, 0), mask)
    # inner dark plate
    plate = Image.new("L", (S, S), 0)
    ImageDraw.Draw(plate).rounded_rectangle([96, 96, S - 96, S - 96], radius=170, fill=255)
    img.paste(Image.new("RGBA", (S, S), (14, 16, 28, 255)), (0, 0), plate)
    # bolt (glow + body)
    bolt = [(560, 140), (250, 590), (480, 590), (420, 890), (780, 400), (545, 400), (640, 140)]
    glow = Image.new("L", (S, S), 0)
    ImageDraw.Draw(glow).polygon(bolt, fill=200)
    glow = glow.filter(ImageFilter.GaussianBlur(40))
    img.paste(Image.new("RGBA", (S, S), (0, 229, 255, 255)), (0, 0), glow)
    bmask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(bmask).polygon(bolt, fill=255)
    img.paste(grad, (0, 0), bmask)
    hi = Image.new("L", (S, S), 0)
    ImageDraw.Draw(hi).polygon([(560, 140), (250, 590), (330, 590), (600, 170)], fill=110)
    img.paste(Image.new("RGBA", (S, S), (255, 255, 255, 255)), (0, 0), hi)
    return img.resize((size, size), Image.LANCZOS)

big = render(256)
big.save(os.path.join(HERE, "pcmax.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
big.save(os.path.join(HERE, "pcmax.png"))
print("ok")
