#!/usr/bin/env python3
"""Generates src/Vibra/Assets/Vibra.ico (a vibrant color ring) without third-party packages."""
import colorsys
import math
import struct
import zlib
from pathlib import Path

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 4


def pixel(x: float, y: float, size: int):
    """Colour of a point in a size x size canvas, or None if transparent."""
    c = size / 2
    dx, dy = x - c, y - c
    r = math.hypot(dx, dy) / c
    outer, inner = 0.96, (0.46 if size >= 32 else 0.38)
    if r > outer or r < inner:
        return None
    # Conic sweep through saturated hues, starting at the top.
    angle = (math.atan2(dx, -dy) / (2 * math.pi)) % 1.0
    hue = (0.53 + angle * 0.62) % 1.0  # cyan -> blue -> violet -> pink -> orange
    rgb = colorsys.hsv_to_rgb(hue, 0.85, 1.0)
    return tuple(int(v * 255) for v in rgb)


def render(size: int) -> bytes:
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter type: none
        for px in range(size):
            acc = [0, 0, 0]
            hits = 0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    colour = pixel(px + (sx + 0.5) / SUPERSAMPLE, py + (sy + 0.5) / SUPERSAMPLE, size)
                    if colour:
                        hits += 1
                        for i in range(3):
                            acc[i] += colour[i]
            if hits:
                row += bytes([acc[0] // hits, acc[1] // hits, acc[2] // hits, 255 * hits // SUPERSAMPLE ** 2])
            else:
                row += bytes([0, 0, 0, 0])
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size: int, raw: bytes) -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    images = [render(s) for s in SIZES]
    out = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for size, data in zip(SIZES, images):
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for data in images:
        out += data
    target = Path(__file__).resolve().parent.parent / "src" / "Vibra" / "Assets" / "Vibra.ico"
    target.write_bytes(out)
    print(f"wrote {target} ({len(out)} bytes)")


if __name__ == "__main__":
    main()
