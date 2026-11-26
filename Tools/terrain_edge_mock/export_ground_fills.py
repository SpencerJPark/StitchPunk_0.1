"""Export seamlessly tileable 512x512 ground fills (periodic versions of edge_tile_mock.make_fill)."""
import math
import os
import re
import sys
import uuid

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import edge_tile_mock as mock  # noqa: E402

SIZE = 512
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
FILL_DIR = os.path.join(REPO, "Assets", "PaintedGround", "Textures", "EdgeTiles", "Fills")
TEMPLATE_META = os.path.join(REPO, "Assets", "PaintedGround", "Textures", "EdgeTiles", "T_EdgeAtlas_GrassOverDirt.png.meta")
FOLDER_META_SOURCE = os.path.join(REPO, "Assets", "PaintedGround", "Textures", "EdgeTiles.meta")
PREVIEW_PATH = "C:/Users/spenc/AppData/Local/Temp/claude/fbb2198090845187/b95f0095-1d15-486c-88fa-a5895636278a/scratchpad/fills_preview.png"
NAMES = {mock.DIRT: "Dirt", mock.GRASS: "Grass", mock.FOREST: "Forest", mock.STONE: "Stone"}


def periodic_value_noise(cell_pixels, seed):
    cells = SIZE // cell_pixels
    grid = np.random.default_rng(seed).random((cells, cells))
    tiled = np.tile(grid, (3, 3))
    image = Image.fromarray((tiled * 255).astype(np.uint8)).resize((cells * 3 * cell_pixels,) * 2, Image.BICUBIC)
    return np.asarray(image)[SIZE:2 * SIZE, SIZE:2 * SIZE] / 255.0


def periodic_cobbles(seed, tones):
    random_generator = np.random.default_rng(seed)
    count = 13
    cell = SIZE / count
    jitter_x = random_generator.uniform(0.15, 0.85, (count, count))
    jitter_y = random_generator.uniform(0.15, 0.85, (count, count))
    tone_index = random_generator.integers(0, 3, (count, count))
    pixel_y, pixel_x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    base_row = (pixel_y // cell).astype(int)
    base_column = (pixel_x // cell).astype(int)
    nearest = np.full((SIZE, SIZE), 1e9)
    second = np.full((SIZE, SIZE), 1e9)
    nearest_tone = np.zeros((SIZE, SIZE), dtype=int)
    for offset_row in (-1, 0, 1):
        for offset_column in (-1, 0, 1):
            row = base_row + offset_row
            column = base_column + offset_column
            seed_x = (column + jitter_x[row % count, column % count]) * cell
            seed_y = (row + jitter_y[row % count, column % count]) * cell
            distance = np.hypot(pixel_x - seed_x, pixel_y - seed_y)
            closer = distance < nearest
            second = np.where(closer, nearest, np.minimum(second, distance))
            nearest_tone = np.where(closer, tone_index[row % count, column % count], nearest_tone)
            nearest = np.where(closer, distance, nearest)
    palette = np.array(tones, dtype=np.float64)
    image = palette[nearest_tone]
    mortar = np.clip((second - nearest - 1.5) / 2.0, 0.0, 1.0)[..., None]
    image = image * mortar + np.array(mock.shade(mock.INK, 1.6)) * (1.0 - mortar)
    return image / 255.0


def periodic_fill(biome, seed):
    tones = mock.FILL_COLOURS[biome]
    if biome == mock.STONE:
        return periodic_cobbles(seed, tones)
    blotch = periodic_value_noise(64, seed) * 0.65 + periodic_value_noise(32, seed + 1) * 0.35
    image = np.zeros((SIZE, SIZE, 3))
    image[:] = tones[0]
    image[blotch > 0.64] = tones[1]
    image[blotch < 0.30] = tones[2]
    canvas = Image.fromarray(image.astype(np.uint8), "RGB")
    draw = ImageDraw.Draw(canvas)
    random_generator = np.random.default_rng(seed + 7)
    mark_count = int(SIZE * SIZE / (mock.PIXELS_PER_METRE ** 2) * {mock.GRASS: 1.6, mock.DIRT: 1.2, mock.FOREST: 2.2}[biome])
    for _ in range(mark_count):
        centre_x = random_generator.uniform(0, SIZE)
        centre_y = random_generator.uniform(0, SIZE)
        if biome == mock.GRASS:
            shapes = []
            for side in (-1, 1):
                length = random_generator.uniform(5, 9)
                shapes.append(("line", side * length * 0.45, -length))
        elif biome == mock.DIRT:
            radius_x = random_generator.uniform(2.5, 5.5)
            shapes = [("ellipse", radius_x, radius_x * random_generator.uniform(0.55, 0.85))]
        else:
            angle = random_generator.uniform(0, math.pi)
            length = random_generator.uniform(5, 10)
            shapes = [("line", math.cos(angle) * length, math.sin(angle) * length)]
        for shift_x in (-SIZE, 0, SIZE):
            for shift_y in (-SIZE, 0, SIZE):
                origin_x = centre_x + shift_x
                origin_y = centre_y + shift_y
                for kind, first, second in shapes:
                    if kind == "line":
                        colour = mock.shade(tones[2], 0.78 if biome == mock.GRASS else 0.7)
                        draw.line([(origin_x, origin_y), (origin_x + first, origin_y + second)], fill=colour, width=2)
                    else:
                        draw.ellipse([origin_x - first, origin_y - second, origin_x + first, origin_y + second],
                                     fill=tones[1], outline=mock.shade(mock.INK, 1.35), width=2)
    return np.asarray(canvas).astype(np.float64) / 255.0


def write_texture_meta(meta_path):
    if os.path.exists(meta_path):
        return None
    text = open(TEMPLATE_META, encoding="utf-8").read()
    guid = uuid.uuid4().hex
    text = re.sub(r"^guid: .*$", "guid: " + guid, text, count=1, flags=re.M)
    for key, value in (("sRGBTexture", 1), ("wrapU", 0), ("wrapV", 0), ("wrapW", 0), ("enableMipMap", 1)):
        text = re.sub(r"^(\s*%s): .*$" % key, r"\g<1>: %d" % value, text, flags=re.M)
    text = re.sub(r"^(\s*maxTextureSize): .*$", r"\g<1>: 1024", text, flags=re.M)
    with open(meta_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
    return guid


def seam_numbers(array):
    grey = array.astype(np.float64).mean(axis=2)
    column_seam = np.abs(grey[:, 0] - grey[:, -1]).mean()
    row_seam = np.abs(grey[0, :] - grey[-1, :]).mean()
    column_interior = np.abs(np.diff(grey, axis=1)).mean()
    row_interior = np.abs(np.diff(grey, axis=0)).mean()
    return column_seam, column_interior, row_seam, row_interior


def main():
    os.makedirs(FILL_DIR, exist_ok=True)
    folder_meta = FILL_DIR + ".meta"
    if not os.path.exists(folder_meta):
        text = open(FOLDER_META_SOURCE, encoding="utf-8").read()
        text = re.sub(r"^guid: .*$", "guid: " + uuid.uuid4().hex, text, count=1, flags=re.M)
        with open(folder_meta, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
    images = {}
    for biome, name in NAMES.items():
        array = (np.clip(periodic_fill(biome, 1000 + biome * 17), 0, 1) * 255).round().astype(np.uint8)
        path = os.path.join(FILL_DIR, "T_Fill_%s.png" % name)
        Image.fromarray(array, "RGB").save(path)
        guid = write_texture_meta(path + ".meta")
        column_seam, column_interior, row_seam, row_interior = seam_numbers(array)
        median = np.median(array.reshape(-1, 3), axis=0).astype(int).tolist()
        print("%s guid=%s seam col %.2f (interior %.2f) row %.2f (interior %.2f) median %s vs %s" % (
            name, guid, column_seam, column_interior, row_seam, row_interior, median, mock.FILL_COLOURS[biome][0]))
        images[biome] = array
    tile = 400
    sheet = Image.new("RGB", (tile * 4, tile * 4))
    for index, biome in enumerate(NAMES):
        row, column = divmod(index, 2)
        two_by_two = Image.fromarray(np.tile(images[biome], (2, 2, 1)), "RGB").resize((tile * 2, tile * 2), Image.LANCZOS)
        sheet.paste(two_by_two, (column * tile * 2, row * tile * 2))
    sheet.resize((1600, 1600), Image.LANCZOS).save(PREVIEW_PATH)


if __name__ == "__main__":
    main()
