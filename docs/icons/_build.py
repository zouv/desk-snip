"""Build the DeskSnip icon PNG/ICO sets from the SVG sources.

Renders each SVG with headless Microsoft Edge at 2048x2048, centers it in a
larger transparent viewport, crops the artwork, then downscales straight to
each target size. Outputs stay in docs/icons; this script does NOT touch the
app assets, so the production icon remains
src/DeskSnip.App/Assets/DeskSnipIcon.xaml until a design is chosen and wired in.

Requires Python 3 + Pillow + NumPy and a local Microsoft Edge. No network.
Set DESKSNIP_EDGE to msedge.exe if Edge is installed elsewhere.
"""
from pathlib import Path
import os
import shutil
import subprocess
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
RENDER = 2048        # 8x the 256 viewBox
VIEW = 2400          # page viewport; keeps Edge's chrome from clipping the art
TARGET = 1024        # master output size
ICO_SIZES = [256, 128, 64, 48, 32, 24, 16]
PNG_SIZES = [1024, 512, 256, 128, 64, 48, 32, 16]

SOURCES = {
    "desksnip-original": HERE / "desksnip-original.svg",
    "desksnip-light": HERE / "desksnip-light.svg",
    "desksnip-frosted": HERE / "desksnip-frosted.svg",
}


def find_edge() -> str:
    candidates = [
        os.environ.get("DESKSNIP_EDGE"),
        shutil.which("msedge"),
        shutil.which("msedge.exe"),
    ]
    for base in (
        os.environ.get("PROGRAMFILES(X86)"),
        os.environ.get("PROGRAMFILES"),
        os.environ.get("LOCALAPPDATA"),
    ):
        if base:
            candidates.append(str(Path(base) / "Microsoft" / "Edge" / "Application" / "msedge.exe"))
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return candidate
    raise SystemExit("Microsoft Edge not found; set DESKSNIP_EDGE to msedge.exe")


def _render(edge: str, svg_path: Path, transparent: bool) -> Image.Image:
    svg_text = svg_path.read_text(encoding="utf-8")
    # Let CSS set the render size. Rewriting every width="256" in the file
    # would also resize the background rect inside the 256 viewBox.
    svg_big = svg_text
    background = "transparent" if transparent else "#ff00ff"

    html = f"""<!DOCTYPE html>
<html><head><meta charset="utf-8"><style>
html,body{{margin:0;padding:0;background:{background};width:{VIEW}px;height:{VIEW}px;overflow:hidden;display:flex;align-items:center;justify-content:center}}
svg{{display:block;width:{RENDER}px;height:{RENDER}px;flex:0 0 auto}}
</style></head><body>
{svg_big}
</body></html>"""

    html_path = HERE / "_build.html"
    raw_path = HERE / ("_raw-transparent.png" if transparent else "_raw-magenta.png")
    html_path.write_text(html, encoding="utf-8")

    print(f"Rendering {svg_path.name} at {RENDER}x{RENDER} ...")
    command = [
        edge,
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        "--force-device-scale-factor=1",
        f"--window-size={VIEW},{VIEW}",
        f"--screenshot={raw_path}",
    ]
    if transparent:
        command.insert(-1, "--default-background-color=00000000")
    command.append(f"file:///{str(html_path).replace(os.sep, '/')}?t={int(time.time() * 1000)}")
    try:
        subprocess.run(command, check=True, capture_output=True)
        return Image.open(raw_path).convert("RGBA")
    finally:
        html_path.unlink(missing_ok=True)
        raw_path.unlink(missing_ok=True)


def _crop_to_art(image: Image.Image, transparent: bool) -> Image.Image:
    arr = np.array(image)
    if transparent:
        mask = arr[:, :, 3] > 10
    else:
        rgb = arr[:, :, :3].astype(np.float32)
        distance = np.sqrt(((rgb - np.array([255.0, 0.0, 255.0], dtype=np.float32)) ** 2).sum(axis=2))
        mask = distance > 30.0
    ys, xs = np.where(mask)
    if not len(xs):
        raise SystemExit("rendered icon is empty")
    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    if box[2] - box[0] != box[3] - box[1]:
        raise SystemExit(f"rendered artwork is not square: {box}")
    return image.crop(box)


def _chroma_key(image: Image.Image) -> Image.Image:
    arr = np.array(image)
    rgb = arr[:, :, :3].astype(np.float32)
    distance = np.sqrt(((rgb - np.array([255.0, 0.0, 255.0], dtype=np.float32)) ** 2).sum(axis=2))
    arr[:, :, 3] = (np.clip((distance - 24.0) / 40.0, 0.0, 1.0) * 255.0).astype(np.uint8)
    keyed = distance < 24.0
    arr[keyed, 0] = 0
    arr[keyed, 1] = 0
    arr[keyed, 2] = 0
    return Image.fromarray(arr, "RGBA")


def render_svg(edge: str, svg_path: Path) -> Image.Image:
    image = _render(edge, svg_path, transparent=True)
    if np.array(image)[:, :, 3].min() < 255:
        art = _crop_to_art(image, transparent=True)
    else:
        # Older Edge builds may ignore the transparent-background flag; fall
        # back to magenta chroma-keying. The magenta fringe is acceptable for
        # a build-time fallback, but the transparent path is preferred.
        image = _render(edge, svg_path, transparent=False)
        art = _chroma_key(_crop_to_art(image, transparent=False))
    return art.resize((TARGET, TARGET), Image.Resampling.LANCZOS)


def build_icon(master: Image.Image, prefix: str) -> None:
    master.save(HERE / f"{prefix}-1024.png")
    for size in PNG_SIZES:
        master.resize((size, size), Image.Resampling.LANCZOS).save(HERE / f"{prefix}-{size}.png")

    frames = [master.resize((size, size), Image.Resampling.LANCZOS).convert("RGBA") for size in ICO_SIZES]
    ico_path = HERE / f"{prefix}.ico"
    frames[0].save(ico_path, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[1:])
    print(f"  {prefix}.ico: {ico_path.stat().st_size} bytes")


def load_font(size: int, bold: bool = False):
    names = ["msyhbd.ttc", "seguisb.ttf", "arialbd.ttf"] if bold else ["msyh.ttc", "segoeui.ttf", "arial.ttf"]
    for name in names:
        path = Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts" / name
        if path.is_file():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def paste_icon(canvas: Image.Image, path: Path, size: int, x: int, y: int) -> None:
    icon = Image.open(path).convert("RGBA").resize((size, size), Image.Resampling.LANCZOS)
    canvas.alpha_composite(icon, (x, y))


def build_compare_sheet(
    left_prefix: str,
    right_prefix: str,
    left_label: str,
    right_label: str,
    subtitle: str,
    out_name: str,
) -> None:
    width, height = 1120, 720
    canvas = Image.new("RGBA", (width, height), (244, 246, 250, 255))
    draw = ImageDraw.Draw(canvas)
    title_font = load_font(30, bold=True)
    label_font = load_font(16, bold=True)
    small_font = load_font(13)
    tiny_font = load_font(11)

    draw.text((48, 34), "DeskSnip icon comparison", fill=(24, 33, 48, 255), font=title_font)
    draw.text((48, 76), subtitle, fill=(91, 104, 122, 255), font=small_font)

    # Large previews.
    paste_icon(canvas, HERE / f"{left_prefix}-256.png", 220, 110, 118)
    paste_icon(canvas, HERE / f"{right_prefix}-256.png", 220, 450, 118)
    for center, label in ((220, left_label), (560, right_label)):
        label_width = draw.textlength(label, font=label_font)
        draw.text((center - label_width / 2, 350), label, fill=(40, 52, 70, 255), font=label_font)

    # Small-size strips.
    sizes = [64, 48, 32, 16]
    light_y, dark_y = 404, 544
    panel_h = 124
    for bg, y in (
        ((255, 255, 255, 255), light_y),
        ((17, 24, 39, 255), dark_y),
    ):
        draw.rounded_rectangle((48, y, 1072, y + panel_h), radius=16, fill=bg)
    draw.text((72, light_y + 18), "LIGHT", fill=(120, 132, 150, 255), font=label_font)
    draw.text((72, dark_y + 18), "DARK", fill=(156, 168, 186, 255), font=label_font)

    for prefix, start in ((left_prefix, 210), (right_prefix, 620)):
        x = start
        for size in sizes:
            for panel_y in (light_y, dark_y):
                paste_icon(
                    canvas,
                    HERE / f"{prefix}-{size}.png",
                    size,
                    x,
                    panel_y + 32 + (64 - size) // 2,
                )
            draw.text((x, light_y + 104), f"{size}px", fill=(120, 132, 150, 255), font=tiny_font)
            draw.text((x, dark_y + 104), f"{size}px", fill=(156, 168, 186, 255), font=tiny_font)
            x += size + 34

    out = HERE / out_name
    canvas.convert("RGB").save(out)
    print(f"  compare: {out}")


def build_compare() -> None:
    build_compare_sheet(
        "desksnip-original",
        "desksnip-light",
        "ORIGINAL",
        "LIGHT VERSION",
        "Left: original icon    Right: light version",
        "desksnip-compare.png",
    )


def build_frosted_compare() -> None:
    build_compare_sheet(
        "desksnip-light",
        "desksnip-frosted",
        "PREVIOUS (LIGHT)",
        "CURRENT (FROSTED)",
        "Left: previous light version    Right: current frosted version",
        "desksnip-compare-frosted.png",
    )


def main() -> None:
    edge = find_edge()
    for prefix, svg_path in SOURCES.items():
        if not svg_path.is_file():
            raise SystemExit(f"missing source: {svg_path}")
        master = render_svg(edge, svg_path)
        opaque = np.array(master)[:, :, 3] > 10
        ys, xs = np.where(opaque)
        print(f"  {prefix}: content bbox x[{xs.min()},{xs.max()}] y[{ys.min()},{ys.max()}]")
        build_icon(master, prefix)
    build_compare()
    build_frosted_compare()
    print("DONE")


if __name__ == "__main__":
    main()
