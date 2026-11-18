"""Convert a Shader Graph from the cel lighting nodes to the painted-ground SG_Lighting function.

    python convert_cel_to_painted_lighting.py <input.shadergraph> <output.shadergraph>
        [--oracle Assets/Shaders/Graphs/2DPaintedShader.shadergraph]

Swaps Lit for Unlit, drops the Lit-only blocks, replaces every StitchPunk.CelShadedLighting /
StitchPunk.CelShadedUniform ProviderNode with a clone of the oracle's SG_Lighting Custom Function
node, adds the seven lighting keywords and four painted-ground properties, then sweeps whatever
the removal orphaned. Everything is located by type / name / provider key, never by object id.
Nothing is written when the input has no cel node or is already converted.
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shadergraph_lib import ShaderGraph, new_object_id, new_serialized_guid  # noqa: E402

# provider key -> (positionWS slot, normalWS slot, colour output slot)
CEL_PROVIDER_SLOTS = {
    "StitchPunk.CelShadedLighting": (3, 4, 13),
    "StitchPunk.CelShadedUniform": (5, 6, 8),
}
LIGHTING_FUNCTION_NAME = "SG_Lighting"
KEPT_SURFACE_BLOCKS = {"SurfaceDescription.BaseColor", "SurfaceDescription.Alpha",
                       "SurfaceDescription.AlphaClipThreshold"}
LIGHTING_SLOT_POSITION_WS, LIGHTING_SLOT_NORMAL_WS, LIGHTING_SLOT_OUTPUT = 0, 1, 6
# oracle property override reference name -> custom function input slot id
PROPERTY_TO_LIGHTING_SLOT = {"_AmbientTint": 2, "_ShadowStrength": 3,
                             "_NdotLInfluence": 4, "_PointLightBoost": 5}


class ConversionError(Exception):
    pass


def provider_key_of(node):
    for ref in node.get("references", {}).get("RefIds", []):
        key = ref.get("data", {}).get("m_providerKey")
        if key:
            return key
    return None


def find_cel_nodes(graph):
    found = []
    for node in graph.objects:
        if node["m_Type"].endswith("ProviderNode") and provider_key_of(node) in CEL_PROVIDER_SLOTS:
            found.append(node)
    return found


def find_subtarget(graph):
    for node in graph.objects:
        if node["m_Type"].endswith("SubTarget"):
            return node
    raise ConversionError("input has no sub target object")


def find_lighting_function_node(graph):
    for node in graph.objects:
        if node["m_Type"].endswith("CustomFunctionNode") and node.get("m_FunctionName") == LIGHTING_FUNCTION_NAME:
            return node
    return None


def override_name(prop):
    return prop.get("m_OverrideReferenceName") or prop.get("m_DefaultReferenceName")


def clone_with_slots(source_graph, node):
    """Deep-copy a node and its slot objects with fresh object ids; returns (node, [slots])."""
    node_copy = source_graph.clone(node)
    slot_copies = []
    new_slot_refs = []
    for slot_ref in node["m_Slots"]:
        slot_copy = source_graph.clone(source_graph.by_id[slot_ref["m_Id"]])
        slot_copies.append(slot_copy)
        new_slot_refs.append({"m_Id": slot_copy["m_ObjectId"]})
    node_copy["m_Slots"] = new_slot_refs
    return node_copy, slot_copies


def register_with_slots(graph, node, slots):
    graph.register_node(node)
    for slot in slots:
        graph.register(slot)


def incoming_source(graph, node_id, slot_id):
    for edge in graph.edges():
        if edge["m_InputSlot"]["m_Node"]["m_Id"] == node_id and edge["m_InputSlot"]["m_SlotId"] == slot_id:
            return edge["m_OutputSlot"]["m_Node"]["m_Id"], edge["m_OutputSlot"]["m_SlotId"]
    return None


def convert(input_path, output_path, oracle_path):
    graph = ShaderGraph.load(input_path)
    oracle = ShaderGraph.load(oracle_path)

    cel_nodes = find_cel_nodes(graph)
    if not cel_nodes:
        raise ConversionError("no StitchPunk.CelShadedLighting / CelShadedUniform node found")
    if find_lighting_function_node(graph) is not None:
        raise ConversionError("graph already uses SG_Lighting")

    oracle_function = find_lighting_function_node(oracle)
    oracle_unlit = next((o for o in oracle.objects if o["m_Type"].endswith("UniversalUnlitSubTarget")), None)
    if oracle_function is None or oracle_unlit is None:
        raise ConversionError("oracle has no SG_Lighting node or Unlit sub target")

    # Which nodes fed a block (including the Lit-only ones) before, and which properties had nodes.
    reachable_before = graph.reachable_from_blocks()
    property_ids_with_nodes_before = {
        node["m_Property"]["m_Id"] for node in graph.objects
        if node["m_Type"].endswith("PropertyNode") and node["m_ObjectId"] in graph.by_id
    }

    # 1. sub target Lit -> Unlit (some graphs are already Unlit and keep theirs)
    old_subtarget = find_subtarget(graph)
    if not old_subtarget["m_Type"].endswith("UnlitSubTarget"):
        new_subtarget = graph.clone(oracle_unlit)
        graph.register(new_subtarget)
        target = next(o for o in graph.objects if o["m_Type"].endswith("UniversalTarget"))
        target["m_ActiveSubTarget"] = {"m_Id": new_subtarget["m_ObjectId"]}
        graph.mark_dirty(target)
        graph.remove_object(old_subtarget["m_ObjectId"])

    # 2. drop the Lit-only blocks from the fragment context
    fragment_blocks = graph.graph["m_FragmentContext"]["m_Blocks"]
    surviving_block_refs = []
    for block_ref in fragment_blocks:
        block = graph.by_id[block_ref["m_Id"]]
        if block["m_Type"].endswith("BlockNode") and block["m_Name"].startswith("SurfaceDescription.") \
                and block["m_Name"] not in KEPT_SURFACE_BLOCKS:
            graph.remove_node(block)
        else:
            surviving_block_refs.append(block_ref)
    graph.graph["m_FragmentContext"]["m_Blocks"] = surviving_block_refs

    # 3. keywords
    existing_keyword_names = {graph.by_id[ref["m_Id"]].get("m_Name") for ref in graph.graph["m_Keywords"]}
    for keyword_ref in oracle.graph["m_Keywords"]:
        oracle_keyword = oracle.by_id[keyword_ref["m_Id"]]
        if oracle_keyword["m_Name"] in existing_keyword_names:
            continue
        keyword = graph.clone(oracle_keyword)
        keyword["m_Guid"] = {"m_GuidSerialized": new_serialized_guid()}
        graph.register(keyword)
        graph.graph["m_Keywords"].append({"m_Id": keyword["m_ObjectId"]})
        graph.category["m_ChildObjectList"].append({"m_Id": keyword["m_ObjectId"]})
    graph.mark_dirty(graph.graph)
    graph.mark_dirty(graph.category)

    # 4. the four properties (reuse one that is already present) + one PropertyNode each
    property_node_for_slot = {}
    oracle_properties = {override_name(o): o for o in oracle.objects if "ShaderProperty" in o["m_Type"]}
    first_cel_position = cel_nodes[0]["m_DrawState"]["m_Position"]
    for row, (reference_name, lighting_slot) in enumerate(PROPERTY_TO_LIGHTING_SLOT.items()):
        oracle_property = oracle_properties[reference_name]
        existing = next((o for o in graph.objects if "ShaderProperty" in o["m_Type"]
                         and override_name(o) == reference_name and o["m_ObjectId"] not in graph.removed_ids), None)
        if existing is None:
            existing = graph.clone(oracle_property)
            existing["m_Guid"] = {"m_GuidSerialized": new_serialized_guid()}
            graph.register_property(existing)
        oracle_property_node = oracle.property_nodes_of(oracle_property["m_ObjectId"])[0]
        property_node, property_slots = clone_with_slots(oracle, oracle_property_node)
        property_node["m_Property"] = {"m_Id": existing["m_ObjectId"]}
        position = property_node["m_DrawState"]["m_Position"]
        position["x"] = first_cel_position["x"] - 420.0
        position["y"] = first_cel_position["y"] + 60.0 * row
        register_with_slots(graph, property_node, property_slots)
        property_node_for_slot[lighting_slot] = property_node

    # 5. replace each cel node
    for cel_node in cel_nodes:
        position_slot, normal_slot, color_slot = CEL_PROVIDER_SLOTS[provider_key_of(cel_node)]
        cel_id = cel_node["m_ObjectId"]
        position_source = incoming_source(graph, cel_id, position_slot)
        normal_source = incoming_source(graph, cel_id, normal_slot)
        output_targets = [(e["m_InputSlot"]["m_Node"]["m_Id"], e["m_InputSlot"]["m_SlotId"])
                          for e in graph.edges()
                          if e["m_OutputSlot"]["m_Node"]["m_Id"] == cel_id and e["m_OutputSlot"]["m_SlotId"] == color_slot]

        function_node, function_slots = clone_with_slots(oracle, oracle_function)
        function_node["m_DrawState"]["m_Position"] = dict(cel_node["m_DrawState"]["m_Position"])
        register_with_slots(graph, function_node, function_slots)
        function_id = function_node["m_ObjectId"]

        graph.remove_node(cel_node)
        if position_source:
            graph.add_edge(position_source[0], position_source[1], function_id, LIGHTING_SLOT_POSITION_WS)
        if normal_source:
            graph.add_edge(normal_source[0], normal_source[1], function_id, LIGHTING_SLOT_NORMAL_WS)
        for lighting_slot, property_node in property_node_for_slot.items():
            graph.add_edge(property_node["m_ObjectId"], 0, function_id, lighting_slot)
        for target_node_id, target_slot_id in output_targets:
            graph.add_edge(function_id, LIGHTING_SLOT_OUTPUT, target_node_id, target_slot_id)

    # 6. sweep nodes that fed a block before but no longer do, then properties that lost all their nodes
    reachable_after = graph.reachable_from_blocks()
    removed_properties = []
    for node in list(graph.objects):
        node_id = node["m_ObjectId"]
        if node_id in graph.removed_ids or node_id not in reachable_before or node_id in reachable_after:
            continue
        if node_id in {entry["m_Id"] for entry in graph.graph["m_Nodes"]}:
            graph.remove_node(node)
    surviving_property_ids = {
        node["m_Property"]["m_Id"] for node in list(graph.objects) + graph.new_objects
        if node["m_Type"].endswith("PropertyNode") and node["m_ObjectId"] not in graph.removed_ids
    }
    for property_id in property_ids_with_nodes_before - surviving_property_ids:
        property_object = graph.by_id[property_id]
        removed_properties.append(override_name(property_object))
        graph.graph["m_Properties"] = [e for e in graph.graph["m_Properties"] if e["m_Id"] != property_id]
        graph.category["m_ChildObjectList"] = [e for e in graph.category["m_ChildObjectList"] if e["m_Id"] != property_id]
        graph.remove_object(property_id)
    graph.mark_dirty(graph.graph)
    graph.mark_dirty(graph.category)

    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    graph.save(output_path)
    return removed_properties


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("input")
    parser.add_argument("output")
    parser.add_argument("--oracle", default="Assets/Shaders/Graphs/2DPaintedShader.shadergraph")
    arguments = parser.parse_args()
    try:
        removed = convert(arguments.input, arguments.output, arguments.oracle)
    except ConversionError as error:
        print("CONVERSION REFUSED:", error, file=sys.stderr)
        return 1
    print("converted ->", arguments.output)
    print("removed properties:", ", ".join(removed) if removed else "(none)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
