"""Usage: python Tools/terrain_edge_mock/export_transition_atlas.py [SetName ...]
Default sets: GrassOverDirt ForestOverGrass StoneOverDirt. Reads Docs/Terrain/EdgeTileExamples/T_Edge_<Set>_v0/_v1.png,
writes Assets/PaintedGround/Textures/EdgeTiles/T_EdgeAtlas_<Set>.png (and a .png.meta cloned from the GrassOverDirt one when missing).

Export an edge set (two 1280x256 variant strips) to the 8x4 x 128 px transition atlas the ground shader reads.
Atlas cell: case c, variant v at column c % 8, row c // 8 + 2v; row 0 is the TOP of the PNG and cell-local up = north
(PaintedGroundCore.hlsl line 140: v = 1 - (row + 1 - local.y) / 4, and Unity UV v=1 is the top PNG row).
Channels: R coverage, G rim (0), B ink."""
import os
import re
import sys
import uuid

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from edge_tile_mock import CASE_TABLE, PIECES, pack_piece

REPOSITORY_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
INPUT_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Docs", "Terrain", "EdgeTileExamples")
OUTPUT_DIRECTORY = os.path.join(REPOSITORY_ROOT, "Assets", "PaintedGround", "Textures", "EdgeTiles")
META_TEMPLATE_SET = "GrassOverDirt"
DEFAULT_SETS = ["GrassOverDirt", "ForestOverGrass", "StoneOverDirt"]
PREVIEW_DIRECTORY = "C:/Users/spenc/AppData/Local/Temp/claude/fbb2198090845187/b95f0095-1d15-486c-88fa-a5895636278a/scratchpad"
PIECE_PIXELS = 256
CELL_PIXELS = 128
VARIANT_COUNT = 2


def downsample(channel):
    image = Image.fromarray(channel.astype(np.float32), "F")
    return np.clip(np.asarray(image.resize((CELL_PIXELS, CELL_PIXELS), Image.LANCZOS)), 0.0, 1.0)


def pack_strip(strip_path):
    strip = Image.open(strip_path).convert("RGBA")
    assert strip.size == (PIECE_PIXELS * len(PIECES), PIECE_PIXELS), strip.size
    pieces = {name: strip.crop((index * PIECE_PIXELS, 0, (index + 1) * PIECE_PIXELS, PIECE_PIXELS)) for index, name in enumerate(PIECES)}
    fill_key = np.median(np.asarray(pieces["full"]).reshape(-1, 4)[:, :3], axis=0)
    packed = {}
    for name, piece in pieces.items():
        coverage, detail_rgb, detail_alpha = pack_piece(piece, fill_key)
        luminance = detail_rgb @ np.array([0.299, 0.587, 0.114])
        ink = detail_alpha * np.clip(1.0 - luminance / max(float(fill_key @ np.array([0.299, 0.587, 0.114])) / 255.0, 1e-3), 0.0, 1.0)
        packed[name] = (coverage, ink)
    return packed


def ensure_meta(output_path):
    meta_path = output_path + ".meta"
    if os.path.exists(meta_path):
        return
    template_path = os.path.join(OUTPUT_DIRECTORY, "T_EdgeAtlas_%s.png.meta" % META_TEMPLATE_SET)
    with open(template_path, "rb") as template_file:
        template_bytes = template_file.read()
    new_guid = uuid.uuid4().hex.encode()
    meta_bytes = re.sub(rb"^guid: [0-9a-f]+", b"guid: " + new_guid, template_bytes, count=1, flags=re.MULTILINE)
    with open(meta_path, "wb") as meta_file:
        meta_file.write(meta_bytes)
    print("created meta", meta_path)


def export_set(set_name):
    input_prefix = "T_Edge_" + set_name
    output_path = os.path.join(OUTPUT_DIRECTORY, "T_EdgeAtlas_%s.png" % set_name)
    preview_path = os.path.join(PREVIEW_DIRECTORY, "atlas_preview_%s.png" % set_name)
    atlas = np.zeros((CELL_PIXELS * 4, CELL_PIXELS * 8, 3), dtype=np.float32)
    for variant in range(VARIANT_COUNT):
        packed = pack_strip(os.path.join(INPUT_DIRECTORY, "%s_v%d.png" % (input_prefix, variant)))
        for case_index in range(1, 16):
            piece_name, quarter_turns = CASE_TABLE[case_index]
            coverage, ink = packed[piece_name]
            coverage = np.rot90(coverage, -quarter_turns)  # negative k = clockwise
            ink = np.rot90(ink, -quarter_turns)
            origin_x = (case_index % 8) * CELL_PIXELS
            origin_y = (case_index // 8 + 2 * variant) * CELL_PIXELS
            cell = atlas[origin_y:origin_y + CELL_PIXELS, origin_x:origin_x + CELL_PIXELS]
            cell[..., 0] = downsample(coverage)
            cell[..., 2] = downsample(ink)
            if case_index == 15:
                cell[..., 0] = 1.0
                cell[..., 2] = 0.0
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    Image.fromarray((atlas * 255 + 0.5).astype(np.uint8), "RGB").save(output_path)

    inset = 4
    corner_positions = {1: (inset, inset), 2: (CELL_PIXELS - 1 - inset, inset), 4: (CELL_PIXELS - 1 - inset, CELL_PIXELS - 1 - inset), 8: (inset, CELL_PIXELS - 1 - inset)}
    failures = []
    for variant in range(VARIANT_COUNT):
        for case_index in range(16):
            origin_x = (case_index % 8) * CELL_PIXELS
            origin_y = (case_index // 8 + 2 * variant) * CELL_PIXELS
            for bit, (local_x, local_y) in corner_positions.items():
                covered = atlas[origin_y + local_y, origin_x + local_x, 0] > 0.5
                if covered != bool(case_index & bit):
                    failures.append((variant, case_index, bit))
    assert not failures, failures
    print(set_name, "self-check passed: corner coverage matches case bits for all 16 cases x 2 variants")

    preview = np.zeros_like(atlas)
    preview[..., 1] = atlas[..., 0] * 0.6
    preview[..., 0] = atlas[..., 0] * 0.35
    preview = preview * (1.0 - atlas[..., 2:3])
    image = Image.fromarray((np.clip(preview, 0, 1) * 255).astype(np.uint8), "RGB")
    image = image.resize((image.width * 2, image.height * 2), Image.NEAREST)
    draw = ImageDraw.Draw(image)
    for variant in range(VARIANT_COUNT):
        for case_index in range(16):
            draw.text(((case_index % 8) * CELL_PIXELS * 2 + 8, (case_index // 8 + 2 * variant) * CELL_PIXELS * 2 + 8), str(case_index), fill=(255, 255, 0))
    for line in range(0, 9):
        draw.line([(line * CELL_PIXELS * 2, 0), (line * CELL_PIXELS * 2, image.height)], fill=(80, 80, 80))
    for line in range(0, 5):
        draw.line([(0, line * CELL_PIXELS * 2), (image.width, line * CELL_PIXELS * 2)], fill=(80, 80, 80))
    os.makedirs(os.path.dirname(preview_path), exist_ok=True)
    image.save(preview_path)
    ensure_meta(output_path)
    print("wrote", output_path, "meanR %.4f meanB %.4f" % (atlas[..., 0].mean(), atlas[..., 2].mean()))


def main():
    for set_name in (sys.argv[1:] or DEFAULT_SETS):
        export_set(set_name)


if __name__ == "__main__":
    main()
