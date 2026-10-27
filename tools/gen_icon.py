import struct, zlib, math, os

BASE = 256.0

def clamp01(v):
    return 0.0 if v < 0 else (1.0 if v > 1 else v)

def cov(d):
    # 1px antialias from signed distance
    return clamp01(0.5 - d)

def capsule(px, py, x1, y1, x2, y2, r):
    dx = x2 - x1; dy = y2 - y1
    den = dx*dx + dy*dy
    t = 0.0 if den == 0 else ((px-x1)*dx + (py-y1)*dy) / den
    t = 0.0 if t < 0 else (1.0 if t > 1 else t)
    return math.hypot(px - (x1 + t*dx), py - (y1 + t*dy)) - r

def circle(px, py, cx, cy, r):
    return math.hypot(px-cx, py-cy) - r

def rrect(px, py, cx, cy, hw, hh, r):
    qx = abs(px-cx) - (hw - r)
    qy = abs(py-cy) - (hh - r)
    ax = qx if qx > 0 else 0.0
    ay = qy if qy > 0 else 0.0
    return math.hypot(ax, ay) + (min(max(qx, qy), 0.0)) - r

def over(dst, src, a):
    if a <= 0:
        return dst
    sa = src[3] / 255.0 * a
    if sa <= 0:
        return dst
    da = dst[3] / 255.0
    oa = sa + da * (1 - sa)
    if oa <= 0:
        return (0, 0, 0, 0)
    r = (src[0]*sa + dst[0]*da*(1-sa)) / oa
    g = (src[1]*sa + dst[1]*da*(1-sa)) / oa
    b = (src[2]*sa + dst[2]*da*(1-sa)) / oa
    return (int(r+0.5), int(g+0.5), int(b+0.5), int(oa*255+0.5))

def lerp(c1, c2, t):
    return (int(c1[0]+(c2[0]-c1[0])*t), int(c1[1]+(c2[1]-c1[1])*t), int(c1[2]+(c2[2]-c1[2])*t), 255)

def render(size):
    s = size / BASE
    px_buf = []
    line_w = max(3.0*s, 1.0)
    ring_w = max(2.0*s, 0.9)
    for y in range(size):
        row = []
        for x in range(size):
            px = (x + 0.5) / s
            py = (y + 0.5) / s
            c = (0, 0, 0, 0)

            # 背景圆角方块
            d = rrect(px, py, 128, 128, 128, 128, 58)
            c = over(c, (24, 27, 34, 255), cov(d))

            # 外描边
            if s > 0.2:
                d2 = abs(rrect(px, py, 128, 128, 128, 128, 58)) - ring_w*0.5
                c = over(c, (52, 58, 70, 255), cov(d2) * 0.9)

            # 红色光晕
            dg = circle(px, py, 128, 176, 62)
            if dg < 0:
                a = (1.0 - (-dg)/62.0) ** 2.6 * 0.22
                c = over(c, (255, 64, 64, 255), a)

            # 温度计玻璃外壳
            body = min(
                capsule(px, py, 128, 62, 128, 152, 17),
                circle(px, py, 128, 176, 40)
            )
            c = over(c, (233, 236, 242, 255), cov(body))

            # 内腔（深色）
            hollow = min(
                capsule(px, py, 128, 62, 128, 176, 10.5),
                circle(px, py, 128, 176, 32)
            )
            c = over(c, (24, 27, 34, 255), cov(hollow))

            # 水银
            merc = min(
                capsule(px, py, 128, 106, 128, 176, 7.0),
                circle(px, py, 128, 176, 31)
            )
            mm = cov(merc)
            if mm > 0:
                t = clamp01((py - 100.0) / 90.0)
                col = lerp((255, 150, 70), (255, 66, 72), t)
                c = over(c, col, mm)

            # 刻度线
            if size >= 32:
                for i, ty in enumerate((92, 114, 136, 158)):
                    ln = 16 if i % 2 == 1 else 12
                    dt = capsule(px, py, 156, ty, 156+ln, ty, line_w*0.5)
                    c = over(c, (150, 157, 168, 255), cov(dt) * 0.85)

            row.append(c)
        px_buf.append(row)

    # 转 RGBA bytes
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            r, g, b, a = px_buf[y][x]
            raw += bytes((r, g, b, a))
    return bytes(raw)

def png_bytes(size, raw):
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t+d) & 0xffffffff)
    ihdr = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    comp = zlib.compress(bytes(raw), 9)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', comp) + chunk(b'IEND', b'')

sizes = [256, 128, 64, 48, 32, 16]
pngs = []
for sz in sizes:
    raw = render(sz)
    pngs.append((sz, png_bytes(sz, raw)))
    print('rendered', sz)

# 打包 ICO（PNG 内嵌）
n = len(pngs)
header = struct.pack('<HHH', 0, 1, n)
offset = 6 + 16*n
entries = b''
data = b''
for sz, png in pngs:
    w = 0 if sz >= 256 else sz
    entries += struct.pack('<BBBBHHII', w, w, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
    data += png

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'app.ico')
with open(out, 'wb') as f:
    f.write(header + entries + data)
print('ICO written:', out, os.path.getsize(out), 'bytes')

# 同时导出一张预览图
prev = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'icon_preview.png')
with open(prev, 'wb') as f:
    f.write(pngs[0][1])
print('preview:', prev)
