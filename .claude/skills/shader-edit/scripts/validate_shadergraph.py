"""Referential-integrity validator for Unity .shadergraph / .shadersubgraph files.

Run after EVERY programmatic graph edit; a graph that prints ALL CLEAN has
imported successfully in practice, while dangling references produce importer
NullReferenceExceptions with no useful message.

    python validate_shadergraph.py <path-to-shadergraph>

Checks: every chunk parses; no duplicate m_ObjectIds; every m_Nodes /
m_Properties / category / m_Slots / m_Property reference resolves; slot ids
unique per node; every edge endpoint resolves to an existing node AND slot.
"""

import json
import re
import sys


def validate(path):
    text = open(path, encoding="utf-8").read()
    chunks = [c for c in re.split(r"\n\s*\n", text) if c.strip()]
    objects, by_id, fails = [], {}, 0

    for chunk in chunks:
        try:
            obj = json.loads(chunk)
        except Exception as parse_error:
            print("PARSE FAIL:", str(parse_error)[:120])
            fails += 1
            continue
        objects.append(obj)
        if obj["m_ObjectId"] in by_id:
            print("DUPLICATE m_ObjectId:", obj["m_ObjectId"])
            fails += 1
        by_id[obj["m_ObjectId"]] = obj

    graph = next(o for o in objects if o["m_Type"].endswith("GraphData"))
    categories = [o for o in objects if o["m_Type"].endswith("CategoryData")]

    reference_lists = [("m_Nodes", graph.get("m_Nodes", [])),
                       ("m_Properties", graph.get("m_Properties", [])),
                       ("m_Keywords", graph.get("m_Keywords", []))]
    for cat in categories:
        reference_lists.append(("category", cat.get("m_ChildObjectList", [])))
    for list_name, entries in reference_lists:
        for entry in entries:
            if entry["m_Id"] not in by_id:
                print(f"MISSING {list_name} reference: {entry['m_Id']}")
                fails += 1

    slot_map = {}
    for entry in graph.get("m_Nodes", []):
        node = by_id.get(entry["m_Id"])
        if node is None:
            continue
        seen_slot_ids = set()
        for slot_ref in node.get("m_Slots", []):
            slot = by_id.get(slot_ref["m_Id"])
            if slot is None:
                print(f"MISSING slot object on node '{node.get('m_Name')}': {slot_ref['m_Id']}")
                fails += 1
                continue
            if slot["m_Id"] in seen_slot_ids:
                print(f"DUPLICATE slot id {slot['m_Id']} on node '{node.get('m_Name')}'")
                fails += 1
            seen_slot_ids.add(slot["m_Id"])
        slot_map[node["m_ObjectId"]] = seen_slot_ids
        if node["m_Type"].endswith("PropertyNode"):
            if node.get("m_Property", {}).get("m_Id") not in by_id:
                print(f"PropertyNode '{node.get('m_Name')}' has a dangling property reference")
                fails += 1

    for edge in graph.get("m_Edges", []):
        for end, kind in ((edge["m_OutputSlot"], "output"), (edge["m_InputSlot"], "input")):
            node_id, slot_id = end["m_Node"]["m_Id"], end["m_SlotId"]
            if node_id not in by_id:
                print(f"EDGE {kind}: node {node_id} does not exist")
                fails += 1
            elif slot_id not in slot_map.get(node_id, set()):
                print(f"EDGE {kind}: node '{by_id[node_id].get('m_Name')}' has no slot {slot_id}")
                fails += 1

    print(f"objects: {len(objects)} | nodes: {len(graph.get('m_Nodes', []))} | "
          f"edges: {len(graph.get('m_Edges', []))} | properties: {len(graph.get('m_Properties', []))}")
    print("VALIDATION:", "FAILED" if fails else "ALL CLEAN", f"({fails} problems)")
    return fails


if __name__ == "__main__":
    sys.exit(1 if validate(sys.argv[1]) else 0)
