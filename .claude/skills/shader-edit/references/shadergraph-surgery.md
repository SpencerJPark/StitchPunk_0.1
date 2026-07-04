# Programmatic .shadergraph editing ("graph surgery")

`.shadergraph` / `.shadersubgraph` files are a sequence of JSON objects
separated by blank lines (Unity MultiJson). Every object has `m_Type` and a
32-hex `m_ObjectId`; objects reference each other by `{"m_Id": "<objectId>"}`.
File order doesn't matter (GraphData conventionally first). This is fully
hand-editable — hand-built JSON that passes referential validation has
imported cleanly in practice.

**Workflow (always):**
1. `cp` the graph to the scratchpad as a `.bak`.
2. Write a Python script using `scripts/shadergraph_lib.py` (parse, locate by
   name/wiring — NEVER hardcode object ids from a previous session, the user
   edits graphs in the editor between sessions).
3. Mutate, write back.
4. `python scripts/validate_shadergraph.py <graph>` — must print ALL CLEAN.
5. User focuses Unity; check Console; user opens graph to sanity-check layout.

## Object anatomy (field shapes verified in this project)

### GraphData (the spine)
Keys that matter: `m_Nodes` (list of `{m_Id}`), `m_Edges`, `m_Properties`
(list of `{m_Id}`), `m_Keywords`, `m_CategoryData`. Registering a node =
append to `m_Nodes` AND emit its object chunk. Registering a property =
append to `m_Properties` AND to the CategoryData's `m_ChildObjectList`
(otherwise it won't show on the blackboard).

### Edges
```json
{"m_OutputSlot": {"m_Node": {"m_Id": "<nodeId>"}, "m_SlotId": 2},
 "m_InputSlot":  {"m_Node": {"m_Id": "<nodeId>"}, "m_SlotId": 0}}
```
Slot ids are per-node integers (see slot numbering below). Dynamic-vector
ports auto-adapt widths (vec3 output → vec2 input truncates, float broadcasts).

### ProviderNode (a reflected HLSL node placed in a graph)
The critical fields beyond the common node ones (`m_ObjectId`, `m_Name`,
`m_DrawState.m_Position`, `m_Slots` list of `{m_Id}` refs):
```json
"m_provider": {"rid": 1000},
"references": {"version": 2, "RefIds": [{
    "rid": 1000,
    "type": {"class": "ReflectedFunctionProvider",
             "ns": "UnityEditor.ShaderGraph.ProviderSystem",
             "asm": "Unity.ShaderGraph.Editor"},
    "data": {"m_providerKey": "StitchPunk.<NodeName>",
             "m_sourceAssetId": "<GUID of the source .hlsl>"}}]}
```
Clone an existing ProviderNode (any graph in `Graphs/` has Cel Shaded
Lighting) and swap name/synonyms/slots/providerKey/sourceAssetId.

### Slot numbering
Reflected function ports number **sequentially from 1 in HLSL parameter
order**, `out` params included (a 12-in/1-out void function = slots 1..13).
Each slot is its own JSON object; `m_Id` = the integer slot id,
`m_SlotType` 0 = input, 1 = output, `m_ShaderOutputName` = the HLSL param name
(codegen breaks if wrong), `m_DisplayName` = UI label.

### Slot type ↔ hint mapping
| HLSL param | Hint | Slot m_Type |
|---|---|---|
| float + `<sg:Range>` | slider | `Vector1MaterialRangeSlot` (has `m_sliderRange`) |
| float plain | | `Vector1MaterialSlot` |
| float3 + `<sg:Color/>` | color | `ColorRGBMaterialSlot` (has `m_ColorMode`, `m_DefaultColor`) |
| float3 plain | | `Vector3MaterialSlot` |
| float4 | | `Vector4MaterialSlot` |

Always deep-copy a live slot object of the right type from the same file and
mutate — never write slot JSON from memory (they carry version-specific
fields like `m_StageCapability`, `m_SliderPower`).

### Properties (blackboard)
`Vector1ShaderProperty` (float; `m_FloatType` 1 = slider with `m_RangeValues`,
0 = plain field) and `ColorShaderProperty`. Both need a fresh `m_ObjectId` AND
a fresh `m_Guid.m_GuidSerialized` (dashed UUID). `m_DefaultReferenceName` =
`_PropName` (what lands in the .mat). A property is *used* via a
`PropertyNode` whose `m_Property.m_Id` points at it and which has one output
slot (Vector1MaterialSlot for floats; clone the slot from an existing color
PropertyNode for colors).

### Built-in nodes
Clone from a live instance in any graph (MultiplyNode, AddNode,
SampleTexture2DNode…). Most built-ins regenerate their slots on
deserialization, so a plausible clone with fresh ids for node + slots is
enough. For a node type with no live instance (e.g. ObjectNode), serialize
minimally: correct `m_Type`, one output slot with the right slot id —
`UpdateNodeAfterDeserialization` fills in the rest.

## Signature change = slot rebuild

When a node's HLSL parameter list changes:
1. Delete the node's old slot objects (drop their chunks), build the new full
   slot set, replace `m_Slots`.
2. Re-point every edge touching the node through an explicit old→new slot-id
   map derived from the two signatures.
3. Wire any brand-new inputs (usually new PropertyNodes).
Done in one script, validated after. (Precedent: PainterlyColor 18→21 slots.)

## Deleting / orphan sweep

To remove a dead chain: BFS backwards from all BlockNode ids over the edges;
any node in `m_Nodes` that can't reach a block is orphaned → drop from
`m_Nodes`, drop its chunks + slot chunks, drop edges touching it, and drop
any property whose PropertyNodes are all gone (from `m_Properties`,
CategoryData, and its chunk). NEVER leave a graph or subgraph referencing a
deleted asset — the importer NREs (`GetPromotedInputs`) instead of degrading.

## Known wiring in this project's graphs

- Lighting: `WorldSpaceSurfaceData` subgraph → slots [1]=positionWS,
  [2]=normalWS, [3]=viewDirWS → `Cel Shaded Lighting` [3][4][5]; its color out
  [13] multiplies the albedo → Branch (interactable highlight) →
  `SurfaceDescription.BaseColor` block. No fragment Normal block exists —
  normal work goes through `HeightToNormal` → CelShadedLighting[4].
- PainterlyShader albedo: MainTex sample (mask) → `Painterly Color` → the
  lighting multiply. UV chain: `UV × Tilling → × UniformTiling → + Offset →
  + (ObjectRandom.random3 × UVJitter) → sampler`.

## Materials

`.mat` binds to a graph with
`m_Shader: {fileID: -6465566751694194690, guid: <graph guid>, type: 3}` —
that fileID is the ShaderGraph importer's stable main-shader sub-asset id.
Missing float/color entries fall back to shader property defaults. Stale
entries from removed properties are inert. Texture assignment lives in
`m_TexEnvs` with `{fileID: 2800000, guid: <texture guid>, type: 3}`.
