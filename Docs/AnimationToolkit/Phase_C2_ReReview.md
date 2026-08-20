# Phase C2 Re-Review — M1 Authoring & Data Slice (post-rework gate decision)

**Reviewer:** Reviewer (adversarial re-review) · **Date:** 2026-07-29
**Scope:** the C2 rework only — commits `2163dd7`, `1aca683`, `239cb2f` measured against `19b89ca`. Unchanged C0/C1 code was not re-reviewed except where the rework touched its contract.
**Predecessor verdict:** `Phase_C2_Review.md` — **REJECTED**, four blocking defects (B1–B4), seven required amendments (A5, A10–A15), plus advisories.

**Evidence status:** the package compiles clean and all **189 EditMode tests pass** in the product owner's Editor. That is taken as given and was not re-derived. Green tests are *not* evidence the tests are meaningful; every new and strengthened fixture below was read and judged on whether it could actually fail against the code it claims to pin.

**Method:** `ClipRegistryBlob` walked field-by-field against `ComputeContentHash`/`HashClip`; every enum's underlying type checked for cast-truncation aliasing; `TryResolveClip` traced for empty / single-element / hit / miss / invalid-id; amendments A5, A10–A15 read at source in `Phase_B_Architecture.md` via heading grep + offset reads (never loaded whole) and compared clause-by-clause against shipped code; each new fixture reconstructed against the pre-fix stream; `git diff 19b89ca..HEAD --stat` checked for out-of-scope files; quality bar swept by grep across `Runtime/`, `Authoring/`, `Tests/`.

---

## Verdict summary

| Area | Verdict |
|---|---|
| B3 · hash coverage (code) | **PASS** — every blob field reaches the stream; length-prefixing sound; no cast aliasing |
| B2 / A11 · `clipIndexById` deletion, id-ascending clips | **PASS** — complete, no stale assumption survives; `TryResolveClip` correct in every case |
| B4 / A13 · `localBounds` → `offsetBounds` (code) | **PASS** — rename complete, no code treats it as actor space |
| B1 / A12 · V08 scope + CHANGELOG | **PASS** — doc and shipped text both corrected |
| A2 (C1 gate) · `PlaybackLayer.previousLoop` | **PASS** — threaded correctly through `ClipSampler`; writer side is C4's |
| The four new hash fixtures | **PASS** — each genuinely fails against the pre-fix stream; helper asserts signature first |
| `ContentHashGoldenTests` | **PASS on the hole it closes, FAIL on its frozen-fixture claim** — see **D7** |
| A14 · identity persistence (code) | **PASS on mechanism** — flag set on all five minting paths across four assets |
| A14 · identity persistence (doc) | **PARTIAL** — second required clause (A4 / "both asset paths") not done; amendment filed in the wrong section |
| Strengthened V03/V04/V09/V14, JSON round trips, `ValidateRig`, coverage gaps | **PASS** — each pins the opposite direction and could fail |
| Amendment A10 (doc) vs shipped stream | **FAIL** — normative append order disagrees with the code in two places (**D1**) |
| Amendment A13 (doc §4.6) vs shipped bounds math | **FAIL** — amendment deleted a behaviour the code still performs (**D2**) |
| §8 M1 module contract | **FAIL** — new public surface not added to EXPOSES (**D3**) |
| `TryComputeContentHash` usability for C3 | **FAIL** — sound in isolation, but zero test coverage and not on the EXPOSES list (**D3**, **D4**) |
| `ActorRestBounds` (A13's other half) | **FAIL** — specified in §5.2/§4.6/§5.8, exists nowhere; no module owns producing it (**D8**) |
| Quality bar (no `var` / TODO / stub / editor APIs / headers / namespaces) | **PASS** — clean, grep-verified |
| Nothing outside package + `Docs/AnimationToolkit/` modified | **PASS** |

**FINAL VERDICT: REJECTED — the C2 gate cannot close.**

The engineering half of the rework is good, and materially better than what it replaced. The content hash now genuinely covers the blob it keys; the four new fixtures are real regression tests that would have caught the original defect; the strengthened V-code fixtures each pin the direction that was previously untested, and the V14 one guards a real latent bug; the golden hash closes the "hash function moves silently" hole that no relative comparison can; the coverage gaps named in the gate are closed with fixtures that assert content, not length. Every quality-bar grep comes back empty.

The rejection is for the same reason the last two rejections were: **the architecture document does not say what the code does.** Amendment A10 — the amendment whose entire purpose is to specify the canonical hash stream normatively — describes a stream in a different order from the one the builder emits, in two separate places. Amendment A13 silently deleted a bounds behaviour the builder still performs and a fixture still pins. §8 M1's EXPOSES list, which §8 makes the *only* legal surface another module may reference, was never updated for the two new public types C3 and C7 are meant to consume. And `ActorRestBounds`, the component A13 hands the actor-space problem to, does not exist and is not owned by any module. C3 starts by writing a baker against exactly these four things.

None of the four is expensive to fix. Together they are the defect class this gate exists to stop.

---

## Checklist

| # | Criterion | Verdict | Justification |
|---|---|---|---|
| 1 | Hash stream covers **every** field of `ClipRegistryBlob` | **PASS** | Walked field by field. Registry: `schemaVersion`, `setKey`, `vatSetKey`, `layerCount`, `sortedTargetIds`, `targetBoundsExtents`, all four `vatInfo` fields, `clips` — all present. `ClipBlob`: `clipId`, `debugName`, `duration`, `defaultLoop`, `defaultBlendIn`, `defaultBlendOut`, `transformTracks`, `spriteTracks`, `events`, `vatFrameStart`, `vatFrameCount`, `vatFps`, `offsetBounds` (Center **and** Extents) — all present. Every leaf struct complete: `TransformTrackBlob` (3 + keys), `TransformKeyBlob` (7 values), `SpriteTrackBlob` (2 + keys), `SpriteKeyBlob` (6 values), `EventMarkerBlob` (4), `VatTextureInfoBlob` (4). |
| 2 | The one field not in the stream is safe | **PASS** | `sortedClipIds` is the only blob field not appended. It is not a hole: `FillSortedClipIds` and `FillClip` both read `canonicalClips[i].stableId` from the same list in the same order, so `sortedClipIds[i] ≡ clips[i].clipId` by construction, and every `clipId` **is** hashed; its length equals `clips.Length`, which is hashed. No mutation can change one without changing the other. Worth one sentence in the XML doc (**A-i**) but not a defect. |
| 3 | Arrays length-prefixed so a boundary shift cannot alias | **PASS** | `sortedTargetIds.Length`, `clips.Length`, `debugName.Length`, `transformTracks.Length`, per-track `keys.Length`, `spriteTracks.Length`, per-track `keys.Length`, `events.Length` — every variable-length run is preceded by its count. Two different shapes cannot produce the same stream. |
| 4 | No cast truncation aliasing in the stream | **PASS** | Every `(byte)` cast is against an enum declared `: byte` — `VatFlavor`, `LoopMode`, `TrackBlendOp`, `AnimatedChannels` (`[Flags] : byte`, 4 bits used), `Interpolation`, `SpriteFrameMode`. Verified in `AnimationToolkitEnums.cs`. Lossless. Floats all go through `math.asuint`. `debugName` is streamed as its UTF-8 bytes, not `ToString()`. |
| 5 | A11 — `clipIndexById` deleted with no stale assumption anywhere | **PASS** | Grep across the whole package: the only surviving mentions are the deliberate historical notes in `ClipRegistryBuilder.SchemaVersion`'s remarks, the CHANGELOG "Changed" entry, and a `ClipRegistryUtilTests` comment explaining why the fixture changed. `ClipRegistryBlob.clips`' XML, `ClipRegistryUtil.TryResolveClip`'s XML, `BlobSignature`, `TestBlobFactory`, `DataContractTests` and §4.2's sketch all agree that a clip's dense index is its position in both parallel arrays. |
| 6 | `TryResolveClip` returns the correct dense index in every case | **PASS** | Empty registry: `highBound = -1`, loop body never entered, returns `false` with `clipIndex = -1` — pinned by `Build_ProducesAnEmptyRegistry_ForASetWithNoClips`. Single element: `low = high = mid = 0`, hit returns 0, miss terminates in one step. Invalid id (`Value == 0`) short-circuits before the search. Hit: the search position is the dense index because `FillSortedClipIds` and the `clips` fill walk the same sorted list. Miss inside a populated registry terminates because the array is strictly ascending (V05 guarantees uniqueness). |
| 7 | A13 — `localBounds` → `offsetBounds` rename complete in code | **PASS** | No surviving `localBounds` identifier anywhere in the package. `ClipBlob.offsetBounds`' XML states the frame explicitly, names why (`ClipRegistryBuilder` never sees rest poses), and names who owns the actor-space result. `ComputeOffsetBounds`' remarks agree. No code path treats it as actor space — the only consumer, `RenderBoundsUpdateSystem`, does not exist yet. |
| 8 | A2 (C1 gate) — `PlaybackLayer.previousLoop` threaded correctly | **PASS** | `ClipSampler.cs:400` resolves the *outgoing* clip's mode as `ResolveLoopMode(layer.previousLoop, previousClip.defaultLoop)` and feeds it to `MapTimeNormalized(layer.previousTime, previousClip.duration, …)` — the outgoing clip keeps the mode it was actually playing under, which is the point of the field. Pinned by `DataContractTests` (field presence + type) and exercised by `LayerCompositionTests`. Unchanged by C2. **The writer half — `CommandApplySystem` setting `previousLoop` on a crossfade — does not exist yet (C4); the field is currently only ever read.** |
| 9 | The four new hash fixtures would fail against the pre-fix stream | **PASS** | `ChangingATargetId`: renumbers Body `0x10 → 0x11`, which is still the lowest of `{0x11, 0x20, 0x30}`, so dense order, all `targetIndex` values and all bounds are untouched — the *only* blob delta is `sortedTargetIds[0]`, which the old stream never visited. `ChangingTheVatTextureWidth` / `ChangingTheVatBoneCount`: the only delta is a `vatInfo` field, with every frame range unchanged. `RenamingAClip`: the only delta is `debugName` (`"Walk"` → `"WalkRenamed"`, both length and bytes). All four were invisible to the pre-A10 stream. Genuine. |
| 10 | The shared helper asserts the blob signature **before** the hash | **PASS** | `AssertMutationChangesBlobAndHash` asserts `AreNotEqual(firstSignature, secondSignature)` with a `"Precondition: …"` message first, then the hash. A mutation that silently did nothing fails as "the blob is identical", not as a hashing bug — exactly the ordering required. |
| 11 | `BlobSignature` actually covers the mutated fields | **PASS** | `Describe` emits `sortedTargetIds`, `targetBoundsExtents` (per-component `asuint`), all four `vatInfo` fields and `debugName`. The preconditions in #10 are therefore real assertions, not vacuous ones. |
| 12 | Golden hash: the 64-bit value is assembled from the right words | **PASS** | `ComposeDedupKey` builds `Hash128(lo32(h), hi32(h), schemaVersion, folded)` → `Value = uint4(x,y,z,w)`. The fixture recomputes `((ulong)Value.y << 32) \| Value.x` = `hi<<32 \| lo` = the content hash. Correct, and `TheDedupKey_CarriesTheSchemaVersionAndTheFoldedSetKey` independently pins `.z` and `.w`. |
| 13 | Golden hash: the companion schema-version test is meaningful | **PASS** | It asserts the blob stamps the **literal** `2`, not `ClipRegistryBuilder.SchemaVersion`. A version bump therefore reddens this test as well as the hash test, forcing both to be re-recorded in the same commit — which is precisely the review the failure message asks for. Had it read the constant back from the builder it would have been vacuous. |
| 14 | Golden hash: the fixture is genuinely frozen and fully literal | **FAIL** | `idleClip` never sets `defaultBlendIn`/`defaultBlendOut`, so both inherit `AuthoringTestAssets.CreateClip`'s `0.1f`/`0.2f` — a shared factory serving every other fixture — and both are in the hashed stream. The fixture's own XML claims "it owns a frozen input so it cannot drift when the shared fixtures evolve", which is false, and the failure message's remedy would be wrong in that case. See **D7**. The hole A7/A10 named *is* closed regardless. |
| 15 | A14 — the flag is set on every minting path in all four assets | **PASS** | Five paths, five assignments: `RigAsset.EnsureStableIds` sets it for its own id (`:69-70`) **and** inside the per-target row loop (`:85-86`); `ClipAsset:89-90`; `ClipSetAsset:54-55`; `VatTextureSetAsset:116-117` (into `setKey`, correctly — that is the field that carries this asset's identity). All four types implement `IStableIdMintReporter`; all four funnel `Awake`/`OnEnable`/`OnValidate`/`Reset` into the same idempotent method. |
| 16 | A14 — a flag is the right mechanism | **PASS** | Yes. Minting happens inside `Awake`/`OnEnable`, which Unity raises during deserialization, where `EditorUtility.SetDirty` is illegal; an event raised at that moment could not be acted on. A pollable, `[NonSerialized]` flag lets an `AssetPostprocessor` or a deferred editor callback act once loading has settled. The non-serialization is correct and correctly justified — a persisted "needs persisting" flag is self-contradictory. |
| 17 | A14 — the contract is complete enough for the editor layer | **PARTIAL** | Complete for the *mint* case: report → save → `MarkStableIdPersisted`. **It carries no signal for a duplicated asset** — a duplicate has a non-zero id, so nothing mints and the flag stays false. §3.4's project-wide id→GUID `AssetPostprocessor` is the only thing that separates duplicates, it is M5/C7 work, and it is an entirely independent mechanism from this interface. Consistent with §3.4 and correctly asserted by `DuplicatingAnAsset_CopiesTheIdRatherThanMintingANewOne`, but the editor layer must implement **two** mechanisms, not one. See **D11**. |
| 18 | V03 / V04 / V09 / V14 boundary fixtures pin what they claim | **PASS** | V04 adds the negative-time case (a rule written `time > 1f` now fails). V03 adds two keys at the *same* time (a non-strict ascending check now fails). V09 pins both ends — key 15 fires, key 16 does not (a rule matching named constants instead of the 1–15 band now fails). V14 asserts `-1` validates **cleanly** (a rule written `sliceIndex < 0` now fails). The V14 one guards a real latent bug: `-1` is the "no change" sentinel `ClipSampler` depends on. Each is a genuine strengthening, not extra length. |
| 19 | `EditorJsonUtility` round trips are real serialization | **PASS** | Both now go through the text form, and both pick boundary values a signed round trip corrupts: `0xF0E1D2C3B4A59687` and `0x8000000000000001` (above `long.MaxValue`) and `0xFFFFFFFF` (`uint.MaxValue`). This is a material upgrade over `Object.Instantiate`, which never visited the text representation. Still not `AssetDatabase`/YAML on disk, so `A2` is narrowed, not closed. |
| 20 | `ValidateRig` coverage | **PASS** | Three fixtures where there were none: well-formed → nothing, duplicate target ids → exactly V05, null rig → exactly V13. `AssertOnlyCode` makes each a closure check. The decision to test rather than delete published API is right. |
| 21 | VertexPosition / sprite ordering / empty-set gaps | **PASS** | `Build_UsesTheVertexCount_ForAVertexFlavorVatSet` executes `BuildVatInfo`'s previously dead branch and asserts `vertexCount`, not `boneCount`, reaches `boneOrVertexCount`. `Build_SortsSpriteTracksByDenseTargetIndex_KeepingAuthoringOrderOnTies` uses three tracks across two targets and asserts the tie-break by *mode*, so authoring order is genuinely pinned. `Build_ProducesAnEmptyRegistry_ForASetWithNoClips` covers the shape every new `ClipSetAsset` has and doubles as the empty-registry `TryResolveClip` case. |
| 22 | De-rigged dedup fixtures | **PASS** | `RebakingOneSetFromEquivalentAssets_LandsOnTheSameBlob` now says out loud, in the fixture, that both sides deliberately share one identity and that cross-set dedup is impossible by construction. `TwoDistinctSetsWithIdenticalContent_NeverShareABlob` is the honest counterpart: identical content, `SetKey` vs `SetKey + 1`, must not collapse. The previous version asserted a property it had rigged into existence; this pair asserts two true properties. |
| 23 | Truncation fixture pins content | **PASS** | Was length-only (would have passed if truncation kept one character). Now asserts the surviving text is the leading run of `'N'`s **and** that the length is ≥ 60, so both "kept a fragment" and "dropped most of the name" fail. |
| 24 | Shuffle fixture's `spriteTracks.Reverse()` is live | **PASS** | The rich fixture's `walkClip` gained a second sprite track (`tailAtlasTrack`), so the reverse now actually reorders a list. Previously inert. |
| 25 | A10 (doc) matches the shipped stream | **FAIL** | Two order divergences. See **D1**. |
| 26 | A13 (doc §4.6) matches the shipped bounds math | **FAIL** | The amended §4.6 dropped the untracked-target contribution the builder still computes. See **D2**. |
| 27 | A5, A11, A12, A15 internally consistent and agreeing with code | **PASS** | A5 correctly states the mechanism (`xxHash3.StreamingState`), correctly states *why* (`Authoring` has `allowUnsafeCode: false`; the `Hash64<T>(in T)` overload would hash the buffer struct's own pointer), and correctly labels itself a documentation correction rather than a format change. A11's §4.2 sketch, its narrative, §4.3 and §4.5.1 all now agree, and the consequence note (appending a low-id clip renumbers dense indices) is correct and useful. A12 scopes V08 honestly and the CHANGELOG's false claim is corrected. A15's dangling `§4.5` cross-reference is fixed to `§4.1` with the rule table named. |
| 28 | A14 (doc) complete | **PARTIAL** | The persistence clause landed and is normative. The second required clause — soften §3.4's *"fails the bake with both asset paths"* to asset **context** — did not; §3.4:309 still says "with both asset paths" while `ValidationMessage` carries names + an object. Also filed under §3.5, not §3.4. See **D5**, **D6**. |
| 29 | §8 M1 EXPOSES covers the new public surface | **FAIL** | `TryComputeContentHash` and `IStableIdMintReporter` are new public M1 types/members and appear nowhere in §8 M1. §8: *"No module may reference another module's internals — only its EXPOSES list."* See **D3**. |
| 30 | `ClipRegistryBuilder`'s public surface is usable by C3's baker | **PARTIAL** | The *implementation* is right: `TryComputeContentHash` allocates its probe blob with `Allocator.Temp`, disposes it in a `finally`, hands the caller nothing to own, and returns `false` (rather than throwing) for null or error-bearing sets, so it is safe to call on a store hit. `Build`'s ownership contract is documented precisely and correctly. But the method has **zero test coverage** and is not on the EXPOSES list. See **D3**, **D4**. |
| 31 | `ActorRestBounds` exists where A13 says it does | **FAIL** | Declared in §5.2, depended on by §4.6 and §5.8, referenced by `ClipBlob.offsetBounds`' XML and the CHANGELOG — and **not present in the Runtime assembly**, which already shipped in C1. §8 M2's OWNS/EXPOSES/ACCEPTANCE were not amended, and M2 explicitly *"does not define"* M3's types. See **D8**. |
| 32 | Quality bar | **PASS** | Grep across `Runtime/`, `Authoring/`, `Tests/`: zero `TODO`/`FIXME`/`HACK`/`XXX`/`NotImplemented`/`placeholder`; zero `var`; zero `UnityEditor` references in `Runtime/` or `Authoring/`; copyright header on every one of the 47 `.cs` files; namespaces correct (`DotsAnimationToolkit`, `.Authoring`, `.Tests.EditMode`); no single-letter identifiers observed in any file read. XML docs agree with code everywhere except the two golden-fixture claims in **D7**. |
| 33 | Nothing outside the package or `Docs/AnimationToolkit/` modified | **PASS** | `git diff 19b89ca..HEAD --stat` touches only `Docs/AnimationToolkit/{Phase_B_Architecture.md, Phase_C2_Review.md}` and files under `Packages/com.stitchpunk.dotsanimationtoolkit/`. The unrelated shader/vault edits in the working tree predate `19b89ca` and are not part of the rework. This review wrote one file and modified no source. |
| 34 | New spec/reality conflicts escalated rather than absorbed | **PASS (process)** | No *new* silent resolution was found. The rework escalated correctly: it documented `ValidateRig`/`ValidateClip`'s real (discovery, not rule-number) ordering rather than reordering the code to match a doc claim; it documented `ClipValidationException`'s unenforced precondition and why enforcing it would be worse than the gap; it named the consuming build step for every dead-looking field (`mirrorPairs`, `defaultActive`, `boneTexture`, `runtimeMesh`, the VAT set's `schemaVersion`) and flagged that the VAT set's `schemaVersion` versions the asset, not the blob. That is the behaviour §9 asks for. The failures below are amendments that were made *inaccurately*, not conflicts that were hidden. |

---

## Resolution of the original defects

| Original | Status | Notes |
|---|---|---|
| **B1** — V08 unreachable at bake; false CHANGELOG claim | **FIXED** | §3.5's V08 row annotated; amendment A12 states V08 is editor-only and *silent* at bake, not downgraded; `ValidateForBakeOrThrow` carries a call-site comment explaining why the `Bake` stage argument is honest but inert; CHANGELOG rewritten to match. |
| **B2** — `clips` order contradicted the C1-approved doc | **FIXED** | Option (a) taken as recommended. `clipIndexById` gone from blob, builder, `TestBlobFactory`, `BlobSignature`, `DataContractTests`, `ClipRegistryUtilTests`. §4.2 sketch and narrative amended (A11) and reconciled with §4.5.1. `SchemaVersion` bumped to 2. |
| **B3** — hash stream under-covered the blob | **FIXED (code)** / **NOT FIXED (doc)** | The code is complete and correct — see checklist rows 1–4. The amendment that was supposed to make it normative describes a **different** stream order. See **D1**. |
| **B4** — bounds computed in offset space | **FIXED (code)** / **PARTIALLY FIXED (doc)** | The rename, the XML, and the division of labour are right. But §4.6 lost the untracked-target clause (**D2**) and the component the amendment hands the problem to does not exist (**D8**). |
| **A5** — hash mechanism | **FIXED** | Accurate, well-reasoned, correctly self-classified as a documentation correction. |
| **A10** — hash coverage + `SchemaVersion` + negative fixtures | **PARTIALLY FIXED** | Coverage, version bump and fixtures all landed and are real. The normative order text is wrong (**D1**). |
| **A11** — blob layout decision | **FIXED** | |
| **A12** — V08 scope | **FIXED** | |
| **A13** — bounds frame + naming the rest-bounds component | **PARTIALLY FIXED** | Frame resolved and named; §4.6 under-describes the code (**D2**); `ActorRestBounds` named but unbuilt and unowned (**D8**). |
| **A14** — who persists a minted id | **PARTIALLY FIXED** | Mechanism shipped and tested; §3.4's "both asset paths" clause not softened (**D5**); amendment filed in §3.5 (**D6**). |
| **A15** — §3.1 dangling cross-reference | **FIXED** | |
| **A7** — golden hash constant | **FIXED with a caveat** | The hole is closed. The fixture's frozen-input claim is false (**D7**). |
| **A1** — lazily minted id never persisted | **FIXED (as far as M1 can)** | Reporter interface + four fixtures; the save itself is correctly deferred to the Editor layer. |
| **A2** — round trip was `Object.Instantiate` | **LARGELY FIXED** | Now goes through Unity's serializer at the two boundary values. Disk/YAML still untouched. |
| **A4 / A6** — §3.4 "asset paths"; `ValidateSet`'s extra parameters | **NOT FIXED** | Both were folded into A14/§8 and neither landed. See **D3**, **D5**. |
| **A3** — `UseClipDefault` coerced silently | **NOT FIXED** (carried) | Still a bake-time coercion with no V-code. Non-blocking. |
| **A9** — untied comparators depend on V05 | **FIXED** | `ClipRegistryBuilder.cs:263-268` now documents the cross-rule dependency explicitly and correctly. |
| **A10a** — NaN duration slips V01 | **NOT FIXED** (carried) | `ClipValidation.cs:248` is still `clip.duration < ClipAsset.MinimumDuration`, which is `false` for `NaN`; the NaN then propagates through `math.clamp` into the blob and the hash. Non-blocking; `!(duration >= MinimumDuration)` closes it. |
| **A8 / A11a / A12a** — `StableIdUtility` runtime half; `debugName` staleness; `AnimTechnique` unused | **A11a FIXED** (renaming a clip now changes the dedup key, so the stale-name case is gone). **A8, A12a carried** — `AnimTechnique` still has zero references package-wide. |
| — | **NEWLY INTRODUCED** | **D1**, **D2**, **D4**, **D7**, **D8** (and **D9**). |

---

## Blocking defects

### D1 — Amendment A10's normative stream disagrees with the shipped stream, in two places

This is the amendment whose sole purpose is to make the canonical hash stream normative, and it does not describe the stream the builder emits.

**(a) The per-clip block puts the VAT range and bounds in the wrong position.** A10 specifies, per clip:

> `clipId`, `debugName` (length then UTF-8 bytes), `asuint(duration)`, `defaultLoop`, `asuint(defaultBlendIn)`, `asuint(defaultBlendOut)`, **`vatFrameStart`, `vatFrameCount`, `asuint(vatFps)`, `offsetBounds`**; then transform tracks …; then sprite tracks and events …

`HashClip` (`ClipRegistryBuilder.cs:805-883`) emits: `clipId`, `debugName`, `duration`, `defaultLoop`, `defaultBlendIn`, `defaultBlendOut`, **transform tracks, sprite tracks, events**, *then* `vatFrameStart`, `vatFrameCount`, `vatFps`, `offsetBounds`. The four fields the doc places fifth-through-eighth are emitted last.

**(b) The target block is specified as two counted arrays and implemented as one interleaved block.** A10 specifies `sortedTargetIds` (count, then each `uint32`) followed by `targetBoundsExtents` (count, then each component). `ComputeContentHash` (`:771-781`) emits **one** count — `sortedTargetIds.Length` — then a single loop appending, per target, `targetId` followed immediately by its three extent components.

Both are order-only differences, and order is the whole content of a hash-stream specification. An implementer re-deriving the stream from the amended document would produce a hash that differs from every blob already keyed, and the compatibility break would only be caught after the fact by `ContentHashGoldenTests` — whose failure message would send them to bump `SchemaVersion`, entrenching the divergence. This is the exact defect class that sank C1 and C2: the document is normative, the code is right, and they disagree.

**Remedy:** rewrite A10's stream enumeration to match `ComputeContentHash`/`HashClip` exactly — per-target interleaving under a single count, and the VAT/bounds fields after the events block. No code change; no `SchemaVersion` bump; the golden constant stays valid.

### D2 — Amendment A13 deleted a bounds behaviour the builder still performs

The pre-amendment §4.6 read: *"union over transform tracks of … **plus the rest-pose bounds of untracked targets**; VAT clips union their `VatClipRange.bounds`."* The amended §4.6:466 reads:

> Per clip: `offsetBounds` = union over transform tracks of, per key, `position.xy` offset ⊕ target's `boundsExtents` scaled by `max(|scale.x|, |scale.y|, 1)`; VAT clips union their `VatClipRange.bounds`.

The untracked-target clause is gone. The A13 paragraph beneath it explains the *frame* problem at length and never restores the behaviour in offset-space terms.

The code still does it. `ComputeOffsetBounds` (`:692-700`) walks every dense target no key moved and encapsulates `±targetBoundsExtents[i]`. `Build_UnionsScaledKeyExtentsWithTheRestPoseOfUntrackedTargets` pins it to five decimal places. `ComputeOffsetBounds`' own XML says *"the origin-centred rest box of every target no key moved"*. And the behaviour is load-bearing twice over: it is what makes the box conservative for a rig whose targets are not all animated, and it is the path by which a target's extents reach a clip with no transform tracks at all.

A reader of §4.6 today would conclude that an untracked target contributes nothing, and a future implementer of the M2 actor-space union would size their combination wrongly. The amendment fixed the frame and silently dropped a term.

**Remedy:** restore the clause in offset-space language — *"plus, for every target no transform key moves, its authored `boundsExtents` box centred on the origin"* — inside the amended §4.6.

### D3 — §8 M1's EXPOSES list was not amended for the new public surface

§8's preamble is unambiguous: *"No module may reference another module's internals — only its EXPOSES list."* M1's EXPOSES list still reads exactly as it did before C2:

> …`ClipRegistryBuilder.Build(ClipSetAsset, out BlobAssetReference<ClipRegistryBlob>, out Unity.Entities.Hash128 contentHash)`; `ClipValidation.ValidateClip / ValidateSet / ValidateRig`.

C2 shipped two new public surfaces and amended neither:

- **`ClipRegistryBuilder.TryComputeContentHash`** — the method whose XML documents *the canonical baker pattern* that C3 is expected to write. As the contract stands, C3's baker calling it is referencing M1 surface that M1 does not expose.
- **`IStableIdMintReporter`** (public interface, plus `HasUnpersistedStableId`/`MarkStableIdPersisted` on all four asset types) — the entire mechanism A14 requires the Editor layer (M5/C7) to consume, exposed by no module contract.
- Carried from **A6**: `ValidateSet`'s two optional parameters (`vatSourceHashRecomputed`, `recomputedVatSourceHash`) are still absent from the EXPOSES signature, which now understates the surface in the one place A12 makes it matter.

**Remedy:** add all three to §8 M1's EXPOSES list, with `TryComputeContentHash`'s full signature and a sentence on its ownership semantics (nothing to dispose). This is the item C3 is most immediately blocked by.

### D4 — `TryComputeContentHash` ships as public API with zero test coverage

Grep across the package returns exactly four hits: its own declaration, two mentions inside its own XML doc, and one CHANGELOG line. **No fixture calls it.** The "189 tests pass" evidence therefore says nothing whatever about it.

Three specific claims are shipped unverified:

1. Its XML promises the hash is *"byte-identical to the one `Build` produces for the same asset"*. Nothing asserts that. The two paths share `BuildValidatedBlob` and `HashRegistry`, so it is very likely true — but "very likely true" is what the last two gates rejected.
2. It builds and disposes a blob asset under **`Allocator.Temp`**, which no other code path in the package does (`Build` and every fixture use `Persistent`). `BlobBuilder.CreateBlobAssetReference` routes the allocator into `Memory.Unmanaged.Allocate` and `BlobAssetReference.Dispose` frees against `Header->Allocator`; the round trip should work, but it has never been executed.
3. Its documented failure contract — `false` for a null set and `false` for a set carrying validation errors, with `contentHash = default` — is untested in both branches.

This is the single API C3's baker is directed to build on, and it is the one piece of C2 that no test touches.

**Remedy:** one fixture class, three tests — hash equals `Build`'s for the rich set; `false` + `default` for null; `false` + `default` for an error-bearing set. Ideally a fourth asserting no leak (build under a `BlobAssetReferenceScope`, call the probe, assert the scope's blob is still valid).

### D5 — A14's second required clause did not land

The C2 review's A14 row required two things: name who persists a minted id, **and** *"soften 'with both asset paths' to asset context — paths are Editor-only (A4)"*. Only the first landed. §3.4:309 still reads:

> bake still validates uniqueness within a `ClipSetAsset` and **fails the bake** with both asset paths on violation.

`ValidationMessage` carries `assetContext` (the object) and V05's text names both asset *names*, because resolving a path needs `AssetDatabase`, which §1.3 forbids the Authoring assembly. The doc still demands something the architecture forbids the module from doing — a live, if small, spec/reality contradiction, in the same section the rework was editing.

**Remedy:** one sentence in §3.4.

### D6 — Amendment A14 is filed in §3.5, not §3.4

Both A12 and A14 were appended immediately after the V14 row of the §3.5 rule table. A12 belongs there. A14 amends §3.4 — it opens *"§3.4 says identity-bearing assets self-assign …"* — and a reader of §3.4, the normative identity section, will never see it. §3.4 as it currently reads still contains the unqualified *"Assignment happens in `OnValidate` …"* with no mention of persistence.

**Remedy:** move the A14 paragraph into §3.4, after **Persistence**.

### D7 — `ContentHashGoldenTests` is not the frozen, self-owned fixture it claims to be

The fixture's XML states:

> It owns a frozen input so it cannot drift when the shared fixtures evolve: changing the fixture below changes the expected value, which is exactly the review that a hash-format change deserves.

and

> Every id and value is a literal — nothing minted, nothing derived from a name — so the hash is reproducible on any machine and in any session.

Both claims are false as written:

- `BuildFrozenSet` sets `walkClip`'s blends explicitly but **never sets `idleClip.defaultBlendIn`/`defaultBlendOut`**, so both take `AuthoringTestAssets.CreateClip`'s `0.1f` / `0.2f`. Both are in the hashed stream. `AuthoringTestAssets` is the shared factory every other C2 fixture uses; an unrelated edit to those defaults for a future test would redden the golden test — and the failure message would tell the reader *"the canonical hash stream changed … bump `ClipRegistryBuilder.SchemaVersion` and set `ExpectedContentHash` to …"*. Following that advice would bump the blob format and invalidate every baked subscene for a test-fixture edit. That is the one wrong action, and the message recommends it.
- `debugName` is `clip.name`, so the hash **is** partly name-derived. The names are literals in this fixture so the value is still reproducible, but the claim is wrong and it is the claim a future editor would rely on when deciding a name change here is safe.

The hole A7/A10 identified is genuinely closed — a Collections-side `xxHash3` change or a stream reorder now reddens this test, which no relative comparison could do — so this is a defect in the fixture's contract, not in its function.

**Remedy (two lines, and the constant does not change):** set `idleClip.defaultBlendIn = 0.1f;` and `idleClip.defaultBlendOut = 0.2f;` explicitly in `BuildFrozenSet`, and correct the two XML claims (the second to *"every value is a literal, including the asset names that become `debugName`"*).

### D8 — `ActorRestBounds` is specified everywhere and exists nowhere, and no module owns producing it

A13 hands the actor-space bounds problem to a component that does not exist:

- §5.2 declares `public struct ActorRestBounds : IComponentData { public AABB value; }`.
- §4.6's resolution paragraph makes the entity-baking step (§4.1 / M2) responsible for producing it.
- §5.8 makes `RenderBoundsUpdateSystem` union it with each clip's `offsetBounds`.
- §8 M3's ACCEPTANCE was updated to assert on it.
- `ClipBlob.offsetBounds`' XML and the CHANGELOG both promise it.
- **Grep across the package: the type does not exist.** M3's Runtime assembly — which shipped in C1 — has no such component.

The ownership is also unresolved. §8 M3 OWNS *"everything in §5.2"*; §8 M2's EXPOSES says explicitly that *"component/buffer layouts it must produce on baked entities are **M3's types** — M2 writes them per §4.1/§5.2 but does not define them"*. M2's OWNS, EXPOSES and ACCEPTANCE lists were not amended to mention `ActorRestBounds` at all. So C3 (M2) must write a component that M3 owns and never shipped — either violating the ownership rule or landing an unrecorded M3 change.

**Remedy before C3 starts:** either (a) add `ActorRestBounds` to the Runtime assembly now as a recorded M3 addendum and add producing it to §8 M2's ACCEPTANCE list, or (b) amend §5.2/§4.6/§5.8 to defer the component to C4 and state that C3's baker leaves actor-space bounds unwritten. (a) is cleaner — the type is four lines and C3 needs it in the same pass.

---

## Advisories (non-blocking)

| # | Finding |
|---|---|
| **D9** | **The CHANGELOG's C2 entry is incomplete and stale.** It never mentions amendment A14, the identity-persistence fix, or the new public `IStableIdMintReporter` interface — a new public type in a shipping 0.3.0 package with no changelog line. `TryComputeContentHash` is filed under **Fixed** rather than **Added**. The entry claims *"66 EditMode tests"*; C2 now contributes 87 of the package's 189. Shipped user-facing text, and the last gate rejected partly for exactly that. |
| **D10** | **No fixture covers the per-target-row mint setting the flag.** `EveryIdentityBearingAssetType_ReportsItsMint` creates a bare `RigAsset`, whose target list is empty, so `RigAsset.EnsureStableIds`' second minting path (`:85-86`) is never observed through `HasUnpersistedStableId`. `EnsureStableIds_AssignsIdsToTargetRows_ThatWereAddedWithoutOne` asserts the id but not the flag. The code is correct; the path is untested. Two lines. |
| **D11** | **The mint reporter is silent for duplicated assets, by construction.** A duplicate carries a non-zero id, so nothing mints and the flag stays false — correctly asserted by `DuplicatingAnAsset_CopiesTheIdRatherThanMintingANewOne`. Separation depends entirely on §3.4's project-wide id→GUID `AssetPostprocessor`, which is M5/C7. Until C7 ships, duplicate-then-edit silently shares an id, and the only symptom is a V05 bake failure — and only if both copies land in one set. Worth a `Documentation~` note so an early adopter is not bitten. |
| **A-i** | `sortedClipIds` is the one blob field not appended to the hash stream. It is safe (identical to `clips[i].clipId` by construction, and both come from one list) but `ComputeContentHash`' XML claims the stream *"visits **every field of the blob**"*, which is literally untrue. One clause — "…except `sortedClipIds`, which is byte-for-byte derived from the clip ids already hashed" — makes the doc exact. |
| **A-ii** | `ChangingATargetsAuthoredExtents_ChangesTheContentHash` was left on the old hash-only pattern while its four siblings were converted to `AssertMutationChangesBlobAndHash`. It now passes for the right reason (extents are hashed directly), but it no longer asserts the blob actually changed. Converting it costs one line and makes the negative-fixture family uniform. |
| **A10a** *(carried)* | NaN `duration` still slips V01: `clip.duration < ClipAsset.MinimumDuration` is `false` for NaN, so a NaN reaches `math.clamp` and lands in `defaultBlendIn`/`defaultBlendOut`, the blob and the hash. `!(clip.duration >= ClipAsset.MinimumDuration)` closes it, matching how V04 is already written. |
| **A3** *(carried)* | `LoopMode.UseClipDefault` authored on a `ClipAsset` is still coerced to `Once` at bake rather than reported. §3.2 lists the field's legal values as Once/Loop/PingPong, so this is illegal authoring data being silently reinterpreted. Wants a V-code. |
| **A12a** *(carried)* | `AnimTechnique` still has **zero** references package-wide (grep-verified excluding its own declaration). Public API of a shipped 0.3.0 package with no consumer, deferred from C1's N2 to C2 and again past C2. |
| **A8** *(carried)* | `StableIdUtility`'s "runtime half" still has no Runtime caller. |
| **A2** *(narrowed)* | The round trips now go through Unity's serializer at the boundary values, which is a real improvement, but still never touch `AssetDatabase`/YAML on disk. A `CreateAsset` + reload fixture remains the only thing that would prove `[SerializeField] internal ulong` survives the text asset format under the package's asmdef. |

---

## What C3 must know before it starts

1. **`ActorRestBounds` does not exist** (**D8**). C3's baker is the module A13 makes responsible for actor-space bounds, and the component it must write is neither shipped nor owned by M2. Resolve before writing the baker.
2. **`TryComputeContentHash` is not on M1's EXPOSES list** (**D3**) and **has never been executed by a test** (**D4**). It is the API C3's canonical baker pattern is documented around. Do not build on it until both are closed.
3. **A clip's dense index is its position in `sortedClipIds` *and* `clips`** (A11) and **appending a low-id clip renumbers every dense index above it**. Any `clipIndex` C3 bakes into an entity is valid only against the blob it was resolved from.
4. **`offsetBounds` is offset space** (A13). Never write it into `RenderBounds` directly; combine with rest poses read from the prefab.
5. **`SchemaVersion` is 2**, and `ContentHashGoldenTests` pins `0x7262FF88711EB9F9` to it. Any change to the canonical stream must bump the version and re-record the constant *in the same commit*; both tests are wired to force that.
6. **V08 is silent at bake.** A bake cannot detect stale VAT textures. Do not add a bake-time check that pretends otherwise (A12).
7. **`PlaybackLayer.previousLoop` is currently read-only.** `ClipSampler` consumes it correctly; nothing writes it yet. C4's `CommandApplySystem` must set it on every crossfade start or every outgoing clip silently reverts to its authored default mid-blend.

---

## Required before this gate can close

1. **D1** — rewrite amendment A10's stream enumeration to match `ComputeContentHash`/`HashClip` byte for byte (interleaved target block under one count; VAT range and bounds after the events block). Doc only.
2. **D2** — restore the untracked-target contribution to §4.6, in offset-space language. Doc only.
3. **D3** — add `TryComputeContentHash`, `IStableIdMintReporter` and `ValidateSet`'s optional parameters to §8 M1's EXPOSES list. Doc only.
4. **D4** — add fixtures for `TryComputeContentHash`: hash equality with `Build`, both `false` branches, no leak.
5. **D5** — soften §3.4's "with both asset paths" to asset context. Doc only.
6. **D7** — set `idleClip`'s two blend values explicitly in `BuildFrozenSet` and correct the fixture's two false XML claims. The golden constant is unaffected.
7. **D8** — decide and record where `ActorRestBounds` comes from, before C3 writes a baker that needs it.
8. Then: the product owner's Editor compile + Test Runner run, and a short re-check of the changed surface.

**D6, D9, D10, D11, A-i, A-ii** and the carried advisories (**A2**, **A3**, **A8**, **A10a**, **A12a**) may be taken into C3 as tracked backlog at the product owner's discretion. **D6** and **D9** are one edit each and are cheapest to do in the same pass.

---

## Rework pass 3 (2026-07-29, coordinator) — response to the re-review

All eight defects addressed. Three of them (D1, D2, D5/D6) were introduced by the coordinator's own amendment writing in rework pass 2 — amendments that described intent rather than the shipped code. That is the same defect class that sank C1 and C2, committed by the reviewer of those gates.

| # | Defect | Resolution |
|---|---|---|
| D1 | §4.5/A10's normative stream disagreed with `HashClip` twice | **Fixed in the doc, code unchanged.** The amendment was rewritten from the shipped emission order: per-clip VAT and bounds fields come **after** the track and event blocks, and the target block is **one count with id and extents interleaved per target**, not two separately counted arrays. Also records why `sortedClipIds` is deliberately not hashed separately — it is byte-for-byte the `clipId` of each `clips` entry in the same order, with the already-hashed clip count as its length. |
| D2 | A13 deleted "plus rest-pose bounds of untracked targets" while the code still does it | **Fixed in the doc.** §4.6 restates the behaviour and explains it in the corrected frame: an unkeyed target still renders, and in offset space it sits at offset zero. |
| D3 | §8 M1 EXPOSES never amended for the new public surface | **Fixed** as amendment A16: `TryComputeContentHash`, `SchemaVersion` and `IStableIdMintReporter` are now on M1's EXPOSES list, with the reason recorded — without it M2's baker cannot legally call the API §4.5 documents it around. |
| D4 | `TryComputeContentHash` shipped with zero coverage | **Fixed.** Three fixtures: byte-identity with `Build`'s key for the same asset (the property that makes store probing work at all), a 64-iteration repeat proving nothing is left to dispose, and both `false` branches (null set, set carrying a V02 error). |
| D5 | §3.4 demanded the bake fail "with both asset paths", which §1.3 forbids Authoring from producing | **Fixed.** The collision policy now says the failure names the offending *assets* via `ValidationMessage.context`, and that resolving a path needs `AssetDatabase` and therefore belongs to the editor layer. Also states plainly that duplication copies an id by design and separating the copy is the editor's import-time postprocessor, not the bake's job. |
| D6 | A14 filed under §3.5, invisible to §3.4 readers | **Fixed** — moved into §3.4 ahead of the collision policy it qualifies. |
| D7 | `ContentHashGoldenTests` was not the frozen fixture it claimed | **Fixed.** `idleClip`'s `defaultBlendIn`/`defaultBlendOut` were inherited from `AuthoringTestAssets.CreateClip` and are in the hashed stream, so an unrelated edit to that helper would have moved the golden value and demanded a schema bump for no format change. Both are now restated locally at the same values, so **the golden constant is unchanged**. |
| D8 | `ActorRestBounds` specified in §5.2/§4.6/§5.8, existed nowhere | **Fixed.** Added to `Runtime/Components/ActorStateComponents.cs` as an M3 type (§5.2 is M3's, and §8 M2 explicitly does not define M3's types), typed `AABB` so §5.8 can hand it to `RenderBounds.Value` without conversion, documented with the offset-vs-actor-space distinction and the prohibition on writing `offsetBounds` into `RenderBounds` directly. Added to `DataContractTests`' §5.2 inventory. |

192 EditMode tests. Pending the user's compile + Test Runner run; the golden constant is expected to be unchanged, and if it is not, D7's fix was wrong.

---

# Verification of rework pass 3 — reviewer, 2026-07-29

**Scope:** commit `86c6455` only (`git diff 239cb2f..86c6455`), against the eight defects raised above. The rework record immediately preceding this section was treated as a claim, not as evidence — every row was re-derived from source.

**Evidence status:** 192 EditMode tests pass and the golden constant `0x7262FF88711EB9F9` is **unchanged**. That last fact is real evidence for D7 and I weight it as such: a "freeze" that had altered any value reaching the hashed stream would necessarily have moved it. It is not evidence for anything else.

**Method:** the amended §4.5/A10 stream walked block-by-block against `ComputeContentHash`/`HashClip`; §4.6 compared against `ComputeOffsetBounds` including the zero-key-track edge; §8 M1's EXPOSES list checked against every public member C3 must touch; the three new fixtures read for what they actually assert versus what their names promise; **every** value reaching the hashed stream in `BuildFrozenSet` re-traced to its source; `ActorRestBounds` checked for assembly, type, ownership, documentation, contract coverage, and for any effect on the existing reflection-based blob scan.

## Per-defect verdict

| # | Defect | Verdict | Verification |
|---|---|---|---|
| **D1** | A10's stream disagreed with the code twice | **CLOSED** | Walked all four blocks. Header: `schemaVersion`/`setKey`/`vatSetKey`/`layerCount` — matches. Target block: one `sortedTargetIds.Length`, then per target its id followed by three `asuint` extent components, interleaved — **matches `:771-781` exactly**, and the doc now justifies the single count (parallel arrays, always equal length). VAT info: four fields in code order — matches. Per clip: `clipId`, `debugName` length + bytes, `duration`, `defaultLoop`, `defaultBlendIn`, `defaultBlendOut`, transform tracks, sprite tracks, events, **then** `vatFrameStart`/`vatFrameCount`/`vatFps`/`offsetBounds` — matches `HashClip:805-883`, and the doc now calls the position out explicitly in bold as normative. Sprite-track fields are enumerated where they previously were not. No divergence remains. |
| — | the `sortedClipIds` argument | **SOUND** | It rests on the construction invariant that `FillSortedClipIds` and the `clips` fill both read `canonicalClips[i].stableId` in one order, which A11 makes *normative* rather than incidental (§4.2: "`clips` and `sortedClipIds` are now normatively parallel"). Since every `clipId` is hashed and the shared length is the hashed clip count, a separate pass adds no discriminating power. The argument would fail only for a blob that violates A11, which this builder cannot emit and which the hash is never asked to key. Correct, and correctly reasoned. |
| **D2** | §4.6 lost the untracked-target union | **CLOSED** | §4.6:466 now reads "plus, for every rig target the clip does not key, that target's `boundsExtents` box centred at the origin (an unkeyed target still renders, and in offset space it sits at its rest pose, i.e. offset zero)". That matches `ComputeOffsetBounds:692-700` — including the subtlety that a track with a null or empty key list does **not** mark its target keyed (`:673-676`), so such a target still contributes its origin-centred box. The offset-space framing is right and the "still renders" rationale is the correct one. `Build_UnionsScaledKeyExtentsWithTheRestPoseOfUntrackedTargets` continues to pin it. |
| **D3** | §8 M1 EXPOSES not amended | **CLOSED** | Amendment A16 adds `ClipRegistryBuilder.TryComputeContentHash(ClipSetAsset, out Unity.Entities.Hash128)`, `ClipRegistryBuilder.SchemaVersion` and `IStableIdMintReporter` (with both members) to M1's EXPOSES list, each with its consumer named. That is everything C3's baker needs beyond `Build`: the asset types, `ClipValidation`'s three entry points and `ClipRegistryBuilder.Build` were already listed. The module-boundary rule is satisfied — C3 can now legally express the canonical `TryGet`/build/`TryAdd` pattern. One tidy-up remains (**R9**). |
| **D4** | `TryComputeContentHash` untested | **CLOSED on substance** | `TryComputeContentHash_MatchesTheKeyBuildProduces_ForTheSameAsset` asserts full `Hash128` equality against `Build`'s key **and** against the golden literal — so byte-identity is pinned absolutely, not merely relatively, which is stronger than I asked for. `TryComputeContentHash_ReportsFailure_ForANullSetAndForAnInvalidOne` genuinely exercises both branches: null → `false` + `default`, and a track repointed at target `0xDEAD` → V02 → `false` + `default`. The `Allocator.Temp` build/dispose round trip — the one path no other code in the package executes — is now executed 67 times across the three fixtures, so it is empirically valid. The no-leak *property* is asserted only by proxy (**R7**). |
| **D5** | §3.4 demanded asset paths Authoring cannot produce | **CLOSED** | The collision policy now says the bake fails naming the offending *assets*, carries the object for the editor layer to resolve into a path, and states that duplication copies an id by design with separation belonging to the import-time postprocessor. It is now implementable from the Authoring assembly, and it agrees with what `ValidationMessage` and V05 actually do. Two small slips in the new sentence (**R2**, **R3**). |
| **D6** | A14 filed in §3.5 | **CLOSED** | A14 now sits in §3.4 between **Remapping** and **Collision policy**, and is gone from §3.5. A §3.4 reader meets it immediately after the persistence discussion it qualifies. |
| **D7** | golden fixture not genuinely frozen | **CLOSED on substance** | I re-traced **every** value that reaches the hashed stream, not only the two named. Header: `setKey`, `vatSetKey`, `layerCount` (2, passed explicitly) — all fixture-local. Target block: both ids literal, and **both** targets' `boundsExtents` explicitly overridden past `CreateRig`'s default. VAT info: `flavor`, `textureWidth`, `rowsPerFrame` and `boneCount` all explicitly overridden past `CreateVatTextureSet`'s defaults. `walkClip`: id, duration, loop, both blends, every track/key/event field, and the whole `VatClipRange` explicit. `idleClip`: id, duration, loop explicit, and **both blends now restated locally** — the two that were inherited. Nothing else on `ClipAsset`, `RigAsset` or `VatTextureSetAsset` that the helpers set (`sampleFps`, `kind`, `sourceHash`, layer names, `vertexCount`) reaches the blob. The fixture is now genuinely self-owned, and the unchanged golden constant confirms the restated values are the inherited ones — a drift risk removed without a format change. The XML's "nothing derived from a name" clause is still false (**R4**). |
| **D8** | `ActorRestBounds` specified nowhere implemented | **CLOSED** | The type now exists in `Runtime/Components/ActorStateComponents.cs` — the **Runtime** assembly, which is M3, which §8 makes the owner of "everything in §5.2", so the ownership is right and §8 M2's "writes them but does not define them" rule is respected. Typed `AABB` (the Runtime asmdef already references `Unity.Mathematics.Extensions` for `ClipBlob.offsetBounds`, so no asmdef change was needed), documented with the offset-vs-actor-space distinction and an explicit prohibition on writing `offsetBounds` into `RenderBounds`. `DataContractTests` pins `IComponentData` plus the single `AABB value` field alongside the rest of the §5.2 inventory. |

## Did the fixes break or weaken anything?

- **`DataContractTests`' reflection scan is unaffected.** `DiscoverBlobReachableTypes` seeds only from `BlobAssetReference`/`BlobArray`/`BlobPtr` fields; `ActorRestBounds` has neither, so it is not a seed and not reached. The scan's coverage is unchanged, and the new explicit contract assertion is a net strengthening. No existing assertion was relaxed anywhere in the diff.
- **No source file outside the package was touched.** The commit is four files plus this review: one Runtime component, two test files, the architecture doc. No production logic changed at all — every D1/D2/D3/D5/D6 fix is documentation, and the only new runtime code is a four-line component with no consumer yet.
- **§4.5's amended text does not contradict any other section.** §4.2's sketch, §4.3's lookup contract, §4.5.1's canonical ordering and A11 all remain mutually consistent with it. §5.2/§5.8/§4.6 and the new `ActorRestBounds` XML agree on who writes the component and who reads it. One cosmetic regression in §4.5's list structure (**R1**) and one wording tension (**R6**).

## Residual items (none blocking)

| # | Item |
|---|---|
| **R1** | **§4.5's outer numbering is now ambiguous.** A10's rewritten stream was inserted as a `1.`–`4.` list at column zero inside outer item **3**, and outer item **4** ("Determinism test") follows it. The document is cross-referenced by *number* from code (`ClipRegistryBuilder`'s header cites "§4.5 point 3") and from §8 M1 ACCEPTANCE ("builder determinism (§4.5 point 4)"). Indent the inserted list three spaces under item 3, or letter it 3a–3d. Content is correct and unambiguous; only the numbering is. |
| **R2** | §3.4's new collision-policy sentence names **`ValidationMessage.context`**; the field is **`assetContext`** (`ValidationMessage.cs:106`). One word — but it is a doc naming a member that does not exist, which is the defect class this gate keeps catching. |
| **R3** | The same sentence cites "rule V05/V06" for uniqueness. **V06 is the rig-mismatch rule** ("clip in set whose `rig != set.rig`"); only **V05** covers duplicate ids. Drop V06. |
| **R4** | `BuildFrozenSet`'s XML still claims "nothing … derived from a name". `ClipBlob.debugName` **is** `clip.name`, so renaming `"GoldenWalk"`/`"GoldenIdle"` moves the golden hash — and the failure message would then advise a `SchemaVersion` bump, which would be wrong. The class-level "cannot drift when the shared fixtures evolve" claim is now true; this one is not. Replace with "every value is a literal, including the asset names that become `debugName` — renaming a clip here changes the expected hash." |
| **R5** | A10 enumerates every block field-by-field except **events**, which is left as "then per marker its fields". Every sibling block is exact; this one requires the reader to infer `asuint(normalizedTime)`, `eventKey` (uint32), `intParam` (int32), `asuint(floatParam)`. Under-specified rather than wrong. |
| **R6** | §4.5 item 3's lead sentence and `ComputeContentHash`'s XML both still say the stream covers "**every field** of the finished blob", which A10's own `sortedClipIds` clause now qualifies. Harmless and explained in place, but "every field that carries independent information" makes both exact. |
| **R7** | `TryComputeContentHash_LeavesNothingForTheCallerToDispose` asserts purity across 65 probes; it does **not** assert the no-leak property its name promises. A blob leaked from `Memory.Unmanaged.Allocate(…, Persistent)` is a raw allocation and would not be reported by Unity's leak detection, so the comment's "would surface as an allocator error" is optimistic. The `Allocator.Temp` path *is* genuinely exercised. Either rename it to what it proves (probing is a pure function) or add a real assertion. |
| **R8** | §8 M2's OWNS/ACCEPTANCE still do not require the baker to produce `ActorRestBounds`. §4.6 binds C3 normatively, so the obligation exists — but C3's own acceptance list will not check it, and an unwritten component leaves §5.8 unioning an all-zero box. One line in §8 M2 ACCEPTANCE, best added when C3 opens. |
| **R9** | `ClipValidationException`, `ValidationCode`, `ValidationSeverity` and `ValidationStage` are public M1 types named in §3.5 and in §8 M1's ACCEPTANCE but absent from its OWNS/EXPOSES lists — and `IStableIdMintReporter` is now EXPOSED without being OWNED. C3 must catch `ClipValidationException`, so the list understates the contract. Tidy with A16. |
| carried | **A3** (`UseClipDefault` coerced silently, wants a V-code), **A10a** (NaN `duration` slips V01 — `!(duration >= MinimumDuration)` closes it), **A12a** (`AnimTechnique` still has zero references package-wide), **A8**, **A2** (no `AssetDatabase`/YAML round trip), **D9** (CHANGELOG omits A14 and `IStableIdMintReporter`, says "66 EditMode tests"), **D10** (per-target-row mint's flag untested), **D11** (`Documentation~` note on duplicate ids before C7). |

## Verdict

**YES — the C2 gate can close.**

All eight blocking defects are closed on substance, verified at source rather than accepted from the record. D1's stream now matches the code block for block and calls out the two orderings that were wrong; D2's bounds clause is restored and correctly framed; D3's EXPOSES list covers everything C3 must call; D4's fixtures pin byte-identity absolutely and both failure branches genuinely; D5's collision policy is implementable from Authoring; D6 puts A14 where a §3.4 reader finds it; D7's fixture is frozen against every hashed value I could trace, with the unchanged golden constant as corroboration; D8's component exists, in the right assembly, under the right owner, documented and contract-tested. Nothing was broken or weakened: the reflection scan is untouched, no assertion was relaxed, and no production logic changed in this pass at all.

Nine residual items remain. Every one is a single-line documentation or test-naming fix, none changes behaviour, and none would mislead C3 about the blob format or the hash stream. **R1** and **R4** are the two with any teeth — an ambiguous normative cross-reference that code comments cite by number, and a stale claim in the fixture whose whole value is being trustworthy — and both should be swept in C3's first commit rather than carried further.

The rework record's self-assessment is accurate. That it names its own three defects plainly is the behaviour §9 asks for.

## C3 handoff notes

1. **The canonical baker pattern is now legal and tested.** `TryComputeContentHash` → `BlobAssetStore.TryGet` → `Build` → `TryAdd`, exactly as `ClipRegistryBuilder`'s XML shows. Both `Build` and the probe are on M1's EXPOSES list (A16). The probe leaves nothing to own; `Build`'s blob is yours until you hand it to the store.
2. **You own producing `ActorRestBounds`.** It exists now (`Runtime/Components/ActorStateComponents.cs`, M3's type, `AABB value`) but nothing writes it. §4.6 assigns the actor-space combination to the entity-baking step — you are it. Add the obligation to §8 M2's ACCEPTANCE list when you open (**R8**). **Never write `offsetBounds` into `RenderBounds` directly.**
3. **`offsetBounds` is offset space** and includes an origin-centred box for every target the clip does not key. Combine with prefab rest poses; do not assume unkeyed targets contribute nothing.
4. **Dense clip index = position in both `clips` and `sortedClipIds`.** Appending a clip whose id sorts low renumbers every index above it — a baked `clipIndex` is valid only against the blob it was resolved from.
5. **`SchemaVersion` is 2** and `ContentHashGoldenTests` pins `0x7262FF88711EB9F9` to it. Any stream change needs the version bump and the re-recorded constant *in the same commit*; both tests are wired to force it. If the golden test reddens after a change you believe is format-neutral, check **R4** first — a clip asset rename in that fixture also moves it.
6. **V08 is silent at bake.** A bake cannot detect stale VAT textures; do not add a check that implies otherwise.
7. **`PlaybackLayer.previousLoop` is still read-only.** `ClipSampler` consumes it correctly; nothing writes it. C4's `CommandApplySystem` must set it on every crossfade start, or the outgoing clip silently reverts to its authored default mid-blend.
