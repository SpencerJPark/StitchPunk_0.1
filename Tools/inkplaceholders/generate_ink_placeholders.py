"""Procedural placeholder art for the hand-drawn ink look (spec: Tasks/Plans/InkLook_System.md).

Every texture tiles seamlessly. Run: python Tools/inkplaceholders/generate_ink_placeholders.py
Output: Assets/PaintedGround/Textures/Ink/ (+ a preview sheet in the scratch dir passed as argv[1], optional).

Channel layouts (the shader reads these, keep them in sync with the spec):
  T_InkDetail_<Surface>.png  R = ink lines (1 = full ink), G = per-shape tone (0.5 = neutral), B = scratch strokes
  T_InkHatch.png             R = diagonal hatch strokes, G = paper grain (0.5 = neutral)
  T_TransitionAtlas.png      8 x 4 cells of 128 px: column = case % 8 + 8 * variant... see atlas_cell_origin()
                             R = coverage, G = highlight rim, B = ink line
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUTPUT_DIRECTORY = os.path.join(REPO_ROOT, "Assets", "PaintedGround", "Textures", "Ink")


def tileable_value_noise(size, lattice_cells, seed):
    random_generator = np.random.default_rng(seed)
    lattice = random_generator.random((lattice_cells, lattice_cells))
    coordinates = np.arange(size) * lattice_cells / size
    cell_index = np.floor(coordinates).astype(int)
    fraction = coordinates - cell_index
    smooth = fraction * fraction * (3 - 2 * fraction)
    index_next = (cell_index + 1) % lattice_cells
    rows_low = lattice[cell_index][:, cell_index] * (1 - smooth)[None, :] + lattice[cell_index][:, index_next] * smooth[None, :]
    rows_high = lattice[index_next][:, cell_index] * (1 - smooth)[None, :] + lattice[index_next][:, index_next] * smooth[None, :]
    return rows_low * (1 - smooth)[:, None] + rows_high * smooth[:, None]


def tileable_fractal_noise(size, base_cells, octaves, seed):
    total = np.zeros((size, size))
    amplitude_sum = 0.0
    for octave in range(octaves):
        amplitude = 0.5 ** octave
        total += tileable_value_noise(size, base_cells * (2 ** octave), seed + octave * 17) * amplitude
        amplitude_sum += amplitude
    return total / amplitude_sum


def periodic_noise_1d(sample_positions, period, lattice_points, seed):
    random_generator = np.random.default_rng(seed)
    lattice = random_generator.random(lattice_points)
    scaled = (np.mod(sample_positions, period) / period) * lattice_points
    index_low = np.floor(scaled).astype(int) % lattice_points
    index_high = (index_low + 1) % lattice_points
    fraction = scaled - np.floor(scaled)
    smooth = fraction * fraction * (3 - 2 * fraction)
    return lattice[index_low] * (1 - smooth) + lattice[index_high] * smooth


def pencil_break_up(ink_layer, size, seed):
    """Pencil lines are never solid: modulate pressure with fine grain and a slower wobble."""
    fine_grain = tileable_value_noise(size, size // 4, seed)
    pressure = tileable_value_noise(size, 12, seed + 1)
    return np.clip(ink_layer * (0.55 + 0.45 * fine_grain) * (0.7 + 0.5 * pressure), 0, 1)


def draw_wrapped_line(draw_context, points, size, width, fill):
    for offset_x in (-size, 0, size):
        for offset_y in (-size, 0, size):
            shifted = [(point_x + offset_x, point_y + offset_y) for point_x, point_y in points]
            draw_context.line(shifted, fill=fill, width=width, joint="curve")


def wobbly_stone_outline(centre_x, centre_y, half_width, half_height, random_generator, point_count=22):
    outline_points = []
    phase = random_generator.random() * math.tau
    for point_index in range(point_count + 1):
        angle = point_index / point_count * math.tau
        cosine, sine = math.cos(angle), math.sin(angle)
        # Superellipse: a squared-off pebble, like the reference cobbles.
        exponent = 0.55
        radius_x = half_width * math.copysign(abs(cosine) ** exponent, cosine)
        radius_y = half_height * math.copysign(abs(sine) ** exponent, sine)
        wobble = 1.0 + 0.07 * math.sin(angle * 3 + phase) + random_generator.normal(0, 0.025)
        outline_points.append((centre_x + radius_x * wobble, centre_y + radius_y * wobble))
    return outline_points


def generate_cobble_detail(size=512, seed=7):
    random_generator = np.random.default_rng(seed)
    ink_image = Image.new("L", (size, size), 0)
    tone_image = Image.new("L", (size, size), 128)
    ink_draw = ImageDraw.Draw(ink_image)
    tone_draw = ImageDraw.Draw(tone_image)
    row_count = 16
    row_height = size / row_count
    for row_index in range(row_count):
        widths = random_generator.uniform(0.9, 1.9, 40) * row_height * 1.5
        cumulative = np.cumsum(widths)
        stone_count = int(np.searchsorted(cumulative, size)) + 1
        widths = widths[:stone_count] * (size / cumulative[stone_count - 1])
        row_offset = random_generator.uniform(0, size)
        left_edge = row_offset
        centre_y = (row_index + 0.5) * row_height
        for stone_width in widths:
            centre_x = left_edge + stone_width / 2
            outline = wobbly_stone_outline(centre_x, centre_y + random_generator.normal(0, 0.8),
                                           stone_width / 2 - 3.0, row_height / 2 - 2.6, random_generator)
            tone_value = int(np.clip(128 + random_generator.normal(0, 22), 70, 190))
            for offset_x in (-size, 0, size):
                for offset_y in (-size, 0, size):
                    tone_draw.polygon([(point_x + offset_x, point_y + offset_y) for point_x, point_y in outline], fill=tone_value)
            draw_wrapped_line(ink_draw, outline + outline[:1], size, 4, 255)
            # A short inner hook stroke on some stones, the "drawn by hand" tell in the reference.
            if random_generator.random() < 0.45:
                hook_start_x = centre_x - stone_width * 0.25
                hook = [(hook_start_x, centre_y + row_height * 0.18),
                        (hook_start_x + stone_width * 0.2, centre_y + row_height * 0.24)]
                draw_wrapped_line(ink_draw, hook, size, 3, 230)
            left_edge += stone_width
    ink_layer = np.asarray(ink_image.filter(ImageFilter.GaussianBlur(0.6)), dtype=np.float64) / 255.0
    ink_layer = np.clip(ink_layer * (0.85 + 0.15 * tileable_value_noise(size, size // 4, seed + 3)), 0, 1)
    tone_layer = np.asarray(tone_image, dtype=np.float64) / 255.0
    scratch_layer = generate_scratch_strokes(size, seed + 9, stroke_count=220, stroke_length=7, curl=0.4, line_width=2)
    return ink_layer, tone_layer, scratch_layer


def generate_scratch_strokes(size, seed, stroke_count, stroke_length, curl, line_width=1):
    """Short curved hair strokes in loose clumps, like the Don't Starve ground noise."""
    random_generator = np.random.default_rng(seed)
    stroke_image = Image.new("L", (size, size), 0)
    stroke_draw = ImageDraw.Draw(stroke_image)
    flow_angle_field = tileable_value_noise(size, 4, seed + 1) * math.tau
    for stroke_index in range(stroke_count):
        clump_x, clump_y = random_generator.uniform(0, size, 2)
        base_angle = flow_angle_field[int(clump_y) % size, int(clump_x) % size]
        for hair_index in range(random_generator.integers(3, 8)):
            start_x = clump_x + random_generator.normal(0, stroke_length * 0.6)
            start_y = clump_y + random_generator.normal(0, stroke_length * 0.6)
            angle = base_angle + random_generator.normal(0, 0.25)
            bend = random_generator.normal(0, curl)
            hair_points = []
            for step_index in range(6):
                progress = step_index / 5
                current_angle = angle + bend * progress
                hair_points.append((start_x + math.cos(current_angle) * stroke_length * progress * 2,
                                    start_y + math.sin(current_angle) * stroke_length * progress * 2))
            draw_wrapped_line(stroke_draw, hair_points, size, line_width, int(random_generator.uniform(190, 255)))
    stroke_layer = np.asarray(stroke_image, dtype=np.float64) / 255.0
    return pencil_break_up(stroke_layer, size, seed + 5)


def generate_grass_detail(size=512, seed=21):
    ink_layer = generate_scratch_strokes(size, seed, stroke_count=320, stroke_length=8, curl=0.9, line_width=3)
    tone_layer = 0.5 + (tileable_fractal_noise(size, 6, 3, seed + 2) - 0.5) * 0.35
    scratch_layer = generate_scratch_strokes(size, seed + 40, stroke_count=300, stroke_length=6, curl=0.3, line_width=2)
    return ink_layer, tone_layer, scratch_layer


def generate_dirt_detail(size=512, seed=33):
    random_generator = np.random.default_rng(seed)
    pebble_image = Image.new("L", (size, size), 0)
    pebble_draw = ImageDraw.Draw(pebble_image)
    for pebble_index in range(110):
        centre_x, centre_y = random_generator.uniform(0, size, 2)
        radius = random_generator.uniform(4.0, 10.0)
        outline = wobbly_stone_outline(centre_x, centre_y, radius * 1.3, radius, random_generator, point_count=12)
        draw_wrapped_line(pebble_draw, outline + outline[:1], size, 3, 240)
    pebble_layer = np.asarray(pebble_image.filter(ImageFilter.GaussianBlur(0.4)), dtype=np.float64) / 255.0
    stipple = (tileable_value_noise(size, size // 3, seed + 4) > 0.9).astype(np.float64) * 0.9
    ink_layer = np.clip(pebble_layer + stipple, 0, 1)
    tone_layer = 0.5 + (tileable_fractal_noise(size, 5, 3, seed + 8) - 0.5) * 0.3
    scratch_layer = generate_scratch_strokes(size, seed + 50, stroke_count=160, stroke_length=7, curl=0.2, line_width=2)
    return ink_layer, tone_layer, scratch_layer


def generate_hatch(size=128, line_spacing=4, seed=55):
    """'/' diagonal pencil hatching. x + y = constant is a '/' line with y pointing down the image."""
    pixel_y, pixel_x = np.mgrid[0:size, 0:size].astype(np.float64)
    lines_per_tile = size // line_spacing
    across = pixel_x + pixel_y
    along = pixel_x - pixel_y
    line_index = np.floor(across / line_spacing).astype(int) % lines_per_tile
    wobble = (periodic_noise_1d(along + line_index * 97.0, size, 16, seed) - 0.5) * 1.6
    distance_to_line = np.abs(np.mod(across + wobble, line_spacing) - line_spacing / 2)
    stroke_core = np.clip(1.4 - distance_to_line / 0.8, 0, 1)
    # Strokes start and stop: each line gets its own gap pattern and pressure along its length.
    gap_noise = periodic_noise_1d(along + line_index * 53.0, size, 10, seed + 1)
    pressure = 0.9 + 0.1 * periodic_noise_1d(along + line_index * 31.0, size, 24, seed + 2)
    stroke_visible = np.clip((gap_noise - 0.1) * 8.0, 0, 1)
    hatch_layer = np.clip(stroke_core * stroke_visible * pressure, 0, 1)
    paper_layer = 0.5 + (tileable_fractal_noise(size, 16, 3, seed + 4) - 0.5) * 0.6
    return hatch_layer, paper_layer


# ---- Transition atlas (marching squares, Don't Starve style) ---------------------------------
# Case bits: 1 = top-left, 2 = top-right, 4 = bottom-right, 8 = bottom-left corner is inside the layer.
# Atlas: 8 columns x 4 rows of 128 px. Variant 0 = rows 0-1, variant 1 = rows 2-3; case c sits at
# column c % 8, row (c // 8) + 2 * variant. The shader must use the same mapping.
ATLAS_CELL_PIXELS = 128
ATLAS_COLUMNS = 8
ATLAS_ROWS = 4


def atlas_cell_origin(case_index, variant):
    return (case_index % ATLAS_COLUMNS) * ATLAS_CELL_PIXELS, (case_index // ATLAS_COLUMNS + 2 * variant) * ATLAS_CELL_PIXELS


def generate_transition_cell(case_index, variant, seed):
    """Don't Starve style edge: the turf ends in hairy brush flicks poking outward, with clusters of curved ink
    strokes along the edge and no continuous outline or highlight band (DontStarveTiles.png)."""
    cell = ATLAS_CELL_PIXELS
    random_generator = np.random.default_rng(seed)
    corner_top_left = 1.0 if case_index & 1 else 0.0
    corner_top_right = 1.0 if case_index & 2 else 0.0
    corner_bottom_right = 1.0 if case_index & 4 else 0.0
    corner_bottom_left = 1.0 if case_index & 8 else 0.0
    pixel_v, pixel_u = (np.mgrid[0:cell, 0:cell].astype(np.float64) + 0.5) / cell
    field = (corner_top_left * (1 - pixel_u) * (1 - pixel_v) + corner_top_right * pixel_u * (1 - pixel_v)
             + corner_bottom_right * pixel_u * pixel_v + corner_bottom_left * (1 - pixel_u) * pixel_v)
    # Organic wobble that fades to zero on the cell border, so every case meets its neighbours at the
    # same edge midpoints whatever variant either side picked.
    distance_to_border = np.minimum(np.minimum(pixel_u, 1 - pixel_u), np.minimum(pixel_v, 1 - pixel_v))
    border_fade = np.clip((distance_to_border - 0.04) / 0.18, 0, 1)
    border_fade = border_fade * border_fade * (3 - 2 * border_fade)
    wobble = (tileable_fractal_noise(cell, 2, 3, seed) - 0.5) * 0.45 * border_fade
    field = field + wobble
    gradient_v, gradient_u = np.gradient(field)
    gradient_length = np.sqrt(gradient_u ** 2 + gradient_v ** 2) + 1e-6
    signed_pixels = (field - 0.5) / gradient_length            # > 0 inside, in pixels
    coverage = np.clip(signed_pixels / 1.2 + 0.5, 0, 1)
    rim = np.zeros((cell, cell))
    ink_line = np.zeros((cell, cell))
    if case_index == 0 or case_index == 15:
        coverage[:] = 0.0 if case_index == 0 else 1.0
        return coverage, rim, ink_line

    flick_image = Image.new("L", (cell, cell), 0)
    stroke_image = Image.new("L", (cell, cell), 0)
    flick_draw = ImageDraw.Draw(flick_image)
    stroke_draw = ImageDraw.Draw(stroke_image)
    # Strokes only away from the cell border: there the edge is the shared field alone, so neighbours always join.
    edge_rows, edge_columns = np.nonzero((np.abs(signed_pixels) < 0.8) & (border_fade > 0.2))
    if len(edge_rows) > 0:
        chosen = random_generator.choice(len(edge_rows), size=min(len(edge_rows), 45), replace=False)
        for chosen_index in chosen:
            row, column = edge_rows[chosen_index], edge_columns[chosen_index]
            outward_u = -gradient_u[row, column] / gradient_length[row, column]
            outward_v = -gradient_v[row, column] / gradient_length[row, column]
            tangent_u, tangent_v = -outward_v, outward_u
            lean = random_generator.normal(0, 0.45)
            flick_length = random_generator.uniform(5, 14)
            start = (column - outward_u * 3, row - outward_v * 3)
            middle = (column + (outward_u + tangent_u * lean * 0.5) * flick_length * 0.5,
                      row + (outward_v + tangent_v * lean * 0.5) * flick_length * 0.5)
            tip = (column + (outward_u + tangent_u * lean) * flick_length,
                   row + (outward_v + tangent_v * lean) * flick_length)
            # The flick is part of the turf: it adds coverage outward.
            flick_draw.line([start, middle, tip], fill=255, width=3, joint="curve")
            # Most flicks get an ink stroke drawn down their middle and curving back along the edge.
            if random_generator.random() < 0.35:
                back = (column - tangent_u * lean * 6 - outward_u * 4, row - tangent_v * lean * 6 - outward_v * 4)
                stroke_draw.line([tip, middle, back], fill=int(random_generator.uniform(200, 255)), width=1, joint="curve")
        # Clusters of curved strokes just inside the edge, following it.
        inner_rows, inner_columns = np.nonzero((signed_pixels > 2.0) & (signed_pixels < 7.0) & (border_fade > 0.2))
        if len(inner_rows) > 0:
            for chosen_index in random_generator.choice(len(inner_rows), size=min(len(inner_rows), 12), replace=False):
                row, column = inner_rows[chosen_index], inner_columns[chosen_index]
                tangent_u = gradient_v[row, column] / gradient_length[row, column]
                tangent_v = -gradient_u[row, column] / gradient_length[row, column]
                arc_length = random_generator.uniform(5, 11)
                bow = random_generator.normal(0, 1.5)
                stroke_draw.line([(column - tangent_u * arc_length, row - tangent_v * arc_length),
                                  (column + bow * tangent_v, row - bow * tangent_u),
                                  (column + tangent_u * arc_length, row + tangent_v * arc_length)],
                                 fill=int(random_generator.uniform(170, 240)), width=1, joint="curve")
    flick_layer = np.asarray(flick_image.filter(ImageFilter.GaussianBlur(0.5)), dtype=np.float64) / 255.0
    stroke_layer = np.asarray(stroke_image.filter(ImageFilter.GaussianBlur(0.4)), dtype=np.float64) / 255.0
    coverage = np.maximum(coverage, flick_layer)
    # A broken pencil trace on the edge itself, never a solid outline.
    edge_trace = np.clip(1.0 - np.abs(signed_pixels) / 1.2, 0, 1) * (tileable_value_noise(cell, 12, 4343) > 0.45)
    ink_line = np.clip(stroke_layer * 1.2 + edge_trace * 0.7, 0, 1)
    return coverage, rim, ink_line


def generate_transition_atlas():
    width = ATLAS_COLUMNS * ATLAS_CELL_PIXELS
    height = ATLAS_ROWS * ATLAS_CELL_PIXELS
    atlas = np.zeros((height, width, 3))
    for variant in range(2):
        for case_index in range(16):
            coverage, rim, ink_line = generate_transition_cell(case_index, variant, seed=1000 + case_index * 13 + variant * 7)
            origin_x, origin_y = atlas_cell_origin(case_index, variant)
            atlas[origin_y:origin_y + ATLAS_CELL_PIXELS, origin_x:origin_x + ATLAS_CELL_PIXELS] = np.stack([coverage, rim, ink_line], axis=-1)
    return atlas


def to_rgb_image(red_channel, green_channel, blue_channel):
    stacked = np.stack([red_channel, green_channel, blue_channel], axis=-1)
    return Image.fromarray((np.clip(stacked, 0, 1) * 255).astype(np.uint8), "RGB")


def preview_surface(ink_layer, tone_layer, scratch_layer, base_colour, ink_colour):
    tone = 0.85 + (tone_layer - 0.5) * 0.35
    colour = np.array(base_colour)[None, None, :] * tone[..., None]
    colour = colour * (1 - scratch_layer[..., None] * 0.25)
    ink_amount = ink_layer[..., None]
    return colour * (1 - ink_amount) + colour * np.array(ink_colour)[None, None, :] * ink_amount


def main():
    os.makedirs(OUTPUT_DIRECTORY, exist_ok=True)
    preview_directory = sys.argv[1] if len(sys.argv) > 1 else None
    ink_colour = (0.32, 0.24, 0.20)
    surfaces = {
        "Cobble": (generate_cobble_detail(), (0.56, 0.52, 0.60)),
        "Grass": (generate_grass_detail(), (0.50, 0.46, 0.24)),
        "Dirt": (generate_dirt_detail(), (0.62, 0.52, 0.36)),
    }
    previews = []
    for surface_name, ((ink_layer, tone_layer, scratch_layer), base_colour) in surfaces.items():
        to_rgb_image(ink_layer, tone_layer, scratch_layer).save(os.path.join(OUTPUT_DIRECTORY, f"T_InkDetail_{surface_name}.png"))
        previews.append(preview_surface(ink_layer, tone_layer, scratch_layer, base_colour, ink_colour))
    hatch_layer, paper_layer = generate_hatch()
    to_rgb_image(hatch_layer, paper_layer, np.zeros_like(hatch_layer)).save(os.path.join(OUTPUT_DIRECTORY, "T_InkHatch.png"))
    atlas = generate_transition_atlas()
    Image.fromarray((atlas * 255).astype(np.uint8), "RGB").save(os.path.join(OUTPUT_DIRECTORY, "T_TransitionAtlas.png"))

    if preview_directory:
        os.makedirs(preview_directory, exist_ok=True)
        surface_strip = np.concatenate(previews, axis=1)
        Image.fromarray((np.clip(surface_strip, 0, 1) * 255).astype(np.uint8)).save(os.path.join(preview_directory, "preview_surfaces.png"))
        # Shadow preview: half of the cobble tile in a flat one-tone shadow with hatching on top.
        cobble_preview = previews[0].copy()
        hatch_tiled = np.tile(hatch_layer, (4, 4))
        shadow_mask = np.zeros(cobble_preview.shape[:2]); shadow_mask[:, 256:] = 1
        shadowed = cobble_preview * 0.72 * np.array((0.85, 0.85, 1.0))[None, None, :]
        shadowed = shadowed * (1 - hatch_tiled[..., None] * 0.55)
        shadow_preview = cobble_preview * (1 - shadow_mask[..., None]) + shadowed * shadow_mask[..., None]
        Image.fromarray((np.clip(shadow_preview, 0, 1) * 255).astype(np.uint8)).save(os.path.join(preview_directory, "preview_hatch_shadow.png"))
        atlas_preview = np.where(atlas[..., :1] > 0.5, np.array((0.50, 0.46, 0.24)), np.array((0.62, 0.52, 0.36)))
        atlas_preview = atlas_preview * (1 + atlas[..., 1:2] * 0.25)
        atlas_preview = atlas_preview * (1 - atlas[..., 2:3]) + atlas_preview * np.array(ink_colour) * atlas[..., 2:3]
        Image.fromarray((np.clip(atlas_preview, 0, 1) * 255).astype(np.uint8)).save(os.path.join(preview_directory, "preview_atlas.png"))


if __name__ == "__main__":
    main()
