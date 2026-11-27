"""L1 look proof for ribbon-mesh cliff edges (NC-D14/NC-D15): hashed straight-edge pieces along a 2 m ribbon.

Atlas layout (shader PaintedGroundCore.hlsl): 8 x 4 cells of 128 px, case c variant v at column c % 8, row c // 8 + 2 * v,
row 0 at the top of the image. Case bits: 1 top-left, 2 top-right, 4 bottom-right, 8 bottom-left (top = +Z).
"""
import argparse
import math
import os
from typing import List, Tuple

import numpy
from PIL import Image, ImageDraw

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ATLAS_FOLDER = os.path.join(REPO_ROOT, "Assets", "PaintedGround", "Textures", "EdgeTiles")
ATLASES = [
    ("GrassOverDirt", "T_EdgeAtlas_GrassOverDirt.png", (0x7F, 0x9A, 0x55)),
    ("ForestOverGrass", "T_EdgeAtlas_ForestOverGrass.png", (0x4F, 0x6E, 0x3A)),
    ("StoneOverDirt", "T_EdgeAtlas_StoneOverDirt.png", (0x9A, 0x95, 0x8A)),
]
ROCK_COLOUR = (0x8C, 0x7F, 0x70)
INK_COLOUR = (16, 12, 10)
CELL_PIXELS = 128
CELL_METRES = 2.0
RIBBON_WIDTH_METRES = 1.55
WALL_SIDE_MID_LINE_METRES = 0.55
# Quarter turns (numpy.rot90, counter-clockwise) that bring the covered side to the top of the image (+v).
CASE_TO_QUARTER_TURNS = {3: 0, 6: 1, 12: 2, 9: 3}
PIECE_ORDER: List[Tuple[int, int]] = [(case, variant) for case in (3, 6, 12, 9) for variant in (0, 1)]
JOIN_MISMATCH_LIMIT = 0.15


def variant_hash(cell_x: int, cell_y: int, seed: int) -> int:
    mask = 0xFFFFFFFF
    value = ((cell_x * 73856093) ^ (cell_y * 19349663) ^ (seed * 83492791)) & mask
    value ^= value >> 13
    value = (value * 0x5BD1E995) & mask
    value ^= value >> 15
    return value


def piece_index_for_segment(segment_number: int) -> int:
    return variant_hash(segment_number, 0, 0) % len(PIECE_ORDER)


def load_pieces(atlas_path: str) -> numpy.ndarray:
    atlas = numpy.asarray(Image.open(atlas_path).convert("RGB"), dtype=numpy.float32) / 255.0
    pieces = []
    for case, variant in PIECE_ORDER:
        top = (case // 8 + 2 * variant) * CELL_PIXELS
        left = (case % 8) * CELL_PIXELS
        cell = atlas[top:top + CELL_PIXELS, left:left + CELL_PIXELS]
        rotated = numpy.rot90(cell, CASE_TO_QUARTER_TURNS[case])
        half_difference = rotated[:CELL_PIXELS // 2, :, 0].mean() - rotated[CELL_PIXELS // 2:, :, 0].mean()
        assert half_difference > 0.1, f"case {case} var {variant}: covered side did not land on top ({half_difference:.2f})"
        pieces.append(numpy.ascontiguousarray(rotated))
    return numpy.stack(pieces)


def shade(texels: numpy.ndarray, biome_colour: Tuple[int, int, int]) -> numpy.ndarray:
    covered = (texels[..., 0] > 0.5)[..., None]
    colour = numpy.where(covered, numpy.array(biome_colour, dtype=numpy.float32), numpy.array(ROCK_COLOUR, dtype=numpy.float32))
    colour = colour + (255.0 - colour) * 0.45 * texels[..., 1:2]
    colour = colour + (numpy.array(INK_COLOUR, dtype=numpy.float32) - colour) * texels[..., 2:3]
    return numpy.clip(colour, 0, 255).astype(numpy.uint8)


def ribbon_path(total_length: float, step: float, curvature_at) -> numpy.ndarray:
    count = int(total_length / step)
    points = numpy.zeros((count, 4), dtype=numpy.float64)  # x, z, heading, arc length
    heading = 0.0
    position_x = position_z = 0.0
    for sample in range(count):
        arc = sample * step
        points[sample] = (position_x, position_z, heading, arc)
        position_x += math.cos(heading) * step
        position_z += math.sin(heading) * step
        heading += curvature_at(arc) * step
    return points


def curved_curvature(arc: float) -> float:
    first_arc_length = 40.0
    bend_length = 8.0 * math.pi / 2.0
    if arc < first_arc_length:
        return 1.0 / 12.0
    if arc < first_arc_length + bend_length:
        return -1.0 / 8.0
    return 1.0 / 8.0


def render_ribbon(path: numpy.ndarray, pieces: numpy.ndarray, biome_colour: Tuple[int, int, int],
                  pixels_per_metre: float, canvas_size: Tuple[int, int], origin: Tuple[float, float]) -> numpy.ndarray:
    across_step = 1.0 / pixels_per_metre / 2.0
    across = numpy.arange(0.0, RIBBON_WIDTH_METRES, across_step)
    canvas = numpy.full((canvas_size[1], canvas_size[0], 3), 24, dtype=numpy.uint8)
    heading = path[:, 2:3]
    world_x = path[:, 0:1] - numpy.sin(heading) * across[None, :]
    world_z = path[:, 1:2] + numpy.cos(heading) * across[None, :]
    arc_grid = numpy.broadcast_to(path[:, 3:4], world_x.shape)
    across_grid = numpy.broadcast_to(across[None, :], world_x.shape)

    segment_number = numpy.floor(arc_grid / CELL_METRES).astype(numpy.int64)
    along = (arc_grid / CELL_METRES - segment_number) * CELL_PIXELS
    across_v = 0.5 + (across_grid - WALL_SIDE_MID_LINE_METRES) / CELL_METRES
    texel_x = numpy.clip(along.astype(numpy.int64), 0, CELL_PIXELS - 1)
    texel_y = numpy.clip(((1.0 - across_v) * CELL_PIXELS).astype(numpy.int64), 0, CELL_PIXELS - 1)
    lookup = numpy.array([piece_index_for_segment(int(number)) for number in range(int(segment_number.max()) + 1)])
    texels = pieces[lookup[segment_number], texel_y, texel_x]

    pixel_x = ((world_x - origin[0]) * pixels_per_metre).astype(numpy.int64)
    pixel_y = ((origin[1] - world_z) * pixels_per_metre).astype(numpy.int64)
    inside = (pixel_x >= 0) & (pixel_x < canvas_size[0]) & (pixel_y >= 0) & (pixel_y < canvas_size[1])
    canvas[pixel_y[inside], pixel_x[inside]] = shade(texels[inside], biome_colour)
    return canvas


def seam_report(pieces: numpy.ndarray, name: str) -> None:
    straight = [piece_index_for_segment(number) for number in range(16)]
    joins = [float(numpy.abs(pieces[straight[number], :, -1, 0] - pieces[straight[number + 1], :, 0, 0]).mean()) for number in range(15)]
    print(f"{name}: straight-ribbon seam score (mean |dR| over 15 joins) = {sum(joins) / len(joins):.4f}, worst = {max(joins):.4f}")
    bad_pairs = []
    for left_index, (left_case, left_variant) in enumerate(PIECE_ORDER):
        for right_index, (right_case, right_variant) in enumerate(PIECE_ORDER):
            difference = float(numpy.abs(pieces[left_index, :, -1, 0] - pieces[right_index, :, 0, 0]).mean())
            if difference > JOIN_MISMATCH_LIMIT:
                bad_pairs.append(f"case {left_case} v{left_variant} -> case {right_case} v{right_variant} ({difference:.2f})")
    print(f"{name}: {len(bad_pairs)} of 64 right->left piece pairs exceed {JOIN_MISMATCH_LIMIT}" + ("" if not bad_pairs else ": " + "; ".join(bad_pairs)))


def build_sheet(output_folder: str) -> None:
    straight_scale = 30.0
    curve_scale = 20.0
    straight_path = ribbon_path(32.0, 0.01, lambda arc: 0.0)
    curved_path = ribbon_path(40.0 + 8.0 * math.pi, 0.01, curved_curvature)
    curve_min_x, curve_max_x = curved_path[:, 0].min() - 2.0, curved_path[:, 0].max() + 2.0
    curve_min_z, curve_max_z = curved_path[:, 1].min() - 2.0, curved_path[:, 1].max() + 2.0
    curve_size = (int((curve_max_x - curve_min_x) * curve_scale), int((curve_max_z - curve_min_z) * curve_scale))
    print(f"curve panel {curve_size[0]} x {curve_size[1]} px")

    block_height = 660
    sheet = Image.new("RGB", (1990, block_height * len(ATLASES)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    for block, (name, file_name, biome_colour) in enumerate(ATLASES):
        pieces = load_pieces(os.path.join(ATLAS_FOLDER, file_name))
        seam_report(pieces, name)
        top = block * block_height
        draw.text((8, top + 4), f"{name}: straight ribbon 32 m (16 segments, hashed), 2 m cell = 60 px", fill=(230, 230, 230))
        straight = render_ribbon(straight_path, pieces, biome_colour, straight_scale, (960, 50), (0.0, 1.55))
        sheet.paste(Image.fromarray(straight), (8, top + 20))

        draw.text((8, top + 90), "the 8 pieces, common orientation (covered side up), edge to edge", fill=(230, 230, 230))
        for slot, (case, variant) in enumerate(PIECE_ORDER):
            tile = Image.fromarray(shade(pieces[slot], biome_colour)).resize((120, 120), Image.NEAREST)
            sheet.paste(tile, (8 + slot * 120, top + 106))
            draw.text((8 + slot * 120 + 2, top + 228), f"case {case} rot {CASE_TO_QUARTER_TURNS[case] * 90} var {variant}", fill=(230, 230, 230))

        curve = render_ribbon(curved_path, pieces, biome_colour, curve_scale, curve_size, (curve_min_x, curve_max_z))
        curve_image = Image.fromarray(curve)
        curve_image.thumbnail((1010, block_height - 20))
        sheet.paste(curve_image, (975, top + 10))
        draw.text((978, top + 12), "bent: 40 m arc r12 (left), then S-bend r8", fill=(230, 230, 230))

    if max(sheet.size) > 2000:
        sheet.thumbnail((2000, 2000))
    os.makedirs(output_folder, exist_ok=True)
    output_path = os.path.join(output_folder, "L1_edge_strip_mock.png")
    sheet.save(output_path)
    print(f"wrote {output_path} {sheet.size}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    build_sheet(parser.parse_args().out)
