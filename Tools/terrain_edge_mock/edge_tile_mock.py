"""Offline mock-up of the Terrain Edge Tiles spec (Assets/_Vault/Tasks/Plans/TerrainEdgeTiles_System.md).

Generates example 5-piece edge sets that follow Docs/Terrain/TerrainEdgeTileGuide.md, runs them through the
packer split + validation the spec describes, and renders:
  - a contact sheet per set (5 pieces x variants, plus the auto-rotated 16-case sheet),
  - a top-down biome map composited exactly as the shader would (corner grid -> case -> rotated piece -> layers),
  - a 2.5D cliff scene: top edge -> top bevel -> rock face -> foot bevel -> foot edge -> lower biome.

Usage: python Tools/terrain_edge_mock/edge_tile_mock.py <capture_dir> <example_set_dir>
Only numpy + Pillow. Nothing here is read by Unity.
"""
import math
import os
import sys
import zlib
from collections import deque

import numpy as np
from PIL import Image, ImageDraw, ImageFont

CELL = 256
SUPERSAMPLE = 2
CANVAS = CELL * SUPERSAMPLE
CROSSING_STRAIGHT_PIXELS = 16
SAFE_MARGIN = 8
PIXELS_PER_METRE = 64
INK = (16, 12, 10)

DIRT, GRASS, FOREST, STONE = 0, 1, 2, 3
BIOME_NAMES = {DIRT: "Dirt", GRASS: "Grass", FOREST: "Forest", STONE: "Stone"}
DRAW_ORDER = {DIRT: 0, GRASS: 1, FOREST: 2, STONE: 3}

PIECES = ["outer", "edge", "inner", "diagonal", "full"]
CANONICAL_CASE = {"outer": 1, "edge": 3, "inner": 7, "diagonal": 5, "full": 15}


# ---------------------------------------------------------------------------------------------------------
# Case table: rotating a piece 90 degrees clockwise moves top-left (1) to top-right (2), i.e. bits rotate left.
# ---------------------------------------------------------------------------------------------------------
def rotate_case_clockwise(case_index, quarter_turns):
    rotated = case_index
    for _ in range(quarter_turns):
        rotated = ((rotated << 1) | (rotated >> 3)) & 15
    return rotated


def build_case_table():
    table = {}
    for piece_name in PIECES:
        for quarter_turns in range(4):
            case_index = rotate_case_clockwise(CANONICAL_CASE[piece_name], quarter_turns)
            if case_index not in table:
                table[case_index] = (piece_name, quarter_turns)
    return table


CASE_TABLE = build_case_table()
assert sorted(CASE_TABLE.keys()) == list(range(1, 16)), CASE_TABLE


# ---------------------------------------------------------------------------------------------------------
# Canonical piece geometry (256 px cell, y down = south). Every boundary meets a side at its midpoint, square on.
# ---------------------------------------------------------------------------------------------------------
def boundary_segments(piece_name):
    """List of segments: (kind, centre, radius, inside_is_within_circle) for arcs, ('line',) for the edge."""
    if piece_name == "outer":
        return [("arc", (0.0, 0.0), 128.0, True)]
    if piece_name == "edge":
        return [("line",)]
    if piece_name == "inner":
        return [("arc", (0.0, 256.0), 128.0, False)]
    if piece_name == "diagonal":
        return [("arc", (0.0, 0.0), 128.0, True), ("arc", (256.0, 256.0), 128.0, True)]
    return []


def segment_field(segment, pixel_x, pixel_y):
    """Signed distance (negative inside), arc-length parameter, distance of the nearest boundary point to a side."""
    if segment[0] == "line":
        signed_distance = pixel_y - 128.0
        arc_parameter = pixel_x
        side_distance = np.minimum(pixel_x, 256.0 - pixel_x)
        return signed_distance, arc_parameter, side_distance
    _, (centre_x, centre_y), radius, inside_within = segment
    offset_x = pixel_x - centre_x
    offset_y = pixel_y - centre_y
    distance = np.hypot(offset_x, offset_y)
    signed_distance = distance - radius if inside_within else radius - distance
    angle = np.arctan2(np.abs(offset_y), np.abs(offset_x))
    arc_parameter = radius * angle
    side_distance = np.minimum(radius * np.sin(angle), radius * np.cos(angle))
    return signed_distance, arc_parameter, side_distance


def segment_samples(segment, step):
    """Boundary points along a segment: (arc_parameter, x, y, outward_normal_x, outward_normal_y, side_distance)."""
    samples = []
    if segment[0] == "line":
        position = 0.0
        while position <= 256.0:
            samples.append((position, position, 128.0, 0.0, 1.0, min(position, 256.0 - position)))
            position += step
        return samples
    _, (centre_x, centre_y), radius, inside_within = segment
    sign_x = 1.0 if centre_x < 128 else -1.0
    sign_y = 1.0 if centre_y < 128 else -1.0
    arc_length = radius * math.pi / 2
    position = 0.0
    while position <= arc_length:
        angle = position / radius
        point_x = centre_x + sign_x * radius * math.cos(angle)
        point_y = centre_y + sign_y * radius * math.sin(angle)
        normal_x = sign_x * math.cos(angle)
        normal_y = sign_y * math.sin(angle)
        if not inside_within:
            normal_x, normal_y = -normal_x, -normal_y
        side_distance = min(radius * math.sin(angle), radius * math.cos(angle))
        samples.append((position, point_x, point_y, normal_x, normal_y, side_distance))
        position += step
    return samples


def crossing_window(side_distance):
    """0 within the straight 16 px next to a side, 1 from 32 px in: the edge may only wobble where this is > 0."""
    normalised = np.clip((np.asarray(side_distance) - CROSSING_STRAIGHT_PIXELS) / 16.0, 0.0, 1.0)
    return normalised * normalised * (3.0 - 2.0 * normalised)


class EdgeProfile:
    """Outward wobble of the silhouette along a segment: clumps (turf) or chips (stone)."""

    def __init__(self, random_generator, style):
        self.style = style
        self.bumps = []
        position = -20.0
        while position < 460.0:
            width = random_generator.uniform(*style["clump_width"])
            height = random_generator.uniform(*style["clump_height"])
            self.bumps.append((position, width, height))
            position += random_generator.uniform(*style["clump_spacing"])
        self.phases = random_generator.uniform(0, 2 * math.pi, size=3)

    def evaluate(self, arc_parameter):
        arc_parameter = np.asarray(arc_parameter, dtype=np.float64)
        total = np.full(arc_parameter.shape, -self.style["clump_baseline"])
        for centre, width, height in self.bumps:
            if self.style["chipped"]:
                total = total + height * np.clip(1.0 - np.abs(arc_parameter - centre) / width, 0.0, 1.0)
            else:
                total = total + height * np.exp(-((arc_parameter - centre) / width) ** 2)
        total = total + self.style["ripple"] * np.sin(arc_parameter / 5.3 + self.phases[0])
        total = total + self.style.get("bend", 0.0) * np.sin(arc_parameter / 38.0 + self.phases[1])
        return total


# ---------------------------------------------------------------------------------------------------------
# Styles. Colours sit in the NatureAtlas range (flat 2-3 tone fills, warm dark ink).
# ---------------------------------------------------------------------------------------------------------
FILL_COLOURS = {
    DIRT: [(146, 119, 88), (154, 126, 93), (136, 110, 81)],
    GRASS: [(107, 125, 92), (114, 132, 97), (100, 117, 86)],
    FOREST: [(66, 82, 57), (72, 89, 62), (60, 75, 52)],
    STONE: [(139, 135, 123), (150, 146, 133), (126, 122, 111)],
}

TURF_STYLE = dict(clump_width=(14.0, 22.0), clump_height=(60.0, 80.0), clump_spacing=(60.0, 84.0),
                  clump_baseline=30.0, ripple=1.0, bend=6.0, chipped=False)
FOREST_STYLE = dict(clump_width=(14.0, 22.0), clump_height=(60.0, 80.0), clump_spacing=(60.0, 84.0),
                    clump_baseline=30.0, ripple=1.0, bend=6.0, chipped=False)
STONE_STYLE = dict(clump_width=(30.0, 46.0), clump_height=(56.0, 76.0), clump_spacing=(60.0, 84.0),
                   clump_baseline=28.0, ripple=0.4, bend=6.0, chipped=True)
LIP_STYLE = dict(clump_width=(6.0, 13.0), clump_height=(5.0, 15.0), clump_spacing=(10.0, 22.0),
                 clump_baseline=6.0, ripple=1.0, bend=4.0, chipped=False)

EDGE_SETS = {
    "GrassOverDirt": dict(upper=GRASS, style=TURF_STYLE, strokes="turf", offset=0.0),
    "ForestOverGrass": dict(upper=FOREST, style=FOREST_STYLE, strokes="forest", offset=0.0),
    "StoneOverDirt": dict(upper=STONE, style=STONE_STYLE, strokes="stone", offset=0.0),
    "StoneOverGrass": dict(upper=STONE, style=STONE_STYLE, strokes="stone_mossy", offset=0.0),
}
DEFAULT_SET_FOR_BIOME = {GRASS: "GrassOverDirt", FOREST: "ForestOverGrass", STONE: "StoneOverDirt"}
PAIR_OVERRIDES = {(STONE, GRASS): "StoneOverGrass"}


def shade(colour, factor):
    return tuple(int(max(0, min(255, channel * factor))) for channel in colour)


# ---------------------------------------------------------------------------------------------------------
# Strokes: drawn as chains of discs on the supersampled canvas, so they taper and anti-alias on downsample.
# ---------------------------------------------------------------------------------------------------------
def quadratic_points(start, control, end, count):
    points = []
    for index in range(count):
        fraction = index / (count - 1)
        inverse = 1.0 - fraction
        points.append((inverse * inverse * start[0] + 2 * inverse * fraction * control[0] + fraction * fraction * end[0],
                       inverse * inverse * start[1] + 2 * inverse * fraction * control[1] + fraction * fraction * end[1]))
    return points


def inside_safe_margin(points):
    return all(SAFE_MARGIN <= point_x <= CELL - SAFE_MARGIN and SAFE_MARGIN <= point_y <= CELL - SAFE_MARGIN
               for point_x, point_y in points)


def draw_tapered(draw, points, start_width, end_width, colour):
    for index, (point_x, point_y) in enumerate(points):
        fraction = index / max(1, len(points) - 1)
        radius = 0.5 * (start_width + (end_width - start_width) * fraction) * SUPERSAMPLE
        centre_x = point_x * SUPERSAMPLE
        centre_y = point_y * SUPERSAMPLE
        draw.ellipse([centre_x - radius, centre_y - radius, centre_x + radius, centre_y + radius], fill=colour)


def draw_piece_strokes(stroke_layer, piece_name, profiles, set_definition, random_generator):
    draw = ImageDraw.Draw(stroke_layer)
    stroke_kind = set_definition["strokes"]
    if stroke_kind == "none":
        return
    upper_colours = FILL_COLOURS[set_definition["upper"]]
    offset = set_definition["offset"]
    for segment_index, segment in enumerate(boundary_segments(piece_name)):
        profile = profiles[segment_index]
        samples = segment_samples(segment, 1.0)
        boundary_points_parameter = [sample[0] for sample in samples]
        boundary_points = []
        for arc_parameter, point_x, point_y, normal_x, normal_y, side_distance in samples:
            push = float(crossing_window(side_distance)) * float(profile.evaluate(arc_parameter)) + offset
            boundary_points.append((point_x + normal_x * push, point_y + normal_y * push, normal_x, normal_y,
                                    side_distance))

        if stroke_kind in ("stone", "stone_mossy"):
            shadow_band = [(point[0] + point[2] * 4.0, point[1] + point[3] * 4.0) for point in boundary_points
                           if float(crossing_window(point[4])) > 0.3]
            if shadow_band:
                shadow_layer = Image.new("RGBA", stroke_layer.size, (0, 0, 0, 0))
                draw_tapered(ImageDraw.Draw(shadow_layer), shadow_band, 6.0, 6.0, INK + (80,))
                stroke_layer.alpha_composite(shadow_layer)
            outline = [(point[0], point[1]) for point in boundary_points if point[4] > 12.0]
            draw_tapered(draw, outline, 7.0, 7.0, INK)
            highlight = [(point[0] - point[2] * 4.5, point[1] - point[3] * 4.5) for point in boundary_points
                         if float(crossing_window(point[4])) > 0.6]
            draw_tapered(draw, highlight, 2.4, 2.4, shade(upper_colours[1], 1.12)) if highlight else None

        if stroke_kind in ("turf", "forest"):
            run_start = None
            run_length = 0
            for point_index, boundary_point in enumerate(boundary_points):
                inked = float(crossing_window(boundary_point[4])) > 0.15 and boundary_point[4] > 10.0
                if inked and run_start is None:
                    run_start = point_index
                    run_length = int(random_generator.uniform(40, 110))
                if run_start is not None and (not inked or point_index - run_start >= run_length):
                    run_points = [(point[0], point[1]) for point in boundary_points[run_start:point_index]]
                    if len(run_points) > 2 and inside_safe_margin(run_points):
                        draw_tapered(draw, run_points, 6.5 if stroke_kind == "forest" else 5.0, 3.2 if stroke_kind == "forest" else 2.6, INK)
                    run_start = None if not inked else point_index + int(random_generator.uniform(5, 12))
                    if run_start is not None and run_start >= len(boundary_points):
                        run_start = None
                    if run_start is not None:
                        run_length = int(random_generator.uniform(40, 110))
            if run_start is not None:
                run_points = [(point[0], point[1]) for point in boundary_points[run_start:]]
                if len(run_points) > 2 and inside_safe_margin(run_points):
                    draw_tapered(draw, run_points, 6.5 if stroke_kind == "forest" else 5.0, 3.2 if stroke_kind == "forest" else 2.6, INK)

        position = random_generator.uniform(4, 12)
        while position < len(boundary_points):
            point_x, point_y, normal_x, normal_y, side_distance = boundary_points[int(position)]
            tangent_x, tangent_y = -normal_y, normal_x
            if float(crossing_window(side_distance)) < 0.5:
                position += 3
                continue
            if stroke_kind in ("turf", "forest"):
                lobe_height = float(profile.evaluate(boundary_points_parameter[int(position)]))
                if lobe_height < 6.0 and random_generator.random() < (0.5 if stroke_kind == "forest" else 0.8):
                    position += random_generator.uniform(3, 6)
                    continue
                root_depth = random_generator.uniform(3, 9)
                length = random_generator.uniform(14, 30)
                curl = (1 if math.sin(boundary_points_parameter[int(position)] / 23.0) > 0 else -1) * random_generator.uniform(4, 13)
                root = (point_x - normal_x * root_depth, point_y - normal_y * root_depth)
                tip = (point_x + normal_x * length + tangent_x * curl, point_y + normal_y * length + tangent_y * curl)
                control = ((root[0] + tip[0]) / 2 + tangent_x * curl * 0.6, (root[1] + tip[1]) / 2 + tangent_y * curl * 0.6)
                points = quadratic_points(root, control, tip, 14)
                if inside_safe_margin(points):
                    draw_tapered(draw, points, random_generator.uniform(5.0, 7.0) if stroke_kind == "turf" else random_generator.uniform(6.0, 8.0), 0.8, INK)
                if random_generator.random() < 0.55:
                    hair_root = (point_x - normal_x * random_generator.uniform(6, 15),
                                 point_y - normal_y * random_generator.uniform(6, 15))
                    direction = 1 if random_generator.random() < 0.5 else -1
                    hair_length = random_generator.uniform(9, 20)
                    hair_tip = (hair_root[0] + tangent_x * hair_length * direction - normal_x * 3,
                                hair_root[1] + tangent_y * hair_length * direction - normal_y * 3)
                    hair_control = ((hair_root[0] + hair_tip[0]) / 2 + normal_x * 3, (hair_root[1] + hair_tip[1]) / 2 + normal_y * 3)
                    hair_points = quadratic_points(hair_root, hair_control, hair_tip, 12)
                    if inside_safe_margin(hair_points):
                        draw_tapered(draw, hair_points, 1.9, 0.5, shade(INK, 1.15))
                if stroke_kind == "forest" and random_generator.random() < 0.45:
                    tuft_root = (point_x - normal_x * 3, point_y - normal_y * 3)
                    tuft_tip = (point_x + normal_x * random_generator.uniform(4, 9) - tangent_x * 4,
                                point_y + normal_y * random_generator.uniform(4, 9) - tangent_y * 4)
                    tuft_points = quadratic_points(tuft_root, tuft_root, tuft_tip, 8)
                    if inside_safe_margin(tuft_points):
                        draw_tapered(draw, tuft_points, 4.4, 1.2, shade(upper_colours[1], 1.25))
                position += random_generator.uniform(3, 6) if stroke_kind == "turf" else random_generator.uniform(3, 7)
            else:
                chip_length = random_generator.uniform(10, 22)
                chip_tip = (point_x - normal_x * chip_length + tangent_x * random_generator.uniform(-4, 4),
                            point_y - normal_y * chip_length + tangent_y * random_generator.uniform(-4, 4))
                chip_points = quadratic_points((point_x, point_y), (point_x, point_y), chip_tip, 8)
                if inside_safe_margin(chip_points):
                    draw_tapered(draw, chip_points, 6.0, 0.8, INK)
                if stroke_kind == "stone_mossy" and random_generator.random() < 0.6:
                    moss_root = (point_x + normal_x * random_generator.uniform(4, 9), point_y + normal_y * random_generator.uniform(4, 9))
                    moss_tip = (point_x - normal_x * random_generator.uniform(2, 7) + tangent_x * random_generator.uniform(-6, 6),
                                point_y - normal_y * random_generator.uniform(2, 7) + tangent_y * random_generator.uniform(-6, 6))
                    moss_points = quadratic_points(moss_root, ((moss_root[0] + moss_tip[0]) / 2 + tangent_x * 3,
                                                               (moss_root[1] + moss_tip[1]) / 2 + tangent_y * 3), moss_tip, 10)
                    if inside_safe_margin(moss_points):
                        draw_tapered(draw, moss_points, 3.2, 0.8, FILL_COLOURS[GRASS][2])
                position += random_generator.uniform(3, 7)


# ---------------------------------------------------------------------------------------------------------
# One piece = what an artist exports: RGBA, flat fill colour inside the silhouette, ink on top.
# ---------------------------------------------------------------------------------------------------------
def make_artist_piece(piece_name, set_definition, seed):
    random_generator = np.random.default_rng(seed)
    fill_key = FILL_COLOURS[set_definition["upper"]][0]
    if piece_name == "full":
        return Image.new("RGBA", (CELL, CELL), fill_key + (255,))
    pixel_y, pixel_x = (np.mgrid[0:CANVAS, 0:CANVAS] + 0.5) / SUPERSAMPLE
    segments = boundary_segments(piece_name)
    profiles = [EdgeProfile(random_generator, set_definition["style"]) for _ in segments]
    coverage = np.zeros((CANVAS, CANVAS))
    for segment, profile in zip(segments, profiles):
        signed_distance, arc_parameter, side_distance = segment_field(segment, pixel_x, pixel_y)
        push = crossing_window(side_distance) * profile.evaluate(arc_parameter) + set_definition["offset"]
        coverage = np.maximum(coverage, np.clip((push - signed_distance) * SUPERSAMPLE + 0.5, 0.0, 1.0))
    base = np.zeros((CANVAS, CANVAS, 4))
    base[..., :3] = fill_key
    base[..., 3] = coverage * 255
    canvas = Image.fromarray(base.astype(np.uint8), "RGBA")
    stroke_layer = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    draw_piece_strokes(stroke_layer, piece_name, profiles, set_definition, random_generator)
    canvas.alpha_composite(stroke_layer)
    return canvas.convert("RGBa").resize((CELL, CELL), Image.LANCZOS).convert("RGBA")


# ---------------------------------------------------------------------------------------------------------
# Packer split (spec ET-D8): coverage = alpha of fill-coloured pixels (holes under ink closed),
# detail = pixels whose colour differs from the fill key.
# ---------------------------------------------------------------------------------------------------------
def pack_piece(artist_image, fill_key, tolerance=0.12):
    pixels = np.asarray(artist_image).astype(np.float64) / 255.0
    alpha = pixels[..., 3]
    colour_difference = np.linalg.norm(pixels[..., :3] - np.array(fill_key) / 255.0, axis=-1) / math.sqrt(3)
    detail_alpha = alpha * np.clip((colour_difference - tolerance) / tolerance, 0.0, 1.0)
    coverage = alpha * (1.0 - detail_alpha)
    closed = coverage.copy()
    for shift_y in range(-3, 4):
        for shift_x in range(-3, 4):
            closed = np.maximum(closed, np.roll(np.roll(coverage, shift_y, 0), shift_x, 1))
    neighbour_count = np.zeros_like(coverage)
    for shift_y, shift_x in ((-3, 0), (3, 0), (0, -3), (0, 3)):
        neighbour_count += np.roll(np.roll(coverage, shift_y, 0), shift_x, 1) > 0.5
    under_ink = (detail_alpha > 0.05) & (neighbour_count >= 3)
    coverage = np.where(under_ink, np.maximum(coverage, closed), coverage)
    detail_rgb = pixels[..., :3]
    return coverage, detail_rgb, detail_alpha


def validate_piece(piece_name, coverage):
    """The guide's section-3 rules: crossings at 128 px, solid sides solid. Returns a list of problems."""
    problems = []
    canonical_case = CANONICAL_CASE[piece_name]
    corner_inside = {bit: bool(canonical_case & bit) for bit in (1, 2, 4, 8)}
    sides = {
        "top": (coverage[1, :], corner_inside[1], corner_inside[2]),
        "right": (coverage[:, CELL - 2], corner_inside[2], corner_inside[4]),
        "bottom": (coverage[CELL - 2, :], corner_inside[8], corner_inside[4]),
        "left": (coverage[:, 1], corner_inside[1], corner_inside[8]),
    }
    for side_name, (line, first_inside, second_inside) in sides.items():
        if first_inside == second_inside:
            expected = 1.0 if first_inside else 0.0
            worst = float(np.max(np.abs(line[4:-4] - expected)))
            if worst > 0.2:
                problems.append("%s side should be solid %s (worst %.2f)" % (side_name, "inside" if first_inside else "outside", worst))
        else:
            inside_mask = line > 0.5
            transitions = np.nonzero(np.diff(inside_mask.astype(int)))[0]
            if len(transitions) != 1 or abs(transitions[0] + 0.5 - 128) > 3:
                problems.append("%s side crossing at %s, expected 128" % (side_name, list(transitions)))
    return problems


class PackedSet:
    def __init__(self, name, definition, variant_count=2):
        self.name = name
        self.definition = definition
        self.fill_key = FILL_COLOURS[definition["upper"]][0]
        self.artist_pieces = {}
        self.packed = {}
        for variant in range(variant_count):
            for piece_index, piece_name in enumerate(PIECES):
                seed = zlib.crc32(("%s|%d|%s" % (name, variant, piece_name)).encode())
                artist = make_artist_piece(piece_name, definition, seed)
                self.artist_pieces[(variant, piece_name)] = artist
                self.packed[(variant, piece_name)] = pack_piece(artist, self.fill_key)
        self.variant_count = variant_count
        self.resized_cache = {}

    def validation_report(self):
        lines = []
        for (variant, piece_name), (coverage, _, _) in self.packed.items():
            if piece_name == "full":
                continue
            for problem in validate_piece(piece_name, coverage):
                lines.append("%s v%d %s: %s" % (self.name, variant, piece_name, problem))
        return lines

    def case_layers(self, case_index, variant, cell_pixels):
        """Coverage, detail rgb, detail alpha for a case at a cell size, rotated from its piece."""
        key = (case_index, variant, cell_pixels)
        if key in self.resized_cache:
            return self.resized_cache[key]
        piece_name, quarter_turns = CASE_TABLE[case_index]
        coverage, detail_rgb, detail_alpha = self.packed[(variant, piece_name)]
        layers = []
        for array in (coverage, detail_rgb, detail_alpha):
            rotated = np.rot90(array, k=-quarter_turns, axes=(0, 1))
            if cell_pixels != CELL:
                if rotated.ndim == 2:
                    resized = np.asarray(Image.fromarray((rotated * 255).astype(np.uint8)).resize((cell_pixels, cell_pixels), Image.LANCZOS)) / 255.0
                else:
                    resized = np.asarray(Image.fromarray((rotated * 255).astype(np.uint8), "RGB").resize((cell_pixels, cell_pixels), Image.LANCZOS)) / 255.0
                rotated = resized
            layers.append(np.ascontiguousarray(rotated))
        self.resized_cache[key] = tuple(layers)
        return self.resized_cache[key]


# ---------------------------------------------------------------------------------------------------------
# World-space fill textures (flat, posterised, sparse ink marks).
# ---------------------------------------------------------------------------------------------------------
def value_noise(height, width, cell_pixels, seed):
    random_generator = np.random.default_rng(seed)
    grid = random_generator.random((height // cell_pixels + 3, width // cell_pixels + 3))
    image = Image.fromarray((grid * 255).astype(np.uint8)).resize(((width // cell_pixels + 3) * cell_pixels,
                                                                   (height // cell_pixels + 3) * cell_pixels), Image.BICUBIC)
    return np.asarray(image)[:height, :width] / 255.0


def make_fill(biome, height, width, seed):
    tones = FILL_COLOURS[biome]
    blotch = value_noise(height, width, 96, seed) * 0.65 + value_noise(height, width, 40, seed + 1) * 0.35
    image = np.zeros((height, width, 3))
    image[:] = tones[0]
    image[blotch > 0.64] = tones[1]
    image[blotch < 0.30] = tones[2]
    canvas = Image.fromarray(image.astype(np.uint8), "RGB")
    draw = ImageDraw.Draw(canvas)
    random_generator = np.random.default_rng(seed + 7)
    if biome == STONE:
        return stone_cobbles(height, width, seed, tones)
    mark_count = int(height * width / (PIXELS_PER_METRE * PIXELS_PER_METRE) * {GRASS: 1.6, DIRT: 1.2, FOREST: 2.2}[biome])
    for _ in range(mark_count):
        centre_x = random_generator.uniform(0, width)
        centre_y = random_generator.uniform(0, height)
        if biome == GRASS:
            for side in (-1, 1):
                length = random_generator.uniform(5, 9)
                draw.line([(centre_x, centre_y), (centre_x + side * length * 0.45, centre_y - length)], fill=shade(tones[2], 0.78), width=2)
        elif biome == DIRT:
            radius_x = random_generator.uniform(2.5, 5.5)
            radius_y = radius_x * random_generator.uniform(0.55, 0.85)
            draw.ellipse([centre_x - radius_x, centre_y - radius_y, centre_x + radius_x, centre_y + radius_y],
                         fill=tones[1], outline=shade(INK, 1.35), width=2)
        else:
            angle = random_generator.uniform(0, math.pi)
            length = random_generator.uniform(5, 10)
            draw.line([(centre_x, centre_y), (centre_x + math.cos(angle) * length, centre_y + math.sin(angle) * length)],
                      fill=shade(tones[2], 0.7), width=2)
    return np.asarray(canvas).astype(np.float64) / 255.0


def stone_cobbles(height, width, seed, tones):
    random_generator = np.random.default_rng(seed)
    cell = 40
    rows = height // cell + 3
    columns = width // cell + 3
    seed_x = (np.arange(columns)[None, :] + random_generator.uniform(0.15, 0.85, (rows, columns))) * cell - cell
    seed_y = (np.arange(rows)[:, None] + random_generator.uniform(0.15, 0.85, (rows, columns))) * cell - cell
    tone_index = random_generator.integers(0, 3, (rows, columns))
    pixel_y, pixel_x = np.mgrid[0:height, 0:width].astype(np.float64)
    base_row = (pixel_y // cell).astype(int) + 1
    base_column = (pixel_x // cell).astype(int) + 1
    nearest = np.full((height, width), 1e9)
    second = np.full((height, width), 1e9)
    nearest_tone = np.zeros((height, width), dtype=int)
    for offset_row in (-1, 0, 1):
        for offset_column in (-1, 0, 1):
            row = np.clip(base_row + offset_row, 0, rows - 1)
            column = np.clip(base_column + offset_column, 0, columns - 1)
            distance = np.hypot(pixel_x - seed_x[row, column], pixel_y - seed_y[row, column])
            closer = distance < nearest
            second = np.where(closer, nearest, np.minimum(second, distance))
            nearest_tone = np.where(closer, tone_index[row, column], nearest_tone)
            nearest = np.where(closer, distance, nearest)
    palette = np.array(tones, dtype=np.float64)
    image = palette[nearest_tone]
    mortar = np.clip((second - nearest - 1.5) / 2.0, 0.0, 1.0)[..., None]
    image = image * mortar + np.array(shade(INK, 1.6)) * (1.0 - mortar)
    return image / 255.0


# ---------------------------------------------------------------------------------------------------------
# Corner grid generation + cleanup (spec ET-D4, ET-D7).
# ---------------------------------------------------------------------------------------------------------
def lattice_value_noise(sample_x, sample_y, seed):
    cell_x = np.floor(sample_x)
    cell_y = np.floor(sample_y)
    fraction_x = sample_x - cell_x
    fraction_y = sample_y - cell_y
    fraction_x = fraction_x * fraction_x * (3 - 2 * fraction_x)
    fraction_y = fraction_y * fraction_y * (3 - 2 * fraction_y)
    def lattice(offset_x, offset_y):
        hashed = np.sin((cell_x + offset_x) * 127.1 + (cell_y + offset_y) * 311.7 + seed * 74.7) * 43758.5453
        return hashed - np.floor(hashed)
    top = lattice(0, 0) * (1 - fraction_x) + lattice(1, 0) * fraction_x
    bottom = lattice(0, 1) * (1 - fraction_x) + lattice(1, 1) * fraction_x
    return top * (1 - fraction_y) + bottom * fraction_y


def warped_field(rows, columns, seed, frequency, rotation_degrees):
    """Rotated, domain-warped fbm sampled at the corners: grid-aligned noise makes long straight block edges."""
    corner_y, corner_x = np.mgrid[0:rows, 0:columns].astype(np.float64)
    angle = math.radians(rotation_degrees)
    rotated_x = corner_x * math.cos(angle) - corner_y * math.sin(angle)
    rotated_y = corner_x * math.sin(angle) + corner_y * math.cos(angle)
    warp_x = lattice_value_noise(rotated_x * 0.11, rotated_y * 0.11, seed + 31) - 0.5
    warp_y = lattice_value_noise(rotated_x * 0.11 + 17.3, rotated_y * 0.11, seed + 47) - 0.5
    warped_x = rotated_x + warp_x * 9.0
    warped_y = rotated_y + warp_y * 9.0
    total = 0.65 * lattice_value_noise(warped_x * frequency, warped_y * frequency, seed)
    total += 0.35 * lattice_value_noise(warped_x * frequency * 2.3, warped_y * frequency * 2.3, seed + 5)
    return total


def generate_corner_biomes(rows, columns, seed):
    grass_field = warped_field(rows, columns, seed + 1, 0.27, 27.0)
    forest_field = warped_field(rows, columns, seed + 2, 0.24, -38.0)
    stone_field = warped_field(rows, columns, seed + 3, 0.30, 61.0)
    corners = np.full((rows, columns), DIRT, dtype=np.int32)
    corners[grass_field > 0.42] = GRASS
    corners[(forest_field > 0.60) & (grass_field > 0.34)] = FOREST
    corners[stone_field > 0.66] = STONE
    return corners


def connected_components(corners, biome):
    rows, columns = corners.shape
    seen = np.zeros_like(corners, dtype=bool)
    components = []
    for start_row in range(rows):
        for start_column in range(columns):
            if seen[start_row, start_column] or corners[start_row, start_column] != biome:
                continue
            queue = deque([(start_row, start_column)])
            seen[start_row, start_column] = True
            members = []
            while queue:
                row, column = queue.popleft()
                members.append((row, column))
                for delta_row, delta_column in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    next_row, next_column = row + delta_row, column + delta_column
                    if 0 <= next_row < rows and 0 <= next_column < columns and not seen[next_row, next_column] \
                            and corners[next_row, next_column] == biome:
                        seen[next_row, next_column] = True
                        queue.append((next_row, next_column))
            components.append(members)
    return components


def clean_corners(corners, minimum_island_corners=3, adjacency_rules=((FOREST, STONE, GRASS),)):
    cleaned = corners.copy()
    rows, columns = cleaned.shape
    for biome in BIOME_NAMES:
        for members in connected_components(cleaned, biome):
            if len(members) >= minimum_island_corners:
                continue
            neighbour_votes = {}
            for row, column in members:
                for delta_row, delta_column in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    next_row, next_column = row + delta_row, column + delta_column
                    if 0 <= next_row < rows and 0 <= next_column < columns and cleaned[next_row, next_column] != biome:
                        neighbour = int(cleaned[next_row, next_column])
                        neighbour_votes[neighbour] = neighbour_votes.get(neighbour, 0) + 1
            if neighbour_votes:
                replacement = max(neighbour_votes, key=neighbour_votes.get)
                for row, column in members:
                    cleaned[row, column] = replacement
    for biome_a, biome_b, buffer_biome in adjacency_rules:
        for row in range(rows):
            for column in range(columns):
                if cleaned[row, column] != biome_a:
                    continue
                touches = False
                for delta_row in (-1, 0, 1):
                    for delta_column in (-1, 0, 1):
                        next_row, next_column = row + delta_row, column + delta_column
                        if 0 <= next_row < rows and 0 <= next_column < columns and cleaned[next_row, next_column] == biome_b:
                            touches = True
                if touches:
                    cleaned[row, column] = buffer_biome
    return cleaned


# ---------------------------------------------------------------------------------------------------------
# The compositor: same logic as the planned shader (spec section 2).
# ---------------------------------------------------------------------------------------------------------
def variant_for(cell_row, cell_column, biome, variant_count):
    return ((cell_row * 73856093) ^ (cell_column * 19349663) ^ (biome * 83492791)) % variant_count


def edge_set_for(upper_biome, lower_biome, packed_sets):
    name = PAIR_OVERRIDES.get((upper_biome, lower_biome), DEFAULT_SET_FOR_BIOME.get(upper_biome))
    return packed_sets[name]


def render_ground(corners, fills, packed_sets, cell_pixels):
    rows, columns = corners.shape
    height, width = (rows - 1) * cell_pixels, (columns - 1) * cell_pixels
    output = np.zeros((height, width, 3))
    for cell_row in range(rows - 1):
        for cell_column in range(columns - 1):
            top_left, top_right = corners[cell_row, cell_column], corners[cell_row, cell_column + 1]
            bottom_right, bottom_left = corners[cell_row + 1, cell_column + 1], corners[cell_row + 1, cell_column]
            cell_corners = [top_left, top_right, bottom_right, bottom_left]
            distinct = sorted(set(int(value) for value in cell_corners), key=lambda biome: DRAW_ORDER[biome])
            row_slice = slice(cell_row * cell_pixels, (cell_row + 1) * cell_pixels)
            column_slice = slice(cell_column * cell_pixels, (cell_column + 1) * cell_pixels)
            colour = fills[distinct[0]][row_slice, column_slice].copy()
            for layer_index in range(1, len(distinct)):
                upper = distinct[layer_index]
                lower = max((biome for biome in distinct if DRAW_ORDER[biome] < DRAW_ORDER[upper]), key=lambda biome: DRAW_ORDER[biome])
                case_index = sum(bit for bit, corner in zip((1, 2, 4, 8), cell_corners) if DRAW_ORDER[int(corner)] >= DRAW_ORDER[upper])
                upper_fill = fills[upper][row_slice, column_slice]
                if case_index == 15:
                    colour = upper_fill.copy()
                    continue
                edge_set = edge_set_for(upper, lower, packed_sets)
                coverage, detail_rgb, detail_alpha = edge_set.case_layers(case_index, variant_for(cell_row, cell_column, upper, edge_set.variant_count), cell_pixels)
                colour = colour * (1 - coverage[..., None]) + upper_fill * coverage[..., None]
                colour = colour * (1 - detail_alpha[..., None]) + detail_rgb * detail_alpha[..., None]
            output[row_slice, column_slice] = colour
    return output


def render_mask(corner_levels, piece_layers_for_case, cell_pixels):
    """Coverage + ink of a binary corner grid using a per-case layer function (cliff body / lip / foot)."""
    rows, columns = corner_levels.shape
    height, width = (rows - 1) * cell_pixels, (columns - 1) * cell_pixels
    coverage_out = np.zeros((height, width))
    detail_rgb_out = np.zeros((height, width, 3))
    detail_alpha_out = np.zeros((height, width))
    for cell_row in range(rows - 1):
        for cell_column in range(columns - 1):
            cell_corners = [corner_levels[cell_row, cell_column], corner_levels[cell_row, cell_column + 1],
                            corner_levels[cell_row + 1, cell_column + 1], corner_levels[cell_row + 1, cell_column]]
            case_index = sum(bit for bit, corner in zip((1, 2, 4, 8), cell_corners) if corner)
            row_slice = slice(cell_row * cell_pixels, (cell_row + 1) * cell_pixels)
            column_slice = slice(cell_column * cell_pixels, (cell_column + 1) * cell_pixels)
            if case_index == 0:
                continue
            if case_index == 15:
                coverage_out[row_slice, column_slice] = 1.0
                continue
            coverage, detail_rgb, detail_alpha = piece_layers_for_case(case_index, cell_row, cell_column)
            coverage_out[row_slice, column_slice] = coverage
            detail_rgb_out[row_slice, column_slice] = detail_rgb
            detail_alpha_out[row_slice, column_slice] = detail_alpha
    return coverage_out, detail_rgb_out, detail_alpha_out


# ---------------------------------------------------------------------------------------------------------
# Output helpers
# ---------------------------------------------------------------------------------------------------------
def to_image(array):
    return Image.fromarray((np.clip(array, 0, 1) * 255).astype(np.uint8), "RGB")


def checker(width, height):
    pixel_y, pixel_x = np.mgrid[0:height, 0:width]
    value = np.where(((pixel_x // 16) + (pixel_y // 16)) % 2 == 0, 235, 250)
    return Image.fromarray(np.stack([value] * 3, -1).astype(np.uint8), "RGB").convert("RGBA")


def label(image, text, position, size=18, colour=(20, 20, 20)):
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("arial.ttf", size)
    except OSError:
        font = ImageFont.load_default()
    draw.text(position, text, fill=colour, font=font)


def write_contact_sheet(packed_set, path):
    margin = 40
    sheet = Image.new("RGB", (CELL * 5 + margin, (CELL + margin) * packed_set.variant_count + CELL * 4 // 2 + 3 * margin), (255, 255, 255))
    label(sheet, "%s: artist pieces (as exported, alpha on checker)" % packed_set.name, (8, 8))
    for variant in range(packed_set.variant_count):
        for piece_index, piece_name in enumerate(PIECES):
            tile = checker(CELL, CELL)
            tile.alpha_composite(packed_set.artist_pieces[(variant, piece_name)])
            origin = (margin + piece_index * CELL, margin + variant * (CELL + margin))
            sheet.paste(tile.convert("RGB"), origin)
            label(sheet, "v%d %s" % (variant, piece_name), (origin[0] + 4, origin[1] + CELL + 2), 14)
    top = margin + packed_set.variant_count * (CELL + margin) + margin
    label(sheet, "auto-rotated into all 16 cases (v0, over the Dirt fill), case 0..15 left to right", (8, top - 30))
    half = CELL // 2
    lower_fill = np.zeros((half, half, 3)) + np.array(FILL_COLOURS[DIRT][0]) / 255.0
    upper_fill = np.zeros((half, half, 3)) + np.array(packed_set.fill_key) / 255.0
    for case_index in range(16):
        if case_index == 0:
            cell_colour = lower_fill
        elif case_index == 15:
            cell_colour = upper_fill
        else:
            coverage, detail_rgb, detail_alpha = packed_set.case_layers(case_index, 0, half)
            cell_colour = lower_fill * (1 - coverage[..., None]) + upper_fill * coverage[..., None]
            cell_colour = cell_colour * (1 - detail_alpha[..., None]) + detail_rgb * detail_alpha[..., None]
        origin = (margin // 2 + (case_index % 8) * (half + 4), top + (case_index // 8) * (half + 4))
        sheet.paste(to_image(cell_colour), origin)
    sheet.save(path)


# ---------------------------------------------------------------------------------------------------------
# Scenes
# ---------------------------------------------------------------------------------------------------------
def biome_map_scene(packed_sets, capture_dir):
    cell_pixels = 128
    rows, columns = 13, 21
    raw = generate_corner_biomes(rows, columns, seed=11)
    cleaned = clean_corners(raw)
    height, width = (rows - 1) * cell_pixels, (columns - 1) * cell_pixels
    fills = {biome: make_fill(biome, height, width, 100 + biome) for biome in BIOME_NAMES}
    for name, corners in (("raw", raw), ("cleaned", cleaned)):
        image = to_image(render_ground(corners, fills, packed_sets, cell_pixels))
        image.save("%s/%s.png" % (capture_dir, {"raw": "05_biome_map_raw_noise_before_cleanup",
                                                 "cleaned": "06_biome_map_after_cleanup_and_adjacency"}[name]))
    debug = to_image(render_ground(cleaned, fills, packed_sets, cell_pixels)).convert("RGBA")
    overlay = Image.new("RGBA", debug.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for column in range(columns):
        draw.line([(column * cell_pixels, 0), (column * cell_pixels, height)], fill=(255, 255, 255, 90), width=1)
    for row in range(rows):
        draw.line([(0, row * cell_pixels), (width, row * cell_pixels)], fill=(255, 255, 255, 90), width=1)
    corner_colours = {DIRT: (120, 80, 40), GRASS: (40, 160, 40), FOREST: (10, 70, 20), STONE: (150, 150, 160)}
    for row in range(rows):
        for column in range(columns):
            centre = (column * cell_pixels, row * cell_pixels)
            draw.ellipse([centre[0] - 7, centre[1] - 7, centre[0] + 7, centre[1] + 7],
                         fill=corner_colours[int(cleaned[row, column])] + (255,), outline=(255, 255, 255, 255), width=2)
    debug.alpha_composite(overlay)
    debug.convert("RGB").save("%s/07_biome_map_corner_grid_debug.png" % capture_dir)
    return cleaned


def nearest_fill(corners, keep_mask):
    """Corners outside keep_mask take the biome of the nearest kept corner (BFS), so each level has its own surface."""
    rows, columns = corners.shape
    result = corners.copy()
    assigned = keep_mask.copy()
    queue = deque((row, column) for row in range(rows) for column in range(columns) if keep_mask[row, column])
    while queue:
        row, column = queue.popleft()
        for delta_row, delta_column in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            next_row, next_column = row + delta_row, column + delta_column
            if 0 <= next_row < rows and 0 <= next_column < columns and not assigned[next_row, next_column]:
                assigned[next_row, next_column] = True
                result[next_row, next_column] = result[row, column]
                queue.append((next_row, next_column))
    return result


def squash(array, factor):
    height = array.shape[0]
    new_height = int(round(height * factor))
    if array.ndim == 2:
        image = Image.fromarray((np.clip(array, 0, 1) * 255).astype(np.uint8))
        return np.asarray(image.resize((array.shape[1], new_height), Image.LANCZOS)) / 255.0
    image = Image.fromarray((np.clip(array, 0, 1) * 255).astype(np.uint8), "RGB")
    return np.asarray(image.resize((array.shape[1], new_height), Image.LANCZOS)) / 255.0


def shift_up(array, pixels):
    shifted = np.zeros_like(array)
    shifted[:-pixels] = array[pixels:]
    return shifted


def erode(mask, offsets):
    """Non-wrapping erosion: np.roll wraps the image and drew a stray ink line along the border."""
    padded = np.pad(mask, 2, mode="edge")
    result = mask.copy()
    height, width = mask.shape
    for delta_row, delta_column in offsets:
        result &= padded[2 + delta_row:2 + delta_row + height, 2 + delta_column:2 + delta_column + width]
    return result


def box_blur(array, radius):
    padded = np.pad(array, radius, mode="edge")
    cumulative = padded.cumsum(0).cumsum(1)
    cumulative = np.pad(cumulative, ((1, 0), (1, 0)))
    size = 2 * radius + 1
    total = cumulative[size:, size:] - cumulative[:-size, size:] - cumulative[size:, :-size] + cumulative[:-size, :-size]
    return total / (size * size)


def rock_face_colour(width, wall_height, top_bevel, seed):
    """Wall texture in (screen column, pixels-down v): large irregular slabs stretched tall, lit top edge per slab."""
    random_generator = np.random.default_rng(seed)
    rock_tones = [(128, 112, 95), (117, 102, 87), (138, 121, 103)]
    stretch = 1.7
    squashed_height = int((wall_height + 1) / stretch) + 2
    slab_cell = 34
    rows = squashed_height // slab_cell + 3
    columns = width // slab_cell + 3
    seed_x = (np.arange(columns)[None, :] + random_generator.uniform(0.1, 0.9, (rows, columns))) * slab_cell - slab_cell
    seed_y = (np.arange(rows)[:, None] + random_generator.uniform(0.1, 0.9, (rows, columns))) * slab_cell - slab_cell
    tone_index = random_generator.integers(0, len(rock_tones), (rows, columns))
    pixel_v, pixel_x = np.mgrid[0:wall_height + 1, 0:width].astype(np.float64)
    pixel_y = pixel_v / stretch
    base_row = (pixel_y // slab_cell).astype(int) + 1
    base_column = (pixel_x // slab_cell).astype(int) + 1
    nearest = np.full(pixel_v.shape, 1e9)
    second = np.full(pixel_v.shape, 1e9)
    nearest_tone = np.zeros(pixel_v.shape, dtype=int)
    nearest_seed_y = np.zeros(pixel_v.shape)
    for offset_row in (-1, 0, 1):
        for offset_column in (-1, 0, 1):
            row = np.clip(base_row + offset_row, 0, rows - 1)
            column = np.clip(base_column + offset_column, 0, columns - 1)
            distance = np.hypot(pixel_x - seed_x[row, column], pixel_y - seed_y[row, column])
            closer = distance < nearest
            second = np.where(closer, nearest, np.minimum(second, distance))
            nearest_tone = np.where(closer, tone_index[row, column], nearest_tone)
            nearest_seed_y = np.where(closer, seed_y[row, column], nearest_seed_y)
            nearest = np.where(closer, distance, nearest)
    texture = np.array(rock_tones, dtype=np.float64)[nearest_tone]
    # Each slab is lit on its upper face and shaded below, like a rounded lump catching sun from the north.
    above_centre = np.clip((nearest_seed_y - pixel_y) / (slab_cell * 0.5), -1.0, 1.0)
    texture = texture * (1.0 + 0.10 * above_centre[..., None])
    seam = np.clip(1.0 - (second - nearest - 1.0) / 2.2, 0.0, 1.0)
    ink_mask = seam * 0.95
    cap_noise = value_noise(1, width + 64, 40, seed + 3)[0, :width]
    cap_v = (top_bevel - 5 + cap_noise * 12).astype(np.int64)
    return texture / 255.0, ink_mask, cap_v


def cliff_scene(packed_sets, capture_dir):
    pixels_per_cliff_cell = 256
    biome_cell_pixels = 128
    cliff_levels = np.array([
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [0, 0, 1, 1, 1, 1, 0, 0, 0, 0],
        [0, 1, 1, 1, 1, 1, 1, 1, 0, 0],
        [0, 1, 1, 1, 1, 1, 1, 1, 1, 0],
        [0, 0, 1, 1, 1, 0, 1, 1, 0, 0],
        [0, 0, 0, 1, 0, 0, 0, 0, 0, 0],
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    ], dtype=bool)
    cliff_rows, cliff_columns = cliff_levels.shape
    biome_rows, biome_columns = (cliff_rows - 1) * 2 + 1, (cliff_columns - 1) * 2 + 1
    height, width = (cliff_rows - 1) * pixels_per_cliff_cell, (cliff_columns - 1) * pixels_per_cliff_cell

    body_set = PackedSet("CliffBody", dict(upper=GRASS, style=dict(clump_width=(1, 2), clump_height=(0, 0), clump_spacing=(50, 60),
                                                                    clump_baseline=0, ripple=0, chipped=True), strokes="none", offset=0.0), 1)
    lip_set = PackedSet("CliffLip", dict(upper=GRASS, style=LIP_STYLE, strokes="turf", offset=11.0), 2)
    foot_set = PackedSet("CliffFoot", dict(upper=DIRT, style=LIP_STYLE, strokes="turf", offset=12.0), 2)
    for packed_set in (lip_set, foot_set):
        for line in packed_set.validation_report():
            print("validation (offset sets are expected to cross off-midpoint by their offset):", line)

    body, _, _ = render_mask(cliff_levels, lambda case_index, row, column: body_set.case_layers(case_index, 0, pixels_per_cliff_cell), pixels_per_cliff_cell)
    lip_coverage, lip_rgb, lip_alpha = render_mask(
        cliff_levels, lambda case_index, row, column: lip_set.case_layers(case_index, variant_for(row, column, 9, 2), pixels_per_cliff_cell), pixels_per_cliff_cell)
    foot_coverage, foot_rgb, foot_alpha = render_mask(
        ~cliff_levels, lambda case_index, row, column: foot_set.case_layers(case_index, variant_for(row, column, 5, 2), pixels_per_cliff_cell), pixels_per_cliff_cell)

    corners = generate_corner_biomes(biome_rows, biome_columns, seed=29)
    corners[corners == STONE] = GRASS
    for row in range(biome_rows):
        for column in range(biome_columns):
            if (row - 3.5) ** 2 / 4.0 + (column - 6.2) ** 2 / 6.0 + 0.4 * math.sin(row * 1.7 + column) < 1.0:
                corners[row, column] = STONE
    corners[9:12, 2:5] = DIRT
    corners = clean_corners(corners)
    corner_inside = np.zeros((biome_rows, biome_columns), dtype=bool)
    for row in range(biome_rows):
        for column in range(biome_columns):
            sample_y = min(height - 1, row * biome_cell_pixels)
            sample_x = min(width - 1, column * biome_cell_pixels)
            corner_inside[row, column] = body[sample_y, sample_x] > 0.5
    upper_corners = nearest_fill(corners, corner_inside)
    lower_corners = nearest_fill(corners, ~corner_inside)
    fills = {biome: make_fill(biome, height, width, 300 + biome) for biome in BIOME_NAMES}
    upper_ground = render_ground(upper_corners, fills, packed_sets, biome_cell_pixels)
    lower_ground = render_ground(lower_corners, fills, packed_sets, biome_cell_pixels)

    ground_squash = 0.8
    wall_height = int(round(3.0 * PIXELS_PER_METRE * 0.62))
    top_bevel = 20
    foot_bevel = 18
    body_s = squash(body, ground_squash) > 0.5
    lip_s = squash(lip_coverage, ground_squash)
    lip_rgb_s = squash(lip_rgb, ground_squash)
    lip_alpha_s = squash(lip_alpha, ground_squash)
    foot_s = squash(foot_coverage, ground_squash)
    foot_rgb_s = squash(foot_rgb, ground_squash)
    foot_alpha_s = squash(foot_alpha, ground_squash)
    upper_s = np.clip(squash(upper_ground, ground_squash) * 1.05, 0, 1)
    lower_s = squash(lower_ground, ground_squash) * 0.93
    screen_height, screen_width = body_s.shape

    previous_true = np.full((screen_height, screen_width), -10 ** 6, dtype=np.int64)
    running = np.full(screen_width, -10 ** 6, dtype=np.int64)
    for row in range(screen_height):
        running = np.where(body_s[row], row, running)
        previous_true[row] = running
    row_index = np.arange(screen_height)[:, None]
    window_end = np.minimum(row_index + wall_height, screen_height - 1)
    front_base = np.take_along_axis(previous_true, np.broadcast_to(window_end, (screen_height, screen_width)), axis=0)
    top_surface = shift_up(lip_s, wall_height)
    is_wall = (front_base >= row_index) & (front_base - row_index < wall_height)
    height_above_ground = np.clip(front_base - row_index, 0, wall_height)
    metres_down = wall_height - height_above_ground

    shadow = box_blur(np.roll(body_s.astype(np.float64), 10, axis=0), 14)
    scene = lower_s * (1.0 - 0.28 * shadow[..., None] * (~body_s)[..., None])

    ink = np.array(INK) / 255.0
    rock, rock_ink, rock_cap = rock_face_colour(screen_width, wall_height, top_bevel, seed=5)
    column_index = np.broadcast_to(np.arange(screen_width)[None, :], (screen_height, screen_width))
    wall_colour = rock[metres_down, column_index]
    wall_ink = rock_ink[metres_down, column_index]
    column_cap = rock_cap[column_index]
    above_cap = metres_down < column_cap
    top_round = np.where(above_cap, 0.55 + 0.45 * (1.0 - metres_down / np.maximum(column_cap, 1)), 0.0)
    wall_colour = wall_colour * (1 - top_round[..., None]) + np.array([166, 150, 126]) / 255.0 * top_round[..., None]
    bevel_line = np.zeros_like(top_round)
    face_darkening = 1.0 - 0.22 * np.clip((metres_down - top_bevel) / (wall_height - top_bevel - foot_bevel), 0, 1)
    foot_round = np.clip(1.0 - height_above_ground / foot_bevel, 0.0, 1.0)
    wall_colour = wall_colour * face_darkening[..., None]
    wall_colour = wall_colour * (1 - 0.30 * foot_round[..., None] * (1 - foot_round[..., None]) * 4)
    wall_colour = wall_colour * (1 - 0.18 * foot_round[..., None] ** 2)
    total_wall_ink = np.clip(np.maximum(wall_ink * (1 - top_round) * (foot_round < 0.6), bevel_line), 0, 1)
    wall_colour = wall_colour * (1 - total_wall_ink[..., None]) + ink * total_wall_ink[..., None]
    scene = np.where(is_wall[..., None], wall_colour, scene)

    shadowed_lower = lower_s * (1.0 - 0.28 * shadow[..., None])
    foot_draw = foot_s * (is_wall & (height_above_ground < foot_bevel + 26))
    scene = scene * (1 - foot_draw[..., None]) + shadowed_lower * foot_draw[..., None]
    foot_ink = foot_alpha_s * ((is_wall & (height_above_ground < foot_bevel + 26)) | ~body_s)
    scene = scene * (1 - foot_ink[..., None]) + foot_rgb_s * foot_ink[..., None]

    top_colour = shift_up(upper_s, wall_height)
    top_ink_alpha = shift_up(lip_alpha_s, wall_height)
    top_ink_rgb = shift_up(lip_rgb_s, wall_height)
    scene = scene * (1 - top_surface[..., None]) + top_colour * top_surface[..., None]
    scene = scene * (1 - top_ink_alpha[..., None]) + top_ink_rgb * top_ink_alpha[..., None]

    top_solid = top_surface > 0.5
    top_inner = erode(top_solid, ((1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2)))
    top_rim = top_solid & ~top_inner & ~is_wall
    scene = np.where(top_rim[..., None], scene * 0.35 + ink * 0.65, scene)
    silhouette = np.clip(np.maximum(is_wall.astype(np.float64), top_surface), 0, 1) > 0.5
    eroded = erode(silhouette, ((1, 0), (-1, 0), (0, 1), (0, -1), (0, 2), (0, -2)))
    side_outline = silhouette & ~eroded & is_wall & (height_above_ground >= foot_bevel // 2)
    scene = np.where(side_outline[..., None], ink * 1.0, scene)

    image = to_image(scene)
    image.save("%s/08_cliff_scene_full.png" % capture_dir)

    south_columns = [column for column in range(screen_width) if is_wall[:, column].any()]
    focus_column = south_columns[len(south_columns) // 3]
    wall_rows = np.nonzero(is_wall[:, focus_column])[0]
    crop_bottom = min(screen_height, int(wall_rows.max()) + 120)
    crop_top = max(0, crop_bottom - 520)
    crop_left = max(0, focus_column - 360)
    crop = image.crop((crop_left, crop_top, min(screen_width, crop_left + 720), crop_bottom)).resize((1440, 1040), Image.LANCZOS)
    crop.save("%s/09_cliff_closeup_2x.png" % capture_dir)
    annotate_cliff(image, is_wall, height_above_ground, top_surface, body_s, wall_height, top_bevel, foot_bevel, capture_dir)


def annotate_cliff(image, is_wall, height_above_ground, top_surface, body_s, wall_height, top_bevel, foot_bevel, capture_dir):
    screen_height, screen_width = is_wall.shape
    wall_count = is_wall.sum(axis=0)
    base_row = np.where(wall_count > 0, np.argmax(is_wall[::-1], axis=0), -1)
    base_row = np.where(wall_count > 0, screen_height - 1 - base_row, -1)
    best_column = None
    best_straightness = 1e9
    for column in range(60, screen_width - 60):
        if wall_count[column] < wall_height - 2 or wall_count[column] > wall_height + 4:
            continue
        neighbourhood = base_row[column - 50:column + 51]
        if (neighbourhood < 0).any():
            continue
        straightness = float(np.ptp(neighbourhood))
        if straightness < best_straightness:
            best_straightness = straightness
            best_column = column
    if best_column is None:
        return
    wall_rows = np.nonzero(is_wall[:, best_column])[0]
    wall_top, wall_bottom = int(wall_rows.min()), int(wall_rows.max())
    crop_top = max(0, wall_top - 110)
    crop_bottom = min(screen_height, wall_bottom + 90)
    crop_left = max(0, best_column - 200)
    crop = image.crop((crop_left, crop_top, crop_left + 400, crop_bottom))
    scale = 2
    crop = crop.resize((crop.width * scale, crop.height * scale), Image.LANCZOS)
    panel = Image.new("RGB", (crop.width + 720, crop.height), (250, 248, 244))
    panel.paste(crop, (0, 0))
    draw = ImageDraw.Draw(panel)
    def screen_y(row):
        return (row - crop_top) * scale
    bands = [
        (wall_top - 60, "upper biome (plateau top)"),
        (wall_top - 6, "TOP EDGE: upper biome's ragged lip overhangs the rim"),
        (wall_top + top_bevel // 2, "TOP BEVEL: rock rounds over, lighter"),
        (wall_top + wall_height // 2, "ROCK FACE: vertical columns, darker toward the foot"),
        (wall_bottom - foot_bevel // 2 - 4, "FOOT BEVEL: rock flares out, in shade"),
        (wall_bottom + 4, "FOOT EDGE: lower biome's ragged edge laps up onto the foot"),
        (wall_bottom + 55, "lower biome (next area)"),
    ]
    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except OSError:
        font = ImageFont.load_default()
    marker_x = (best_column - crop_left) * scale
    for row, text in bands:
        target_y = screen_y(row)
        draw.line([(marker_x, target_y), (crop.width + 16, target_y)], fill=(200, 40, 40), width=2)
        draw.ellipse([marker_x - 5, target_y - 5, marker_x + 5, target_y + 5], fill=(200, 40, 40))
        draw.text((crop.width + 24, target_y - 12), text, fill=(20, 20, 20), font=font)
    panel.save("%s/10_cliff_profile_labelled.png" % capture_dir)


# ---------------------------------------------------------------------------------------------------------
# --cliff-edges: plateau rim/foot art on a 2 m corner grid, in plan and in a gameplay-angle heightfield view.
# Contour, profile and atlas layout mirror what the game will do (decisions CE-D1, CE-D2, CE-D5).
# ---------------------------------------------------------------------------------------------------------
CLIFF_WIDTH_METRES = 20
CLIFF_HEIGHT_METRES = 14
GRID_CELL_METRES = 2.0
CONTOUR_RADIUS = 1.0
TOP_BEVEL_END = 0.45
FACE_END = 0.48
FOOT_BEVEL_END = 0.90
PLATEAU_HEIGHT = 3.0
TOP_BEVEL_DROP = 0.45
FOOT_BEVEL_RISE = 0.42
CAMERA_PITCH_DEGREES = 50.0
ATLAS_CELL_PIXELS = 128
ATLAS_DIRECTORY = "Assets/PaintedGround/Textures/EdgeTiles"
RIM_ROCK_COLOUR = np.array([166, 150, 126]) / 255.0
FOOT_ROCK_COLOUR = np.array([88, 77, 66]) / 255.0
ATLAS_FALLBACK_NAMES = {GRASS: "GrassOverDirt", FOREST: "ForestOverGrass", DIRT: "GrassOverDirt", STONE: "StoneOverDirt"}
_ATLAS_CACHE = {}


def case_contour_signed_distance(case_index, local_x, local_z):
    """Positive inside the plateau. Cell-local metres 0..2; corners TL=(0,2) TR=(2,2) BR=(2,0) BL=(0,0)."""
    radius = CONTOUR_RADIUS

    def distance_to(corner_x, corner_z):
        return np.hypot(local_x - corner_x, local_z - corner_z)

    top_left, top_right, bottom_right, bottom_left = (0.0, 2.0), (2.0, 2.0), (2.0, 0.0), (0.0, 0.0)
    case_index = np.asarray(case_index)
    conditions = [case_index == value for value in range(16)]
    results = [
        -radius + 0 * local_x,
        radius - distance_to(*top_left),
        radius - distance_to(*top_right),
        local_z - 1.0,
        radius - distance_to(*bottom_right),
        np.minimum(distance_to(*top_right), distance_to(*bottom_left)) - radius,
        local_x - 1.0,
        distance_to(*bottom_left) - radius,
        radius - distance_to(*bottom_left),
        1.0 - local_x,
        np.minimum(distance_to(*top_left), distance_to(*bottom_right)) - radius,
        distance_to(*bottom_right) - radius,
        1.0 - local_z,
        distance_to(*top_right) - radius,
        distance_to(*top_left) - radius,
        radius + 0 * local_x,
    ]
    return np.clip(np.select(conditions, results), -radius, radius)


def sample_corner_grid(corner_levels, world_x, world_z):
    cells_z, cells_x = corner_levels.shape[0] - 1, corner_levels.shape[1] - 1
    cell_x = np.clip(np.floor(world_x / GRID_CELL_METRES).astype(np.int64), 0, cells_x - 1)
    cell_z = np.clip(np.floor(world_z / GRID_CELL_METRES).astype(np.int64), 0, cells_z - 1)
    case_index = (corner_levels[cell_z + 1, cell_x] * 1 + corner_levels[cell_z + 1, cell_x + 1] * 2
                  + corner_levels[cell_z, cell_x + 1] * 4 + corner_levels[cell_z, cell_x] * 8)
    return cell_x, cell_z, case_index, world_x - cell_x * GRID_CELL_METRES, world_z - cell_z * GRID_CELL_METRES


def load_edge_atlas(biome, set_kind):
    key = (biome, set_kind)
    if key not in _ATLAS_CACHE:
        path = "%s/T_EdgeAtlas_%s%s.png" % (ATLAS_DIRECTORY, BIOME_NAMES[biome], set_kind)
        if not os.path.exists(path):
            path = "%s/T_EdgeAtlas_%s.png" % (ATLAS_DIRECTORY, ATLAS_FALLBACK_NAMES[biome])
            print("WARNING: no %s%s atlas, falling back to %s" % (BIOME_NAMES[biome], set_kind, path))
        else:
            print("using atlas", path)
        _ATLAS_CACHE[key] = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32) / 255.0
    return _ATLAS_CACHE[key]


def weighted_atlas_sample(biome_weights, set_kind, seed_offset, cell_x, cell_z, case_index, local_x, local_z):
    coverage = np.zeros(case_index.shape)
    ink = np.zeros(case_index.shape)
    for biome, weight in biome_weights:
        atlas = load_edge_atlas(biome, set_kind)
        variant = variant_for(cell_z, cell_x, biome + seed_offset, 2)
        atlas_row = (case_index // 8 + 2 * variant) * ATLAS_CELL_PIXELS + np.clip(((1.0 - local_z / GRID_CELL_METRES) * ATLAS_CELL_PIXELS).astype(np.int64), 0, ATLAS_CELL_PIXELS - 1)
        atlas_column = (case_index % 8) * ATLAS_CELL_PIXELS + np.clip((local_x / GRID_CELL_METRES * ATLAS_CELL_PIXELS).astype(np.int64), 0, ATLAS_CELL_PIXELS - 1)
        texels = atlas[atlas_row, atlas_column]
        coverage += weight * texels[..., 0]
        ink += weight * texels[..., 2]
    return coverage, ink


def weighted_fill(biome_weights, height, width, seed_base):
    fill = np.zeros((height, width, 3))
    for biome, weight in biome_weights:
        fill += weight[..., None] * make_fill(biome, height, width, seed_base + biome)
    return fill


def cliff_profile_height(outward_distance):
    # Cosine rounding, not a circle arc: a circle is vertical at its end, which smears one plan row over dozens of screen rows.
    top_t = np.clip(outward_distance / TOP_BEVEL_END, 0.0, 1.0)
    top_height = PLATEAU_HEIGHT - TOP_BEVEL_DROP * (1.0 - np.cos(top_t * math.pi / 2.0))
    face_t = np.clip((outward_distance - TOP_BEVEL_END) / (FACE_END - TOP_BEVEL_END), 0.0, 1.0)
    face_height = (PLATEAU_HEIGHT - TOP_BEVEL_DROP) + face_t * (FOOT_BEVEL_RISE - (PLATEAU_HEIGHT - TOP_BEVEL_DROP))
    foot_u = np.clip((FOOT_BEVEL_END - outward_distance) / (FOOT_BEVEL_END - FACE_END), 0.0, 1.0)
    foot_height = FOOT_BEVEL_RISE * (1.0 - np.cos(foot_u * math.pi / 2.0))
    return np.where(outward_distance <= TOP_BEVEL_END, top_height, np.where(outward_distance < FACE_END, face_height, foot_height))


def render_cliff_plan(corner_levels, top_weights, lower_weights):
    ppm = PIXELS_PER_METRE
    height_px, width_px = CLIFF_HEIGHT_METRES * ppm, CLIFF_WIDTH_METRES * ppm
    rows, columns = np.mgrid[0:height_px, 0:width_px]
    world_x = (columns + 0.5) / ppm
    world_z = CLIFF_HEIGHT_METRES - (rows + 0.5) / ppm
    cell_x, cell_z, case_index, local_x, local_z = sample_corner_grid(corner_levels, world_x, world_z)
    signed_distance = case_contour_signed_distance(case_index, local_x, local_z)
    outward = -signed_distance

    top_fill = weighted_fill(top_weights, height_px, width_px, 300)
    lower_fill = weighted_fill(lower_weights, height_px, width_px, 400)
    rim_coverage, rim_ink = weighted_atlas_sample(top_weights, "Rim", 100, cell_x, cell_z, case_index, local_x, local_z)
    foot_coverage, foot_ink = weighted_atlas_sample(lower_weights, "Foot", 200, cell_x, cell_z, 15 - case_index, local_x, local_z)

    # Fine, low-amplitude grain: broad noise gets stretched vertically by the gameplay projection and reads as a blur.
    rock_noise = (0.97 + 0.06 * value_noise(height_px, width_px, 6, 77))[..., None]
    top_t = np.clip(outward / TOP_BEVEL_END, 0.0, 1.0)[..., None]
    rim_rock = RIM_ROCK_COLOUR * (1.12 - 0.42 * top_t ** 2) * rock_noise
    foot_u = np.clip((FOOT_BEVEL_END - outward) / (FOOT_BEVEL_END - FACE_END), 0.0, 1.0)[..., None]
    foot_rock = FOOT_ROCK_COLOUR * (0.62 + 0.30 * (1.0 - foot_u)) * rock_noise
    shadow = (1.0 - 0.30 * np.exp(-np.maximum(outward - FOOT_BEVEL_END, 0.0) / 0.7))[..., None]

    ink_colour = np.array(INK) / 255.0
    rim_zone = outward <= TOP_BEVEL_END
    face_zone = (outward > TOP_BEVEL_END) & (outward < FACE_END)
    foot_zone = outward >= FACE_END
    rim_colour = np.where((rim_coverage > 0.5)[..., None], top_fill, rim_rock)
    rim_colour = rim_colour * (1 - np.clip(rim_ink, 0, 1)[..., None]) + ink_colour * np.clip(rim_ink, 0, 1)[..., None]
    foot_colour = np.where((foot_coverage > 0.5)[..., None], lower_fill * shadow, foot_rock)
    foot_colour = foot_colour * (1 - np.clip(foot_ink, 0, 1)[..., None]) + ink_colour * np.clip(foot_ink, 0, 1)[..., None]
    plan = np.where(rim_zone[..., None], rim_colour, foot_colour)
    plan = np.where(face_zone[..., None], ink_colour * 0.5 + FOOT_ROCK_COLOUR * 0.5, plan)
    return plan, outward, face_zone


def render_gameplay_view(plan, outward, face_zone, rock_seed):
    ppm = PIXELS_PER_METRE
    pitch = math.radians(CAMERA_PITCH_DEGREES)
    ground_scale, height_scale = math.sin(pitch) * ppm, math.cos(pitch) * ppm
    plan_height, plan_width = outward.shape
    base_row = CLIFF_HEIGHT_METRES * ground_scale + PLATEAU_HEIGHT * height_scale + 1.0
    screen_height = int(base_row) + 2
    heights = cliff_profile_height(outward)
    wall_top = PLATEAU_HEIGHT - TOP_BEVEL_DROP
    wall_pixels = int(round((wall_top - FOOT_BEVEL_RISE) * height_scale))
    rock, rock_ink, _ = rock_face_colour(plan_width, wall_pixels, 0, rock_seed)
    ink_colour = np.array(INK) / 255.0

    screen = np.zeros((screen_height, plan_width, 3))
    screen[:] = np.array([0.12, 0.12, 0.12])
    lowest_drawn_row = np.full(plan_width, screen_height, dtype=np.int64)
    previous_height = np.zeros(plan_width)
    column_index = np.arange(plan_width)
    for plan_row in range(plan_height - 1, -1, -1):
        world_z = CLIFF_HEIGHT_METRES - (plan_row + 0.5) / ppm
        row_heights = heights[plan_row]
        screen_rows = np.floor(base_row - world_z * ground_scale - row_heights * height_scale + 0.5).astype(np.int64)
        visible = screen_rows < lowest_drawn_row
        if not visible.any():
            continue
        gap = lowest_drawn_row - screen_rows
        for step in range(int(gap[visible].max())):
            active = visible & (gap > step)
            target_rows = lowest_drawn_row[active] - 1 - step
            columns = column_index[active]
            colour = plan[plan_row, active]
            # Wall-ness comes from the interpolated height, not the plan row: the wall spans only two plan rows,
            # so the rows it leaves in the gap would otherwise take a bevel or foot colour and smear.
            blend = (lowest_drawn_row[active] - target_rows) / np.maximum(gap[active], 1)
            height_here = previous_height[active] + blend * (row_heights[active] - previous_height[active])
            face_active = (height_here < wall_top - 1e-3) & (height_here > FOOT_BEVEL_RISE + 1e-3)
            if face_active.any():
                wall_fraction = np.clip((wall_top - height_here) / (wall_top - FOOT_BEVEL_RISE), 0.0, 1.0)
                texture_row = np.clip((wall_fraction * wall_pixels).astype(np.int64), 0, wall_pixels)
                face_colour = rock[texture_row, columns] * (1.0 - 0.22 * wall_fraction)[:, None]
                seam = rock_ink[texture_row, columns][:, None]
                face_colour = face_colour * (1 - seam) + ink_colour * seam
                colour = np.where(face_active[:, None], face_colour, colour)
            screen[target_rows, columns] = colour
        lowest_drawn_row[visible] = screen_rows[visible]
        previous_height[visible] = row_heights[visible]
    return screen[int(lowest_drawn_row.max()):]


def draw_plan_grid(plan):
    gridded = plan.copy()
    spacing = int(GRID_CELL_METRES * PIXELS_PER_METRE)
    gridded[::spacing, :] = gridded[::spacing, :] * 0.75 + 0.25
    gridded[:, ::spacing] = gridded[:, ::spacing] * 0.75 + 0.25
    return gridded


def build_corner_levels(predicate):
    cells_x, cells_z = int(CLIFF_WIDTH_METRES / GRID_CELL_METRES), int(CLIFF_HEIGHT_METRES / GRID_CELL_METRES)
    corner_z, corner_x = np.mgrid[0:cells_z + 1, 0:cells_x + 1]
    return predicate(corner_x, corner_z).astype(np.int64)


def save_cliff_capture(path, corner_levels, top_weights, lower_weights, caption):
    plan, outward, face_zone = render_cliff_plan(corner_levels, top_weights, lower_weights)
    gameplay = render_gameplay_view(plan, outward, face_zone, 11)
    plan_image = to_image(draw_plan_grid(plan))
    gameplay_image = to_image(gameplay)
    margin = 28
    canvas = Image.new("RGB", (plan_image.width + gameplay_image.width + 12, max(plan_image.height, gameplay_image.height) + margin), (28, 28, 30))
    canvas.paste(plan_image, (0, margin))
    canvas.paste(gameplay_image, (plan_image.width + 12, margin))
    draw = ImageDraw.Draw(canvas)
    draw.text((8, 8), "%s - plan view (2 m grid)" % caption, fill=(230, 230, 230))
    draw.text((plan_image.width + 20, 8), "gameplay angle (%d deg pitch)" % CAMERA_PITCH_DEGREES, fill=(230, 230, 230))
    canvas.save(path)
    print("wrote", path)


def cliff_edges_main(capture_dir):
    os.makedirs(capture_dir, exist_ok=True)
    ones = np.ones((CLIFF_HEIGHT_METRES * PIXELS_PER_METRE, CLIFF_WIDTH_METRES * PIXELS_PER_METRE))
    grass_only, grass_lower = [(GRASS, ones)], [(GRASS, ones)]

    straight = build_corner_levels(lambda corner_x, corner_z: corner_z >= 4)
    save_cliff_capture("%s/01_straight_rim.png" % capture_dir, straight, grass_only, grass_lower, "01 straight rim")

    notched = build_corner_levels(lambda corner_x, corner_z: (corner_x >= 2) & (corner_z >= 2) & ~((corner_x >= 6) & (corner_z <= 3)))
    save_cliff_capture("%s/02_outer_and_inner_corner.png" % capture_dir, notched, grass_only, grass_lower, "02 outer + inner corner")

    staircase = build_corner_levels(lambda corner_x, corner_z: (corner_x + corner_z >= 7) & (corner_z >= 1))
    save_cliff_capture("%s/03_staircase_45deg.png" % capture_dir, staircase, grass_only, grass_lower, "03 45 degree staircase")

    plan_height, plan_width = ones.shape
    rows, columns = np.mgrid[0:plan_height, 0:plan_width]
    world_x = (columns + 0.5) / PIXELS_PER_METRE
    world_z = CLIFF_HEIGHT_METRES - (rows + 0.5) / PIXELS_PER_METRE
    border_x = 10.0 + 0.35 * np.sin(world_z * 1.1)
    forest_weight = np.clip((world_x - border_x + 0.3) / 0.6, 0.0, 1.0)
    forest_weight = forest_weight * forest_weight * (3 - 2 * forest_weight)
    junction = [(GRASS, 1.0 - forest_weight), (FOREST, forest_weight)]
    save_cliff_capture("%s/04_grass_forest_junction.png" % capture_dir, straight, junction, junction, "04 grass/forest junction")


def main():
    if len(sys.argv) >= 3 and sys.argv[1] == "--cliff-edges":
        cliff_edges_main(sys.argv[2])
        return
    capture_dir = sys.argv[1]
    example_dir = sys.argv[2]
    packed_sets = {}
    for name, definition in EDGE_SETS.items():
        packed_set = PackedSet(name, definition)
        packed_sets[name] = packed_set
        for variant in range(packed_set.variant_count):
            strip = Image.new("RGBA", (CELL * 5, CELL), (0, 0, 0, 0))
            for piece_index, piece_name in enumerate(PIECES):
                strip.paste(packed_set.artist_pieces[(variant, piece_name)], (piece_index * CELL, 0))
            strip.save("%s/T_Edge_%s_v%d.png" % (example_dir, name, variant))
        for line in packed_set.validation_report():
            print("VALIDATION", line)
    for index, name in enumerate(EDGE_SETS):
        write_contact_sheet(packed_sets[name], "%s/%02d_set_%s.png" % (capture_dir, index + 1, name))
    biome_map_scene(packed_sets, capture_dir)
    cliff_scene(packed_sets, capture_dir)
    print("done")


if __name__ == "__main__":
    main()
