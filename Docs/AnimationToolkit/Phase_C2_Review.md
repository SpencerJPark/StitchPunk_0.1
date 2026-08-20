# Phase C2 Review — M1 Authoring & Data Slice (Reviewer-B: spec conformance & judgement calls)

**Reviewer:** Reviewer-B · **Date:** 2026-07-29 · **Scope:** SPEC CONFORMANCE and the builder's JUDGEMENT CALLS only. Test integrity and code quality belong to a separate reviewer and are not adjudicated here, except where a test *encodes a divergence as spec* or where a fixture is the only evidence a Definition-of-Done bullet has.

**Deliverable:** 15 new files under `Packages/com.dotsanimationtoolkit/` — `Runtime/Identity/StableIdUtility.cs`; `Authoring/Assets/{RigAsset,ClipAsset,ClipSetAsset,VatTextureSetAsset}.cs`; `Authoring/Validation/{ClipValidation,ClipValidationException,ValidationMessage}.cs`; `Authoring/Build/ClipRegistryBuilder.cs` (758 lines, the core deliverable); `Tests/EditMode/{StableIdentityTests,ClipValidationTests,ClipRegistryBuilderTests,ClipRegistryDeterminismTests,AuthoringTestAssets,BlobSignature,BlobAssetReferenceScope}.cs`.

**Normative refs:** `Phase_B_Architecture.md` §1.2, §1.3, §3.1–3.5, §4.1–4.6, §5.8, §8 M1, §9 C2, §11. Precedent for format and rigor: `Phase_C1_Review.md`.

**Evidence status:** the package compiles clean and all 168 EditMode tests pass in the product owner's Editor. Compilability was therefore not re-derived. Green tests are treated as evidence that the code does what its author intended — never as evidence that what the author intended is what the spec requires. Two of the four blocking findings below sit underneath green tests.

**Method:** every spec section read at source (grep for headings, then offset reads — the 111 KB document was never loaded whole). Every claim about the Unity API re-derived against the installed sources in `Library/PackageCache/com.unity.collections@a43cabe808ca`, not assumed. Every V-code traced from the §3.5 table into `ClipValidation` and back out to its fixture. The §4.5 hash stream compared field-for-field against the normative append order, and the blob compared field-for-field against the §4.2 sketch.

---

## Verdict summary

| Area | Verdict |
|---|---|
| §8 M1 acceptance bullets | **PASS with one hole** (V08 unreachable at bake) |
| §9 C2 Definition of Done (determinism + id stability) | **PARTIAL** — determinism genuinely established *within a session*; id stability established for in-memory mutation only |
| §3.5 V01–V14 rule table | **FAIL** — 14/14 implemented, but V08's bake behaviour is unreachable and the changelog claim about it is false |
| §4.2 blob population | **PASS on fields, FAIL on ordering contract** — every field correct; `clips` order contradicts the C1-gate-approved doc |
| §4.3 lookup invariant | **PASS** — `TryResolveClip` resolves every baked clip, by construction |
| §4.5 canonical ordering | **PASS** — total orders throughout; independent of `List<T>.Sort` instability |
| §4.5 content hash | **FAIL (spec defect, inherited silently)** — the normative stream under-covers the blob it keys |
| §4.6 bounds union | **FAIL (spec gap, resolved silently)** — computed in offset space, not the actor space §4.2 promises |
| Textures never in blobs | **PASS** |
| §1.2 placement of `StableIdUtility` | **PASS** — §1.2 names it there; no amendment needed |
| §3.1–3.3 serialized shapes | **PASS** |
| `ClipValidationException` gating the bake | **PASS** |

**Final verdict: REJECTED — REWORK REQUIRED.**

This is not a rejection for weak engineering. `ClipRegistryBuilder` is careful work: every sort comparator carries an explicit tie-break so the result is independent of `List<T>.Sort`'s instability; the `BlobBuilder` is disposed in a `finally`; the blob is hashed *after* it is built rather than from the authoring graph, so the key can never disagree with the bytes it stands for; negative determinism fixtures exist alongside the positive ones; the validation table is complete and its fixtures assert that *exactly* one rule fires. The rejection is for the same reason C1 was rejected: the builder hit four places where the normative document disagreed with reality, resolved each one in code, documented three of them in XML comments addressed to future readers, and escalated none of them. §9's rule is explicit — *"any §8 contract change discovered mid-build is a **stop-the-line** doc amendment, not a silent divergence"*. The builder died before reporting, but the decision to code past each conflict rather than file it was taken well before that.

---

## Checklist

| # | Criterion | Verdict | Justification |
|---|---|---|---|
| 1 | §8 M1 — "id auto-assignment on creation" | **PASS** | All four identity-bearing assets funnel `Awake`/`OnEnable`/`OnValidate`/`Reset` into one idempotent `EnsureStableIds`; `RigAsset.EnsureStableIds` also walks target rows. `StableIdentityTests.CreateInstance_MintsANonZeroStableId_OnEveryIdentityBearingAssetType`. Persistence caveat: advisory **A1**. |
| 2 | §8 M1 — "id survives rename/reorder/move (serialize→deserialize fixture)" | **PASS (weakened)** | Rename and reorder are asserted against real mutations, not comments. The "serialize→deserialize" fixture is `Object.Instantiate`, which exercises Unity's serialization backend but never disk. See **A1**/**A2**. |
| 3 | §8 M1 — "every V-code in §3.5 has a fixture that triggers exactly it" | **PASS** | 17 fixtures cover V01–V14 (V05 and V13 twice, V07 twice, V08 three times). `AssertOnlyCode` asserts the expected code *and* that no other code fires — a real closure check, not a containment check. |
| 4 | §8 M1 — "builder determinism (§4.5 point 4)" | **PASS (session-scoped)** | `BuildingTheSameSetTwice…` compares both the `Hash128` and a full `BlobSignature` of every field with floats as `asuint` bit patterns. Four negative fixtures prove the hash is not a constant. Limit: advisory **A7**. |
| 5 | §8 M1 — "builder canonical ordering (shuffled input → identical hash)" | **PASS** | `ShufflingEveryOrderIndependentAuthoringList_…` reverses clip list, target rows, both track lists and the marker list, and asserts hash *and* full blob signature unchanged. The comment correctly explains why track order within one target is deliberately not shuffled (§4.5 makes it the tie-break). |
| 6 | §8 M1 — "builder rejects V-errors by throwing `ClipValidationException` listing codes" | **PASS** | `ClipRegistryBuilder.cs:72-77` validates before touching the rig, and `ClipValidationException.FormatMessage` lists every error code. `Build_ThrowsClipValidationException_ListingTheOffendingCodes` additionally asserts no blob is left behind. Warnings correctly do not block (`Build_SucceedsWhenOnlyWarningsWereReported`). |
| 7 | §8 M1 — EXPOSES signature `Build(ClipSetAsset, out BlobAssetReference<ClipRegistryBlob>, out Unity.Entities.Hash128)` | **PASS** | Byte-identical to the contract, including the fully-qualified `Unity.Entities.Hash128`. `ValidateClip`/`ValidateSet`/`ValidateRig` all present. `ValidateSet` gained two optional parameters — advisory **A6**. |
| 8 | §8 M1 — `stableId` internal with `[InternalsVisibleTo]` for Editor + Tests | **PASS** | All five `stableId`/`setKey` fields are `[SerializeField] internal`; `Authoring/AssemblyInfo.cs` grants Editor, Tests.EditMode and Tests.PlayMode. Tests read `clip.stableId` directly, proving the grant is live. |
| 9 | §3.5 — all 14 rules implemented at the specified severity | **FAIL** | 14/14 present at the correct severities in `ClipValidation`. **V08's bake behaviour is unreachable through the only bake path in the package.** See **B1**. |
| 10 | §3.5 — V08 "error while authoring, warning at bake" (changelog claim) | **FAIL** | The severity *selection* is implemented and unit-tested; the *bake* never reaches it, because `Build` passes `vatSourceHashRecomputed = false`. At bake V08 is silent, not a warning. See **B1**. |
| 11 | §4.2 — blob populated field-for-field | **PASS** | All 10 `ClipRegistryBlob` fields and all 14 `ClipBlob` fields written; `TransformTrackBlob`, `SpriteTrackBlob`, `TransformKeyBlob`, `SpriteKeyBlob`, `EventMarkerBlob`, `VatTextureInfoBlob` complete. `vatFrameStart = -1` / `vatFrameCount = 0` / `vatFps = 0` for a clip with no range, per the sketch's `// -1 when clip has no VAT range`. |
| 12 | §4.2 — `clips` authored/dense order, `sortedClipIds`+`clipIndexById` provide the mapping | **FAIL** | The builder emits dense order == id-ascending order, so `clipIndexById` is the identity map in **every blob the shipped builder produces** — precisely what `ClipRegistryBlob.clips`' own XML doc (amended at the C1 gate, product-owner-approved) says readers must never assume and calls "pointless". See **B2**. |
| 13 | §4.3 — `TryResolveClip` resolves every clip the builder bakes | **PASS, by construction** | `FillClipIdIndex` writes `sortedClipIds` from `Array.Sort` over the same `canonicalClips` list that fills `clips`, with the permutation captured in `clipIndexById`. Every baked clip therefore has an entry, and `TryResolveClip`'s binary search over an ascending array with unique keys (V05 guarantees uniqueness) finds it. Asserted end-to-end for `{0x50, 0x02, 0x100, 0x09}` and again after rename+reorder. |
| 14 | §4.5.1 — clips sorted by `clipId` ascending, duplicates deduped | **PASS** | `BuildCanonicalClips` dedups by `HashSet<ClipAsset>` (UnityEngine.Object identity semantics), then sorts. `ListingAClipTwice_BakesTheSameBlobAsListingItOnce` asserts hash *and* signature equality against the single-listing set. |
| 15 | §4.5.1 — targets sorted by `targetId` ascending; dense index = position | **PASS** | `BuildCanonicalTargets` + `BuildDenseTargetIndexMap`; `ResolveTargetIndex` returns the array position, which is the same number. `Build_AssignsDenseTargetIndicesByAscendingTargetId` asserts both sides agree. |
| 16 | §4.5.1 — tracks by dense target index, ties by authoring order, both kept | **PASS** | `TransformTrackEntry.authoringIndex` is the tie-break. `Build_SortsTracksByDenseTargetIndex_AndKeepsBothTracksThatShareATarget` uses three tracks across two targets and asserts all three survive in the right order. "Transform before sprite" is satisfied structurally — §4.2 puts them in separate arrays. |
| 17 | §4.5.1 — keys and markers by time, stable original-order tie-break | **PASS** | All five comparators (`…KeyEntries`, `…EventEntries`) carry `authoringIndex`, so every comparison is a **total** order and the result is independent of `List<T>.Sort`'s introsort instability. `Build_SortsEventsByTime_WithAuthoringOrderBreakingTies` asserts the tie-break with two markers at t = 0.5. |
| 18 | §4.5.2 — canonical values (deg→rad, resolved interpolation, blends clamped, floats verbatim) | **PASS** | `math.radians` at `ClipRegistryBuilder.cs:499`, asserted for 90° and −180°. Blends `math.clamp(…, 0f, duration)`, asserted. Interpolation copied per key. No re-quantization anywhere. One unspecified coercion: advisory **A3**. |
| 19 | §4.5.3 — hash over the normative canonical stream | **FAIL** | The append order matches §4.5 field-for-field, but §4.5's stream **under-covers the blob it keys**: `sortedTargetIds`, `targetBoundsExtents`, all four `vatInfo` fields and `debugName` never reach the hash. Since the hash *is* the `BlobAssetStore` dedup key, a change confined to those fields silently returns a stale blob. See **B3**. |
| 20 | §4.5.3 — dedup key shape `Hash128(lo32, hi32, schemaVersion, lo32(setKey)^hi32(setKey))` | **PASS** | `ClipRegistryBuilder.cs:90-94` matches the formula exactly, word for word. `TheDedupKey_CarriesTheSchemaVersionAndTheFoldedSetKey` asserts `.z` and `.w` against independently recomputed values and asserts `.x|.y != 0` so the content words cannot be vacuously zero. |
| 21 | §4.5.3 — `UnsafeAppendBuffer` byte stream | **SUBSTITUTED** | Replaced by `xxHash3.StreamingState`. Adjudicated in full below — **substitution correct, escalation owed**. |
| 22 | §4.6 — per-clip conservative bounds via `MinMaxAABB.Encapsulate` | **FAIL** | The key/scale math and the `Encapsulate` union are exactly right and precisely asserted. But "the rest-pose bounds of untracked targets" was implemented as an **origin-centred** box, and key positions are *local offsets* (§3.2) that no rest offset is ever added to. The result is an offset-space box stamped into a field §4.2 calls "conservative actor-space bounds". See **B4**. |
| 23 | §4.6 — negative authored extents handled | **PASS** | `math.max(boundsExtents, float3.zero)` before any use, asserted by `Build_ClampsNegativeAuthoredTargetExtentsToZero`. Beyond spec (§3.1 says only "`boundsExtents` ≥ 0"), and the right call — clamping is safer than a V-code that would fire on legacy data. |
| 24 | Textures never live in blobs | **PASS** | The only path from a `VatTextureSetAsset` into the blob is `BuildVatInfo`, which copies four `int`/enum fields, plus `setKey` and per-clip frame ranges. `boneTexture`/`positionTexture`/`normalTexture`/`runtimeMesh` are never read by the builder. C2 adds no blob struct, so C1's reflection-based texture scan still covers the whole schema. |
| 25 | §1.2 — `StableIdUtility` in `Runtime/Identity/` | **PASS** | §1.2 names it verbatim: `Identity/ (ClipId, TargetId, StableIdUtility-runtime half)`. The Runtime asmdef needs nothing beyond mscorlib's `System.Guid` to compile it, so the placement is legal against §1.3's reference list. **No §1.2 amendment needed.** Advisory **A8** on the unrealised "half". |
| 26 | §1.3 — asmdefs unchanged and still conformant | **PASS** | No asmdef was touched in C2. `Authoring` remains `allowUnsafeCode: false` with the §1.3 reference list; `PackagingConformanceTests` still asserts both. Zero `UnityEditor` references in `Runtime/` or `Authoring/` (grep-verified). |
| 27 | §3.1 — `RigAsset` serialized shape | **PASS** | `[SerializeField] internal ulong stableId`, `List<RigTargetDefinition> targets`, `List<LayerDefinition> layers`, `MirrorPair[] mirrorPairs` — array not list, matching the sketch. `RigTargetDefinition` carries `displayName`, `[SerializeField] internal uint stableId`, `kind`, `boundsExtents` defaulting to 0.5. `LayerDefinition` and `MirrorPair` exact. |
| 28 | §3.2 — `ClipAsset` serialized shape | **PASS** | Every field present with the specified attributes: `[Min(MinimumDuration)] duration`, `[Min(0f)]` on both blends, `[Min(1f)] sampleFps` defaulting to 30. `TransformKey`, `SpriteKey`, `EventMarker` are structs; `TransformTrack`, `SpriteTrack`, `VatClipSource` are sealed classes — matching the sketch's class/struct split exactly. |
| 29 | §3.3 — `ClipSetAsset` / `VatTextureSetAsset` shape | **PASS** | All 13 `VatTextureSetAsset` fields present in sketch order; `VatClipRange` exact including `UnityEngine.Bounds`. Correctly carries **no** `[CreateAssetMenu]`, being generator-owned. |
| 30 | §4.1 — validation errors fail the bake | **PASS** | §4.1 itself is silent on this; §8 M1's acceptance list and §3.4's collision policy ("**fails the bake**") are the binding statements, and both are satisfied. The one wrinkle is §3.4's "with both asset paths" — advisory **A4**. |
| 31 | Nothing outside the package modified | **PASS** | The C2 change set is confined to the package plus this review file. No source file was modified by this review. |

---

## Blocking defects

### B1 — V08 is unreachable at bake; the shipped changelog claim about it is false

`Authoring/Build/ClipRegistryBuilder.cs:72-73`:

```csharp
List<ValidationMessage> validationMessages =
    ClipValidation.ValidateSet(clipSet, ValidationStage.Bake);
```

`ValidateSet`'s V08 branch (`ClipValidation.cs:139-152`) is guarded by `vatSourceHashRecomputed`, which defaults to `false` and is never passed by `Build`. **V08 does not fire at bake at all** — it is silent, not downgraded. The `ValidationStage.Bake` argument therefore selects a severity that no bake can ever reach; it is dead in the only bake path the package ships.

The CHANGELOG's C2 entry states: *"Rule V08 reports as an error while authoring and as a warning at bake time, where stale VAT textures still render."* That is true of `ClipValidation.ValidateSet` as an API and false of baking. This is user-facing text that ships.

The two green fixtures do not catch it because they call `ValidateSet(clipSet, ValidationStage.Bake, true, hash + 1)` **directly**, never through `Build`. A third fixture, `V08_StaysSilentWhenTheSourceHashWasNotRecomputed`, asserts the silence as intended behaviour — the builder knew, wrote a test to lock it in, and did not escalate.

**And it cannot be fixed inside M1.** Recomputing a `VatTextureSetAsset.sourceHash` means re-reading the source mesh and `AnimationClip` curves — `VatTextureBaker`, which §4.1 and §8 M2 place in the **Editor** asmdef, which §1.3 forbids `Authoring` from referencing. §3.5's *"at entity-bake time V08 downgrades to Warning"* is therefore **structurally unimplementable as written**. That is a spec defect the builder was uniquely positioned to find, and §9 required it be filed as a stop-the-line amendment.

**Remedy:** amend §3.5's V08 row to state that V08 is evaluable only where the source hash can be recomputed (Editor asmdef: inspectors, clip editor, VAT bake window), and that entity baking cannot judge it; or specify a mechanism by which the Editor stamps a recomputed hash onto the asset for the baker to compare cheaply. Then correct the CHANGELOG. Until one of those lands, drop the misleading `ValidationStage.Bake` argument from `Build` or document at the call site why it is inert.

### B2 — The builder's `clips` order contradicts the C1-gate-approved normative doc

`Runtime/Blobs/ClipRegistryBlob.cs` (as amended during C1 rework, product-owner-approved, defect **C1** of `Phase_C1_Review.md`):

> *"Readers must not assume this array is ordered by `ClipBlob.clipId`: `sortedClipIds` plus `clipIndexById` are what provide the id → dense-index mapping … and if the dense order were the id order that indirection would be an identity map and the binary search pointless."*

`ClipRegistryBuilder.BuildCanonicalClips` sorts `canonicalClips` ascending by `stableId` and then fills `clips` from it. `FillClipIdIndex` sorts an already-sorted array. **`clipIndexById[i] == i` for every blob the shipped builder produces.** The builder's own XML doc says so out loud (`ClipRegistryBuilder.cs:254-258`: *"the mapping this produces is currently the identity"*), then justifies computing it anyway "because `ClipRegistryUtil.TryResolveClip` contracts readers to go through the indirection".

The builder is not wrong about §4.5.1 — *"Clips sorted by `clipId` ascending (list order irrelevant)"* is normative and unambiguous. The problem is that §4.2's amended doc and §4.5.1 now directly contradict each other, and C2 has resolved the contradiction in code without anyone deciding which one wins. `ClipRegistryUtilTests` (C1) still asserts a blob with ids `{5,2,100,9}` mapping to dense `{0,1,2,3}` — a shape the builder can no longer produce. The runtime supports both; the schema pays for the flexibility in every baked scene.

This must be settled **before C3**, because C3 bakes this layout into subscenes and any later change is a `schemaVersion` bump plus a rebake of all host content.

**Remedy — product owner picks one:**
- **(a)** §4.5.1 wins: amend `ClipRegistryBlob.clips`' doc back to "ascending clip id", **delete `clipIndexById` from §4.2**, simplify `TryResolveClip` to return the binary-search index, update `DataContractTests`/`ClipRegistryUtilTests`/`BlobSignature`, bump `SchemaVersion`. Smaller blob, simpler contract, one fewer array to keep parallel.
- **(b)** §4.2 wins: amend §4.5.1 to say the *hash stream* visits clips in `sortedClipIds` order while `clips` keeps deduped authoring order, and change `BuildCanonicalClips` not to sort. Keeps the indirection meaningful and keeps authored order legible in a blob inspector, at the cost of one extra array forever.

Reviewer-B's recommendation is **(a)**. The indirection exists for a use case — dense order carrying meaning independent of id order — that nothing in the design actually wants.

### B3 — §4.5's normative hash stream under-covers the blob it keys (spec defect, inherited without escalation)

§4.5 point 3 enumerates the append order, and `HashClip` implements it faithfully. But the enumerated stream **omits four things that are in the blob**:

| Blob field | In hash? | Consequence of a change confined to it |
|---|---|---|
| `sortedTargetIds` | no | Adding a rig target changes the blob; if no track binds it and its box sits inside the existing bounds union, the hash is unchanged |
| `targetBoundsExtents` | only indirectly, via `localBounds` | Changing a target's extents inside the union of every clip leaves the hash unchanged |
| `vatInfo` (`flavor`, `textureWidth`, `rowsPerFrame`, `boneOrVertexCount`) | no | Rebaking a VAT texture set to a new width/rows/bone count with unchanged frame ranges leaves the hash unchanged |
| `ClipBlob.debugName` | no | Renaming a clip leaves the hash unchanged |

§4.1 gives the blob's lifetime to `AddBlobAssetWithCustomHash` and §4.5 states plainly that this Hash128 *"**is the BlobAssetStore dedup key**"*. A hash collision here is not a hash collision in the cryptographic sense — it is a **guaranteed** stale-blob read: the store finds the existing entry and discards the freshly built one. The first three rows are silent data corruption (wrong dense target set, wrong VAT addressing); the fourth is cosmetic.

The determinism suite cannot catch this. Its four negative fixtures cover a key position, an event payload, the set key, and target extents. A fifth fixture mutating `vatTextureSet.textureWidth` **would fail today**. And `ChangingATargetsAuthoredExtents_ChangesTheContentHash` passes only incidentally: the rich fixture's `idleClip` has no transform tracks at all, so *every* target contributes its rest box to that clip's union, which is what carries the extents change into the hash. Remove `idleClip` and the fixture's guarantee evaporates.

The builder demonstrably noticed the stream was under-specified — it added `transformTracks.Length`, `spriteTracks.Length` and `events.Length` beyond the normative list, with a comment explaining why (`ClipRegistryBuilder.cs:694-695`: *"so that two different array shapes can never produce the same stream"*). That is correct reasoning and a strictly strengthening change, and the hash is only ever compared to itself so the addition is safe. But having decided §4.5's "normative" stream was amendable, the builder amended it for the small hole and left the large one.

**Remedy:** amend §4.5 point 3 to append, after `layerCount`: the target block (`sortedTargetIds.Length` (int32), then per target `targetId` (uint32) and the three `asuint` components of `targetBoundsExtents`), the four `vatInfo` fields, and `debugName` (as its raw bytes plus length); and to record the three array-count fields the builder already added. Bump `SchemaVersion`. Add the corresponding negative determinism fixtures — at minimum one mutating `vatInfo.textureWidth` and one mutating a target extent that does *not* widen any clip's union.

### B4 — §4.6 bounds are computed in offset space, not the actor space §4.2 promises (spec gap, resolved silently)

`ComputeLocalBounds` implements §4.6 literally: per key, `(position.x, position.y, 0)` ± `boundsExtents × max(|scale.x|, |scale.y|, 1)`, and for untracked targets `±boundsExtents`. The `MinMaxAABB.Encapsulate` union (now available after C1's `Unity.Mathematics.Extensions` amendment) is used correctly, `IsEmpty` is handled, VAT range bounds are unioned in, and the whole thing is asserted to five decimal places by `Build_UnionsScaledKeyExtentsWithTheRestPoseOfUntrackedTargets`. The arithmetic is right.

The frame of reference is not. §3.2 defines `TransformKey.position` as an **"x/y local offset"**, and §4.1 has `RigTargetBaker` capture each part's `TargetRestPose` **"from the authoring transform"**. So a part's actual position is `restPose.localPosition + key.position`. `ClipRegistryBuilder` has no access to rest poses — it sees a `ClipSetAsset` graph, never a prefab — so it centres every box on the origin. §4.2 nonetheless labels the resulting field *"conservative actor-space bounds for this clip"*, and §5.8 has `RenderBoundsUpdateSystem` write it into `RenderBounds`.

For any rig whose parts sit away from the actor origin — a head at y ≈ +1, feet at y ≈ −1, i.e. essentially every cutout character the toolkit exists to serve — the baked box is **smaller than the silhouette**. That reintroduces exactly the failure §5.8 claims to have closed (*"Large-offset clips can no longer cull visibly (audit §7 bounds gap closed)"*), and it will surface as parts popping out at screen edges during C4's smoke scene, where it will look like a runtime bug rather than a bake one.

§5.8's phrase *"plus rest bounds"* is the only escape hatch in the document, and it is not backed by any type: §5.2 has no actor-level rest-bounds component, C1 shipped none, and `RenderBoundsUpdateSystem` iterates roots, not parts. Nor would a flat union rescue it — a correct conservative box needs `restPose[i] ⊕ offsetBounds[i]` **per target**, and `localBounds` has already flattened the per-target dimension away.

This one is genuinely the spec's fault: within M1 there is no other implementation available, and the builder wrote an honest XML doc describing what it actually does. But §9 makes discovering an unimplementable normative statement a stop-the-line event, and this is the third one in this module that was absorbed instead.

**Remedy — needs a decision before C3/C4:**
- **(a)** Keep `localBounds` explicitly *offset-space*, rename it or document it as such in §4.2, and have `ActorBaker`/`RigBindingBakingSystem` (C3, which *does* see rest poses) compute a per-actor `RestBounds` component; §5.8 unions `RestBounds ⊕ localBounds`. Still conservative, still cheap. Recommended.
- **(b)** Add an authored rest offset to `RigTargetDefinition` so M1 can do the union itself — duplicates prefab data and will drift.

---

## Adjudication — the §4.5 `UnsafeAppendBuffer` → `xxHash3.StreamingState` substitution

**Ruling: the substitution is CORRECT and should be adopted. The coordinator's supporting reasoning is right on the facts but overstated on necessity. §4.5 needs a stop-the-line amendment. Escalation was owed and was not made.**

**1. Is §4.5 self-contradictory with §1.3? Yes — verified independently.**

`UnsafeAppendBuffer` can be *declared* and *filled* from safe code: `Add<T>(T value) where T : unmanaged` and the `(int, int, AllocatorHandle)` constructor have no pointers in their signatures. The contradiction is one step later. The only way to hash the buffer's **contents** is `xxHash3.Hash64(void* input, long length)` (`Unity.Collections/xxHash3.cs:42`) — a pointer parameter, so the call site needs an `unsafe` context, so the assembly needs `allowUnsafeCode: true`. The alternative overload `Hash64<T>(in T input)` would hash the `UnsafeAppendBuffer` **struct's own bytes** — a `byte* Ptr` plus lengths — which is a different value on every run and would silently destroy the determinism §4.5 exists to guarantee. There is no safe path.

`Authoring/DotsAnimationToolkit.Authoring.asmdef` has `"allowUnsafeCode": false`, §1.3 grants the flag only to Runtime ("blob building helpers"), and `PackagingConformanceTests.Supplementary_UnsafeCodeFlags_MatchSection13` asserts the whole five-assembly set. So §4.5-as-written cannot be implemented in the assembly §8 M1 assigns `ClipRegistryBuilder` to. **Confirmed self-contradictory.**

**2. Is the substitution therefore *required*? No — it is one of two options, both needing an amendment.**

§1.3 gives Runtime `allowUnsafeCode: true` explicitly for *blob-building helpers*. A canonical-stream helper living in `Runtime/` would satisfy §4.5 literally **and** §1.3, with `ClipRegistryBuilder` calling it across the M1→M3 dependency §8 M1 already permits. That path is not free either — §8 M3's OWNS and EXPOSES lists contain no such helper, and §8 states *"No module may reference another module's internals — only its EXPOSES list"* — so it too needs an amendment. The honest framing is: **every route out of this contradiction required a doc amendment, so one was owed regardless of which route was taken.** The coordinator's "arguably required" should be downgraded to "legitimately chosen"; the escalation obligation is unaffected either way.

**3. Is the resulting hash equivalent in strength and determinism? Yes — byte-for-byte, not merely morally.**

- **Strength:** identical. Same `xxHash3` 64-bit function, same default seed 0, same compiled-in `SecretKey`. `new xxHash3.StreamingState(true)` sets `IsHash64 = 1` (`xxHash3.StreamingState.cs:36,55`), and `DigestHash64` returns the same `uint2` the one-shot `Hash64` returns.
- **Stream identity:** `Update<T>(in T input)` is `Update(UnsafeUtilityExtensions.AddressOf(input), sizeof(T))` (`xxHash3.StreamingState.cs:143-146`) — exactly `sizeof(T)` bytes of the value's own memory, tightly packed, no alignment padding. That is byte-for-byte what `UnsafeAppendBuffer.Add<T>` would have written. Streaming-equals-one-shot over the concatenation is the defining contract of a streaming hash, and Unity's implementation buffers into a fixed internal buffer then digests through the same code path. So this is not an equivalent-strength substitute — it produces **the same bytes**.
- **Cross-platform:** value-memory order is little-endian on every Unity target, which is precisely the assumption `UnsafeAppendBuffer` made. `Update(byte)` → 1 byte, `Update(int/uint)` → 4, `Update(ulong)` → 8, matching §4.5's declared widths (`layerCount` (byte), `defaultLoop` (byte), `interpolation` (byte), `clipId` (uint64), `math.asuint(...)` (uint32) — all correct in `HashClip`). **No portability risk is introduced that §4.5's own approach did not already carry.**
- **Cross-Unity-version:** *not* established, by either approach, and nothing in the package would notice. See advisory **A7** — this is the one real gap in the determinism story and the fix is one line.
- **Canonical append order:** fully specified in code and matching §4.5 field-for-field, plus three array-length fields §4.5 omits (safe and strengthening — see **B3**). The order is *not* fully specified in the document any more, because the document still describes a mechanism the code does not use.

**4. Should it have been escalated? Yes, unambiguously.**

§9's rules are one sentence: *"any §8 contract change discovered mid-build is a **stop-the-line** doc amendment (this file), not a silent divergence."* The builder found a normative section that contradicts another normative section, chose between two amendment-requiring routes, extended a stream the document calls normative, and wrote its reasoning into an XML doc comment addressed to future code readers rather than into the architecture document or a report. The builder's termination explains the missing *report*; it does not explain the missing *amendment*, which §9 requires **before** building on the resolution. This is the same failure mode C1 was rejected for, in a module built after that rejection was filed.

**Amendment required (A5 below):** rewrite §4.5 point 3 to specify `xxHash3.StreamingState` with per-value `Update` calls, state that this is byte-identical to the `UnsafeAppendBuffer` formulation and is chosen because §1.3 denies `Authoring` unsafe code, and fold in the coverage fix from **B3**.

---

## Adjudications — the builder's other judgement calls

| # | Call | Ruling |
|---|---|---|
| a | `xxHash3.StreamingState` instead of `UnsafeAppendBuffer` | **ACCEPTED on substance, REJECTED on process.** See above. Amendment **A5**. |
| b | Hash the **finished blob** rather than the authoring graph | **ACCEPTED, and it is the better design.** §4.5 does not say which side to hash. Hashing the built bytes makes it structurally impossible for the dedup key to disagree with what it keys — including through the canonicalisation the builder itself performs (deg→rad, clamped blends, resolved loop mode). Worth recording in the §4.5 amendment as normative. |
| c | Array counts added to the hash stream beyond §4.5's list | **ACCEPTED on substance** (prevents shape aliasing; the hash is only ever compared to itself), **REJECTED on process** — an unrecorded edit to a stream §4.5 calls normative. Fold into **A5**. |
| d | `clips` emitted in id-ascending order | **REJECTED as silently accepted.** Correct against §4.5.1, contradicts the C1-gate-approved §4.2 doc, and the builder documented the contradiction instead of escalating it. See **B2**. |
| e | V08 gated behind two optional parameters that `Build` never sets | **REJECTED.** The engineering (the Authoring assembly must not guess a hash it cannot compute) is right; freezing the resulting silence into a passing test instead of filing the unimplementable §3.5 row is not. See **B1**. |
| f | Origin-centred rest box for untracked targets | **REJECTED as silently accepted.** The only implementation M1 permits, so the substance is not the builder's error — but discovering that a normative section cannot be implemented is the textbook stop-the-line trigger. See **B4**. |
| g | `LoopMode.UseClipDefault` on a `ClipAsset` coerced to `Once` at bake | **ACCEPTED with reservation.** §4.2 requires a resolved mode in the blob and `UseClipDefault` would be circular, so *something* must happen. But §3.2 lists the authoring field's legal values as "Once, Loop, PingPong", making `UseClipDefault` illegal authoring data that silently changes meaning at bake. Advisory **A3**: it wants a V-code, not a coercion. |
| h | `StableIdUtility` wholly in `Runtime/Identity/` | **ACCEPTED. No §1.2 amendment needed.** §1.2 names it there verbatim; `System.Guid` needs no asmdef reference, so §1.3 permits it; §8 M1 explicitly scopes M1 as "asmdef: Authoring, **plus identity structs in Runtime/Identity**". The zero-retry loop for the reserved 0 value is a correct small addition to §3.4's `Fold(Guid.NewGuid())`. Advisory **A8** only. |
| i | Negative `boundsExtents` clamped at bake rather than rejected by a V-code | **ACCEPTED.** §3.1 states the constraint but §3.5 assigns it no code. Clamping is fail-soft and cannot break existing content; a new error code could. Correct instinct. |
| j | `Awake` + `OnEnable` + `Reset` minting, beyond §3.4's `OnValidate` | **ACCEPTED with a caveat that needs a spec answer.** Broader coverage is right — it is what makes the tests' `CreateInstance` path work without an editor factory. But nothing persists a lazily minted id (see **A1**), so the extra hooks convert "asset with no id" into "asset with a *different* id every session", silently. §3.4 must name who persists. |
| k | `[CreateAssetMenu]` on Rig/Clip/ClipSet | **ACCEPTED.** M5 owns inspectors and the "New Clip in Set" action, but without this there is no way to create an asset before C7, and the attribute is `UnityEngine`, not `UnityEditor`. Correctly omitted from `VatTextureSetAsset`, which is generator-owned. |
| l | Two sort comparators with no tie-break (`CompareTargetsByStableId`, `CompareClipsByStableId`) | **ACCEPTED, but undocumented.** `List<T>.Sort` is unstable, so these are total orders **only because V05 rejects duplicate ids** — a real cross-rule dependency with no comment on it. Advisory **A9**. |

---

## Doc amendments required before C3 (stop-the-line, product-owner sign-off)

| # | Section | Amendment |
|---|---|---|
| **A5** | §4.5 point 3 | Replace the `UnsafeAppendBuffer` mechanism with `xxHash3.StreamingState` + per-value `Update`, noting it is byte-identical and is required because §1.3 denies `Authoring` `allowUnsafeCode`. Record that the finished blob (not the authoring graph) is what gets hashed. Fold in the coverage fix and the three array-count fields (**B3**, adjudications b/c). |
| **A10** | §4.5 point 3 | Extend the normative stream to cover `sortedTargetIds`, `targetBoundsExtents`, all four `vatInfo` fields and `debugName`; bump `ClipRegistryBuilder.SchemaVersion`; add the two missing negative determinism fixtures (**B3**). |
| **A11** | §4.2 / §4.5.1 | Decide whether `clips` is id-ascending (delete `clipIndexById`, simplify `TryResolveClip`) or authoring-ordered (stop sorting in `BuildCanonicalClips`). Must land before C3 bakes the layout into subscenes (**B2**). Recommendation: id-ascending, delete the indirection. |
| **A12** | §3.5 V08 row | State that V08 is evaluable only where the source hash can be recomputed — i.e. the Editor asmdef — and that entity baking cannot judge it; or specify the mechanism by which a recomputed hash reaches the baker. Correct the CHANGELOG (**B1**). |
| **A13** | §4.2 / §4.6 / §5.8 | Resolve the offset-space vs actor-space frame for `ClipBlob.localBounds`, and make §5.8's *"plus rest bounds"* concrete by naming the component that carries it (**B4**). |
| **A14** | §3.4 | Name the party responsible for **persisting** a lazily minted `stableId`; the Authoring assembly cannot call `EditorUtility.SetDirty` (**A1**). Also soften "with both asset paths" to asset *context* — paths are Editor-only (**A4**). |
| **A15** | §3.1 | Fix the dangling cross-reference *"enforced at bake per §4.5"* — §4.5 is determinism; the validation gate is §3.5 / §4.1 / §8 M1. |

---

## Advisories (non-blocking)

| # | Finding |
|---|---|
| **A1** | **A lazily minted id is never persisted.** `EnsureStableIds` runs in `OnEnable`, so an asset that reaches the Authoring layer with `stableId == 0` — a hand-written `.asset`, a pre-toolkit file, a migration artifact — is given a **fresh id on every domain reload**, none of them written to disk, with no warning. That is the exact opposite of the stability §3.4 promises, and it is invisible: the asset always *looks* identified. No fixture can catch it; none touch `AssetDatabase`. Fixed by the C7 factories plus a `SetDirty` path, but §3.4 must say so (**A14**). |
| **A2** | The §8 M1 "serialize→deserialize fixture" is `Object.Instantiate`, which exercises Unity's serialization backend for the fields but never disk. It does not prove a 64-bit `ulong` survives a YAML `.asset` round trip, nor that `internal` fields serialize under the package's asmdef. A one-line `AssetDatabase.CreateAsset` + `Resources.UnloadAsset` + reload fixture would close it — Tests.EditMode already references the Editor assembly. |
| **A3** | `LoopMode.UseClipDefault` authored on a `ClipAsset` is silently rewritten to `Once` at bake. §3.2 lists the field's legal values as "Once, Loop, PingPong", so this is illegal authoring data being silently reinterpreted rather than reported. A new V-code (or a widened V01) would surface it in the inspector where the author can see it. |
| **A4** | §3.4 requires the bake failure to name **"both asset paths"**; V05's message names both asset *names*. Paths need `AssetDatabase`, which §1.3 forbids here. `ValidationMessage.assetContext` carries the object so M5 can resolve the path at display time — the right structure, but §3.4's wording should follow the reality (**A14**). |
| **A6** | `ValidateSet` gained two optional parameters (`vatSourceHashRecomputed`, `recomputedVatSourceHash`) beyond §8 M1's EXPOSES signature. Source-compatible and defensible, but §8 EXPOSES lists are the module contract and this one now understates the surface. |
| **A7** | **Determinism is only ever proven against itself, within one session.** Every fixture compares two builds made seconds apart in one process. Nothing pins a literal expected hash, so a future change to Unity's `xxHash3`, to the Collections package, or to the append order would silently re-validate itself — while invalidating every already-baked subscene in the host project, with no red test anywhere. §4.5 promises byte-identical output *"on every machine"*; one `Assert.AreEqual(new Hash128(…), …)` golden constant for the rich fixture is what would actually defend that promise. Strongly recommended, and cheap. |
| **A8** | `StableIdUtility`'s "runtime half" (§1.2) never materialised — the whole class is in the Runtime assembly and **zero Runtime code calls it** (grep-verified: five call sites in `Authoring/Assets/`, seven in tests). It allocates a `Guid` and a 16-byte array, so it can never be Bursted. It is M1 code occupying M3's assembly and enlarging the shipped runtime API surface. Legal per §1.2 and not worth churning now; worth revisiting at C8's API review. |
| **A9** | `CompareTargetsByStableId` and `CompareClipsByStableId` carry no tie-break, unlike the five comparators that do. They are total orders **only because V05 rejects duplicate ids** — a genuine cross-rule dependency with nothing documenting it, in front of an unstable `List<T>.Sort`. One comment, or a defensive tie-break, removes a future footgun. |
| **A10a** | NaN `duration` slips through V01 (`float.NaN < 0.001f` is `false`), then propagates through `math.clamp` into the blob's `defaultBlendIn`/`defaultBlendOut` and into the hash. NaN `normalizedTime` **is** correctly caught, because V04 is written as `>= 0f && <= 1f`. Writing V01 the same way (`!(duration >= MinimumDuration)`) would close it. |
| **A11a** | `ClipBlob.debugName` uses `CopyFromTruncated(clip.name)`, so a clip's *asset name* becomes baked content. Combined with **B3**, renaming a clip changes the blob but not its dedup key, so the stale name persists after a rebake. Cosmetic, but it will confuse someone reading a log. |
| **A12a** | `AnimTechnique` — carried forward from C1's **N2** as "deferred to C2" — still has zero references anywhere in the package. C2 did not wire it in. It is now public API of a shipped 0.3.0 package with no consumer. |

---

## Required before this gate can close

1. Product-owner sign-off on amendments **A5, A10, A11, A12, A13, A14, A15** — of which **A11** (blob layout) and **A13** (bounds frame) are the two that C3 cannot start without.
2. Fix **B1** (V08 reachability + CHANGELOG), **B2** (blob ordering, per the chosen amendment), **B3** (hash coverage + `SchemaVersion` bump + two negative fixtures), **B4** (bounds frame, per the chosen amendment).
3. Fix **A7** (golden hash constant) — it is one assertion and it is the only thing that would defend §4.5's "on every machine, in every version" claim.
4. Re-review of the changed surface, then the product owner's Editor compile + Test Runner run.

**A1**–**A4**, **A6**, **A8**, **A9**, **A10a**, **A11a**, **A12a** may be carried as tracked backlog into C3 at the product owner's discretion, except that **A1** should be answered in the §3.4 amendment while the identity scheme is still being edited.

---

## Rework record (2026-07-29)

Product owner approved all three decisions: **id-ascending clips with `clipIndexById` deleted**, **blob keeps offset-space bounds with C3 combining rest poses**, and the remaining amendments/fixes as one batch. The rework agent was terminated by a session limit partway through; the coordinator completed the items below directly. The user is away from the machine, so **none of this has been compiled or test-run** — see "Outstanding" below.

### Landed

| Item | Status |
|---|---|
| A11 · delete `clipIndexById`, clips id-ascending | **Done.** Removed from `ClipRegistryBlob`; `TryResolveClip` returns the binary-search position; builder, `TestBlobFactory`, `ClipRegistryUtilTests`, `ClipRegistryBuilderTests`, `DataContractTests` and `BlobSignature` all updated. §4.2 amended and reconciled with §4.5.1. |
| A13 · `localBounds` → `offsetBounds` | **Done.** Renamed through runtime, builder and all tests. §4.2, §4.6, §5.8 amended; §5.2 gains `ActorRestBounds`, the component §5.8 assumed but never defined. The bake's union is now normatively offset space, with actor-space bounds owed by the entity baker (M2). |
| A10 · hash coverage | **Done.** `sortedTargetIds`, `targetBoundsExtents`, all four `vatInfo` fields and `debugName` are in the stream; every array is preceded by its element count; `schemaVersion` bumped to 2. §4.5 amended with the full normative order. |
| A5 · hash mechanism | **Done.** §4.5 now documents the `xxHash3.StreamingState` accumulation actually used, records why the `UnsafeAppendBuffer` formulation is unimplementable in an assembly without unsafe code, and states the two are byte-for-byte equivalent. |
| A12 · V08 scope | **Done.** §3.5 amended: V08 is editor-only and *silent* at bake, not "downgraded to Warning". The CHANGELOG's false claim corrected. |
| A14 · id persistence | **Doc only.** §3.4 amended to make persisting a minted id normative and to require the Authoring layer to surface the mint so the Editor layer can save it. **The code change is outstanding.** |
| A15 · §3.1 dangling cross-ref | **Done.** |
| Dedup API | **Done** (by the agent): `TryComputeContentHash` lets a baker probe the store without allocating a blob it may discard. |
| The missing invariant | **Done.** Four fixtures mutate a target id, `vatInfo.textureWidth`, the VAT bone count and a clip name, asserting the blob signature *and* the hash both change. Each fails against the pre-A10 stream. The shared helper asserts the signature first, so a mutation that silently did nothing fails as "the blob is identical" rather than masquerading as a hashing bug. |

### Outstanding

- **Not compiled, not test-run.** Every change above is hand-verified only.
- **Golden hash constant** (A10's last clause) — deliberately not written: producing the literal requires executing the bake once, and inventing a value would be worse than the gap it closes.
- **Test strengthening not yet done:** the rigged cross-set dedup fixture; the two `SerializationRoundTrip_*` tests that use `Object.Instantiate` and never exercise YAML; the tests that pin nothing (`RenamingAssetsAndTargets_*`, `ReorderingTargetAndClipLists_*`, `NewClipIdAndNewTargetId_*`, V03, V04, V09, V14, the truncation fixture).
- **Coverage gaps not yet closed:** `VatFlavor.VertexPosition` branch, sprite track/key ordering assertions, empty clip set, the inert `spriteTracks.Reverse()` in the shuffle fixture.
- **Six doc/code contradictions** from the Reviewer-C scratchpad, and the dead-surface decisions (`mirrorPairs`, `boneTexture`, `runtimeMesh`, `VatTextureSetAsset.schemaVersion`, `LayerDefinition.defaultActive`, `ClipValidation.ValidateRig`).
- **Re-review** of the changed surface.

### Second rework pass (2026-07-29, coordinator, user away)

- **A14 implemented** — see the table above. Test count 176.
- **Dead key sort**: already resolved in the agent's pass; the builder no longer re-sorts keys and its remarks explain why (V03 makes non-ascending keys an error and `Build` gates on it first). The surviving sorts — targets, clips, tracks, events — are all live.
- **`ClipValidation.ValidateRig` retained**, not deleted: it is public surface of a validation library and a consumer integrating the package may legitimately call it. Deleting published API is a larger call than this gate should make unilaterally. It still has no test.
- **Caught before it reached the user**: the new interface's XML doc referenced the literal token `UnityEditor`, which C0's conformance test (c) scans for as plain text outside `Editor/` and `Tests/`. It would have failed the suite despite being only a comment. Rephrased.

Still outstanding, unchanged: the golden hash constant (needs one execution to produce the literal); the rigged cross-set dedup fixture; the two `SerializationRoundTrip_*` tests that use `Object.Instantiate` and never exercise YAML; the seven tests that pin nothing; four coverage gaps (`VatFlavor.VertexPosition`, sprite ordering, empty clip set, the inert `spriteTracks.Reverse()`); six doc/code contradictions; and the remaining dead-surface decisions. **Nothing in either rework pass has been compiled or run.**
