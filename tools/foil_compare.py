#!/usr/bin/env python3
"""Lays two sets of foil screenshots side by side (tools/foil_shots.sh output) and says what changed.

    tools/foil_compare.py <before dir> <after dir> <out dir>

Writes <out dir>/index.html: for each PNG in both sets (matched by path), before, after, a hover-to-flip view and, when
they differ, a diff picture (changed pixels in red, brighter for bigger changes, over a dimmed copy of before). The
pictures are copied into <out dir>, so the page stands alone. Prints a line per picture: identical (same bytes), the
share of pixels changed and the biggest channel change, or missing. Pixels are read with macOS `sips` (PNG to BMP);
without it the page still shows the pictures, only "identical" or "differs".
"""
import hashlib
import html
import os
import shutil
import struct
import subprocess
import sys
import tempfile


def pngs(root):
    found = []
    for folder, _, files in os.walk(root):
        for name in files:
            if name.lower().endswith(".png"):
                found.append(os.path.relpath(os.path.join(folder, name), root))
    return sorted(found)


def digest(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def read_bmp(path):
    """Width, height and top-down rows of (b, g, r) bytes, 3 per pixel, from an uncompressed 24- or 32-bit BMP."""
    with open(path, "rb") as f:
        data = f.read()
    offset = struct.unpack_from("<I", data, 10)[0]
    width, height = struct.unpack_from("<ii", data, 18)
    bpp = struct.unpack_from("<H", data, 28)[0] // 8
    if bpp not in (3, 4):
        raise ValueError(f"{path}: {bpp * 8}-bit BMP")
    stride = (width * bpp + 3) & ~3
    rows = []
    for y in range(abs(height)):
        start = offset + y * stride
        row = data[start:start + width * bpp]
        if bpp == 4:
            row = b"".join(row[i:i + 3] for i in range(0, len(row), 4))
        rows.append(row)
    if height > 0:
        rows.reverse()  # stored bottom-up
    return width, abs(height), rows


def write_bmp(path, width, rows):
    stride = (width * 3 + 3) & ~3
    pad = b"\0" * (stride - width * 3)
    body = b"".join(row + pad for row in reversed(rows))
    header = struct.pack("<2sIHHI", b"BM", 54 + len(body), 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, width, len(rows), 1, 24, 0, len(body), 2835, 2835, 0, 0)
    with open(path, "wb") as f:
        f.write(header + info + body)


def sips(src, dst, fmt):
    subprocess.run(["sips", "-s", "format", fmt, src, "--out", dst], check=True, capture_output=True)


def compare(before, after, diff_png, scratch):
    """(changed share, biggest channel change) and writes the diff picture; None when the pixels can't be read."""
    if shutil.which("sips") is None:
        return None
    a_bmp, b_bmp = os.path.join(scratch, "a.bmp"), os.path.join(scratch, "b.bmp")
    sips(before, a_bmp, "bmp")
    sips(after, b_bmp, "bmp")
    wa, ha, ra = read_bmp(a_bmp)
    wb, hb, rb = read_bmp(b_bmp)
    if (wa, ha) != (wb, hb):
        return f"size {wa}x{ha} vs {wb}x{hb}"
    changed, biggest, out = 0, 0, []
    for row_a, row_b in zip(ra, rb):
        dim = bytes(v // 4 for v in row_a)
        if row_a == row_b:
            out.append(dim)
            continue
        line = bytearray(dim)
        for i in range(0, len(row_a), 3):
            d = max(abs(row_a[i] - row_b[i]), abs(row_a[i + 1] - row_b[i + 1]), abs(row_a[i + 2] - row_b[i + 2]))
            if d:
                changed += 1
                biggest = max(biggest, d)
                line[i:i + 3] = bytes((0, 0, min(255, 96 + d * 2)))
        out.append(bytes(line))
    if changed:
        diff_bmp = os.path.join(scratch, "diff.bmp")
        write_bmp(diff_bmp, wa, out)
        sips(diff_bmp, diff_png, "png")
    return changed / (wa * ha), biggest


def main():
    if len(sys.argv) != 4:
        print(__doc__.strip().splitlines()[2].strip())
        sys.exit(2)
    before_dir, after_dir, out_dir = (os.path.abspath(p) for p in sys.argv[1:])
    names = sorted(set(pngs(before_dir)) | set(pngs(after_dir)))
    rows = []
    with tempfile.TemporaryDirectory() as scratch:
        for name in names:
            b, a = os.path.join(before_dir, name), os.path.join(after_dir, name)
            cells = {}
            for side, path in (("before", b), ("after", a)):
                if os.path.exists(path):
                    copy = os.path.join(out_dir, side, name)
                    os.makedirs(os.path.dirname(copy), exist_ok=True)
                    shutil.copy2(path, copy)
                    cells[side] = f"{side}/{name}"
            diff = None
            if len(cells) < 2:
                status = "missing " + ("after" if "before" in cells else "before")
            elif digest(b) == digest(a):
                status = "identical"
            else:
                diff_path = os.path.join(out_dir, "diff", name)
                os.makedirs(os.path.dirname(diff_path), exist_ok=True)
                result = compare(b, a, diff_path, scratch)
                if result is None:
                    status = "differs"
                elif isinstance(result, str):
                    status = "differs: " + result
                else:
                    share, biggest = result
                    status = f"differs: {share:.2%} of pixels, up to {biggest}/255" if share else "same pixels (bytes differ)"
                    if share:
                        diff = f"diff/{name}"
            print(f"{name}: {status}")
            rows.append((name, status, cells, diff))

    def img(src, label):
        if not src:
            return f"<div class=none>{label}: none</div>"
        q = html.escape(src)
        return f'<a href="{q}"><img src="{q}" alt="{label}"></a>'

    parts = []
    for name, status, cells, diff in rows:
        flip = ""
        if len(cells) == 2:
            flip = (f'<div class=flip><img src="{html.escape(cells["before"])}" alt="before">'
                    f'<img class=top src="{html.escape(cells["after"])}" alt="after"></div>')
        parts.append(
            f"<section><h2>{html.escape(name)} <span class=status>{html.escape(status)}</span></h2><div class=grid>"
            f"<figure>{img(cells.get('before'), 'before')}<figcaption>before</figcaption></figure>"
            f"<figure>{img(cells.get('after'), 'after')}<figcaption>after</figcaption></figure>"
            f"<figure>{flip}<figcaption>hover: after</figcaption></figure>"
            f"<figure>{img(diff, 'diff') if diff else ''}<figcaption>{'changed pixels' if diff else ''}</figcaption></figure>"
            f"</div></section>")
    page = f"""<!doctype html><meta charset=utf-8><title>Foil compare</title>
<style>
body{{background:#10131a;color:#e6e8ee;font:14px -apple-system,sans-serif;margin:16px}}
h1{{font-size:18px}} h2{{font-size:15px;margin:24px 0 8px}} .status{{color:#9aa3b5;font-weight:normal}}
.grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}} figure{{margin:0}}
img{{width:100%;display:block}} figcaption{{color:#8a8f9a;font-size:12px;margin-top:2px}}
.flip{{position:relative}} .flip .top{{position:absolute;inset:0;opacity:0}} .flip:hover .top{{opacity:1}}
.none{{color:#8a8f9a;padding:20px 0}}
</style>
<h1>Foil compare</h1><p>before: {html.escape(before_dir)}<br>after: {html.escape(after_dir)}</p>
{''.join(parts)}
"""
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "index.html"), "w") as f:
        f.write(page)
    print(f"wrote {os.path.join(out_dir, 'index.html')}")


if __name__ == "__main__":
    main()
