"""Draws the disk image's Finder background (docs/Platform.md §3.1) from arrow.png: the arrow centered between the two
icons that settings.py places, on a dark canvas the size of the window, at 1x and 2x. dmgbuild picks up the @2x file
next to the 1x one and folds both into one Retina TIFF, so the pair must be regenerated together:

    python3 build/dmg/make-background.py        (from the repository root; writes build/dmg/background*.png)

Standard library only, so that it runs on any Python 3 at hand (the repository installs none): PNG is read and written
here, and the arrow is scaled with an area-averaging filter in premultiplied alpha.

The canvas carries its own color rather than staying transparent: the Finder has no appearance-aware folder backgrounds,
and once a picture is set it paints the picture over white, so a transparent canvas gave a white window in dark mode.
A dark canvas with a light arrow reads the same in both appearances. The arrow is recolored from arrow.png's own gray.
The rows below the icons are a light band: with a background picture the Finder draws the item labels black in dark
mode as well, and on the dark canvas alone the names were unreadable.
"""
import struct
import sys
import zlib
from pathlib import Path

CANVAS = (640, 320)  # points; keep in step with window_rect in settings.py
# Icons of 128pt centered at x=140 and x=500 leave the gap 204...436; the arrow takes 160pt of it, centered.
ARROW_WIDTH = 160
ARROW_CENTER = (320, 120)  # Finder coordinates (y from the top), as in icon_locations
BACKGROUND_COLOR = (0x28, 0x28, 0x28)  # close to the Finder's dark window, so the picture does not read as a panel
ARROW_COLOR = (0xC0, 0xC0, 0xC0)  # light gray: visible on the canvas without the glare of pure white
FOOTER_TOP = 188  # points from the top; the 128pt icons centered at y=120 end at 184, the labels start right below
FOOTER_COLOR = (0xD4, 0xD4, 0xD4)  # light enough for the Finder's black labels, not the white of a light-mode window
SCALES = (1, 2)
SIGNATURE = b"\x89PNG\r\n\x1a\n"


def read_png(path):
    """Returns (width, height, rows) of an 8-bit RGBA PNG, rows as bytearrays of r, g, b, a per pixel."""
    data = Path(path).read_bytes()
    if data[:8] != SIGNATURE:
        raise ValueError(f"{path} is not a PNG file")
    position, idat, width, height = 8, b"", None, None
    while position < len(data):
        length, kind = struct.unpack(">I4s", data[position:position + 8])
        body = data[position + 8:position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if (depth, color, interlace) != (8, 6, 0):
                raise ValueError(f"{path} must be 8-bit RGBA without interlacing (depth {depth}, color type {color})")
        elif kind == b"IDAT":
            idat += body
        elif kind == b"IEND":
            break
    raw = zlib.decompress(idat)
    stride = width * 4
    rows, previous = [], bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        line = bytearray(raw[start + 1:start + 1 + stride])
        unfilter(line, previous, raw[start])
        rows.append(line)
        previous = line
    return width, height, rows


def unfilter(line, previous, filter_type, bpp=4):
    """Undoes a PNG scanline filter in place (None, Sub, Up, Average, Paeth)."""
    if filter_type == 0:
        return
    for i in range(len(line)):
        a = line[i - bpp] if i >= bpp else 0
        b = previous[i]
        c = previous[i - bpp] if i >= bpp else 0
        if filter_type == 1:
            predicted = a
        elif filter_type == 2:
            predicted = b
        elif filter_type == 3:
            predicted = (a + b) // 2
        elif filter_type == 4:
            p = a + b - c
            pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
            predicted = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
        else:
            raise ValueError(f"unknown PNG filter type {filter_type}")
        line[i] = (line[i] + predicted) & 255


def write_png(path, width, height, rows, dpi):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    pixels_per_meter = round(dpi / 0.0254)
    raw = b"".join(b"\x00" + bytes(row) for row in rows)
    Path(path).write_bytes(
        SIGNATURE
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        # The pixel density tells tiffutil (and anything else reading the pair) which file is which.
        + chunk(b"pHYs", struct.pack(">IIB", pixels_per_meter, pixels_per_meter, 1))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def opaque_bounds(width, height, rows):
    """The box (left, top, right, bottom) of the pixels that are not (nearly) transparent."""
    left, top, right, bottom = width, height, -1, -1
    for y, row in enumerate(rows):
        columns = [x for x, alpha in enumerate(row[3::4]) if alpha > 8]
        if columns:
            left, right = min(left, columns[0]), max(right, columns[-1])
            top, bottom = min(top, y), y
    if right < 0:
        raise ValueError("the arrow image is fully transparent")
    return left, top, right + 1, bottom + 1


def box_weights(source, destination):
    """For each destination index, the (source index, weight) pairs of an area-averaging filter."""
    scale = source / destination
    result = []
    for i in range(destination):
        start, end, pairs = i * scale, (i + 1) * scale, []
        j = int(start)
        while j < end and j < source:
            overlap = min(end, j + 1) - max(start, j)
            if overlap > 0:
                pairs.append((j, overlap / scale))
            j += 1
        result.append(pairs)
    return result


def resample(rows, box, size):
    """Scales the box of the image to size, averaging in premultiplied alpha so that edges keep their color."""
    left, top, right, bottom = box
    width, height = size
    source = [
        [(r * a, g * a, b * a, a * 255) for r, g, b, a in zip(row[left * 4:right * 4:4], row[left * 4 + 1:right * 4:4],
                                                             row[left * 4 + 2:right * 4:4], row[left * 4 + 3:right * 4:4])]
        for row in rows[top:bottom]
    ]
    horizontal = [
        [tuple(sum(row[j][k] * w for j, w in pairs) for k in range(4)) for pairs in box_weights(right - left, width)]
        for row in source
    ]
    result = []
    for pairs in box_weights(bottom - top, height):
        line = bytearray()
        for x in range(width):
            pr, pg, pb, pa = (sum(horizontal[j][x][k] * w for j, w in pairs) for k in range(4))
            alpha = pa / 255
            if alpha > 0:
                r, g, b = (min(255, round(v / alpha)) for v in (pr, pg, pb))
            else:
                r = g = b = 0
            line += bytes((r, g, b, min(255, round(alpha))))
        result.append(line)
    return result


def recolor(rows, color):
    """The same shape (alpha) in a single color."""
    r, g, b = color
    for row in rows:
        for i in range(0, len(row), 4):
            row[i], row[i + 1], row[i + 2] = r, g, b
    return rows


def compose(canvas, arrow, arrow_size, center, color, footer):
    """An opaque canvas of color, footer = (color, top) painting the rows from top (pixels) down in another color, with
    the arrow blended in, its center at center (from the top-left)."""
    canvas_width, canvas_height = canvas
    arrow_width, arrow_height = arrow_size
    left = round(center[0] - arrow_width / 2)
    top = round(center[1] - arrow_height / 2)
    rows = []
    for y in range(canvas_height):
        base = footer[0] if y >= footer[1] else color
        row = bytearray(bytes(base) + b"\xff") * canvas_width
        if top <= y < top + arrow_height:
            source = arrow[y - top]
            for x in range(arrow_width):
                r, g, b, a = source[x * 4:x * 4 + 4]
                offset = (left + x) * 4
                row[offset] = (base[0] * (255 - a) + r * a + 127) // 255
                row[offset + 1] = (base[1] * (255 - a) + g * a + 127) // 255
                row[offset + 2] = (base[2] * (255 - a) + b * a + 127) // 255
        rows.append(row)
    return rows, (left, top)


def main(directory):
    width, height, rows = read_png(directory / "arrow.png")
    box = opaque_bounds(width, height, rows)
    visible_width, visible_height = box[2] - box[0], box[3] - box[1]
    # The 1x height is rounded first and doubled for 2x, so that the pair is an exact HiDPI set.
    arrow_height = round(ARROW_WIDTH * visible_height / visible_width)
    print(f"arrow: {width}x{height} px, visible {visible_width}x{visible_height} px")
    for scale in SCALES:
        size = (ARROW_WIDTH * scale, arrow_height * scale)
        canvas = (CANVAS[0] * scale, CANVAS[1] * scale)
        arrow = recolor(resample(rows, box, size), ARROW_COLOR)
        composed, origin = compose(canvas, arrow, size, (ARROW_CENTER[0] * scale, ARROW_CENTER[1] * scale),
                                  BACKGROUND_COLOR, (FOOTER_COLOR, FOOTER_TOP * scale))
        name = "background.png" if scale == 1 else f"background@{scale}x.png"
        write_png(directory / name, canvas[0], canvas[1], composed, 72 * scale)
        print(f"wrote {name}: {canvas[0]}x{canvas[1]} px, arrow {size[0]}x{size[1]} px at {origin} from the top-left")


if __name__ == "__main__":
    main(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent)
