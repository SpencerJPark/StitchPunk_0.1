"""Generates Shaders/PaintedGround.shadergraph: a URP Unlit Shader Graph that reproduces
Painted/Ground using Custom Function nodes bound to PaintedGroundGraph.hlsl.
Serialization format copied from real Shader Graph 17.x files."""
import json, uuid, os

HLSL_GUID = "7f3a1c9e5b2d4e6f8a9b0c1d2e3f4a5b"     # must match PaintedGroundGraph.hlsl.meta
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Shaders", "PaintedGround.shadergraph")

objs = []          # all serialized objects, in order
def oid(): return uuid.uuid4().hex
def add(o): objs.append(o); return o["m_ObjectId"]

# ---------------------------------------------------------------- slots
def slot(kind, sid, name, is_output, value=None, stage=3, extra=None):
    o = {"m_SGVersion": 0, "m_Type": f"UnityEditor.ShaderGraph.{kind}", "m_ObjectId": oid(),
         "m_Id": sid, "m_DisplayName": name, "m_SlotType": 1 if is_output else 0, "m_Hidden": False,
         "m_ShaderOutputName": name.replace(" ", ""), "m_StageCapability": stage}
    if kind == "Vector1MaterialSlot":
        v = 0.0 if value is None else float(value)
        o.update({"m_Value": v, "m_DefaultValue": v, "m_Labels": []})
    elif kind == "Vector2MaterialSlot":
        v = value or (0, 0); d = {"x": float(v[0]), "y": float(v[1])}
        o.update({"m_Value": d, "m_DefaultValue": dict(d), "m_Labels": []})
    elif kind == "Vector3MaterialSlot":
        v = value or (0, 0, 0); d = {"x": float(v[0]), "y": float(v[1]), "z": float(v[2])}
        o.update({"m_Value": d, "m_DefaultValue": dict(d), "m_Labels": []})
    elif kind == "Vector4MaterialSlot":
        v = value or (0, 0, 0, 0); d = {"x": float(v[0]), "y": float(v[1]), "z": float(v[2]), "w": float(v[3])}
        o.update({"m_Value": d, "m_DefaultValue": dict(d), "m_Labels": []})
    elif kind == "Texture2DInputMaterialSlot":
        o.update({"m_BareResource": False, "m_Texture": {"m_SerializedTexture": "", "m_Guid": ""}, "m_DefaultType": 0})
    elif kind == "Texture2DMaterialSlot":
        o.update({"m_BareResource": False})
    if extra: o.update(extra)
    return o

# ---------------------------------------------------------------- nodes
def node(ntype, name, slots, x, y, w=220, h=200, extra=None, sgv=0):
    ids = [add(s) for s in slots]
    o = {"m_SGVersion": sgv, "m_Type": f"UnityEditor.ShaderGraph.{ntype}", "m_ObjectId": oid(),
         "m_Group": {"m_Id": ""}, "m_Name": name,
         "m_DrawState": {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": float(x), "y": float(y), "width": float(w), "height": float(h)}},
         "m_Slots": [{"m_Id": i} for i in ids], "synonyms": [], "m_Precision": 0, "m_PreviewExpanded": False,
         "m_DismissedVersion": 0, "m_PreviewMode": 0, "m_CustomColors": {"m_SerializableColors": []}}
    if extra: o.update(extra)
    add(o)
    return o["m_ObjectId"]

edges = []
def edge(out_node, out_slot, in_node, in_slot):
    edges.append({"m_OutputSlot": {"m_Node": {"m_Id": out_node}, "m_SlotId": out_slot},
                  "m_InputSlot": {"m_Node": {"m_Id": in_node}, "m_SlotId": in_slot}})

# ---------------------------------------------------------------- properties
props = []
def prop(kind, name, ref, value, extra=None):
    base = {"m_SGVersion": 1, "m_Type": f"UnityEditor.ShaderGraph.Internal.{kind}", "m_ObjectId": oid(),
            "m_Guid": {"m_GuidSerialized": str(uuid.uuid4())}, "m_Name": name, "m_DefaultRefNameVersion": 1,
            "m_RefNameGeneratedByDisplayName": name, "m_DefaultReferenceName": "_" + name.replace(" ", "_"),
            "m_OverrideReferenceName": ref, "m_GeneratePropertyBlock": True, "m_UseCustomSlotLabel": False,
            "m_CustomSlotLabel": "", "m_DismissedVersion": 0, "m_Precision": 0, "overrideHLSLDeclaration": False,
            "hlslDeclarationOverride": 0, "m_Hidden": False}
    if kind == "Texture2DShaderProperty":
        base.update({"m_SGVersion": 0, "m_Value": {"m_SerializedTexture": "", "m_Guid": ""}, "isMainTexture": False,
                     "useTilingAndOffset": False, "useTexelSize": False, "m_Modifiable": True, "m_DefaultType": 0})
    elif kind == "Vector1ShaderProperty":
        base.update({"m_Value": float(value), "m_FloatType": 0, "m_RangeValues": {"x": 0.0, "y": 1.0}})
        if extra: base.update(extra)
    elif kind == "Vector4ShaderProperty":
        base.update({"m_Value": {"x": value[0], "y": value[1], "z": value[2], "w": value[3]}})
    elif kind == "ColorShaderProperty":
        base.update({"m_SGVersion": 3, "m_Value": {"r": value[0], "g": value[1], "b": value[2], "a": value[3]}, "isMainColor": False, "m_ColorMode": 0})
    add(base); props.append(base["m_ObjectId"]); return base

def prop_node(p, x, y):
    kind = p["m_Type"].rsplit(".", 1)[1]
    if kind == "Texture2DShaderProperty": s = slot("Texture2DMaterialSlot", 0, p["m_Name"], True)
    elif kind == "Vector1ShaderProperty": s = slot("Vector1MaterialSlot", 0, p["m_Name"], True)
    else: s = slot("Vector4MaterialSlot", 0, p["m_Name"], True)
    s["m_ShaderOutputName"] = "Out"
    return node("PropertyNode", "Property", [s], x, y, 160, 40, {"m_Property": {"m_Id": p["m_ObjectId"]}})

# ---------------------------------------------------------------- custom function node
def cf(fname, inputs, outputs, x, y):
    """inputs/outputs: list of (kind, name, default)"""
    slots = []; sid = 0
    for kind, name, dflt in inputs:
        slots.append(slot(kind, sid, name, False, dflt)); sid += 1
    for kind, name, dflt in outputs:
        slots.append(slot(kind, sid, name, True, dflt)); sid += 1
    n = node("CustomFunctionNode", f"{fname} (Custom Function)", slots, x, y, 260, 40 + 26 * len(slots),
             {"m_SourceType": 0, "m_FunctionName": fname, "m_FunctionSource": HLSL_GUID,
              "m_FunctionSourceUsePragmas": True, "m_FunctionBody": "Enter function body here..."}, sgv=1)
    return n

V1, V2, V3, V4, TEX = "Vector1MaterialSlot", "Vector2MaterialSlot", "Vector3MaterialSlot", "Vector4MaterialSlot", "Texture2DInputMaterialSlot"

# ================================================================ build
# properties (reference names match Painted/Ground so the same textures drop in)
P = {}
P["BaseTex"]   = prop("Texture2DShaderProperty", "Base Layer (dirt)", "_BaseTex", None)
P["LayerR"]    = prop("Texture2DShaderProperty", "R Layer (grass)", "_LayerRTex", None)
P["LayerG"]    = prop("Texture2DShaderProperty", "G Layer (forest)", "_LayerGTex", None)
P["LayerB"]    = prop("Texture2DShaderProperty", "B Layer (stone)", "_LayerBTex", None)
P["Cliff"]     = prop("Texture2DShaderProperty", "Cliff", "_CliffTex", None)
P["Macro"]     = prop("Texture2DShaderProperty", "Macro Noise", "_MacroTex", None)
P["Brush"]     = prop("Texture2DShaderProperty", "Brush Noise", "_BlendNoiseTex", None)
P["Fringe"]    = prop("Texture2DShaderProperty", "Edge Fringe Strip", "_FringeTex", None)
P["Splat"]     = prop("Texture2DShaderProperty", "World Splat Map", "_SplatMap", None)
P["TileSizes"] = prop("Vector4ShaderProperty", "Tile Sizes (Base R G B)", "_TileSizes", (12, 10, 14, 12))
P["SplatBounds"] = prop("Vector4ShaderProperty", "Splat World Rect", "_SplatBounds", (-128, -128, 256, 256))
def f(name, ref, v, lo=None, hi=None):
    extra = None
    if lo is not None: extra = {"m_FloatType": 1, "m_RangeValues": {"x": float(lo), "y": float(hi)}}
    return prop("Vector1ShaderProperty", name, ref, v, extra)
P["SecondScale"]  = f("Second Sample Scale", "_SecondScale", 0.61, 0.2, 1.5)
P["SecondBlend"]  = f("Second Sample Amount", "_SecondBlend", 1.0, 0, 1)
P["PatchSize"]    = f("Patch Size (m)", "_PatchSize", 35)
P["MacroSize"]    = f("Macro Size (m)", "_MacroSize", 140)
P["MacroStrength"]= f("Macro Strength", "_MacroStrength", 0.6, 0, 1)
P["BrushSize"]    = f("Brush Noise Size (m)", "_BlendNoiseSize", 7)
P["Wobble"]       = f("Edge Wobble", "_BlendNoiseAmount", 0.25, 0, 1)
P["FringeSize"]    = f("Fringe Size (m)", "_FringeSize", 3.0, 0.5, 14)
P["FringeStretch"] = f("Fringe Stretch", "_FringeStretch", 1.0, 0.5, 2)
P["GradStep"]     = f("Edge Direction Step (m)", "_SplatGradStep", 0.35, 0.05, 2)
P["CliffTile"]    = f("Cliff Tile Size (m)", "_CliffTileSize", 6)
P["CliffStart"]   = f("Cliff Start (normal.y)", "_CliffStart", 0.45, 0, 1)
P["CliffEnd"]     = f("Cliff End (normal.y)", "_CliffEnd", 0.70, 0, 1)
P["ShadowStr"]    = f("Shadow Strength", "_ShadowStrength", 0.6, 0, 1)
P["NdotL"]        = f("N.L Influence", "_NdotLInfluence", 0.15, 0, 1)
P["PointBoost"]   = f("Point Light Boost", "_PointLightBoost", 1.0, 0, 4)
P["MacroDark"]  = prop("ColorShaderProperty", "Macro Dark Tint", "_MacroDark", (0.36, 0.39, 0.45, 1))
P["MacroLight"] = prop("ColorShaderProperty", "Macro Light Tint", "_MacroLight", (0.58, 0.55, 0.48, 1))
P["RimColor"]   = prop("ColorShaderProperty", "Highlight Rim", "_RimColor", (0.95, 0.92, 0.80, 0.55))
P["InkColor"]   = prop("ColorShaderProperty", "Ink Line", "_InkColor", (0.12, 0.10, 0.12, 0.75))
P["Ambient"]    = prop("ColorShaderProperty", "Ambient Tint", "_AmbientTint", (1, 1, 1, 1))

# keywords URP needs for the lighting function
keywords = []
for kw in ["MAIN_LIGHT_SHADOWS", "MAIN_LIGHT_SHADOWS_CASCADE", "MAIN_LIGHT_SHADOWS_SCREEN",
           "ADDITIONAL_LIGHTS", "ADDITIONAL_LIGHT_SHADOWS", "CLUSTER_LIGHT_LOOP", "SHADOWS_SOFT"]:
    k = {"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.ShaderKeyword", "m_ObjectId": oid(),
         "m_Guid": {"m_GuidSerialized": str(uuid.uuid4())}, "m_Name": kw, "m_DefaultRefNameVersion": 1,
         "m_RefNameGeneratedByDisplayName": kw, "m_DefaultReferenceName": "_" + kw, "m_OverrideReferenceName": "",
         "m_GeneratePropertyBlock": True, "m_UseCustomSlotLabel": False, "m_CustomSlotLabel": "", "m_DismissedVersion": 0,
         "m_KeywordType": 0, "m_KeywordDefinition": 1, "m_KeywordScope": 1, "m_KeywordStages": 63, "m_Entries": [],
         "m_Value": 0, "m_IsEditable": True}
    add(k); keywords.append(k["m_ObjectId"])

category = {"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.CategoryData", "m_ObjectId": oid(), "m_Name": "",
            "m_ChildObjectList": [{"m_Id": i} for i in props + keywords]}
add(category)

# ---- source nodes
X0 = -2200
posN = node("PositionNode", "Position", [slot(V3, 0, "Out", True)], X0, -400, 206, 130, {"m_Space": 2, "m_PositionSource": 0}, sgv=1)
nrmN = node("NormalVectorNode", "Normal Vector", [slot(V3, 0, "Out", True)], X0, -200, 206, 130, {"m_Space": 2})

# property nodes, laid out in a column
pn = {}
y = 0
for key in P:
    pn[key] = prop_node(P[key], X0, y); y += 50

# ---- custom function nodes
X1, X2, X3, X4, X5 = -1700, -1250, -800, -350, 100
noise = cf("SG_Noise", [(TEX, "macro", None), (TEX, "brush", None), (V3, "posWS", None), (V1, "macroSize", 140), (V1, "patchSize", 35), (V1, "brushSize", 7), (V1, "secondBlend", 1)],
           [(V1, "macroNoise", 0), (V1, "patchMask", 0), (V3, "brushNoise", None)], X1, -600)
splat = cf("SG_SplatGradient", [(TEX, "splat", None), (V3, "posWS", None), (V4, "bounds", (-128, -128, 256, 256)), (V1, "step", 0.35)],
           [(V3, "weights", None), (V2, "gradR", None), (V2, "gradG", None), (V2, "gradB", None)], X1, -250)
layers = {}
for i, (key, ty) in enumerate([("BaseTex", "x"), ("LayerR", "y"), ("LayerG", "z"), ("LayerB", "w")]):
    layers[key] = cf("SG_Layer", [(TEX, "tex", None), (V3, "posWS", None), (V1, "tileSize", 12), (V1, "secondScale", 0.61), (V1, "patchMask", 0)],
                     [(V3, "col", None)], X1, 100 + i * 230)
cliff = cf("SG_Cliff", [(TEX, "cliff", None), (V3, "posWS", None), (V3, "normalWS", (0, 1, 0)), (V1, "tileSize", 6), (V1, "cliffStart", 0.45), (V1, "cliffEnd", 0.7)],
           [(V3, "cliffCol", None), (V1, "steep", 0), (V2, "grad", None)], X1, 1050)

# split nodes for weights, brush noise, tile sizes
def split(x, y):
    dv = {"m_Value": {"x": 0, "y": 0, "z": 0, "w": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0, "w": 0}}
    return node("SplitNode", "Split", [slot("DynamicVectorMaterialSlot", 0, "In", False, extra=dv), slot(V1, 1, "R", True), slot(V1, 2, "G", True), slot(V1, 3, "B", True), slot(V1, 4, "A", True)], x, y, 120, 140)
wSplit = split(X2, -250); bSplit = split(X2, -600); tSplit = split(X2, 100)

fr_in = lambda row: [(TEX, "fringe", None), (V3, "posWS", None), (V1, "weight", 0), (V2, "grad", None), (V1, "row", row), (V1, "rows", 4),
                     (V1, "sizeM", 3), (V1, "stretch", 1), (V1, "wobble", 0.5), (V1, "wobbleAmount", 0.25),
                     (V3, "layerCol", None), (V3, "colIn", None), (V1, "inkIn", 0), (V1, "rimIn", 0)]
fr_out = [(V3, "colOut", None), (V1, "inkOut", 0), (V1, "rimOut", 0)]
frR = cf("SG_Fringe", fr_in(0), fr_out, X3, -500)
frG = cf("SG_Fringe", fr_in(1), fr_out, X3, 50)
frB = cf("SG_Fringe", fr_in(2), fr_out, X4, -500)
frC = cf("SG_Fringe", fr_in(3), fr_out, X4, 50)
finish = cf("SG_Finish", [(V3, "col", None), (V1, "macroNoise", 0.5), (V3, "macroDark", (0.36, 0.39, 0.45)), (V3, "macroLight", (0.58, 0.55, 0.48)), (V1, "macroStrength", 0.6),
                          (V1, "rim", 0), (V4, "rimColor", (0.95, 0.92, 0.8, 0.55)), (V1, "ink", 0), (V4, "inkColor", (0.12, 0.1, 0.12, 0.75))],
            [(V3, "outCol", None)], X5, -500)
light = cf("SG_Lighting", [(V3, "posWS", None), (V3, "normalWS", (0, 1, 0)), (V3, "ambientTint", (1, 1, 1)), (V1, "shadowStrength", 0.6), (V1, "ndotlInfluence", 0.15), (V1, "pointBoost", 1)],
           [(V3, "lighting", None)], X5, 0)
mul = node("MultiplyNode", "Multiply", [slot("DynamicVectorMaterialSlot", 0, "A", False, extra={"m_Value": {"x": 0, "y": 0, "z": 0, "w": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0, "w": 0}}),
                                        slot("DynamicVectorMaterialSlot", 1, "B", False, extra={"m_Value": {"x": 2, "y": 2, "z": 2, "w": 2}, "m_DefaultValue": {"x": 2, "y": 2, "z": 2, "w": 2}}),
                                        slot("DynamicVectorMaterialSlot", 2, "Out", True, extra={"m_Value": {"x": 0, "y": 0, "z": 0, "w": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0, "w": 0}})],
           X5 + 350, -250, 130, 120)

# ---- blocks
def block(desc, s):
    return node("BlockNode", desc, [s], 0, 0, 0, 0, {"m_SerializedDescriptor": desc})
vPos = block("VertexDescription.Position", slot("PositionMaterialSlot", 0, "Position", False, stage=1, extra={"m_Value": {"x": 0, "y": 0, "z": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0}, "m_Labels": [], "m_Space": 0}))
vNrm = block("VertexDescription.Normal", slot("NormalMaterialSlot", 0, "Normal", False, stage=1, extra={"m_Value": {"x": 0, "y": 0, "z": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0}, "m_Labels": [], "m_Space": 0}))
vTan = block("VertexDescription.Tangent", slot("TangentMaterialSlot", 0, "Tangent", False, stage=1, extra={"m_Value": {"x": 0, "y": 0, "z": 0}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0}, "m_Labels": [], "m_Space": 0}))
fCol = block("SurfaceDescription.BaseColor", slot("ColorRGBMaterialSlot", 0, "Base Color", False, stage=2, extra={"m_Value": {"x": 0.5, "y": 0.5, "z": 0.5}, "m_DefaultValue": {"x": 0, "y": 0, "z": 0}, "m_Labels": [], "m_ColorMode": 0, "m_DefaultColor": {"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0}}))
fAlp = block("SurfaceDescription.Alpha", slot(V1, 0, "Alpha", False, 1.0, stage=2))

# ---- edges
E = edge
# noise
E(pn["Macro"], 0, noise, 0); E(pn["Brush"], 0, noise, 1); E(posN, 0, noise, 2)
E(pn["MacroSize"], 0, noise, 3); E(pn["PatchSize"], 0, noise, 4); E(pn["BrushSize"], 0, noise, 5); E(pn["SecondBlend"], 0, noise, 6)
E(noise, 9, bSplit, 0)
# splat
E(pn["Splat"], 0, splat, 0); E(posN, 0, splat, 1); E(pn["SplatBounds"], 0, splat, 2); E(pn["GradStep"], 0, splat, 3)
E(splat, 4, wSplit, 0)
E(pn["TileSizes"], 0, tSplit, 0)
# layers
for key, ts in [("BaseTex", 1), ("LayerR", 2), ("LayerG", 3), ("LayerB", 4)]:
    n = layers[key]
    E(pn[key], 0, n, 0); E(posN, 0, n, 1); E(tSplit, ts, n, 2); E(pn["SecondScale"], 0, n, 3); E(noise, 8, n, 4)
# cliff
E(pn["Cliff"], 0, cliff, 0); E(posN, 0, cliff, 1); E(nrmN, 0, cliff, 2); E(pn["CliffTile"], 0, cliff, 3); E(pn["CliffStart"], 0, cliff, 4); E(pn["CliffEnd"], 0, cliff, 5)
# fringes: common inputs
def fringe_common(n, wslot, gnode, gslot, bslot, layerNode, layerSlot, prev):
    E(pn["Fringe"], 0, n, 0); E(posN, 0, n, 1)
    if wslot is not None: E(wSplit, wslot, n, 2)
    E(gnode, gslot, n, 3)
    E(pn["FringeSize"], 0, n, 6); E(pn["FringeStretch"], 0, n, 7); E(bSplit, bslot, n, 8); E(pn["Wobble"], 0, n, 9)
    E(layerNode, layerSlot, n, 10)
    if prev is not None:
        E(prev, 14, n, 11); E(prev, 15, n, 12); E(prev, 16, n, 13)
fringe_common(frR, 1, splat, 5, 1, layers["LayerR"], 5, None); E(layers["BaseTex"], 5, frR, 11)
fringe_common(frG, 2, splat, 6, 2, layers["LayerG"], 5, frR)
fringe_common(frB, 3, splat, 7, 3, layers["LayerB"], 5, frG)
fringe_common(frC, None, cliff, 8, 1, cliff, 6, frB); E(cliff, 7, frC, 2)
# finish
E(frC, 14, finish, 0); E(noise, 7, finish, 1); E(pn["MacroDark"], 0, finish, 2); E(pn["MacroLight"], 0, finish, 3); E(pn["MacroStrength"], 0, finish, 4)
E(frC, 16, finish, 5); E(pn["RimColor"], 0, finish, 6); E(frC, 15, finish, 7); E(pn["InkColor"], 0, finish, 8)
# lighting
E(posN, 0, light, 0); E(nrmN, 0, light, 1); E(pn["Ambient"], 0, light, 2); E(pn["ShadowStr"], 0, light, 3); E(pn["NdotL"], 0, light, 4); E(pn["PointBoost"], 0, light, 5)
E(finish, 9, mul, 0); E(light, 6, mul, 1); E(mul, 2, fCol, 0)

# ---- target
sub = {"m_SGVersion": 2, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget", "m_ObjectId": oid()}
add(sub)
target = {"m_SGVersion": 1, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget", "m_ObjectId": oid(), "m_Datas": [],
          "m_ActiveSubTarget": {"m_Id": sub["m_ObjectId"]}, "m_AllowMaterialOverride": False, "m_SurfaceType": 0, "m_ZTestMode": 4,
          "m_ZWriteControl": 0, "m_AlphaMode": 0, "m_RenderFace": 2, "m_AlphaClip": False, "m_CastShadows": True, "m_ReceiveShadows": True,
          "m_DisableTint": False, "m_AdditionalMotionVectorMode": 0, "m_AlembicMotionVectors": False, "m_SupportsLODCrossFade": False,
          "m_CustomEditorGUI": "", "m_SupportVFX": False}
add(target)

# ---- graph data
node_ids = [o["m_ObjectId"] for o in objs if o["m_Type"].endswith("Node") and o["m_Type"] != "UnityEditor.ShaderGraph.BlockNode"] + [vPos, vNrm, vTan, fCol, fAlp]
graph = {"m_SGVersion": 3, "m_Type": "UnityEditor.ShaderGraph.GraphData", "m_ObjectId": oid(),
         "m_Properties": [{"m_Id": i} for i in props], "m_Keywords": [{"m_Id": i} for i in keywords], "m_Dropdowns": [],
         "m_CategoryData": [{"m_Id": category["m_ObjectId"]}],
         "m_Nodes": [{"m_Id": i} for i in node_ids], "m_GroupDatas": [], "m_StickyNoteDatas": [], "m_Edges": edges,
         "m_VertexContext": {"m_Position": {"x": 900.0, "y": -400.0}, "m_Blocks": [{"m_Id": vPos}, {"m_Id": vNrm}, {"m_Id": vTan}]},
         "m_FragmentContext": {"m_Position": {"x": 900.0, "y": -150.0}, "m_Blocks": [{"m_Id": fCol}, {"m_Id": fAlp}]},
         "m_PreviewData": {"serializedMesh": {"m_SerializedMesh": "{\"mesh\":{\"instanceID\":0}}", "m_Guid": ""}, "preventRotation": False},
         "m_Path": "Painted", "m_GraphPrecision": 0, "m_PreviewMode": 2, "m_OutputNode": {"m_Id": ""}, "m_SubDatas": [],
         "m_ActiveTargets": [{"m_Id": target["m_ObjectId"]}]}

with open(OUT, "w") as fh:
    fh.write(json.dumps(graph, indent=4) + "\n\n")
    for o in objs:
        fh.write(json.dumps(o, indent=4) + "\n\n")
print("wrote", os.path.abspath(OUT), len(objs), "objects,", len(edges), "edges")
