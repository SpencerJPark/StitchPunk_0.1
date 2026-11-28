"""Usage: python Tools/terrain_edge_mock/export_transition_atlas.py [SetName ...] [--preview-dir PATH]
Default sets: GrassOverDirt ForestOverGrass StoneOverDirt. Reads Docs/Terrain/EdgeTileExamples/T_Edge_<Set>_v0/_v1.png,
writes Assets/PaintedGround/Textures/EdgeTiles/T_EdgeAtlas_<Set>.png (and a .png.meta cloned from the GrassOverDirt one when missing).

Export an edge set (two 1280x256 variant strips) to the 8x4 x 128 px transition atlas the ground shader reads.
Atlas cell: case c, variant v at column c % 8, row c // 8 + 2v; row 0 is the TOP of the PNG and cell-local up = north
(PaintedGroundCore.hlsl line 140: v = 1 - (row + 1 - local.y) / 4, and Unity UV v=1 is the top PNG row).
Channels: R coverage, G rim (0), B ink."""
import argparse
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
PIXELS_PER_METRE = 64.0
RIM_OFFSET_METRES = 0.225  # rim silhouette mean line sits at C - r_top/2 with r_top = 0.45 m (CE-D4)
FOOT_OFFSET_METRES = 0.675  # foot silhouette mean line sits at C + r_top + r_foot/2 = 0.675 m from the side midpoint (CE-D4)
DIRT_SIDE_LOCK_PIXELS = 12.0
DIRT_SIDE_BLEND_PIXELS = 40.0
DIRT_BLUR_SIGMA_PIXELS = 7.0
DIRT_INK_HALF_WIDTH_PIXELS = 2.2
DIRT_INK_COVERAGE_FRACTION = 0.45
CROSSING_TOLERANCE_PIXELS = 4.0
SIDE_BLEND_METRES = 0.12
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



def signed_distance_inside_positive(coverage):
    inside = coverage > 0.5
    padded = np.pad(inside, 1, mode="edge")
    inside_neighbour_count = (padded[:-2, 1:-1].astype(int) + padded[2:, 1:-1] + padded[1:-1, :-2] + padded[1:-1, 2:])
    inside_boundary = inside & (inside_neighbour_count < 4)
    outside_boundary = ~inside & (inside_neighbour_count > 0)
    height, width = inside.shape
    grid_y, grid_x = np.mgrid[0:height, 0:width]
    flat_positions = np.stack([grid_y.ravel(), grid_x.ravel()], axis=1).astype(np.float32)
    distance = np.full(height * width, 1.0e4, dtype=np.float32)
    inside_flat = inside.ravel()
    for target_mask, query_mask, sign in ((outside_boundary, inside_flat, 1.0), (inside_boundary, ~inside_flat, -1.0)):
        targets = np.argwhere(target_mask).astype(np.float32)
        if len(targets) == 0:
            continue
        query_indices = np.nonzero(query_mask)[0]
        for chunk_start in range(0, len(query_indices), 2048):
            chunk = query_indices[chunk_start:chunk_start + 2048]
            offsets = flat_positions[chunk][:, None, :] - targets[None, :, :]
            nearest = np.sqrt((offsets * offsets).sum(axis=2).min(axis=1)) - 0.5
            distance[chunk] = sign * np.maximum(nearest, 0.0)
    return distance.reshape(height, width)


def bilinear_sample(array, sample_y, sample_x):
    height, width = array.shape
    sample_y = np.clip(sample_y, 0, height - 1.001)
    sample_x = np.clip(sample_x, 0, width - 1.001)
    floor_y = np.floor(sample_y).astype(int)
    floor_x = np.floor(sample_x).astype(int)
    fraction_y = sample_y - floor_y
    fraction_x = sample_x - floor_x
    top = array[floor_y, floor_x] * (1 - fraction_x) + array[floor_y, floor_x + 1] * fraction_x
    bottom = array[floor_y + 1, floor_x] * (1 - fraction_x) + array[floor_y + 1, floor_x + 1] * fraction_x
    return top * (1 - fraction_y) + bottom * fraction_y


def gaussian_blur(array, sigma):
    radius = int(np.ceil(sigma * 3))
    kernel = np.exp(-0.5 * (np.arange(-radius, radius + 1) / sigma) ** 2)
    kernel /= kernel.sum()
    padded = np.pad(array, radius, mode="edge")
    blurred_rows = np.apply_along_axis(lambda line: np.convolve(line, kernel, mode="valid"), 1, padded)
    return np.apply_along_axis(lambda line: np.convolve(line, kernel, mode="valid"), 0, blurred_rows)[:array.shape[0], :array.shape[1]]


def smoothed_dirt_source(coverage, seed):
    blurred = gaussian_blur(coverage.astype(np.float32), DIRT_BLUR_SIGMA_PIXELS)
    row_indices = np.arange(coverage.shape[0], dtype=np.float32)
    column_indices = np.arange(coverage.shape[1], dtype=np.float32)
    row_side_distance = np.minimum(row_indices, coverage.shape[0] - 1 - row_indices)[:, None]
    column_side_distance = np.minimum(column_indices, coverage.shape[1] - 1 - column_indices)[None, :]
    side_distance = np.minimum(row_side_distance, column_side_distance)
    side_blend = np.clip((side_distance - DIRT_SIDE_LOCK_PIXELS) / DIRT_SIDE_BLEND_PIXELS, 0.0, 1.0)  # keep the art's own side crossing so the smoothing cannot drag it off the midpoint
    smooth_coverage = ((coverage * (1.0 - side_blend) + blurred * side_blend) > 0.5).astype(np.float32)
    distance = signed_distance_inside_positive(smooth_coverage)
    band = np.exp(-(distance / DIRT_INK_HALF_WIDTH_PIXELS) ** 2)
    random_generator = np.random.default_rng(seed)
    dash_noise = gaussian_blur(random_generator.random(coverage.shape).astype(np.float32), 6.0)
    dash_threshold = np.quantile(dash_noise, 1.0 - DIRT_INK_COVERAGE_FRACTION)
    dash_mask = np.clip((dash_noise - dash_threshold) / 0.01 + 0.5, 0.0, 1.0)
    return smooth_coverage, band * dash_mask


def analytic_case_distance_and_gradient(case_index, position_x, position_z):
    # Signed distance in metres (inside positive) of the guide's case arcs inside one 2 m cell, plus its unit gradient.
    radius = 1.0
    corners = {1: (0.0, 2.0), 2: (2.0, 2.0), 4: (2.0, 0.0), 8: (0.0, 0.0)}

    def radial(corner):
        offset_x = position_x - corner[0]
        offset_z = position_z - corner[1]
        length = np.maximum(np.sqrt(offset_x * offset_x + offset_z * offset_z), 1e-6)
        return length, offset_x / length, offset_z / length

    if case_index in (1, 2, 4, 8):
        length, direction_x, direction_z = radial(corners[case_index])
        return radius - length, -direction_x, -direction_z
    if case_index in (14, 13, 11, 7):
        uncovered_bit = 15 - case_index
        length, direction_x, direction_z = radial(corners[uncovered_bit])
        return length - radius, direction_x, direction_z
    ones = np.ones_like(position_x)
    zeros = np.zeros_like(position_x)
    if case_index == 3:
        return position_z - 1.0, zeros, ones
    if case_index == 12:
        return 1.0 - position_z, zeros, -ones
    if case_index == 6:
        return position_x - 1.0, ones, zeros
    if case_index == 9:
        return 1.0 - position_x, -ones, zeros
    first_corner, second_corner = ((2, 8) if case_index == 5 else (1, 4))
    first_length, first_x, first_z = radial(corners[first_corner])
    second_length, second_x, second_z = radial(corners[second_corner])
    first_is_nearer = first_length <= second_length
    return (np.where(first_is_nearer, first_length, second_length) - radius, np.where(first_is_nearer, first_x, second_x), np.where(first_is_nearer, first_z, second_z))


def pixel_centre_metres(pixel_count):
    # Row 0 is the top of the piece (+Z), column 0 is x = 0.
    pixel_indices = (np.arange(pixel_count, dtype=np.float32) + 0.5) / pixel_count * 2.0
    grid_z = 2.0 - pixel_indices[:, None] * np.ones((1, pixel_count), dtype=np.float32)
    grid_x = np.ones((pixel_count, 1), dtype=np.float32) * pixel_indices[None, :]
    return grid_x, grid_z


def offset_case_piece(case_index, coverage, ink, offset_metres):
    # Normal transfer: the eroded boundary sd(p) = offset is pulled back along the analytic normal onto the source boundary sd(q) = 0.
    pixel_count = coverage.shape[0]
    pixels_per_metre = pixel_count / 2.0
    grid_x, grid_z = pixel_centre_metres(pixel_count)
    distance, gradient_x, gradient_z = analytic_case_distance_and_gradient(case_index, grid_x, grid_z)
    source_x = grid_x - offset_metres * gradient_x
    source_z = grid_z - offset_metres * gradient_z
    source_column = source_x * pixels_per_metre - 0.5
    source_row = (2.0 - source_z) * pixels_per_metre - 0.5
    inside_piece = (source_column >= 0) & (source_column <= pixel_count - 1) & (source_row >= 0) & (source_row <= pixel_count - 1)
    sampled_coverage = bilinear_sample(coverage, source_row, source_column)
    sampled_ink = bilinear_sample(ink, source_row, source_column)
    fallback_coverage = (distance - offset_metres > 0).astype(np.float32)
    transferred_coverage = np.where(inside_piece, sampled_coverage, fallback_coverage)
    transferred_ink = np.where(inside_piece, np.clip(sampled_ink, 0.0, 1.0), 0.0)
    # Near a cell side the transferred art drifts off the offset line; blend to the analytic step so both cells sharing a side cross at the same point.
    side_distance = np.minimum(np.minimum(grid_x, 2.0 - grid_x), np.minimum(grid_z, 2.0 - grid_z))
    blend_parameter = np.clip(side_distance / SIDE_BLEND_METRES, 0.0, 1.0)
    side_weight = blend_parameter * blend_parameter * (3.0 - 2.0 * blend_parameter)
    analytic_coverage = np.clip((distance - offset_metres) / (1.0 / pixels_per_metre) + 0.5, 0.0, 1.0)
    blended_coverage = analytic_coverage + (transferred_coverage - analytic_coverage) * side_weight
    return blended_coverage.astype(np.float32), (transferred_ink * side_weight).astype(np.float32)


def check_corner_coverage(atlas):
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


def side_crossing_distance_from_covered_corner(line, covered_at_start):
    values = line if covered_at_start else line[::-1]
    for index in range(len(values) - 1):
        if values[index] > 0.5 >= values[index + 1]:
            fraction = (values[index] - 0.5) / max(values[index] - values[index + 1], 1e-6)
            return index + 0.5 + fraction
    return None


def check_side_crossings(atlas, offset_pixels_at_cell_scale, label_text):
    sample_line_inset = 2
    sides = {"top": (1, 2), "right": (2, 4), "bottom": (8, 4), "left": (1, 8)}
    expected_distance = CELL_PIXELS / 2 - offset_pixels_at_cell_scale
    worst_error = 0.0
    checked = 0
    failures = []
    for variant in range(VARIANT_COUNT):
        for case_index in range(1, 15):
            origin_x = (case_index % 8) * CELL_PIXELS
            origin_y = (case_index // 8 + 2 * variant) * CELL_PIXELS
            cell = atlas[origin_y:origin_y + CELL_PIXELS, origin_x:origin_x + CELL_PIXELS, 0]
            for side_name, (start_bit, end_bit) in sides.items():
                start_covered = bool(case_index & start_bit)
                end_covered = bool(case_index & end_bit)
                if start_covered == end_covered:
                    continue
                if side_name == "top":
                    line = cell[sample_line_inset, :]
                elif side_name == "bottom":
                    line = cell[CELL_PIXELS - 1 - sample_line_inset, :]
                elif side_name == "left":
                    line = cell[:, sample_line_inset]
                else:
                    line = cell[:, CELL_PIXELS - 1 - sample_line_inset]
                crossing = side_crossing_distance_from_covered_corner(line, start_covered)
                checked += 1
                if crossing is None or abs(crossing - expected_distance) > CROSSING_TOLERANCE_PIXELS:
                    failures.append((variant, case_index, side_name, crossing))
                else:
                    worst_error = max(worst_error, abs(crossing - expected_distance))
    for failure in failures:
        print(label_text, "CROSSING OFF (variant, case, side, measured px):", failure)
    print(label_text, "side crossings: %d sides checked, %d off, expected %.1f px from covered corner, worst passing error %.2f px" % (checked, len(failures), expected_distance, worst_error))
    assert not failures, failures


def write_preview(atlas, preview_path):
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


def export_set(set_name, preview_directory=None):
    input_prefix = "T_Edge_" + set_name
    output_path = os.path.join(OUTPUT_DIRECTORY, "T_EdgeAtlas_%s.png" % set_name)
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

    check_corner_coverage(atlas)
    print(set_name, "self-check passed: corner coverage matches case bits for all 16 cases x 2 variants")
    if preview_directory:
        write_preview(atlas, os.path.join(preview_directory, "atlas_preview_%s.png" % set_name))
    ensure_meta(output_path)
    print("wrote", output_path, "meanR %.4f meanB %.4f" % (atlas[..., 0].mean(), atlas[..., 2].mean()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("sets", nargs="*")
    parser.add_argument("--preview-dir", default=None)
    arguments = parser.parse_args()
    for set_name in (arguments.sets or DEFAULT_SETS):
        export_set(set_name, arguments.preview_dir)


if __name__ == "__main__":
    main()
