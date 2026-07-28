# Prompt: Build "the ultimate DOTS animation tool" (multi-agent, AAA, sellable)

> Paste everything below into Fable. Fill in the `<<...>>` blanks first.

---

## 0. Project facts (fill these in)

- Repo / existing tool location: 
    Assets/_Scripts/Editor/AnimationEditor/
    Assets/_Scripts/Data/SOs/KeyframeSO.cs
    Assets/_Scripts/Data/SOs/AnimationClipSO.cs
    Assets/_Scripts/Data/Structs/AnimationBlobs.cs
    Assets/_Scripts/Data/SOs/AnimationClipSO.cs
    Assets/_Scripts/Systems/AnimationSystemGroup

- Unity version: 6000.5
- Entities / Entities Graphics versions: 6.5
- Render pipeline: URP
- Target platforms: PC / Console / Switch
- Intended distribution: standalone UPM package to be sold on the Asset Store / privately licensed. **Treat everything as shippable product code, not a prototype.**

---

## 1. Mission

You are the lead engineer building a **production-grade Unity DOTS animation tool** that unifies several animation techniques I already use into one coherent, beautiful authoring package. It must be good enough to sell as a standalone commercial package held to AAA standards: no placeholders, no stubs, no `TODO`s left in shipped code, no "left as an exercise." If a thing is in scope, it is finished, tested, and documented.

The tool unifies these animation forms behind one authoring model and one runtime:

1. **VAT deformation** — vertex-animation-texture playback in the vertex shader. Support **both** flavors: (a) baked per-vertex position/normal textures (rigid/arbitrary deformation), and (b) baked per-frame **bone-matrix** textures with bone weights in UV channels (skeletal, GPU-skinned). Bone flavor is the priority.
2. **Transform/positional animation** — parts that translate, rotate, and scale over time, including 2D cutout / skeletal-style hierarchies (paper-doll pieces driven by a bone tree).
3. **Flipbook / atlas UV animation** — cycling frames packed in a texture atlas by offsetting UVs, drivable per sub-part.
4. **Billboarding & 2.5D multiplane** — camera-facing planes (full and Y-axis/upright variants), layered at depth for a papercraft/diorama 2.5D look.
5. **Composition** — a single animated character can combine all of the above (e.g. a bone-VAT body with flipbook eyes on a billboarded plane, plus positional part motion), with blending, animation events, bounds, and LOD.

---

## 2. How you must operate (multi-agent + gates)

Run this as an orchestrated team of **specialized sub-agents**, each owning one subsystem, coordinated by you as **Lead Architect**. Between phases there are **hard gates**: nothing proceeds until the Reviewer signs off.

**Sub-agents (spawn one per role; give each a written charter and a hand-off contract):**

- **Auditor** — reverse-engineers my existing tool, pipeline, and shaders (Phase A).
- **Architect** — owns the data model, module boundaries, and the integration contract every other agent codes against.
- **Authoring/Data agent** — ScriptableObject authoring assets, sub-assets, validation, custom icons & thumbnails.
- **Baking agent** — SO → `BlobAsset` bake pipeline, VAT texture baking, dedup via `BlobAssetStore`, deterministic output.
- **Runtime agent** — ECS systems (playback, blending, events, LOD, bounds) — **`ISystem` + Burst first** (see §4).
- **Shader agent** — DOTS-instancing-compatible shaders for VAT (both flavors), flipbook, and billboarding, plus correct shadow/motion-vector passes.
- **Editor UI agent** — the authoring window, timeline, and live preview in **UI Toolkit** (UXML/USS), held to AAA UX standards.
- **Packaging agent** — UPM layout, asmdefs, samples, docs, changelog, licensing headers.
- **Reviewer** — the gatekeeper (see §3). Super picky. Has veto power.

**Rules for the team:**

- Agents communicate through written interface contracts (the Architect's integration doc), not by guessing at each other's internals.
- No agent writes gameplay/runtime code until Phase A and the approved architecture (Phase B) exist.
- Every module ships with tests before the Reviewer sees it.
- Prefer parallel work, but the Reviewer serializes merges: one module integrates at a time, and integration must keep the whole package compiling and its sample scene running.

---

## 3. The Reviewer (make it painfully strict)

The Reviewer is a separate, adversarial agent whose only job is to reject work that isn't AAA. It does not write features; it blocks them. It must **run twice per module** — once on design, once on implementation — and once more at final integration. It rejects (with specific, actionable reasons) on any of:

- Placeholders, stubs, dead code, commented-out blocks, `TODO`/`FIXME`, or "not implemented" paths.
- API that isn't ergonomic, or naming that isn't consistent with the rest of the package.
- Missing or shallow tests; untested edge cases (empty clips, single-frame clips, zero-bone meshes, LOD swaps mid-blend, hot-reload of authoring assets).
- Allocations in per-frame runtime hot paths; non-Burst code where Burst is achievable; main-thread stalls.
- Shaders that break batching, shadows, or motion vectors, or that aren't DOTS-instancing compatible.
- UI that looks like default editor IMGUI, is cramped, misaligned, unthemed, or janky under resize/undo/multi-select.
- Non-deterministic bakes, or bakes that don't dedup shared blobs.
- Any public API without XML doc comments; any feature without user-facing documentation.

The Reviewer must produce a checklist verdict (PASS/FAIL per item) and may not pass a module with any FAIL. If it's unsure, it FAILs and asks for evidence (a test, a profiler capture, a screenshot).

---

## 4. Technical requirements

### 4.1 Discovery first (Phase A — mandatory, no feature code)
Before designing anything, the Auditor must **read and understand my existing animation tool, pipeline, and shaders** at the path above. Produce a written audit: what techniques it implements, its data flow (authoring → bake → runtime), its shader conventions (naming, instancing setup, passes), its assumptions, its strengths, and its weaknesses. Explicitly identify what to preserve, what to replace, and what to absorb into the new unified model. **Detect** the render pipeline, Entities/Entities Graphics versions, and existing conventions rather than assuming them. Nothing else starts until this audit and the resulting architecture are Reviewer-approved.

### 4.2 Runtime architecture
- **`ISystem` + `[BurstCompile]` first.** My codebase is pure-ISystem; match it. Only use managed access (`SystemAPI.ManagedAPI`, un-Bursted paths) where genuinely required, and isolate it. Do not introduce `SystemBase` unless a subsystem truly needs managed state on the system itself, and justify it to the Reviewer if you do.
- Render through **Entities Graphics / BatchRendererGroup**. All animation state that the shader needs (current time/frame, clip offset in the VAT, flipbook frame, billboard mode, blend weights) is delivered as **DOTS-instanced material properties** via `[MaterialProperty]` `IComponentData`, so thousands of characters sharing a mesh+material collapse into instanced draws. Preserve batching as a first-class constraint.
- Jobify heavy work; no per-frame GC allocations; use `EntityCommandBuffer` for structural changes.
- Support blending between clips (accepting VAT blending's vertex-interpolation caveats and documenting them), animation events, per-instance playback speed, looping/one-shot/ping-pong, correct world-space `RenderBounds`, and LOD (bone/vertex LOD for VAT, mesh LOD generally).

### 4.3 Authoring data → ScriptableObjects
- Authoring lives in **ScriptableObject** assets. Support clips as **sub-assets**, thorough validation with clear inline error surfacing, and undo/redo/multi-select correctness.
- Each authoring asset gets a **branded custom type icon** in the Project window, **and** a **generated per-asset thumbnail** (override `Editor.RenderStaticPreview`) so each asset shows a preview of its own content, not a generic icon.
- VAT textures are referenced *by* the ScriptableObject (the SO owns the reference to its baked texture set), consistent with my mental model.

### 4.4 Bake pipeline → BlobAssets
- A deterministic bake turns authoring SOs into runtime data: CPU animation metadata (clip table, frame ranges, bone counts, event tracks, bounds, sampling params) goes into **`BlobAsset`s** referenced in-game via `BlobAssetReference<T>`, deduplicated with `BlobAssetStore`.
- VAT **textures are GPU assets and cannot live inside a blob** — implement this correctly: the blob stores a stable texture-set id/key plus metadata, while the actual `Texture2D`s are referenced at runtime via `UnityObjectRef<Texture2D>` (or bound to the material) and resolved from the SO's reference. Keep the SO → texture and blob → metadata links coherent and round-trippable. Bakes must be reproducible and cache-friendly.

### 4.5 Shaders
- Provide DOTS-instancing-compatible shaders (correct `UNITY_DOTS_INSTANCING_START/PROP/END` blocks, guarded by `UNITY_DOTS_INSTANCING_ENABLED`) for: bone-matrix VAT skinning, vertex-position VAT, flipbook/atlas UV, and billboarding (full + upright). They must apply displacement in **all** passes so shadows and motion vectors are correct. Provide ShaderGraph subgraph equivalents where feasible for user extensibility.

### 4.6 Editor UI (this is a selling point — make it beautiful)
- Build the authoring window in **UI Toolkit** (UXML + USS), not IMGUI. AAA standards: a considered visual design with a proper theme, consistent spacing/typography, dark/light support, resizable and dockable panels, no layout jank on resize/undo.
- Include a **timeline** (clips, tracks per animation form, events, scrubbing), a **live preview viewport** (play/pause/scrub, LOD toggle, billboard preview, bone/vertex overlays), an asset browser, and inline validation. Interactions should feel polished and immediate.
- Follow a real design direction — intentional, not templated. Ship the USS theme as part of the package.

### 4.7 Packaging (sellable)
- Proper UPM package: `package.json`, versioned, correct asmdefs (editor vs runtime vs shaders), no editor code leaking into player builds.
- **Samples~** with at least one showcase scene per animation form and one combined 2.5D character demonstrating everything together.
- Documentation: getting-started, per-feature guides, API reference (XML docs on all public API), performance guidance, and known limitations (including VAT blending caveats). Include a CHANGELOG and license headers.

---

## 5. Quality bar / Definition of Done

- Compiles clean (no warnings) in the target Unity version; player build succeeds; no editor-only code in runtime asmdefs.
- Performance target: **thousands of animated entities on screen in a single-digit-millisecond frame budget**, batched into instanced draws. Provide a profiler capture proving it in the showcase scene.
- Tests: edit-mode + play-mode covering bake determinism, blob dedup, playback correctness, blending, events, LOD swaps, and authoring validation. Edge cases enumerated in §3 are all covered.
- Every public type/method has XML documentation; every feature has user docs; the showcase scene runs out of the box.
- The Reviewer has signed off on every module and on final integration with zero outstanding FAILs.

---

## 6. Deliverables & order of work

1. **Phase A — Audit** of my existing tool/pipeline/shaders (Auditor) → Reviewer gate.
2. **Phase B — Architecture** doc + integration contracts + module plan (Architect) → Reviewer gate.
3. **Phase C — Build**, per subsystem, in dependency order (data model → bake → runtime → shaders → UI → packaging), each behind its own Reviewer gate.
4. **Phase D — Integration**: unified showcase 2.5D character, performance capture, docs/samples finalized → final Reviewer gate.

Start with Phase A now. Do not write any feature code until the audit and architecture are approved. Report at each gate with the Reviewer's checklist verdict.