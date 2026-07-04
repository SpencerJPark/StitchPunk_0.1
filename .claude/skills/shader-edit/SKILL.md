---
name: shader-edit
description: Author and edit shaders in the Stitch Punk project — Unity 6.5 Shader Function Reflection API HLSL nodes (UNITY_EXPORT_REFLECTION, one node per file, StitchPunk.* provider keys under Assets/Shaders/Nodes/) and programmatic .shadergraph surgery (adding nodes, properties, and edges to graphs like PainterlyShader without opening the editor, with backup + validation via the bundled scripts). Use this skill whenever the user asks to add or change a shader node, create a new HLSL node, change a node's signature, wire something into a shader graph, add material properties/sliders, tune the painterly or cel-shading look, create a material from a graph, or debug shader import errors (ProviderNode issues, subgraph NullReferenceExceptions, magenta materials, RenderMeshArray index errors). Also use it before touching anything under Assets/Shaders/. Do NOT use for: render-feature C# scripts, DOTS/ECS systems (dots-* skills), or UI/Rive shaders.
---

# Shader Edit — reflection nodes + shadergraph surgery

Two workflows live here. Decide which one the task needs first:

1. **HLSL node work** — creating or editing the reflection-API node files under
   `Assets/Shaders/Nodes/`. Pure file editing; Unity turns each file into a
   Shader Graph node on import. Read
   [references/reflection-nodes.md](references/reflection-nodes.md) before
   writing a node.
2. **Graph surgery** — programmatically editing a `.shadergraph` (adding
   nodes/properties/edges, rewiring, deleting chains) when the user wants it
   done for them instead of wiring in the editor. Read
   [references/shadergraph-surgery.md](references/shadergraph-surgery.md) and
   use [scripts/shadergraph_lib.py](scripts/shadergraph_lib.py) — do not
   re-derive the file format from scratch.

A signature change to an existing node that is already placed in a graph is
BOTH workflows (see "Signature changes" below) — this is the easiest way to
silently break a graph, treat it with care.

## Project shader layout (post-2026-07 reorg)

| Folder | Contents |
|---|---|
| `Assets/Shaders/Graphs/` | Production graphs: `2DShader`, `2DTextureArrayShader`, `3DShader`, `PainterlyShader` — all lit via the **Cel Shaded Lighting** node |
| `Assets/Shaders/Nodes/{Lighting,Screen,Painterly,Utility}/` | Reflection-API node library, one exported function per `.hlsl` file, plus non-exported `*Common.hlsl` shared-math files |
| `Assets/Shaders/RenderFeatures/` | Hand-written `.shader` passes for the outline render features — NOT graph-related, rarely touched |
| `Assets/Shaders/SubGraphs/` | Only `WorldSpaceSurfaceData` (geometry-context bundle; correctly a subgraph — do not convert it to a node, HLSL functions can't access geometry implicitly) |
| `Assets/Shaders/Legacy/` | Parked experiments, delete-at-will |

The living context doc is `Assets/_Vault/Memories/Code/Shaders.md` — update it
whenever you add a node, change a graph, or move files, so the next session
doesn't rediscover.

## Hard rules (each one broke something once)

- **One `UNITY_EXPORT_REFLECTION` function per file**, named like the file.
  Shared math goes in a `*Common.hlsl` with an include guard, no reflection
  include, no export macro.
- **Create the `.hlsl.meta` yourself (with a fresh 32-hex GUID) whenever a
  graph edit will reference the new file before Unity has imported it.**
  Provider nodes reference the source file by GUID; if Unity assigns a random
  GUID on import the graph reference is dead on arrival. Meta template is in
  the reflection-nodes reference.
- **Never delete an asset another graph references without checking its GUID
  first**: `grep -rl "<guid>" Assets/ --include="*.shadergraph" --include="*.shadersubgraph" --include="*.mat" --include="*.asset"`.
  The ShaderGraph importer throws an unrecoverable NullReferenceException on a
  graph whose subgraph dependency is missing — if you delete a subgraph, delete
  or fix its consumers in the same pass.
- **Back up a `.shadergraph` to the scratchpad before surgery** and validate
  with `scripts/validate_shadergraph.py` after. Hand-built JSON that passes
  referential validation has imported cleanly every time; unvalidated edits
  are how you hand the user a broken graph.
- **No Unity MCP / no Editor bridge.** The compile/import gate is: ask the
  user to focus Unity and report the Console, or grep
  `C:/Users/spenc/AppData/Local/Unity/Editor/Editor.log` for fresh
  `error CS` / `BC` / import errors after they have. Never claim an edit
  imports when you couldn't check.
- After material or graph changes, entities in baked subscenes can log
  `RenderMeshArray ... invalid out of bounds index` — that is a **stale
  subscene bake**, not a shader bug. Tell the user to reopen/rebake the
  subscene.

## Signature changes (HLSL param list of a node already used in a graph)

Reflected node ports are numbered **sequentially from 1 in parameter order**
(outs included, at the end for void functions). Changing the parameter list
renumbers the slots, and every edge in every graph that touches the node
still points at the old numbers. Protocol:

1. Write the old→new slot-id map from the two signatures.
2. Find every graph containing the node's ProviderKey:
   `grep -rl "StitchPunk.<Name>" Assets/Shaders --include="*.shadergraph"`.
3. For each: rebuild the ProviderNode's slot objects to the new signature and
   re-point its edges through the map (shadergraph-surgery reference, "Slot
   rebuild" section). Prefer `void` + `out` params over return values — out
   slots then number predictably after the inputs.
4. Validate, then have the user reimport.

Appending parameters is NOT safe either — out-slot ids shift.

## Materials

A `.mat` binds to a graph via
`m_Shader: {fileID: -6465566751694194690, guid: <shadergraph guid>, type: 3}`
(that fileID is the ShaderGraph importer's stable main-shader id). Minimal
hand-written materials work; Unity re-serializes them with the full property
list on first save. Floats/colors absent from the `.mat` fall back to the
shader property defaults, so keep good defaults in the graph properties.
When tuning look values for the user, edit the `.mat` directly — but warn
that an open Editor with unsaved material tweaks may overwrite (or be
overwritten by) the disk edit.

## Verification loop

1. Save files → user focuses Unity → Console clean (or Editor.log grep).
2. New nodes: user searches "StitchPunk" in the Create Node menu.
3. Graph edits: open the graph once in the editor (import success ≠ sensible
   layout; positions matter to humans).
4. Visual result: subscene rebake if entities render it, then a screenshot
   from the user — you cannot capture the Editor.
