"""Helpers for parsing and editing Unity .shadergraph / .shadersubgraph files.

Unity MultiJson format: a sequence of JSON objects separated by blank lines,
cross-referencing each other via {"m_Id": "<m_ObjectId>"}.

Typical surgery script:

    from shadergraph_lib import ShaderGraph
    g = ShaderGraph.load(path)
    painterly = g.find_provider("Painterly Color")
    prop = g.clone(g.find_property("Smoothness"))   # then mutate + g.register(prop)
    g.add_edge(nodeA_id, 2, nodeB_id, 0)
    g.save(path)

Always back the file up first and run validate_shadergraph.py afterwards.
"""

import copy
import json
import re
import uuid


def new_object_id():
    return uuid.uuid4().hex


def new_serialized_guid():
    return str(uuid.uuid4())


class ShaderGraph:
    def __init__(self, chunks, objects):
        self.chunks = chunks              # original text chunks, index-aligned with objects
        self.objects = objects            # parsed dicts, file order
        self.by_id = {o["m_ObjectId"]: o for o in objects}
        self.dirty_ids = set()            # objects to re-serialize on save
        self.new_objects = []             # objects appended on save
        self.removed_ids = set()          # objects dropped on save
        self.graph = self._single("GraphData")
        self.category = self._single("CategoryData")

    # ---- load / save -----------------------------------------------------------

    @classmethod
    def load(cls, path):
        text = open(path, encoding="utf-8").read()
        chunks = [c for c in re.split(r"\n\s*\n", text) if c.strip()]
        objects = [json.loads(c) for c in chunks]
        return cls(chunks, objects)

    def save(self, path):
        final_chunks = []
        for i, chunk in enumerate(self.chunks):
            oid = self.objects[i]["m_ObjectId"]
            if oid in self.removed_ids:
                continue
            if oid in self.dirty_ids:
                final_chunks.append(json.dumps(self.objects[i], indent=4))
            else:
                final_chunks.append(chunk)
        for o in self.new_objects:
            final_chunks.append(json.dumps(o, indent=4))
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n\n".join(final_chunks) + "\n")

    # ---- lookup ----------------------------------------------------------------

    def _single(self, type_suffix):
        return next(o for o in self.objects if o["m_Type"].endswith(type_suffix))

    def find_provider(self, display_name):
        """A reflected-HLSL node placed in the graph, by its display name."""
        return next(o for o in self.objects
                    if o["m_Type"].endswith("ProviderNode") and o.get("m_Name") == display_name)

    def find_property(self, name):
        return next(o for o in self.objects
                    if "ShaderProperty" in o["m_Type"] and o.get("m_Name") == name)

    def property_nodes_of(self, property_object_id):
        return [o for o in self.objects
                if o["m_Type"].endswith("PropertyNode")
                and o.get("m_Property", {}).get("m_Id") == property_object_id]

    def find_nodes_by_type(self, type_suffix):
        return [o for o in self.objects if o["m_Type"].endswith(type_suffix)]

    def edges(self):
        return self.graph["m_Edges"]

    def sources_into(self, node_id, slot_id):
        """(sourceNodeObj, sourceSlotId) pairs feeding node_id's slot."""
        results = []
        for e in self.edges():
            if e["m_InputSlot"]["m_Node"]["m_Id"] == node_id and e["m_InputSlot"]["m_SlotId"] == slot_id:
                results.append((self.by_id[e["m_OutputSlot"]["m_Node"]["m_Id"]], e["m_OutputSlot"]["m_SlotId"]))
        return results

    # ---- mutation --------------------------------------------------------------

    def mark_dirty(self, obj):
        self.dirty_ids.add(obj["m_ObjectId"])

    def register(self, obj):
        """Append a brand-new object chunk (does NOT wire it anywhere)."""
        self.new_objects.append(obj)
        self.by_id[obj["m_ObjectId"]] = obj
        return obj

    def clone(self, template):
        """Deep-copy a live object with a fresh m_ObjectId; caller mutates + registers."""
        obj = copy.deepcopy(template)
        obj["m_ObjectId"] = new_object_id()
        return obj

    def register_node(self, node_obj):
        """register() + add to GraphData.m_Nodes."""
        self.register(node_obj)
        self.graph["m_Nodes"].append({"m_Id": node_obj["m_ObjectId"]})
        self.mark_dirty(self.graph)
        return node_obj

    def register_property(self, prop_obj):
        """register() + add to GraphData.m_Properties + blackboard category."""
        self.register(prop_obj)
        self.graph["m_Properties"].append({"m_Id": prop_obj["m_ObjectId"]})
        self.category["m_ChildObjectList"].append({"m_Id": prop_obj["m_ObjectId"]})
        self.mark_dirty(self.graph)
        self.mark_dirty(self.category)
        return prop_obj

    def add_edge(self, out_node_id, out_slot, in_node_id, in_slot):
        self.graph["m_Edges"].append({
            "m_OutputSlot": {"m_Node": {"m_Id": out_node_id}, "m_SlotId": out_slot},
            "m_InputSlot": {"m_Node": {"m_Id": in_node_id}, "m_SlotId": in_slot},
        })
        self.mark_dirty(self.graph)

    def remove_edges(self, predicate, expected=None):
        """Remove edges matching predicate(edge); assert count if expected given."""
        before = len(self.graph["m_Edges"])
        self.graph["m_Edges"] = [e for e in self.graph["m_Edges"] if not predicate(e)]
        removed = before - len(self.graph["m_Edges"])
        if expected is not None:
            assert removed == expected, f"expected to remove {expected} edges, removed {removed}"
        self.mark_dirty(self.graph)
        return removed

    def remap_node_slots(self, node_id, old_to_new):
        """Re-point every edge endpoint on node_id through an old->new slot-id map."""
        for e in self.graph["m_Edges"]:
            for end in (e["m_OutputSlot"], e["m_InputSlot"]):
                if end["m_Node"]["m_Id"] == node_id:
                    end["m_SlotId"] = old_to_new[end["m_SlotId"]]
        self.mark_dirty(self.graph)

    def remove_object(self, object_id):
        self.removed_ids.add(object_id)

    def remove_node(self, node_obj):
        """Drop a node, its slot objects, its m_Nodes entry, and its edges."""
        nid = node_obj["m_ObjectId"]
        self.graph["m_Nodes"] = [e for e in self.graph["m_Nodes"] if e["m_Id"] != nid]
        self.remove_edges(lambda e: e["m_OutputSlot"]["m_Node"]["m_Id"] == nid
                          or e["m_InputSlot"]["m_Node"]["m_Id"] == nid)
        self.remove_object(nid)
        for s in node_obj.get("m_Slots", []):
            self.remove_object(s["m_Id"])
        self.mark_dirty(self.graph)

    # ---- analysis ---------------------------------------------------------------

    def reachable_from_blocks(self):
        """Node ids that (transitively) feed any master-stack BlockNode."""
        block_ids = {o["m_ObjectId"] for o in self.objects if o["m_Type"].endswith("BlockNode")}
        incoming = {}
        for e in self.graph["m_Edges"]:
            incoming.setdefault(e["m_InputSlot"]["m_Node"]["m_Id"], []).append(
                e["m_OutputSlot"]["m_Node"]["m_Id"])
        reachable, frontier = set(block_ids), list(block_ids)
        while frontier:
            nid = frontier.pop()
            for src in incoming.get(nid, []):
                if src not in reachable:
                    reachable.add(src)
                    frontier.append(src)
        return reachable
