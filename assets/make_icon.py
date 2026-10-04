from PIL import Image, ImageDraw

def frame(n):
    s = 4  # supersample
    S = n * s
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = int(S * 0.22)
    d.rounded_rectangle([0, 0, S - 1, S - 1], r, fill=(18, 18, 24, 255), outline=(34, 197, 94, 255), width=max(s, S // 32))
    # checkbox
    bx0, by0, bx1, by1 = S * 0.22, S * 0.22, S * 0.78, S * 0.78
    d.rounded_rectangle([bx0, by0, bx1, by1], int(S * 0.1), fill=(34, 197, 94, 255))
    w = max(s * 2, int(S * 0.085))
    pts = [(S * 0.33, S * 0.51), (S * 0.45, S * 0.63), (S * 0.68, S * 0.37)]
    d.line(pts, fill=(255, 255, 255, 255), width=w, joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=(255, 255, 255, 255))
    return im.resize((n, n), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = frame(256)
big.save("taskpad.ico", sizes=[(n, n) for n in sizes], append_images=[frame(n) for n in sizes[:-1]])
big.save("taskpad.png")
