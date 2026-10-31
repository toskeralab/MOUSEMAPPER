#!/usr/bin/env python3
"""Gera Assets/mousemapper.ico (frames DIB + PNG 256) sem dependencias externas.

Uso: python3 scripts/generate-icon.py
"""
from __future__ import annotations

import math
import os
import struct
import zlib

OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "MouseMapper", "Assets")
PREVIEW = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "artifacts", "icon-preview.png")

SIZES = [16, 24, 32, 48, 64, 128, 256]
SUPERSAMPLE = 4

# Paleta
TOP = (30, 40, 74)      # #1E284A
BOTTOM = (11, 15, 27)   # #0B0F1B
ACCENT = (76, 141, 255)  # #4C8DFF
WHITE = (245, 249, 255)

ARROW = [
    (0.34, 0.15),
    (0.34, 0.73),
    (0.45, 0.61),
    (0.53, 0.81),
    (0.615, 0.765),
    (0.53, 0.575),
    (0.675, 0.575),
]


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def inside_polygon(x: float, y: float, poly) -> bool:
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            x_cross = (xj - xi) * (y - yi) / (yj - yi) + xi
            if x < x_cross:
                inside = not inside
        j = i
    return inside


def inside_rounded_rect(x: float, y: float, x0: float, y0: float, x1: float, y1: float, r: float) -> bool:
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def render(size: int):
    """Retorna lista de pixels RGBA (bytearray) com antialiasing por supersampling."""
    px = bytearray(size * size * 4)
    step = 1.0 / SUPERSAMPLE
    offset = step / 2.0
    radius = size * 0.215
    inset = max(1.0, size * 0.012)
    border_width = max(1.2, size * 0.028)
    arrow = [(x * size, y * size) for (x, y) in ARROW]
    dot_cx, dot_cy, dot_r = size * 0.735, size * 0.755, size * 0.072
    ring = (size * 0.075, size * 0.075, size * 0.925, size * 0.925)

    for y in range(size):
        for x in range(size):
            r_sum = g_sum = b_sum = a_sum = 0.0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    fx = x + offset + sx * step
                    fy = y + offset + sy * step
                    cr = cg = cb = 0.0
                    ca = 0.0
                    if inside_rounded_rect(fx, fy, inset, inset, size - inset, size - inset, radius):
                        t = (fx + fy) / (2.0 * size)
                        cr = lerp(TOP[0], BOTTOM[0], t)
                        cg = lerp(TOP[1], BOTTOM[1], t)
                        cb = lerp(TOP[2], BOTTOM[2], t)
                        ca = 1.0
                        # anel de destaque
                        outer = inside_rounded_rect(fx, fy, inset, inset, size - inset, size - inset, radius)
                        inner = inside_rounded_rect(
                            fx, fy,
                            inset + border_width, inset + border_width,
                            size - inset - border_width, size - inset - border_width,
                            max(1.0, radius - border_width),
                        )
                        if outer and not inner:
                            cr, cg, cb = ACCENT
                    if ca > 0 and inside_polygon(fx, fy, arrow):
                        cr, cg, cb = WHITE
                    if ca > 0 and (fx - dot_cx) ** 2 + (fy - dot_cy) ** 2 <= dot_r ** 2:
                        cr, cg, cb = ACCENT
                    r_sum += cr * ca
                    g_sum += cg * ca
                    b_sum += cb * ca
                    a_sum += ca
            samples = SUPERSAMPLE * SUPERSAMPLE
            alpha = a_sum / samples
            idx = (y * size + x) * 4
            if alpha <= 0.003:
                px[idx:idx + 4] = b"\x00\x00\x00\x00"
                continue
            px[idx] = int(round(min(255.0, r_sum / a_sum)))
            px[idx + 1] = int(round(min(255.0, g_sum / a_sum)))
            px[idx + 2] = int(round(min(255.0, b_sum / a_sum)))
            px[idx + 3] = int(round(alpha * 255))
    return px


def to_png(size: int, pixels: bytearray) -> bytes:
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filtro None
        raw.extend(pixels[y * stride:(y + 1) * stride])

    def chunk(tag: bytes, data: bytes) -> bytes:
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def to_dib(size: int, pixels: bytearray) -> bytes:
    header = struct.pack(
        "<IiiHHIIiiII",
        40, size, size * 2, 1, 32, 0, size * size * 4, 0, 0, 0, 0,
    )
    rows = []
    stride = size * 4
    for y in range(size - 1, -1, -1):
        row = pixels[y * stride:(y + 1) * stride]
        bgra = bytearray()
        for i in range(0, len(row), 4):
            bgra += bytes((row[i + 2], row[i + 1], row[i], row[i + 3]))
        rows.append(bytes(bgra))
    mask_stride = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_stride * size)
    return header + b"".join(rows) + mask


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    frames = []
    for size in SIZES:
        pixels = render(size)
        data = to_png(size, pixels) if size >= 256 else to_dib(size, pixels)
        frames.append((size, data))
        if size == 256:
            os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
            with open(PREVIEW, "wb") as fh:
                fh.write(data)

    count = len(frames)
    offset = 6 + 16 * count
    directory = bytearray()
    blob = bytearray()
    for size, data in frames:
        dim = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blob += data
        offset += len(data)

    ico = struct.pack("<HHH", 0, 1, count) + bytes(directory) + bytes(blob)
    target = os.path.join(OUT_DIR, "mousemapper.ico")
    with open(target, "wb") as fh:
        fh.write(ico)
    print(f"Gerado {target} ({len(ico)} bytes, {count} frames)")


if __name__ == "__main__":
    main()
