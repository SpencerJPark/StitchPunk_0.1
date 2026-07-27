# Phase B Review — Verdict on the Architect's Architecture

**Reviewer:** Reviewer agent · **Deliverable:** `Docs/AnimationToolkit/Phase_B_Architecture.md`
**Round 1 (2026-07-26):** REJECTED on five defects (full record in §R1 below).
**Round 2 (2026-07-26, resubmission):** each defect re-verified against the amended doc itself (not the Architect's claims), plus a consistency re-sweep. **APPROVED.**

---

## Round 2 — Defect resolutions (verified in the doc)

| # | Defect (round 1) | Status | Evidence in amended doc |
|---|---|---|---|
| 1 | §5.2 `AnimEventOutput` comment said "user keys ≥ 2", contradicting §3.2/§3.5(V09)/§5.4/§5.5 ("≥ 16") | **RESOLVED** | Line 573 now reads `// user keys ≥ 16 (V09); 0 invalid, 1–15 reserved (ClipFinished = 1, ClipResolveFailed = 2)`. Doc-wide grep for the key range finds only the ≥ 16 form (lines 228, 324, 573, 623, 652); no "≥ 2" remains. |
| 2 | `RenderBoundsUpdateSystem` "runs on clip change" via a change-version filter on `PlaybackLayer` — impossible, since `PlaybackTimeSystem` dirties that buffer every frame | **RESOLVED** | §5.8 (line 682) replaces the filter with a `BoundsDirty` enableable and explicitly names why the old mechanism cannot work. Signal contract is complete and sound: baked **enabled** (first-frame bounds write guaranteed); enabled by `CommandApplySystem` on any applied `clipIndex` change and by `PlaybackTimeSystem` on queue promotion, Once-completion deactivation, and blend completion (all three shrink the union — correctly included); sole disable path is the consumer, which runs after both writers (presentation follows logic, §5.1). Propagation verified: §5.1 diagram (528-529), §5.2 inventory (585, baked ENABLED), M2 acceptance (865: archetype assert includes `BoundsDirty` enabled), M3 acceptance (872: positive + explicit negative assertion — time-advance-only frame leaves the tag disabled and `RenderBounds` untouched), §10.13 (933), §11.2 (952). |
| 3 | Dedup hash named "xxHash64 via `math.hash` composition" — `math.hash` is 32-bit; normative key underspecified | **RESOLVED** | §4.5 point 3 (line 445) now specifies `Unity.Collections.xxHash3.Hash64` over a canonical `UnsafeAppendBuffer` byte stream with a **normative append order** (field-by-field, floats hashed by bit pattern via `math.asuint`) and an exact `Hash128` composition for `AddBlobAssetWithCustomHash`. §4.7 (line 479) aligns `sourceHash` to the same primitive. Two independent build agents now produce identical keys. |
| 4 | `BillboardTransform` facing source unspecified; ShadowCaster-pass behavior undefined (naïve `UNITY_MATRIX_V` orients quads to the light) | **RESOLVED** | §6.1 (line 727): signature now `BillboardTransform(positionOS, pivotOS, billboardParams, cameraPositionWS)`; facing normatively `_WorldSpaceCameraPos` exclusively, `UNITY_MATRIX_V` forbidden, with the light-vs-camera rationale stated; per-mode math given (spherical, upright XZ-projection, frozen-yaw). §6.3 gains a billboard-across-passes bullet (line 754): intended shadow behavior defined (camera-facing geometry casts its true shadow, self-consistent silhouettes) plus three normative caveats (shadow re-orientation under camera motion, edge-on degenerate shadows, non-camera renders → mode 0 / baked lighting unsupported). M4 EXPOSES freezes the parameter list + facing rule (877); M4 human-verified acceptance adds the shadow-orbits-with-camera observation (879). Technically correct: URP sets `_WorldSpaceCameraPos` per rendering camera and it persists through that camera's shadow/depth/MV passes. |
| 5 | §4.1 declared `RigBindingBakingSystem` `[BurstCompile]` while §4.4 gave it managed material validation | **RESOLVED** | §4.1 (line 342): system is now stated as a pure entity-data pass, Burst-compatible throughout, touching no managed objects. §4.4 (line 437): material↔texture-set validation reassigned to the managed `RigTargetBaker` with a concrete mechanism (part Renderer shared material or `expectedMaterial` override, actor's set via `GetComponentInParent<ActorAuthoring>()`, Baker-dependency-tracked). M2 acceptance adds the mismatch fixture (exactly one warning from `RigTargetBaker`, line 865). Sections reconciled. |

**Consistency re-sweep:** reserved-key range uniform doc-wide; `BoundsDirty` appears in every location its contract touches (§5.1, §5.2, §5.8, M2, M3, §10.13, §11.2) with no stale change-filter language outside §5.8's corrective explanation; billboard signature identical in §6.1, §6.3, and M4; hash primitive identical in §4.5 and §4.7; the amendments are additive/corrective within the five areas and do not disturb the 12 previously passed criteria (spot-confirmed: §5.4 state machine, §5.5 event contract, §6.2 property table, §8 module structure, §11.3 edge-case fixtures all unchanged apart from the intended insertions).

## Round 2 — Refreshed checklist

| # | Criterion | Verdict |
|---|---|---|
| 1 | §1 Package identity & layout, asmdef references + platforms | **PASS** |
| 2 | §2 Domain model + glossary, final names | **PASS** |
| 3 | §3 Authoring model + exact stable-identity scheme | **PASS** |
| 4 | §4 Bake pipeline (blobs, dedup keys, determinism, texture-set keys, VAT baking incl. Switch) | **PASS** (defect 3 resolved) |
| 5 | §5 Runtime (inventory, order, components, state machine, techniques, bounds, LOD, API, managed justification) | **PASS** (defects 1, 2 resolved) |
| 6 | §6 Shaders (per-instance table, displacement all passes, batching, CPU↔GPU contract) | **PASS** (defect 4 resolved) |
| 7 | §7 Editor (UI Toolkit, live-SO preview, undo/multi-select, thumbnails) | **PASS** |
| 8 | §8 Six per-module contracts | **PASS** (defect 5 resolved; M2–M4 acceptance updates verified) |
| 9 | §9 Dependency-ordered build plan + DoD | **PASS** |
| 10 | §10 All 14 audit questions answered; overrules/deferrals justified | **PASS** |
| 11 | §11 Test strategy incl. the five mandated edge cases | **PASS** |
| 12 | §12 Risks & limitations with mitigations | **PASS** |
| 13 | §13 Stitch Punk migration appendix | **PASS** |
| 14 | Technical soundness spot-checks | **PASS** (16/16 sound after amendments) |
| 15 | Internal consistency | **PASS** (re-sweep clean) |

---

## §R1 — Round 1 record (historical; superseded by Round 2 above)

Round 1 verified all 13 charter sections present, traced all 24 Phase A verdicts (honored or explicitly overruled with rationale — pre-filled-slot blob §10.1, CPU BillboardSystem §13.1) and all 14 open-question answers with no silent drops, and ran 16 soundness spot-checks of which 12 were sound. The four unsound/underspecified findings plus one contradiction became the five defects:

1. §5.2 reserved-event-key comment ("user keys ≥ 2") contradicting the normative ≥ 16 rule stated in §3.2, §3.5 (V09), §5.4, §5.5.
2. `RenderBoundsUpdateSystem` change-version-filter trigger degenerating to always-true because `PlaybackTimeSystem` writes `PlaybackLayer.time` every frame.
3. Determinism hash primitive misnamed (`math.hash` is 32-bit, not xxHash64) on the normative BlobAssetStore dedup key.
4. `BillboardTransform` facing source and ShadowCaster/DepthOnly behavior unspecified (light-facing-billboard hazard).
5. §4.1 `[BurstCompile]` on `RigBindingBakingSystem` vs §4.4 assigning it managed material validation.

Round-1 sound spot-checks (carried forward unchanged): asmdef/player-build isolation with M6 conformance tests; blob purity + `UnityObjectRef`/material texture linkage; identity scheme rename/duplicate/reorder/move survival; canonical-ordering determinism; per-instance property set batching safety; VAT texel layout arithmetic (682-bone width cap, 8192 height cap, loop-seam duplicate frame, zero-weight clamp over-read); editor preview reusing the runtime `ClipSampler` with transient-blob lifecycle; bounded state-machine buffers with zero runtime structural changes; Loop/Once/PingPong (+ negative speed) semantics; event clear-per-frame + 1-frame-latency consumer contract; stateless sample-rate quantization with per-entity phase; concrete 4-level LOD design.

---

## Final verdict

**APPROVED**
