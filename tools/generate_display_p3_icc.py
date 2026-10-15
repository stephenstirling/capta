"""Generates the Display P3 ICC profile Capta embeds in PNGs saved in Display P3.

Usage: python tools/generate_display_p3_icc.py

Display P3 is DCI-P3 primaries with a D65 white point and the sRGB tone curve. The profile
is a small ICC v2 matrix/TRC display profile (the most widely understood form), with
colorants Bradford-adapted to the D50 PCS and a chad tag recording that adaptation.
It also prints the linear Rec.709 -> Display P3 matrix used by Capture/ToneMapper.cs.

Output: src/Capta/Capture/DisplayP3.icc (embedded in the app as a resource).
"""

from __future__ import annotations

import struct
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Capta" / "Capture" / "DisplayP3.icc"

D65_XY = (0.3127, 0.3290)
P3_PRIMARIES = ((0.680, 0.320), (0.265, 0.690), (0.150, 0.060))
REC709_PRIMARIES = ((0.640, 0.330), (0.300, 0.600), (0.150, 0.060))
# The ICC PCS illuminant, exactly as s15Fixed16 numbers.
D50 = np.array([0x0000F6D6, 0x00010000, 0x0000D32D]) / 65536.0

BRADFORD = np.array([
    [0.8951, 0.2664, -0.1614],
    [-0.7502, 1.7135, 0.0367],
    [0.0389, -0.0685, 1.0296],
])


def xyz(xy: tuple[float, float]) -> np.ndarray:
    x, y = xy
    return np.array([x / y, 1.0, (1 - x - y) / y])


def rgb_to_xyz(primaries) -> np.ndarray:
    """Columns are the primaries' XYZ, scaled so RGB (1,1,1) is D65 with Y = 1."""
    m = np.column_stack([xyz(p) for p in primaries])
    scale = np.linalg.solve(m, xyz(D65_XY))
    return m * scale


def adapt(source_white: np.ndarray, dest_white: np.ndarray) -> np.ndarray:
    s = BRADFORD @ source_white
    d = BRADFORD @ dest_white
    return np.linalg.inv(BRADFORD) @ np.diag(d / s) @ BRADFORD


def s15f16(v: float) -> bytes:
    return struct.pack(">i", int(round(v * 65536)))


def xyz_tag(v) -> bytes:
    return b"XYZ " + bytes(4) + b"".join(s15f16(c) for c in v)


def text_description(text: str) -> bytes:
    ascii_ = text.encode("ascii") + b"\0"
    return (b"desc" + bytes(4) + struct.pack(">I", len(ascii_)) + ascii_
            + struct.pack(">II", 0, 0)       # no Unicode description
            + struct.pack(">HB", 0, 0) + bytes(67))  # no ScriptCode description


def text(text_: str) -> bytes:
    return b"text" + bytes(4) + text_.encode("ascii") + b"\0"


def srgb_curve(entries: int = 1024) -> bytes:
    values = []
    for i in range(entries):
        v = i / (entries - 1)
        lin = v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
        values.append(int(round(lin * 65535)))
    return b"curv" + bytes(4) + struct.pack(">I", entries) + struct.pack(f">{entries}H", *values)


def build() -> bytes:
    d65 = xyz(D65_XY)
    chad = adapt(d65, D50)
    colorants = chad @ rgb_to_xyz(P3_PRIMARIES)
    # Quantise, then nudge so the colorants sum exactly to the D50 white the header declares.
    q = np.round(colorants * 65536) / 65536
    q[:, 1] += D50 - q.sum(axis=1)

    curve = srgb_curve()
    tags = [
        (b"desc", text_description("Display P3")),
        (b"cprt", text("No copyright, use freely")),
        (b"wtpt", xyz_tag(D50)),
        (b"rXYZ", xyz_tag(q[:, 0])),
        (b"gXYZ", xyz_tag(q[:, 1])),
        (b"bXYZ", xyz_tag(q[:, 2])),
        (b"rTRC", curve),
        (b"gTRC", curve),  # shares rTRC's data
        (b"bTRC", curve),
        (b"chad", b"sf32" + bytes(4) + b"".join(s15f16(v) for v in chad.flatten())),
    ]

    table_size = 4 + 12 * len(tags)
    offset = 128 + table_size
    entries, data, placed = [], b"", {}
    for sig, body in tags:
        if body in placed:
            entries.append((sig, placed[body], len(body)))
            continue
        pad = (-offset) % 4
        data += bytes(pad)
        offset += pad
        placed[body] = offset
        entries.append((sig, offset, len(body)))
        data += body
        offset += len(body)
    data += bytes((-offset) % 4)
    size = 128 + table_size + len(data)

    header = bytearray(128)
    struct.pack_into(">I", header, 0, size)
    struct.pack_into(">I", header, 8, 0x02100000)  # version 2.1
    header[12:16] = b"mntr"
    header[16:20] = b"RGB "
    header[20:24] = b"XYZ "
    struct.pack_into(">6H", header, 24, 2026, 10, 10, 0, 0, 0)  # fixed, so output is reproducible
    header[36:40] = b"acsp"
    header[68:80] = b"".join(s15f16(v) for v in D50)

    table = struct.pack(">I", len(tags)) + b"".join(struct.pack(">4sII", s, o, n) for s, o, n in entries)
    return bytes(header) + table + data


def main() -> None:
    OUT.write_bytes(build())
    print(f"Wrote {OUT.relative_to(ROOT)} ({OUT.stat().st_size} bytes)")
    m = np.linalg.inv(rgb_to_xyz(P3_PRIMARIES)) @ rgb_to_xyz(REC709_PRIMARIES)
    print("Linear Rec.709 -> Display P3 (ToneMapper.cs):")
    for row in m:
        print("  " + ", ".join(f"{v:.7f}f" for v in row))


if __name__ == "__main__":
    main()
