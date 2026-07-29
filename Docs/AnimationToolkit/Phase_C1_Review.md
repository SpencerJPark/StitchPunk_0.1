# Phase C1 Review — M3 Runtime Data Slice

**Reviewers:** Reviewer-A (compilability), Reviewer-B (spec conformance), Reviewer-C (test integrity + code quality) · **Date:** 2026-07-28 · **Deliverable:** 26 files under `Packages/com.stitchpunk.dotsanimationtoolkit/` (16 Runtime, 10 Tests/EditMode) · **Normative refs:** Phase_B_Architecture.md §1.2, §1.3, §2, §3.2–3.4, §4.2, §4.5, §5.2–5.7, §5.10, §5.11, §6.2, §8 M3, §9 C1, §11 · **Method:** the review was split across three independent reviewers with non-overlapping scopes, none able to see the others' findings, after two single-reviewer attempts were killed by a 600s no-progress watchdog. No C1 code has ever been compiled or run — see §"Evidence status".

## Build history

C1 was built in two passes. The first builder was terminated mid-run by infrastructure failure (API credit exhaustion) before self-checking. A second builder audited that work, fixed three defects (D1–D3 below), and reported. Every claim in that report was treated as an assertion to verify, not as evidence.

---

## Verdict summary

| Scope | Verdict |
|---|---|
| Compilability | **PASS** — zero compile errors found |
| Test integrity | **PASS** — ~70 assertions independently re-derived, zero wrong expectations |
| Spec conformance | **FAIL** — 2 major, 2 moderate, 4 minor |
| Code quality | **FAIL** — 1 blocking doc defect, 4 lesser |

**Final verdict: REJECTED — REWORK REQUIRED.**

The rejection is not for weak engineering. The layer sampler — the hardest thing in this module — is correct, including the §5.6 bottom-up composition semantics that the Phase A audit flagged as the most error-prone behaviour in the old system. Test integrity is genuinely strong. The rejection is because three "forward risks" the builder filed as future problems were in fact stop-the-line amendments §9 required raising **before** building on them, and in two cases the builder edited documentation it owned so that it agreed with its own code, instead of escalating that the normative document disagreed.

---

## Checklist

| # | Criterion | Verdict | Justification |
|---|---|---|---|
| 1 | Test→runtime call sites compile | **PASS** | All 15 distinct call sites across 63 invocations checked individually against declared signatures. Modifier discipline correct throughout, including `in`-parameter rvalue passing. |
| 2 | Ref-return and blob API usage legal | **PASS** | Verified against installed sources, not assumed: `BlobAssetReference<T>.Value` is `public ref T` (Blobs.cs:465, not `ref readonly`); `BlobArray<T>` and `BlobBuilderArray<T>` indexers are `ref T`. Zero-length `Allocate` returns early before validation and BlobBuilder `MemClear`s chunks, so empty fixtures are safe at compile *and* runtime. |
| 3 | No duplicate/undefined types, namespaces correct | **PASS** | 60 types each declared once; all runtime files in `StitchPunk.AnimationToolkit`, all EditMode in `…Tests.EditMode`. Grep-verified — no repeat of the first builder's backslash-namespace typo. |
| 4 | `using` directives complete and minimal | **PASS** | Per-file audit; each covers exactly the external types named. |
| 5 | CS0012 risk from `[MaterialProperty]` reflection | **PASS** | Referencing a struct whose attribute lives in an unreferenced assembly compiles — CS0012 fires only when the compiler must *bind* the missing type. The string-based dodge is both sufficient (never names the type) and *necessary* (`typeof(MaterialPropertyAttribute)` would fail CS0246, the namespace not being in scope). It also avoids an `AmbiguousMatchException` that `Attribute.GetCustomAttribute` would risk, since `[MaterialProperty]` is `AllowMultiple = true`. **Do not "fix" this by adding `Unity.Entities.Graphics` to the EditMode asmdef — that would break C0's `Conformance_A` test, which asserts the reference list element-for-element.** |
| 6 | Unity `BlobAssetAnalyzer` (EA0001/2/3/9) clean | **PASS** | Checked though not in the brief. EA0009 requires every parameter of a blob-restricted type to be `ref`, recursively — all 14 such parameters comply; all blob-typed locals are `ref` locals. |
| 7 | Numeric test expectations correct | **PASS** | ~70 assertions independently re-derived against the implementations: all 5 easings incl. the EaseInOut branch boundary; all 12 time-mapping cases incl. negative/reverse; all 15 event-crossing cases traced by hand through the PingPong reflection legs; all 12 quantization assertions emulated in IEEE-754 float32. Zero wrong expectations. |
| 8 | No vacuous/tautological tests | **PASS (with gaps)** | No test computes an expectation using the code under test. `DataContractTests`' field-set comparison has real teeth (exact name+type+count). Gaps recorded as N3/N5 below. |
| 9 | Fixture hygiene, no leaked blob assets | **PASS** | Every ad-hoc blob disposed in a `finally`, every fixture in `[TearDown]` with an `IsCreated` guard. |
| 10 | Test count claim honest | **PASS** | 96 C1 EditMode tests exactly (104 incl. C0's 8). |
| 11 | §4.2 blob structs field-exact | **FAIL** | All 8 structs present and exact **except** `ClipBlob.localBounds`, which is a package-invented `AnimBounds` rather than the specified `AABB`. See **B1**. |
| 12 | "Textures never live in blobs" | **PASS** | Honoured in the code: the only `UnityObjectRef<Texture2D>` is in `VatTextureBinding`, an `IComponentData`, exactly where §4.4 puts it. The *test* guarding this is weaker than it claims — see **N5**. |
| 13 | §5.2 component inventory | **PASS** | Complete. Capacities 8/4/4/16 correct; all 5 enableables implement `IEnableableComponent`; all 11 M3-owned enums present with correct values. (§5.2's own text omits `VatDriven`, which the code correctly ships — doc gap **A4**.) |
| 14 | §6.2 `[MaterialProperty]` strings | **PASS** | All six byte-identical (`_ImageIndex`, `_AtlasFrame`, `_VatFrameA`, `_VatFrameB`, `_VatBlend`, `_BillboardParams`); `float`/`float4` types correct; `_BaseColor` correctly absent as host-owned. None missing. |
| 15 | §3.4 identity types | **PASS** | `ClipId`/`TargetId` match the sketches verbatim; reserved-0 invalid convention correct. |
| 16 | §5.6 sampler semantics | **PASS** | The hard part is right: bottom-up iteration, `continue` not `break` on multi-track, additive mutating the incoming lower-layer composite rather than the rest pose, claim mask gone, blend lerping two same-base samples before composition continues. The audit-Q3 fixture asserts `2 + 0.5 = 2.5` with an explicit "never onto the rest pose" message. |
| 17 | §8 M3 acceptance list | **PASS** | Every listed fixture exists by name: five easings, negative-speed loop/pingpong, all four wrap cases, Override masking, Additive-over-lower, blend lerp, multi-track, single-frame clip, resolve hit/miss. |
| 18 | Blend correctness across loop-mode override | **FAIL** | `PlaybackLayer` has no `previousLoop`, so the outgoing clip maps through `previousClip.defaultLoop`. See **B2**. |
| 19 | Mechanical quality bar | **PASS** | Zero TODO/FIXME/stub, zero `var`, zero single-letter identifiers, copyright header on all files, XML docs on every public member with matching `<param>` names, no `UnityEditor` in `Runtime/`, no host-namespace or `Assets/` leakage, complete metas with no duplicate GUIDs. **The `[ReadOnly]` check is vacuous** — C1 has no jobs, so nothing was tested. |
| 20 | Documentation truthful | **FAIL** | Two XML docs contradict the code they document (**C1**, and the already-"fixed" **D1**); shipped `Documentation~/index.md` is now false (**N1**). |
| 21 | Nothing outside the package modified | **PASS** | Working tree clean apart from a stray blank line in Phase_B_Architecture.md §13.2 (no content change) and the user's own unrelated shader/vault edits. C0 skeleton not restructured. |

---

## Blocking defects

### B1 — `AnimBounds` substitution is a present §1.3 defect, not a future risk
`§4.2` specifies `ClipBlob.localBounds` as `AABB`. The builder substituted a package-owned `AnimBounds { float3 center; float3 extents; }`, reasoning that `Unity.Mathematics.AABB` lives in the `Unity.Mathematics.Extensions` assembly which §1.3 omits, and that C0's conformance test asserts that reference list exactly.

**Both premises verified true. The disposition is wrong.** C# requires a reference to the *defining* assembly, so **any** touch of `RenderBounds.Value` is a CS0012 today — §1.3 as written cannot compile the `RenderBoundsUpdateSystem` it mandates in §5.9, regardless of what type the blob uses. Changing the blob's type did not avoid the problem; it concealed it and deferred it to C4.

Aggravating: `DataContractTests` asserts `typeof(AnimBounds)` inside a test named `ClipBlob_MatchesTheSection42Sketch`, locking a known divergence behind a green test that claims spec conformance.

**Remedy:** amend §1.3 to add `Unity.Mathematics.Extensions` to Runtime + Authoring + both Tests reference lists; update the four asmdefs; restore `AABB localBounds`; delete `AnimBounds`; update the C0 conformance test's expected lists and `DataContractTests`. Layout is identical (two `float3`), so §4.5's bake hash is unaffected. Bonus: C2 gains `MinMaxAABB.Encapsulate` for §4.6's bounds union instead of hand-rolling it.

### B2 — Missing `PlaybackLayer.previousLoop` causes a pop mid-crossfade
During a blend the outgoing clip's time is mapped through `previousClip.defaultLoop`, because `PlaybackLayer` stores no `previousLoop`. Correct only when the outgoing clip used `UseClipDefault`. When a command overrode the loop mode, C4's `CommandApplySystem` overwrites `layer.loop` and destroys the record: a Loop-default clip played `Once`, crossfaded out past its duration, wraps to t=0 instead of holding — a visible pop in exactly the transition the blend exists to smooth. §10 answer 2 calls popping transitions disqualifying.

`PlaybackLayer` already carries four other `previous*` fields, so the omission reads as a §5.2 oversight rather than a builder error. **Remedy:** amend §5.2 to add `LoopMode previousLoop`; add the field and thread it through `CompositeLayers`.

### C1 — `ClipRegistryBlob.cs:53` documents the opposite of the actual design
`/// <summary>The baked clips, in canonical (clip id ascending) order.</summary>`

If `clips` were id-ascending, `clipIndexById` would be the identity map and the entire indirection `TryResolveClip` exists to perform would be dead weight. `TestBlobFactory.cs:106-110` states the correct contract, and `ClipRegistryUtilTests` builds ids `{5,2,100,9}` asserting dense indices `{0,1,2,3}`. This would misdirect the C2 baker author in precisely the wrong direction. **Remedy:** correct the doc to state that `clips` is in authored/dense order and that `sortedClipIds`+`clipIndexById` provide the id→dense mapping.

---

## Doc amendments required before C2 (stop-the-line, product-owner sign-off)

| # | Section | Amendment |
|---|---|---|
| A1 | §1.3 | Add `Unity.Mathematics.Extensions` to the Runtime, Authoring, Tests.EditMode and Tests.PlayMode reference lists (per B1). |
| A2 | §5.2 | Add `LoopMode previousLoop` to `PlaybackLayer` (per B2). |
| A3 | §5.7 | The sprite-slice resolution expression `sliceIndex >= 0 ? … : restSliceIndex` is provably dead given `RestToPose` seeds the rest slice — simplify to `= pose.sliceIndex`. Also document `IdentityAtlasRect`, an undocumented package invention that is now the visible default for atlas actors. |
| A4 | §1.2 / §5.2 | Record `ClipRegistryUtil` under `Api/` (the tree names only three other types there); add the missing `VatDriven` entry to §5.2's inventory. |

---

## Adjudications — the builder's five judgement calls

| # | Call | Ruling |
|---|---|---|
| a | `AnimBounds` instead of `AABB` | **REJECTED** — premises true, disposition wrong. See B1. |
| b | `CompositeLayers` takes `NativeArray<PlaybackLayer>`, not `DynamicBuffer` | **ACCEPTED.** §5.11's signature is elided, and §7.3/§10-answer-10 give the editor preview no ECS world; a `DynamicBuffer` parameter would make §5.11's preview/runtime parity unachievable. The only signature satisfying both. |
| c | `[MaterialProperty]` resolved by attribute full-name string | **ACCEPTED, and it is the only correct option.** Necessary (CS0246 otherwise) and fail-closed: `ResolveMaterialPropertyName` returns `null` into `Assert.AreEqual("_ImageIndex", …)`, so a wrong type name fails all six loudly rather than passing vacuously. Advisory: add a closure check asserting no seventh `[MaterialProperty]` type exists. |
| d | Outgoing clip maps through `previousClip.defaultLoop` | **REJECTED as silently accepted** — a real spec gap that had to be escalated, not absorbed. See B2. |
| e | `ClipRegistryUtil` in `Runtime/Api/` | **ACCEPTED** — `Api/` is the better home for a lookup API; the tree just needs the one-line amendment A4. |

## Adjudications — the builder's three claimed defect fixes

| # | Fix | Ruling |
|---|---|---|
| D1 | `TargetPose.sliceIndex` doc corrected instead of code | **Right on behaviour, wrong on process.** §5.7 traced end to end: there is **no behavioural bug** — every path (no sprite track, `-1` key, one-sided blend, host-written `restSliceIndex`) produces identical visible output, and a clip with no sprite track correctly falls back to the rest slice rather than leaving it unchanged (§5.7 deletes the host's `ImageIndexOverride` staging in favour of exactly this). But the fix renders §5.7's normative expression dead, and the builder rewrote a doc it owned to agree with its code rather than escalating that a section it does *not* own disagreed. Escalated as A3. |
| D2 | `lodDistancesSq` LOD levels 1–4 → 0–3 | **CORRECT.** §5.10 defines levels 0–3 with 0 = full quality; three thresholds plus one spare lane. Verified. |
| D3 | Added `DataContractTests` | **CORRECT in principle, but it encodes one divergence as spec.** `AssertFieldsMatch` asserts exact field count plus name and type, closing the usual omission loophole, and name/type-not-order is correctly justified against §4.5's explicit append order. Two weaknesses: it asserts `AnimBounds` (B1), and the texture-constraint scan iterates a hand-maintained 9-type list, so a C2-added blob struct escapes silently (N5). |

---

## Non-blocking findings

| # | Finding |
|---|---|
| N1 | `Documentation~/index.md` still reads "build step C0 … package skeleton only … What does not exist yet: all runtime components and systems." C1 added 16 runtime files. Shipped user-facing text that is now false. |
| N2 | `AnimTechnique` (`AnimationToolkitEnums.cs:36`) has zero references anywhere — no code, no `<see cref>`, no test. Public API with no consumer; either wire it in C2/C3 or drop it. |
| N3 | `CollectCrossings` returns `Length - initialLength`, but the list is empty at every call site in every test. Replacing the return with `return crossedEventIndices.Length;` would leave all 15 tests green. Add one test that pre-populates the list. |
| N4 | `ClipRegistry.value` is lowercase against `ClipId.Value` / `*Property.Value` everywhere else, and `value` is a contextual keyword. Worth fixing now, before it is public API of a shipped package. |
| N5 | `DataContractTests`' "no textures in blobs" check does not have the teeth its docstring claims: a hardcoded 9-type list, no traversal, and it never unwraps `BlobArray<T>` — a `BlobArray<UnityObjectRef<Texture2D>>` field would pass. |
| N6 | `RuntimeContractTests:59-64` chains `typeof(X).GetField("Value").FieldType` with no null guard. Correct today; a future rename of `Value` would surface as a raw `NullReferenceException` instead of a readable assertion. |
| N7 | The builder described the phase-spread test's step sets as `{10,20,30,40}` / `{5,15,25,35}`; the actual float32 sets are `{11,21,31}` / `{6,16,25,36}` (`k * 0.01f` drifts low — 10×0.01f rounds to 0.099999994). The test passes regardless, asserting only non-emptiness and disjointness. Recorded because it shows "hand-verified" meant "reasoned through in exact arithmetic", not "executed". |

---

## Evidence status

**No C1 code has been compiled or run.** The compilability PASS is a hand-audit, exhaustive but not a compiler result. Three items were reasoned rather than mechanically proven and should be the first things checked when the Editor recompiles:

1. **The CS0012 ruling** — an argument about Roslyn's lazy attribute decoding, not an observed result. Highest-confidence-but-unproven item in this review.
2. **NUnit overload resolution** — the shipped version was confirmed 3.5-based via `com.unity.ext.nunit@2.1.0`'s manifest and the overload shapes checked, but the DLL was not decompiled.
3. **Burst (BC####)** is a separate compile stage no reviewer could exercise. No disqualifiers found, but `public static readonly float4 IdentityAtlasRect`, read inside three `[BurstCompile]` methods, is the only construct in C1 relying on Burst's static-readonly-field support. Any BC diagnostic will be there.

A Unity MCP bridge was found exposed in this session and the product owner approved using it for compile checks, but every call returns `Connection revoked` pending in-Editor approval (Project Settings ▸ AI ▸ Unity MCP). Until that is granted, the compile gate remains the user's manual Editor + Test Runner checkpoint.

---

## Required before this gate can close

1. Product-owner sign-off on amendments **A1–A4**.
2. Fix **B1** (asmdefs + `AABB` restoration + test updates), **B2** (`previousLoop`), **C1** (registry ordering doc).
3. Fix **N1** (stale package documentation) — it ships to customers.
4. Re-review of the changed surface, then the user's Editor compile + Test Runner run.

`N2`–`N7` may be carried as a tracked backlog into C2 rather than blocking, at the product owner's discretion.

---

## Rework record (2026-07-28)

Product owner approved amendments A1–A4 and the associated fixes. The rework agent applied all four doc amendments and most of the code changes before being terminated by a session limit mid-file, leaving `DataContractTests.cs` half-converted (two `AnimBounds` references and a call to a deleted `BlobStructTypes()` helper). The coordinator completed the remainder directly. Three real compile errors surfaced from the user's Editor during this window and were fixed — the first empirical compile feedback this module has received.

| Item | Status |
|---|---|
| A1 §1.3 `Unity.Mathematics.Extensions` | **Done** — doc amended; added to Runtime, Authoring, Tests.EditMode, Tests.PlayMode asmdefs. The Editor asmdef deliberately does *not* take it, and `PackagingConformanceTests`' expected lists match all five. |
| A2 §5.2 `previousLoop` | **Done** — doc amended. |
| A3 §5.7 slice resolution + `IdentityAtlasRect` | **Done** — doc amended. |
| A4 §1.2 `Api/` + §5.2 `VatDriven` | **Done** — doc amended. |
| B1 restore `AABB`, delete `AnimBounds` | **Done** — `ClipBlob.localBounds` is `Unity.Mathematics.AABB`; `AnimBounds` deleted; `TestBlobFactory` and `DataContractTests` updated; zero stale references package-wide. `DataContractTests` now asserts `AABB`'s `Center`/`Extents` shape, since §4.5's hash and §4.6's bounds union depend on it. |
| B2 `previousLoop` field + sampler wiring | **Done** — field added to `PlaybackLayer`; `CompositeLayers` now resolves the outgoing clip through `ResolveLoopMode(layer.previousLoop, previousClip.defaultLoop)` instead of the clip default; `DataContractTests` field list updated. |
| B2 regression test | **Done** — `CompositeLayers_OutgoingClip_KeepsTheLoopModeItWasPlayingUnder` parks the ping-pong-default ramp clip past its end as the outgoing clip at blend weight 0. Under an explicit `Once` override it must hold at x = 2; the pre-fix code reflected it to x = 1. The paired assertion proves `UseClipDefault` still falls back to the clip's own default. |
| C1 registry-ordering doc | **Done** — `clips` is now documented as authored/dense order, stating explicitly that readers must not assume id order and why the indirection would otherwise be pointless. |
| N1 shipped documentation | **Done** — `Documentation~/index.md` rewritten for C1; `CHANGELOG.md` 0.2.0 entry added; version bumped to 0.2.0 in `package.json`, `README.md` and the conformance test's expected identity. |
| N3 crossing-count coverage | **Done** — `CollectCrossings_AppendsToAnExistingList_AndReturnsOnlyTheNewlyAddedCount` pre-populates the list, so returning `Length` instead of the delta now fails. |
| N4 `ClipRegistry.value` → `Value` | **Done** — field renamed, reflection contract string updated. |
| N5 texture-scan teeth | **Done** — the scan now discovers blob-reachable types by reflection over the Runtime assembly and unwraps `BlobArray`/`BlobPtr`/`BlobAssetReference` before judging a field, so `BlobArray<UnityObjectRef<Texture2D>>` is caught. Two `CollectionAssert.Contains` guards prevent a vacuous pass if discovery ever returns nothing. |
| N6 reflection null guard | **Done** — `ResolveValueFieldType` asserts the field exists before reading `FieldType`. |
| N2 `AnimTechnique` unused | **Deferred to C2** by product-owner decision. |
| N7 phase-spread description | **No action** — documentation-only observation about the builder's reasoning; the test itself is correct. |

**Outstanding to close the gate:** re-review of the changed surface, then the user's Editor compile + Test Runner run. Note that the C1 test count is now 98 (96 + the two regression tests added in rework).
