# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors
"""Generates Chippi's tray and app icon from Chippi's geometry (DESIGN.md section 7).

Writes Assets/chippi.ico next to this script: 16, 20, 24, 32, 40, 48, 64 and 256 px (BMP
frames, PNG at 256). Chippi.App uses it as ApplicationIcon and as a WPF resource the tray
loads its frames from.

Small form (body plus enlarged eyes) at 24 px and below; full detail (shine, eye highlights,
mouth) above; the ground shadow from 64 px up, as DESIGN.md section 7 "Sizes" says. Idle mood.

Pure standard library: a small scanline rasterizer with 16 sub-rows per pixel and exact
horizontal coverage, so no Pillow or Cairo is needed.

  python make_icon.py [--preview DIR]   (--preview also writes each frame as PNG, for review)
"""

import math
import os
import re
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SMALL_FORM_MAX = 24
SHADOW_MIN = 64
SUBROWS = 16

# DESIGN.md section 7, on the 120x120 viewBox.
BODY = "M17 76 C17 45 36 31 60 31 C84 31 103 45 103 76 C103 98 87 106 60 106 C33 106 17 98 17 76 Z"
BODY_BOUNDS = (20.0, 16.0, 100.0, 108.0)  # the gradient maps over the geometry's bounding box
VIOLET = (183, 143, 255)
AZURE = (161, 125, 224)
INK = (0x0B, 0x0F, 0x1F)
WHITE = (255, 255, 255)
BLACK = (0, 0, 0)
EYES = [(47.0, 74.0), (73.0, 74.0)]
EYE_RADII = (4.8, 6.2)
SMALL_EYE_RADII = (6.0, 7.4)
HIGHLIGHT_OFFSET = (1.7, -2.3)
HIGHLIGHT_R = 1.7
SHINE = (46.0, 46.0, 9.0, 5.5, -28.0, 0.38)  # cx, cy, rx, ry, rotation (deg), white alpha
MOUTH = ((56.0, 88.0), (60.0, 91.5), (64.0, 88.0))  # quadratic, 2.8 stroke, round caps
MOUTH_WIDTH = 2.8
SHADOW = (60.0, 113.0, 30.0, 4.0, 0.35)  # cx, cy, rx, ry, black alpha

# The viewBox has generous margins around the drop; an icon fills its square, so each frame
# crops to a square around the art (center x, top y, side) in viewBox units.
BOX_PLAIN = (60.0, 14.0, 96.0)    # body 20..100 x 16..108
BOX_SHADOW = (60.0, 12.0, 106.0)  # also the ground shadow down to y 117


def parse_path(d):
    """Flattens an M/C/Z path (the body) into one polygon."""
    tokens = re.findall(r"[MCZ]|-?\d+(?:\.\d+)?", d)
    points, i, cur = [], 0, (0.0, 0.0)
    while i < len(tokens):
        cmd = tokens[i]
        i += 1
        if cmd == "M":
            cur = (float(tokens[i]), float(tokens[i + 1]))
            i += 2
            points.append(cur)
        elif cmd == "C":
            c1 = (float(tokens[i]), float(tokens[i + 1]))
            c2 = (float(tokens[i + 2]), float(tokens[i + 3]))
            end = (float(tokens[i + 4]), float(tokens[i + 5]))
            i += 6
            for step in range(1, 65):
                t = step / 64
                mt = 1 - t
                points.append((
                    mt ** 3 * cur[0] + 3 * mt * mt * t * c1[0] + 3 * mt * t * t * c2[0] + t ** 3 * end[0],
                    mt ** 3 * cur[1] + 3 * mt * mt * t * c1[1] + 3 * mt * t * t * c2[1] + t ** 3 * end[1],
                ))
            cur = end
        elif cmd == "Z":
            pass
        else:
            raise ValueError("unsupported path command " + cmd)
    return points


def ellipse(cx, cy, rx, ry, rotation_deg=0.0, steps=256):
    rot = math.radians(rotation_deg)
    cos_r, sin_r = math.cos(rot), math.sin(rot)
    pts = []
    for k in range(steps):
        a = 2 * math.pi * k / steps
        x, y = rx * math.cos(a), ry * math.sin(a)
        pts.append((cx + x * cos_r - y * sin_r, cy + x * sin_r + y * cos_r))
    return pts


def stroke_quadratic(p0, p1, p2, width, steps=64):
    """Outline of a quadratic stroke with round caps, as one polygon."""
    line = []
    for k in range(steps + 1):
        t = k / steps
        mt = 1 - t
        line.append((mt * mt * p0[0] + 2 * mt * t * p1[0] + t * t * p2[0],
                     mt * mt * p0[1] + 2 * mt * t * p1[1] + t * t * p2[1]))
    half = width / 2
    left, right = [], []
    for k, (x, y) in enumerate(line):
        a = line[max(k - 1, 0)]
        b = line[min(k + 1, len(line) - 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        length = math.hypot(dx, dy)
        nx, ny = -dy / length, dx / length
        left.append((x + nx * half, y + ny * half))
        right.append((x - nx * half, y - ny * half))

    def cap(center, start_angle):
        return [(center[0] + half * math.cos(start_angle + math.pi * k / 32),
                 center[1] + half * math.sin(start_angle + math.pi * k / 32)) for k in range(1, 32)]

    end_dir = math.atan2(line[-1][1] - line[-2][1], line[-1][0] - line[-2][0])
    start_dir = math.atan2(line[1][1] - line[0][1], line[1][0] - line[0][0])
    return left + cap(line[-1], end_dir - math.pi / 2) + right[::-1] + cap(line[0], start_dir + math.pi / 2)


def coverage(polygon, size, to_px):
    """Per-pixel coverage (0..1) of a simple polygon: SUBROWS scanlines per pixel, exact in x."""
    pts = [to_px(p) for p in polygon]
    edges = [(pts[k], pts[(k + 1) % len(pts)]) for k in range(len(pts))]
    cov = [0.0] * (size * size)
    ys = [p[1] for p in pts]
    row_start = max(0, int(math.floor(min(ys))))
    row_end = min(size, int(math.ceil(max(ys))) + 1)
    weight = 1.0 / SUBROWS
    for row in range(row_start, row_end):
        base = row * size
        for sub in range(SUBROWS):
            sy = row + (sub + 0.5) / SUBROWS
            xs = []
            for (x0, y0), (x1, y1) in edges:
                if (y0 <= sy < y1) or (y1 <= sy < y0):
                    xs.append(x0 + (sy - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for k in range(0, len(xs) - 1, 2):
                xa, xb = max(xs[k], 0.0), min(xs[k + 1], float(size))
                if xb <= xa:
                    continue
                ia, ib = int(xa), int(xb)
                if ia == ib:
                    cov[base + ia] += (xb - xa) * weight
                    continue
                cov[base + ia] += (ia + 1 - xa) * weight
                for col in range(ia + 1, min(ib, size)):
                    cov[base + col] += weight
                if ib < size:
                    cov[base + ib] += (xb - ib) * weight
    return [min(c, 1.0) for c in cov]


def snap(center, extent):
    """Moves a center so an extent rounded to whole pixels starts on a pixel edge."""
    whole = max(1, round(extent))
    return (math.floor(center - whole / 2 + 0.5)) + whole / 2


def render(size):
    small = size <= SMALL_FORM_MAX
    shadow = size >= SHADOW_MIN
    cx, top, side = BOX_SHADOW if shadow else BOX_PLAIN
    left = cx - side / 2
    scale = size / side

    def to_px(p):
        return ((p[0] - left) * scale, (p[1] - top) * scale)

    def to_view(px, py):
        return (px / scale + left, py / scale + top)

    # Straight-alpha float RGBA, composited back to front with "over".
    rgba = [[0.0, 0.0, 0.0, 0.0] for _ in range(size * size)]

    def paint(polygon, color_at, alpha=1.0):
        cov = coverage(polygon, size, to_px)
        for i, c in enumerate(cov):
            if c <= 0.0:
                continue
            a = c * alpha
            r, g, b = color_at(i)
            d = rgba[i]
            out_a = a + d[3] * (1 - a)
            if out_a <= 0:
                continue
            for ch, src in enumerate((r, g, b)):
                d[ch] = (src * a + d[ch] * d[3] * (1 - a)) / out_a
            d[3] = out_a

    def solid(color):
        return lambda _i: color

    def synth(i):
        px, py = i % size + 0.5, i // size + 0.5
        x, y = to_view(px, py)
        x0, y0, x1, y1 = BODY_BOUNDS
        t = ((x - x0) / (x1 - x0) + (y - y0) / (y1 - y0)) / 2  # WPF StartPoint 0,0 EndPoint 1,1
        t = min(max(t, 0.0), 1.0)
        return tuple(VIOLET[k] + (AZURE[k] - VIOLET[k]) * t for k in range(3))

    if shadow:
        scx, scy, srx, sry, salpha = SHADOW
        paint(ellipse(scx, scy, srx, sry), solid(BLACK), salpha)
    paint(parse_path(BODY), synth)
    for ex in (47, 73):
        paint(ellipse(ex, 66, 10.5, 12.5), solid(WHITE))
        paint(ellipse(ex + 2, 68, 6, 6), solid(INK))
        paint(ellipse(ex + 3.6, 65.6, 2, 2), solid(WHITE))
    mouth = [(50 + 20*t/64, (1-t/64)**2*82 + 2*(1-t/64)*(t/64)*94 + (t/64)**2*82) for t in range(65)]
    paint(mouth, solid(INK))

    return [tuple(int(round(min(max(v, 0.0), 255.0))) for v in px[:3]) + (int(round(px[3] * 255)),) for px in rgba]


def png_bytes(size, pixels):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            raw.extend(pixels[y * size + x])

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def bmp_bytes(size, pixels):
    """An icon-resource DIB: 32bpp BGRA bottom-up, then a 1bpp AND mask (set where transparent)."""
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = pixels[y * size + x]
            xor.extend((b, g, r, a))
    stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for y in range(size - 1, -1, -1):
        row = bytearray(stride)
        for x in range(size):
            if pixels[y * size + x][3] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask.extend(row)
    return header + bytes(xor) + bytes(mask)


def ico_bytes(frames):
    """frames: list of (size, image bytes)."""
    out = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, blobs = b"", b""
    for size, data in frames:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    return out + entries + blobs


def main():
    preview = None
    if "--preview" in sys.argv:
        preview = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(preview, exist_ok=True)

    frames = []
    for size in SIZES:
        pixels = render(size)
        frames.append((size, png_bytes(size, pixels) if size >= 256 else bmp_bytes(size, pixels)))
        if preview:
            with open(os.path.join(preview, "chippi-%d.png" % size), "wb") as f:
                f.write(png_bytes(size, pixels))
        print("rendered %d px" % size)

    assets = os.path.join(HERE, "Assets")
    os.makedirs(assets, exist_ok=True)
    with open(os.path.join(assets, "chippi.ico"), "wb") as f:
        f.write(ico_bytes(frames))
    print("wrote Assets/chippi.ico")


if __name__ == "__main__":
    main()
