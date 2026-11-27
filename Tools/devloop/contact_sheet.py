"""Pack several captures into one PNG contact sheet so a reviewer judges one image."""
from __future__ import annotations

import argparse
import glob
import math
import statistics
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

BACKGROUND_COLOR: tuple[int, int, int] = (30, 30, 30)
LABEL_COLOR: tuple[int, int, int] = (45, 45, 45)
REFERENCE_LABEL_COLOR: tuple[int, int, int] = (200, 140, 20)
LABEL_TEXT_COLOR: tuple[int, int, int] = (235, 235, 235)
REFERENCE_TEXT_COLOR: tuple[int, int, int] = (20, 20, 20)
GUTTER: int = 8
FONT_SIZE: int = 16
LABEL_HEIGHT: int = FONT_SIZE + 14
TITLE_HEIGHT: int = FONT_SIZE + 22


def load_font(size: int) -> ImageFont.ImageFont:
    try:
        return ImageFont.truetype("arial.ttf", size)
    except OSError:
        return ImageFont.load_default()


def expand_inputs(patterns: list[str]) -> list[Path]:
    paths: list[Path] = []
    for pattern in patterns:
        matches: list[str] = sorted(glob.glob(pattern))
        if not matches:
            print(f"contact_sheet: no such file: {pattern}", file=sys.stderr)
            sys.exit(1)
        paths.extend(Path(match) for match in matches)
    return paths


def build_sheet(
    cells: list[tuple[Path, str, bool]], columns: int, title: str | None, long_side: int
) -> Image.Image:
    images: list[Image.Image] = [Image.open(path).convert("RGB") for path, _, _ in cells]
    median_aspect: float = statistics.median(image.width / image.height for image in images)
    rows: int = math.ceil(len(cells) / columns)
    title_height: int = TITLE_HEIGHT if title else 0
    # Fit the cells to the long side up front so labels stay at FONT_SIZE instead of shrinking with the sheet.
    height_fitting_width: float = (long_side - (columns + 1) * GUTTER) / (columns * median_aspect)
    height_fitting_height: float = (long_side - title_height - rows * LABEL_HEIGHT - (rows + 1) * GUTTER) / rows
    cell_height: int = max(1, int(min(max(image.height for image in images), height_fitting_width, height_fitting_height)))
    cell_width: int = max(1, round(cell_height * median_aspect))
    sheet_width: int = columns * cell_width + (columns + 1) * GUTTER
    sheet_height: int = title_height + rows * (cell_height + LABEL_HEIGHT) + (rows + 1) * GUTTER
    sheet: Image.Image = Image.new("RGB", (sheet_width, sheet_height), BACKGROUND_COLOR)
    draw: ImageDraw.ImageDraw = ImageDraw.Draw(sheet)
    font: ImageFont.ImageFont = load_font(FONT_SIZE)

    if title:
        draw.text((GUTTER, GUTTER), title, fill=LABEL_TEXT_COLOR, font=font)

    for index, ((_, label, is_reference), image) in enumerate(zip(cells, images)):
        column: int = index % columns
        row: int = index // columns
        origin_x: int = GUTTER + column * (cell_width + GUTTER)
        origin_y: int = title_height + GUTTER + row * (cell_height + LABEL_HEIGHT + GUTTER)
        scale: float = min(cell_width / image.width, cell_height / image.height)
        fitted_size: tuple[int, int] = (
            max(1, round(image.width * scale)),
            max(1, round(image.height * scale)),
        )
        fitted: Image.Image = image.resize(fitted_size, Image.LANCZOS)
        sheet.paste(
            fitted,
            (origin_x + (cell_width - fitted_size[0]) // 2, origin_y + (cell_height - fitted_size[1]) // 2),
        )
        bar_top: int = origin_y + cell_height
        bar_color: tuple[int, int, int] = REFERENCE_LABEL_COLOR if is_reference else LABEL_COLOR
        text_color: tuple[int, int, int] = REFERENCE_TEXT_COLOR if is_reference else LABEL_TEXT_COLOR
        draw.rectangle((origin_x, bar_top, origin_x + cell_width - 1, bar_top + LABEL_HEIGHT - 1), fill=bar_color)
        draw.text((origin_x + 6, bar_top + 6), label, fill=text_color, font=font)
    return sheet


def main() -> int:
    parser: argparse.ArgumentParser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", nargs="+", help="capture PNGs or glob patterns")
    parser.add_argument("--out", required=True)
    parser.add_argument("--reference")
    parser.add_argument("--columns", type=int)
    parser.add_argument("--long-side", type=int, default=2000)
    parser.add_argument("--title")
    arguments: argparse.Namespace = parser.parse_args()

    capture_paths: list[Path] = expand_inputs(arguments.captures)
    cells: list[tuple[Path, str, bool]] = [(path, path.stem, False) for path in capture_paths]
    if arguments.reference:
        reference_paths: list[Path] = expand_inputs([arguments.reference])
        reference_path: Path = reference_paths[0]
        cells.insert(0, (reference_path, f"REFERENCE: {reference_path.name}", True))

    columns: int = arguments.columns or math.ceil(math.sqrt(len(cells)))
    columns = max(1, min(columns, len(cells)))
    sheet: Image.Image = build_sheet(cells, columns, arguments.title, arguments.long_side)

    longest_side: int = max(sheet.size)
    if longest_side > arguments.long_side:
        shrink: float = arguments.long_side / longest_side
        sheet = sheet.resize(
            (max(1, round(sheet.width * shrink)), max(1, round(sheet.height * shrink))), Image.LANCZOS
        )

    output_path: Path = Path(arguments.out)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output_path)
    print(f"{output_path}  {sheet.width}x{sheet.height}  {len(cells)} cells")
    return 0


if __name__ == "__main__":
    sys.exit(main())
