# Direction System — Design Spec

> **Status:** ✅ spec ready — but this plan is **decision-first**: §2's fork must be resolved before ANY more part SOs are authored. Implementation can wait; the decision cannot.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "multi-facing characters: model swap or more?" · [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #4

---

**Skills Needed:**
- `dots-system-scaffold` — direction-resolve system if option B/C chosen (§5)
- `dots-test` — extend `DirectionUtilsTests` for the chosen mapping (§10)

---

## 1. Purpose & v1 scope

Decide — then implement — how characters visually face more than one direction in the 2.5D billboard rig. `DirectionUtils.Get4/6/8Direction` + tests already exist (quantization groundwork is half-laid); `UnitFaceDirectionSystem` (AnimationAssignmentSystemGroup) currently drives whatever facing exists. The urgent part: if direction means **per-direction texture slices**, it changes `PartTagRange` layouts, `baseImageIndex` conventions, and possibly `PartDef` itself — every part SO authored before the decision may need re-authoring.

## 2. Architecture — the fork

**← DECISION (blocking):** how a facing is represented in art + data:

| Option | Art cost | Data impact | Runtime |
|---|---|---|---|
| **A. Mirror-flip only** (2 facings: left/right via X-flip) | zero new art | none — `PartTagRange` untouched | scale.x flip in `UnitFaceDirectionSystem`; cheapest, ships the slice |
| **B. Per-direction texture slices** (4 facings in the part atlas) | every part × 4 | `PartTagRange` grows a direction stride; `baseImageIndex` becomes `base + directionOffset`; `DesignApplyUtil.SliceAtOffset` math extends | direction index folded into the image-index push in `UpdateImageIndexSystem` |
| **C. Per-direction part prefabs** (swap rig children) | every part × N prefabs | `PartLibrary` gains direction dimension | structural swap — heaviest, conflicts with the pooled-rig design |

*Recommendation: **A now, B later behind the same seam** — ship the slice with mirror-flip, but make the B-readiness change immediately: reserve a `directionCount` field (default 1) on `PartDefinitionSO` and have `DesignApplyUtil` compute `sliceIndex = baseIndex + facing * strideWhenDirectional`, degenerate at `directionCount == 1`. That makes B an art-only upgrade instead of a re-key of every SO.* Option C should be rejected explicitly — it fights the pooling + `LinkedEntityGroup` reset patterns.

## 3. Entry points

No new request. Facing is derived state: `UnitFaceDirectionSystem` reads movement/aim direction → quantizes via `DirectionUtils` → writes the facing the animation/image push consumes.

## 4. Data model

Option-A-with-B-seam: `PartDefinitionSO.directionCount : int = 1` baked into `PartLibraryBlob` per part. No blob layout change beyond one int per part def. **Do this field addition in the same commit as the decision**, so every part SO authored afterward is future-proof.

## 5. Systems

- **Edited:** `UnitFaceDirectionSystem` — own the flip (A) or facing index (B).
- **Edited (B only):** `DesignApplyUtil.ApplyDesign` + `UpdateImageIndexSystem` — direction-offset slice math.
- **New tests:** extend `DirectionUtilsTests` to pin the facing→flip/offset mapping (isometric quantization is intentionally non-obvious — characterization comments required).

## 8. Proposed file manifest

**Edited:** `Data/SOs/PartDefinitionSO.cs` (+`directionCount`), `PostBakingSystemGroup/PartLibraryBakingSystem.cs`, `Utils/DesignApplyUtil.cs` (seam only), `AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitFaceDirectionSystem.cs`
**Assets:** none for A; per-part atlas re-exports for B (deferred).

## 9. Build phases

1. **Decision** (this doc, §2) — before the next part SO is authored.
2. `directionCount` seam: SO field + blob field + degenerate slice math + tests.
3. Mirror-flip facing in `UnitFaceDirectionSystem` (option A runtime).
4. *(Deferred)* B: author 4-facing atlas for ONE part, verify slice math, then batch the rest.

## 10. Verification

Phase 3: walk a unit left/right in DOTSTestScene → sprite flips, no palette/design regression (`DesignApplySystem` untouched by A). Phase 2: EditMode tests green; re-bake with a `directionCount = 4` dummy part → slice math resolves `base + facing*stride`.

## Open decisions (collected)

- [ ] §2 — facing representation: A / A-with-B-seam (recommended) / B now / C (recommend explicit rejection).
- [ ] §2 — facing count when B activates: 4 vs 6 vs 8 (DirectionUtils supports all three quantizations).
