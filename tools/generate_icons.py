"""Generates the MSIX logo set and .ico files for Stirling apps.

Usage: python tools/generate_icons.py [app]

Icons are drawn procedurally (supersampled, then downscaled) so every size is
crisp and the set can be regenerated whenever the brand changes. Output goes to
src/Stirling.Shared/Assets/<App>/ so any Stirling app can link the files.
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

AMBER = (0xFF, 0xB5, 0x47, 255)
INK = (0x1E, 0x1A, 0x14, 255)
SS = 4  # supersampling factor

ROOT = Path(__file__).resolve().parent.parent
OUT_ROOT = ROOT / "src" / "Stirling.Shared" / "Assets"


def capta_glyph(size: int, plate: bool, glyph_colour=INK) -> Image.Image:
    """Amber rounded plate with viewfinder corners and a centre dot."""
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if plate:
        d.rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.22, fill=AMBER)
        inset, colour = s * 0.22, glyph_colour
    else:
        inset, colour = s * 0.06, AMBER
    w = max(SS, s * (0.085 if plate else 0.11))
    arm = s * 0.2
    lo, hi = inset, s - inset
    for x, dx in ((lo, 1), (hi, -1)):
        for y, dy in ((lo, 1), (hi, -1)):
            d.line([(x, y + dy * arm), (x, y), (x + dx * arm, y)], fill=colour, width=int(w), joint="curve")
            r = w / 2
            for px, py in ((x, y + dy * arm), (x + dx * arm, y), (x, y)):
                d.ellipse((px - r, py - r, px + r, py + r), fill=colour)
    c, r = s / 2, s * 0.09
    d.ellipse((c - r, c - r, c + r, c + r), fill=colour)
    return img.resize((size, size), Image.LANCZOS)


def on_canvas(w: int, h: int, glyph_size: int, plate: bool) -> Image.Image:
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    g = capta_glyph(glyph_size, plate)
    canvas.alpha_composite(g, ((w - glyph_size) // 2, (h - glyph_size) // 2))
    return canvas


def generate_capta(out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    for scale in (100, 125, 150, 200, 400):
        f = scale / 100
        on_canvas(round(44 * f), round(44 * f), round(44 * f), True).save(out / f"Square44x44Logo.scale-{scale}.png")
        on_canvas(round(150 * f), round(150 * f), round(100 * f), True).save(out / f"Square150x150Logo.scale-{scale}.png")
        on_canvas(round(310 * f), round(150 * f), round(100 * f), True).save(out / f"Wide310x150Logo.scale-{scale}.png")
        on_canvas(round(620 * f), round(300 * f), round(160 * f), True).save(out / f"SplashScreen.scale-{scale}.png")
        on_canvas(round(50 * f), round(50 * f), round(50 * f), True).save(out / f"StoreLogo.scale-{scale}.png")
        on_canvas(round(24 * f), round(24 * f), round(24 * f), True).save(out / f"LockScreenLogo.scale-{scale}.png")
    for t in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        on_canvas(t, t, t, True).save(out / f"Square44x44Logo.targetsize-{t}.png")
        on_canvas(t, t, t, True).save(out / f"Square44x44Logo.targetsize-{t}_altform-unplated.png")
        on_canvas(t, t, t, True).save(out / f"Square44x44Logo.targetsize-{t}_altform-lightunplated.png")

    sizes = [16, 20, 24, 32, 40, 48, 64, 256]
    capta_glyph(256, True).save(out / "Capta.ico", sizes=[(s, s) for s in sizes])
    # Tray: bare amber glyph reads on both light and dark taskbars.
    capta_glyph(64, False).save(out / "Tray.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64)])


GENERATORS = {"capta": (generate_capta, "Capta")}

if __name__ == "__main__":
    app = sys.argv[1].lower() if len(sys.argv) > 1 else "capta"
    fn, folder = GENERATORS[app]
    fn(OUT_ROOT / folder)
    print(f"Wrote icons to {OUT_ROOT / folder}")
