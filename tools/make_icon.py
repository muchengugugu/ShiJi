from PIL import Image, ImageDraw
import math, os

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
px = img.load()

# Rounded-square gradient background
r = 230
for y in range(S):
    t = y / (S - 1)
    # deep indigo -> violet
    c = (
        int(70 + (124 - 70) * t),
        int(61 + (92 - 61) * t),
        int(196 + (245 - 196) * t),
        255,
    )
    for x in range(S):
        # rounded mask
        cx = min(max(x, r), S - 1 - r)
        cy = min(max(y, r), S - 1 - r)
        if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
            px[x, y] = c

d = ImageDraw.Draw(img, "RGBA")

# soft highlight blob
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse((-260, -340, 700, 520), fill=(255, 255, 255, 38))
glow = glow.filter(__import__("PIL.ImageFilter", fromlist=["GaussianBlur"]).GaussianBlur(90))
img.alpha_composite(glow)
d = ImageDraw.Draw(img, "RGBA")

# clock ring
cx = cy = S / 2
outer = 326
d.ellipse((cx - outer, cy - outer, cx + outer, cy + outer), outline=(255, 255, 255, 245), width=58)
# inner faint ring
d.ellipse((cx - 260, cy - 260, cx + 260, cy + 260), outline=(255, 255, 255, 30), width=16)

# clock hands
d.line((cx, cy, cx, cy - 178), fill=(255, 255, 255, 255), width=58)
d.line((cx, cy, cx + 140, cy + 62), fill=(255, 255, 255, 255), width=58)
d.ellipse((cx - 48, cy - 48, cx + 48, cy + 48), fill=(255, 255, 255, 255))
d.ellipse((cx - 18, cy - 18, cx + 18, cy + 18), fill=(112, 84, 230, 255))

# 4 tick marks
for a in (0, 90, 180, 270):
    ang = math.radians(a - 90)
    x1 = cx + math.cos(ang) * 300
    y1 = cy + math.sin(ang) * 300
    x2 = cx + math.cos(ang) * 250
    y2 = cy + math.sin(ang) * 250
    d.line((x1, y1, x2, y2), fill=(255, 255, 255, 200), width=26)

# small trailing pulse dots lower-right
for i, (dx, dy, rr, aa) in enumerate([(375, 360, 30, 200), (430, 420, 18, 120)]):
    d.ellipse((cx + dx - rr, cy + dy - rr, cx + dx + rr, cy + dy + rr), fill=(255, 255, 255, aa))

out = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets")
os.makedirs(out, exist_ok=True)
img.save(os.path.join(out, "logo.png"))
img.resize((256, 256), Image.LANCZOS).save(
    os.path.join(out, "app.ico"),
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print("written", out, img.size)
