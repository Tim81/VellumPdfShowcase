"""Generates the sample images the capability catalogue embeds.

The images this repository ships are generated rather than sourced. A public
repository that redistributes photographs inherits their licensing, and a
capability catalogue does not need a photograph to demonstrate that a PDF can
carry one. Generating them means the images are covered by this repository's
own licence, their provenance is this file, and anyone may regenerate them
byte for byte.

The design is a test card rather than decoration. Each panel is chosen to make
a real difference between the formats visible in the rendered PDF:

    gradient      a smooth horizontal ramp, which shows the banding a reduced
                  palette imposes
    edges         high-contrast rules and glyph-like blocks, which show the
                  ringing a lossy encoder introduces around a hard edge
    primaries     saturated blocks, which show a palette's colour quantisation
    detail        a one-pixel checkerboard, which shows what survives scaling

Run it with any Python that has Pillow available:

    python eng/generate-sample-images.py

It writes into src/VellumPdfShowcase.Web/wwwroot/assets/images and reports the
size of each file. It is deterministic: running it twice produces identical
bytes, so a rerun that changes a file indicates a change to this script or to
the encoder, not noise.

NOTE: this script is not part of the build and no gate runs it. The images it
produces are committed, so a contributor never needs Pillow installed.

NOTE: there is deliberately no GIF here, although the model accepts
ImageFormat.Gif and the library advertises GIF support. VellumPdf.Layout 2.3.1
refuses to decode ordinary GIF files, including spec-conformant ones written by
hand, with "Invalid GIF LZW code". Only trivially compressible content, such as
a single flat colour, decodes. The defect and its measurements are recorded in
the library defect report. Add a GIF here once a release decodes one, rather
than shipping a sample that cannot render.
"""

from __future__ import annotations

import pathlib
import sys

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - the message is the whole point
    sys.exit("Pillow is required: python -m pip install Pillow")

WIDTH = 200
HEIGHT = 140

OUTPUT = (
    pathlib.Path(__file__).resolve().parent.parent
    / "src"
    / "VellumPdfShowcase.Web"
    / "wwwroot"
    / "assets"
    / "images"
)

INK = (28, 32, 38)
PAPER = (250, 250, 248)

# Saturated primaries and secondaries, in the order a colour bar conventionally
# runs from brightest to darkest luminance.
BARS = [
    (255, 255, 255),
    (255, 214, 0),
    (0, 174, 239),
    (0, 166, 81),
    (236, 0, 140),
    (237, 28, 36),
    (46, 49, 146),
    (28, 32, 38),
]


def build() -> Image.Image:
    """Draws the test card. Every coordinate is fixed, so the result is stable."""
    image = Image.new("RGB", (WIDTH, HEIGHT), PAPER)
    draw = ImageDraw.Draw(image)

    # Panel 1: a smooth horizontal ramp from ink to paper.
    ramp_bottom = 44
    for x in range(WIDTH):
        t = x / (WIDTH - 1)
        colour = tuple(round(INK[i] + (PAPER[i] - INK[i]) * t) for i in range(3))
        draw.line([(x, 0), (x, ramp_bottom - 1)], fill=colour)

    # Panel 2: colour bars.
    bars_bottom = 92
    bar_width = WIDTH / len(BARS)
    for index, colour in enumerate(BARS):
        left = round(index * bar_width)
        right = round((index + 1) * bar_width) - 1
        draw.rectangle([left, ramp_bottom, right, bars_bottom - 1], fill=colour)

    # Panel 3, left: a one-pixel checkerboard.
    for y in range(bars_bottom, HEIGHT):
        for x in range(0, 96):
            if (x + y) % 2 == 0:
                image.putpixel((x, y), INK)
            else:
                image.putpixel((x, y), PAPER)

    # Panel 3, right: hard edges of decreasing width, down to a single pixel.
    draw.rectangle([96, bars_bottom, WIDTH - 1, HEIGHT - 1], fill=PAPER)
    x = 104
    for width in (8, 6, 4, 3, 2, 1, 1, 1):
        draw.rectangle([x, bars_bottom + 6, x + width - 1, HEIGHT - 7], fill=INK)
        x += width + 4

    # A one-pixel frame, so any cropping by a consumer is immediately visible.
    draw.rectangle([0, 0, WIDTH - 1, HEIGHT - 1], outline=INK)

    return image


def main() -> int:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    card = build()

    # optimize is off for PNG so that the output depends on the image alone and
    # not on the encoder's search, which keeps reruns byte-identical across
    # Pillow versions more often than not.
    written = [
        ("testcard.png", lambda p: card.save(p, "PNG", optimize=False)),
        ("testcard.jpg", lambda p: card.save(p, "JPEG", quality=70, subsampling=2)),
        ("testcard.bmp", lambda p: card.save(p, "BMP")),
        ("testcard.tif", lambda p: card.save(p, "TIFF", compression="tiff_lzw")),
    ]

    for name, save in written:
        path = OUTPUT / name
        save(path)
        print(f"{name:16} {path.stat().st_size:>8,} bytes")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
