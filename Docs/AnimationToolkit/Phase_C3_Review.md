# Phase C3 Review — M2 entity-baking slice (Reviewer B: spec conformance & judgement calls)

**Date:** 2026-07-30
**Scope:** commits `81c82f3` and `026a902` only (`git diff ec44226..HEAD`). `VatTextureBaker` is C6 and out of scope. Test integrity and code quality are Reviewer A's; this review covers **conformance to Phase B §8 M2, §4.1, §4.4, §4.5, §4.6/A13, §5.2, §5.3, §1.2/§1.3, §9's C3 row and §11**, plus the four adjudications the coordinator escalated.

**Evidence status (given, not re-derived):** the package compiles clean; 192 EditMode and 13 PlayMode tests pass, PlayMode run in-editor. Passing tests are not evidence of conformance. Every acceptance bullet below was traced to the specific assertion that does or does not discharge it.

**Method:** §8 M2 read verbatim and split into eight obligations; §5.2's root and part inventories enumerated and diffed component-by-component against `ActorBaker.Bake` and `RigTargetBaker.Bake`; §4.5's probe/build/register pattern traced through `TryAcquireRegistry` and `ClipRegistryBuilder`; `ComputeActorRestBounds` traced against §4.6/A13 including the transform-chain walk and the empty case; every `Debug.Log*` call site in a `[BurstCompile]` context checked against the shipped Burst 1.8.29 `csharp-string-support.md` in `Library/PackageCache`; the C2 re-review's "C3 handoff notes" and "Residual items" checked one by one; `git status`/`git diff --stat` checked for out-of-package writes; §4.5 spec text read via heading grep + offset reads only (never loaded whole).

---

## Verdict summary

**NO — the C3 gate cannot close.**

The engineering is the strongest of the phase so far. The blob-dedup pattern is used correctly with the right API, the actor-space rest bounds are genuinely computed by walking the transform chain with bake dependencies on the intermediate pivots, the single-threaded resolve pass is a correct and well-argued deviation from a parallel schedule, and the `RigPartBakeLink` `[BakingType]` (not `TemporaryBakingType`) reasoning is right and would have been an easy thing to get wrong. Six of eight §8 M2 obligations are satisfied outright.

It is blocked on six items, five of them cheap. The two with real teeth are:

- **the §5.2 *part* archetype is asserted nowhere**, including `PostTransformMatrix`, which §4.1 names as the fix for the audit's dead-scale regression and which this module delegates entirely to an Entities behaviour it neither controls nor tests; and
- **the one §4.4 branch the acceptance bullet actually specifies has no test**, because the fixture cannot construct a material with a `_VatBoneTex` slot — so the specified comparison is dead code and the "correctly configured material stays silent" case is unverified.

Alongside those, the module resolved at least four spec/reality conflicts silently in code rather than escalating them. That is the exact failure that sank C1 and C2, and it recurred here despite the C2 gate closing on the strength of a coordinator who named his own defects plainly.

---

## Checklist

| # | Obligation | Source | Verdict | Justification |
|---|---|---|---|---|
| 1 | Root archetype: every §5.2 component present | §8 M2, §5.2 | **PASS** | All 13 root types produced by `ActorBaker.Bake` (`ActorBaker.cs:72–103`). Enumerated against §5.2 in the table below; nothing missing. |
| 2 | Root archetype asserted "**exactly**" | §8 M2 | **PARTIAL** | `BakingAnActor_ProducesTheSection52RootArchetype` asserts presence of 8 types and the 5 enableable states. It never asserts *absence* of anything unlisted, so "exactly" is proven in one direction only. `AnimLod` is the sole excess-component check, and it is in a different test. |
| 3 | Enableable initial states: `RigBindingUninitialized` enabled, `AnimationCommandPending`/`AnimEventsPending` disabled, `AnimVisible` enabled, `BoundsDirty` enabled | §8 M2, §5.2, §5.3, §5.8, §5.9 | **PASS** | Baked at `ActorBaker.cs:75–87`; asserted individually with contract-quoting messages at `ActorBakingAcceptanceTests.cs:76–90`. Correct in code and correctly pinned. |
| 4 | **Part** archetype per §5.2 | §5.2, §4.1 | **FAIL** | See **B2**. `RigPartBinding`, `TargetRestPose`, `TargetPose` are asserted; `AnimVisible`, `PostTransformMatrix` and all five technique material-property components are asserted nowhere. `PostTransformMatrix` is not even added by the baker — it is delegated to `TransformUsageFlags.NonUniformScale`. |
| 5 | `ActorRestBounds` present and actor-space; far-offset part contained | §8 M2, §4.6/A13 | **PASS** | `ComputeActorRestBounds` (`ActorBaker.cs:348–396`) walks the chain via `TryGetRestPoseInActorSpace`; `BakingAnActor_ProducesActorSpaceRestBounds_ThatContainAFarOffsetPart` asserts `Max.y ≥ 12.4` (head at y=12) *and* `Min.x ≤ −0.85` (arm parented under the torso, not the root). The second assertion is the one that makes this a real test: it fails for any implementation that reads a single local position instead of accumulating the chain. Best-designed fixture in the module. |
| 6 | Bake dependencies on intermediate transforms | §4.6 implication, §4.1 | **PASS (with an inconsistency)** | `TryGetRestPoseInActorSpace` obtains every node via `GetComponent<Transform>(...)` — part, each ancestor, and the actor root — so moving any intermediate pivot retriggers the actor bake. But `RigTargetBaker.CaptureRestPose` reads `authoring.transform` **directly** (`RigTargetBaker.cs:171`), declaring nothing. See **A4**: one of the two files is wrong about how transform dependencies work. |
| 7 | `offsetBounds` never written into `RenderBounds` | §4.6/A13, C2 handoff #4 | **PASS** | Grep across the package: `offsetBounds` appears only in `ClipRegistryBuilder` (writing it), `ClipRegistryBlob` (declaring it), the content-hash stream, and XML comments. No `RenderBounds` write exists anywhere yet. C4 inherits the obligation. |
| 8 | Two actors sharing a set share one blob | §8 M2, §4.5 | **PASS** | `TwoActorsSharingAClipSet_ShareOneRegistryBlob` asserts `BlobAssetReference` equality, and `TwoActorsOnDifferentClipSets_DoNotShareABlob` pins the negative. The second is the one that proves set-scoping rather than accidental sharing. |
| 9 | Dedup pattern: `TryComputeContentHash` → probe → `Build` → `AddBlobAssetWithCustomHash` | §4.5, A16, C2 handoff #1 | **PASS (untested short-circuit)** | `TryAcquireRegistry` (`ActorBaker.cs:155–184`) uses exactly that order and — importantly — probes with **`Baker.TryGetBlobAssetReference`**, not `BlobAssetStore.TryGet`, so a store hit still registers this baker as a holder and incremental rebake of the *other* actor cannot collect the blob out from under this one. That is the correct API and easy to get wrong. However **no test distinguishes a probe hit from a `TryAdd` dedup** — see **A1**. |
| 10 | Store hit allocates nothing persistent; no hand-disposed or leaked registry blob | §4.1, §4.5 | **PASS** | Only two `Dispose()` calls exist in `Authoring/`, both in C2's `ClipRegistryBuilder` (`:163` disposes the probe's own temp blob, `:338` the `BlobBuilder`). Nothing disposes a store-owned registry blob; the miss path hands ownership over at `ActorBaker.cs:182` and never touches it again; the validation-failure path sets `registry = default` before returning. |
| 11 | Part entities carry `RigPartBinding` with correct dense indices, 3-target rig | §8 M2 | **PASS** | `BakingAnActor_ResolvesEveryPartToItsDenseTargetIndex`. The fixture authors targets as Head/Torso/LeftArm with ids 300/100/200, so authoring order and dense order disagree — a pass that returned list positions fails. Both ends of the binding are checked (root buffer entry *and* the part's own `targetIndex`/`actorRoot`). |
| 12 | Unknown-target part logs an error and is skipped without failing the bake | §8 M2, §4.1 | **PASS** | `APartWithAnUnknownTargetId_IsSkippedWithAnError_AndTheBakeStillSucceeds` (`LogAssert.Expect` + `partRefs.Length == 3`), reinforced by `AStrayPartDoesNotEnlargeTheRestBounds`, which pins the second-order consequence. |
| 13 | Material↔texture-set mismatch logs **exactly one** warning from `RigTargetBaker` | §8 M2, §4.4 | **FAIL** | See **B1**. The §4.4-specified comparison (`GetTexture(prop) != expectedTexture`) has zero coverage and the "correct material warns about nothing" case is unconstructible with this fixture. What is tested is an unspecified fourth branch. |
| 14 | `RigBindingBakingSystem` is a Burst-compiled, cross-entity, pure entity-data pass | §4.1 | **PASS** | `[WorldSystemFilter(BakingSystem)]` + `[UpdateInGroup(PostBakingSystemGroup)]` + `[BurstCompile]` on the system and both jobs. The interpolated `Debug.LogError` calls **are legal Burst** — see adjudication (3) below. |
| 15 | "…touches no managed objects"; all managed validation lives in the Bakers | §4.1, §4.4 | **PASS** | The material check is in `RigTargetBaker` where §4.4 puts it. Burst lowers `Debug.LogError` + literal interpolation to a native call; no managed `System.String` materialises, no allocation, no GC. The claim holds in substance. |
| 16 | §4.1's "errors reported with **entity + asset context**" | §4.1 | **FAIL** | See **B3**. Shipped errors carry an entity index:version and a 32-bit hash the user cannot invert into a GameObject. No `UnityEngine.Object` context, no name, no path. |
| 17 | Blob built in the **Baker**, not a baking system | §4.1 decision | **PASS** | `ClipRegistryBuilder.Build` is called from `ActorBaker.TryAcquireRegistry`. `RigBindingBakingSystem` builds nothing and only reads `ClipRegistry` through a `ComponentLookup`. The §4.1 division of labour is respected exactly, including the reason it exists (a baker may write only its own entity, so `RigPartRef`/`actorRoot`/dense index go to the system). |
| 18 | `RigBindingBakingSystem` will not fight §5.3's `RigBindingSystem` | §5.3 | **PASS** | Baked `RigBindingUninitialized` is enabled, so an ECB-instantiated copy starts enabled and C4's rebind runs. `RigPartBinding.targetIndex` is plain data and survives instantiation, which is what §5.3's `LinkedEntityGroup` match keys on. Contract is met; see C4 note 3 for the `−1` hazard. |
| 19 | Placement and asmdefs per §1.2/§1.3 | §1.2, §1.3 | **PASS** | All seven new sources under `Authoring/Baking/`, namespace `StitchPunk.AnimationToolkit.Authoring`, no `UnityEditor` reference anywhere in them. Test files under `Tests/PlayMode/`. No asmdef was edited. |
| 20 | PlayMode asmdef platform declaration matches reality | §1.3, §11.2 | **FAIL** | See **B5** / adjudication (d). `includePlatforms: []` (all platforms) for a suite whose harness reflects into `Unity.Entities.Hybrid` baking, which does not exist as a runnable path in a player. |
| 21 | §9's C3 row DoD: "M2 baking acceptance green (archetype assertions, blob sharing, dense-index resolution)" | §9 | **PARTIAL** | Blob sharing and dense-index resolution are green and meaningful. "Archetype assertions" are root-only and presence-only. |
| 22 | §9's rule: contract changes are stop-the-line doc amendments, never silent divergence | §9 | **FAIL** | See **B4**. Four unescalated divergences. |
| 23 | Tests land in the same change set | §9 | **PASS** | Both commits carry their tests. `026a902` exists solely to close an uncovered acceptance bullet, which is the right instinct. |
| 24 | C2 handoff #1 — canonical baker pattern used as specified | C2 re-review | **PASS** | See row 9. |
| 25 | C2 handoff #2 — `ActorRestBounds` produced | C2 re-review, R8 | **PASS** | Produced, documented, and tested with a fixture designed to fail the offset-space mistake. |
| 26 | C2 handoff #3/#4 — dense clip index semantics respected; `offsetBounds` never used as actor space | C2 re-review, A11/A13 | **PASS** | `SeedStartingLayers` resolves `clipIndex` **in the same pass that builds the blob** (`ActorBaker.cs:271–287`), never carrying an index across bakes — precisely A11's requirement. `ComputeActorRestBounds` reads `targetBoundsExtents` (rig data), never `offsetBounds`. `StartingLayerState`'s XML states the A11 renumbering rule correctly. |
| 27 | C2 handoff #5/#6 — `SchemaVersion` 2 and the golden constant untouched; no bake-time V08 check added | C2 re-review | **PASS** | Neither `ClipRegistryBuilder` nor `ContentHashGoldenTests` was touched by C3. No VAT-staleness check was introduced. |
| 28 | C2 residuals R1 and R4 ("should be swept in C3's first commit") | C2 re-review | **PASS (pre-closed)** | Both were closed by C2's own residual commit `ec44226`, not carried. §4.5 is now lettered 3a–3d; `ContentHashGoldenTests` was corrected there. Nothing was owed. |
| 29 | Nothing outside the package modified | §1.2, review scope | **PASS** | `git diff --stat ec44226..HEAD` is 22 files, all under `Packages/com.stitchpunk.dotsanimationtoolkit/`. `git status --short` on the package is empty. The host-repo modifications in the working tree predate C3 and are untouched by these commits. |
| 30 | Shipped package metadata describes shipped content | §8 M6, §9 | **FAIL** | See **B6**. `package.json` and `CHANGELOG.md` both still say the package "drives no entities yet" and covers build steps C1–C2. |

### §5.2 root archetype, component by component

| §5.2 root type | Baked | Where | Asserted |
|---|---|---|---|
| `ClipRegistry` | yes | `ActorBaker.cs:72` | yes (+ `IsCreated`) |
| `PlaybackLayer` buffer (one per rig layer) | yes | `:73`, `AddPlaybackLayers` | yes (length, seeded clip, dense index, `Active` flag, unseeded layer stays empty) |
| `AnimationCommand` buffer | yes | `:74` | yes (presence) |
| `AnimationCommandPending` (disabled) | yes | `:75–76` | yes |
| `AnimEventOutput` buffer | yes | `:77` | yes (presence) |
| `AnimEventsPending` (disabled) | yes | `:78–79` | yes |
| `RigPartRef` buffer | yes | `:80` | yes (presence + contents) |
| `RigBindingUninitialized` (enabled) | yes | `:85` | yes |
| `AnimVisible` (enabled) | yes | `:86` | yes |
| `BoundsDirty` (enabled) | yes | `:87` | yes |
| `ActorRestBounds` | yes | `:89–92` | yes (far-offset + nested-parent + empty cases) |
| `SampleSettings` | yes | `:93–97` | presence only; neither `rateHz` nor `phase01` is ever asserted |
| `AnimLod` | **conditional** on `addDistanceLod` | `:100–103` | yes, both branches + initial level 0 |
| `VatTextureBinding` | yes | `:98` | presence only; `setKey` and both texture refs never asserted |

Unlisted extras: none. `PlaybackLayer` seeding fully overwrites the `ResizeUninitialized` allocation (every field is either assigned or zeroed by the object initialiser), so no garbage survives — checked explicitly.

`AnimLod`'s conditionality is a documented divergence from §5.2's "complete inventory", but it is directly implied by §8 M2's `bool addDistanceLod` field and by §5.10's "optional". Not a defect; it **is** a C4 handoff item (see note 5).

### §5.2 part archetype, component by component

| §5.2 part type | Baked | Where | Asserted |
|---|---|---|---|
| `RigPartBinding` (`actorRoot`, `targetIndex`) | yes | `RigTargetBaker.cs:75–79`, completed by the system | yes |
| `TargetRestPose` | yes | `:87–88` | presence only — the captured values (`localPosition`, the quaternion-derived signed `rotationZ`, `scale`, `restSliceIndex`) are **never** asserted |
| `TargetPose` (seeded from rest) | yes | `:89–96` | presence only |
| `VatDriven` (VatMesh only) | yes | `:210–213` | presence only, in one test |
| `AnimVisible` (propagated) | yes | `:99` | **never** |
| `PostTransformMatrix` (identity) | **not added** — delegated to `TransformUsageFlags.NonUniformScale` | `:70–71` | **never** |
| `SpriteSliceProperty` / `AtlasFrameProperty` (FlipbookPlane) | yes | `:202–203` | **never** — no fixture ever bakes a `FlipbookPlane` part |
| `VatFrameAProperty` / `VatFrameBProperty` / `VatBlendProperty` (VatMesh) | yes | `:207–209` | **never** |

This is defect **B2**.

---

## Adjudications

| # | Question | Ruling |
|---|---|---|
| **(a)** | `AuthoringPathHash` replacing `GetInstanceID`/`EntityId`; is a path hash an acceptable source for `SampleSettings.phase01`; is the rename/reparent consequence acceptable or a spec amendment; is the hash well-formed? | **Technically correct, correctly motivated, well-formed — but it is an unrecorded spec amendment.** See below. |
| **(b)** | May a shipping commercial package's test suite reach Unity's baking entry points by reflection; is the version guard adequate? | **Permissible, with three conditions. The version guard is not adequate — there isn't one.** |
| **(c)** | Is `[System.Serializable]` on `SampleSettings`, a C1/M3 type, legitimate from C3, or should it have been escalated? | **Substantively required by §8 M2 and harmless; process-wise it should have been recorded, by the project's own precedent.** |
| **(d)** | PlayMode asmdef is all-platforms per §1.3 but the suite is editor-only. Editor-restrict, move to EditMode, or document? | **Editor-restrict the PlayMode asmdef**, with a §1.3 amendment and a C0 conformance-test update, *plus* documentation. Not "document only". |

### (a) `AuthoringPathHash` — correct engineering, missing paperwork

**The problem is real and the diagnosis is right.** `Object.GetInstanceID` is deprecated and its successor `EntityId` is documented as no longer representable by an `int`; both are assigned fresh on every project load. Baking either into entity data makes the same prefab produce different bytes every session, which destroys the reproducible-bake property §4.5 spends an entire section and a pinned golden constant defending. Replacing them was not optional.

**Is a path hash an acceptable source for `phase01`?** Yes. `phase01` has no visual, gameplay, or identity meaning — §5.6 uses it only to stagger which frame an actor re-samples on, and `RigBindingSystem` re-derives it per instance at spawn (§5.3/§5.6), so the baked value only ever governs entities that come straight out of a subscene and are never instantiated. For those, two actors cannot share a hierarchy path, so the hash distinguishes them, which is the entire requirement. The property the value must have is "stable across bakes and different between siblings", and a path hash has exactly that property while an instance id has neither half.

**Is the hash well-formed?** Yes, with two notes.
- Correct: offset basis `2166136261`, prime `16777619`, XOR-then-multiply (FNV-**1a**, not 1), sibling index folded in so identically named siblings diverge, `'/'` separator so `A/BC` and `AB/C` cannot alias, null-safe, walks to the scene root. `(pathHash & 0x00FFFFFF) * (1f/16777216f)` yields `[0,1)` — no off-by-one at either end.
- Note 1 (cosmetic): it hashes UTF-16 `char` units, not bytes, so it is an FNV-1a *variant*, not canonical FNV-1a. Determinism is unaffected. The XML's flat "The algorithm is FNV-1a" overstates it by one word.
- Note 2 (quality): FNV-1a's avalanche is weakest in its **low** bits, and the code takes the low 24. `(pathHash >> 8) & 0x00FFFFFF` would spread short sibling names better for the same cost. Non-blocking; phase collisions merely mean two actors sample on the same frame.

**Is the rename/reparent consequence acceptable?** Yes — the value carries no meaning, so a changed phase is invisible. But the consequence is not the point. **The point is that §5.6 and §5.2 never said where `phase01` comes from, and C3 invented a derivation rule, a new internal type, and a new determinism property without filing an amendment.** This is a determinism rule, in a package whose determinism story is normative, pinned by a golden constant, and the subject of two prior amendments. It belongs in the document. **Ruling: the code stands as written; a one-paragraph amendment to §5.6 (or §4.5) is required before the gate closes** — stating that the baked `phase01` is derived from an FNV-1a hash of the authoring hierarchy path, that it is therefore stable across sessions and machines but *not* across renames or reparenting, and that this is acceptable because `RigBindingSystem` re-derives it per instance. Cost: one paragraph. Not filing it is the C1/C2 pattern.

### (b) Reflection into Unity's internal baking API from a shipped test assembly

**The constraint is genuine, not laziness.** `BakingUtility` and `BakingSettings` are `internal` to `Unity.Entities.Hybrid`; `BakingSystem.GetEntity(GameObject)` is an internal member of a public class. Unity's own baking tests reach them via `[InternalsVisibleTo]`, which a third-party package cannot obtain. The only fully public alternative is to author a real `SubScene` asset and let it stream — which requires `AssetDatabase` (Editor-only, and §1.3 forbids it in this assembly), an on-disk scene, and Play-mode streaming, and would test Unity's scene pipeline more than it tests `ActorBaker`. **There is no public alternative that discharges §8 M2's acceptance list.**

**May a commercial package do this?** **Yes, in test code only, with conditions** — and note that these tests do ship: the package uses `Tests/`, not `Tests~/`, so a licensee who adds the package to `testables` compiles and runs this reflection in their project. That raises the bar. The implementation is careful in the right places: handles resolved once in a static constructor, each with a named `AssertResolved` message, exact-signature `GetMethod` overloads so a signature change fails loudly rather than binding to a wrong overload, and `ExceptionDispatchInfo` re-throwing the bake's real exception instead of a `TargetInvocationException` wrapper. That is better hygiene than most in-house harnesses.

**Is the version guard adequate? No — there is no version guard.** What exists is a *member-presence* check that runs at first use. It cannot distinguish "Entities moved this member" from "Entities changed its semantics", and it fires as a `TypeInitializationException` from a static constructor, which NUnit reports once per test in the class with the real message buried in `InnerException`.

**Conditions for acceptance:**
1. Pin the contract: `package.json` currently declares `"com.unity.entities": "6.5.0"` — record in `BakingTestWorld`'s XML and in `Documentation~` the exact Entities version the reflection was verified against, and state that a major/minor Entities bump requires re-verifying this file.
2. Make the failure legible: catch the resolution failure in `[OneTimeSetUp]` (or a lazily-initialised guard) and `Assert.Ignore`/`Assert.Fail` with the guidance message, rather than letting a static-constructor throw propagate.
3. Say so where a licensee will read it: one line in `Documentation~` and the CHANGELOG that the baking test suite depends on Entities internals and can red on a Unity upgrade without the package itself being broken.

None of these blocks the gate on its own; (1) and (3) fold naturally into the **B6** documentation fix.

### (c) `[System.Serializable]` on `SampleSettings`

**Substantively: correct, required, and harmless.** §8 M2's EXPOSES list mandates `ActorAuthoring { … SampleSettings sampleOverride; … }` verbatim. Unity will not serialise or display a custom struct field without the attribute, so without it the spec-mandated inspector field does not function. The attribute has no runtime, layout, Burst, or ECS effect on an `IComponentData`. Refusing it would have meant either violating §8 M2's field list or shadowing the type with an authoring duplicate — both worse.

**Process: it should have been recorded, and the project's own precedent says so.** §8 gives M3 exclusive write access to §5.2's types, and §9 makes any §8 contract change a stop-the-line amendment. Two commits earlier, when C2 faced the *same* situation with `ActorRestBounds` (a §5.2 type that no shipped module owned producing), the resolution was defect **D8** with an explicit "add it as a recorded M3 addendum" and an amendment to §8 M2's ACCEPTANCE list. That is the standard this project set for itself sixty commits ago. A one-line note — "§5.2's `SampleSettings` carries `[System.Serializable]` because §8 M2 exposes it as an inspector field" — is all that was owed.

**Ruling: legitimate; keep the change; record it.** Folds into **B4**.

**Related advisory, not part of the ruling:** exposing `SampleSettings` as the inspector field means users see and can edit `phase01`, which `ActorBaker` unconditionally discards in favour of the path hash. The `[Tooltip]` says so, but a field the inspector offers and the bake ignores is a support ticket waiting to happen. The fix belongs to M5's custom inspector in C7 — noted there.

### (d) The PlayMode asmdef's platform declaration

The coordinator declined to decide this. It is correctly escalated and it is a live gate question, so I decide it.

**The facts.** `StitchPunk.AnimationToolkit.Tests.PlayMode` has `includePlatforms: []` — all platforms — per §1.3's "All (test framework standard)". Every test in it routes through `BakingTestWorld`, which reflects into `Unity.Entities.Hybrid`'s baking pipeline. Baking has no player-side equivalent; a player test run either fails to resolve the members or produces no bake. A licensee clicking "Run all tests (Player)" gets a wall of red that says nothing about the package's health.

**The three options, weighed:**

- *Move the baking tests to EditMode.* Technically attractive — every test is `[Test]`, not `[UnityTest]`, and nothing needs Play mode; `BakingUtility` works fine in EditMode. But it costs the **most** spec surgery: §8 M2 says "(PlayMode/baking tests)", §11.2 says "Baking tests (M2 acceptance) run here too", and §1.3's `Tests.EditMode` reference list has neither `Unity.Entities.Hybrid` nor `Unity.Transforms`, both of which `BakingTestWorld` needs. Three amendments plus a conformance-test update, to land the tests somewhere the document does not put them.
- *Document as editor-only and change nothing.* Cheapest, and the commit message already does it. But it leaves a **commercial** package shipping an assembly whose platform declaration is false. §1.3's own Editor-only row for the Editor asmdef exists precisely because the audit caught the host project shipping editor code as unrestricted — repeating the inverse mistake in the package that was built to fix it is not defensible. Documentation is a supplement here, not a resolution.
- *Editor-restrict the PlayMode asmdef.* Set `includePlatforms: ["Editor"]`, amend §1.3's one row, update C0's `PackagingConformanceTests` reference/platform assertion in the same commit, and add the Documentation~/CHANGELOG line.

**Ruling: Editor-restrict.** It is the only option that makes the shipped artifact honest, it touches exactly one spec row rather than three, and it keeps the tests where §8 M2 and §11.2 normatively put them. Nothing normative is lost: no acceptance bullet anywhere in §8 requires a *player* test run — M6's player evidence is a Windows build of the `VatCrowd` sample, not a test execution — and §11.2's future C4 content (World integration, the Burst-clean gate) is equally an in-editor PlayMode activity.

**One caveat C4 must accept along with this:** restricting the assembly restricts C4's M3 PlayMode suite too. That is fine and should be stated in the amendment, so C4 does not rediscover it and reopen the question.

---

## Blocking defects

### B1 — The §4.4 branch the acceptance bullet specifies has no test; the branch that is tested is unspecified

§8 M2: *"a material↔texture-set mismatch fixture logs exactly one warning from `RigTargetBaker` (§4.4)"*. §4.4 defines the mismatch precisely: *"logs a warning when the material's `_VatBoneTex`/`_VatPosTex` slot **differs from** the set's textures"*.

`ValidateVatMaterial` implements four branches: (i) no material to check → silent; (ii) no VAT texture set → warn; (iii) material has no `_VatBoneTex`/`_VatPosTex` **property** → warn; (iv) property present but bound to the wrong texture → warn. Only **(iv)** is the §4.4 comparison. Three tests exist, covering (iii), (ii) and (i).

**Branch (iv) is never executed by any test.** It cannot be, with this fixture: `ActorBakeFixture.CreateVatMaterial` builds a material from `Shader.Find("Unlit/Texture")`, which declares `_MainTex` and no `_VatBoneTex`, so every fixture material short-circuits at branch (iii). The fixture's own comment about the shader's slot being "renamed by the material's property block below" describes something that does not exist in the method — it sets `_MainTex` and nothing else.

Two consequences, and the second is the serious one:
1. The acceptance bullet is discharged by a branch the spec does not describe. `AVatPartWhoseMaterialLacksTheTextureSlot_LogsExactlyOneWarning` is a good test of a good extra check — but it is not the bullet.
2. **The correctly-configured case is unverifiable.** No fixture can produce a VAT part whose material *matches* its texture set, so nothing constrains this validator against false positives. `ValidateVatMaterial` could warn on every properly-set-up VAT part in the shipped package and the entire suite would stay green. For a commercial package this is the failure mode that matters most: `026a902`'s own commit message argues that "a validator that warns when there is nothing to compare trains people to ignore the warning that matters" — and then ships without testing that it stays quiet when everything is right.

**Remedy:** give the fixture a material that actually declares the property. A tiny hand-written `Shader` string compiled via `ShaderUtil.CreateShaderAsset` is Editor-only; the cleanest package-safe route is a minimal `.shader` test asset under `Tests/` declaring `_VatBoneTex` and `_VatPosTex`, or `Material.SetTexture` on a shader known to carry the name once M4's shaders land in C5. Then add two tests: (iv) the property is present and bound to the *wrong* texture → exactly one warning; and the negative — property present and bound to the set's own texture → **zero** warnings. The second test is the one worth having.

### B2 — The §5.2 part archetype is asserted nowhere, and `PostTransformMatrix` is not even produced by this module

§8 M2 requires the archetype "**exactly** (assert component-by-component)". For the root, that is discharged in one direction (presence, never absence). For the **part**, it is not discharged at all: five of eight §5.2 part types have no assertion anywhere in the suite — `AnimVisible`, `PostTransformMatrix`, `SpriteSliceProperty`, `AtlasFrameProperty`, and the three `Vat*Property` components (`VatDriven` gets a bare presence check in one test). No fixture ever bakes a `FlipbookPlane` part at all, so the entire flipbook branch of `AddTechniqueComponents` is untested code in a shipped package.

The severe item is **`PostTransformMatrix`**. §4.1 mandates it explicitly, and names why: it is the fix for *"the audit §2.1 dead-scale regression"*. §5.2's part list repeats it. `RigTargetBaker` **does not add it**. It requests `TransformUsageFlags.Dynamic | NonUniformScale` and relies on Entities' transform baking to add `PostTransformMatrix` with `float4x4.Scale(localScale)` — identity for a unit-scaled part. The reasoning in the XML is plausible and, if correct, is better than adding it by hand, because it keeps `LocalTransform.Scale` at 1 and gives every part kind the channel `TransformApplySystem` writes.

But it is an **assumption about third-party behaviour on which the package's entire scale-and-flip feature rests, and nothing verifies it.** If Entities elides `PostTransformMatrix` for a part whose authoring scale is uniform — which is the ordinary case for every cutout part a user will ever author — then C4's `TransformApplySystem` writes to a component that is not there, and the package silently reintroduces the exact regression §4.1 says it fixes. That failure would surface in C5/C6 as "flip doesn't work", days downstream, with the cause three modules back.

**Remedy:** one assertion, in `AssertPartBound` or beside it — `Assert.IsTrue(entityManager.HasComponent<PostTransformMatrix>(partEntity))` on a part authored at **unit scale** — plus `AnimVisible` on the same part, plus one test that bakes a `FlipbookPlane` part and a `VatMesh` part and asserts their technique components and initial values. If the `PostTransformMatrix` assertion fails, that is the finding, and it is far cheaper here than in C5.

### B3 — §4.1's "entity + asset context" is not delivered, and the shortfall was not escalated

*(This is the adjudication the coordinator asked for on the `Debug.LogError`-inside-Burst question. The Burst-legality half resolves in the code's favour; the reporting half does not.)*

**Does `Debug.LogError` with interpolated strings inside a `[BurstCompile] IJobEntity` satisfy "touches no managed objects"? Yes.** Verified against the shipped Burst 1.8.29 documentation (`Library/PackageCache/com.unity.burst@6bb9aca3ef38/Documentation~/csharp-string-support.md`), not from memory. Burst explicitly supports `Debug.Log(object)`, `Debug.LogWarning(object)` **and `Debug.LogError(object)`**; string literals and interpolated strings built from literals; and `string.Format(string, object[])` for more than three holes provided the array is constant-size and no hole contains control flow. All four call sites in `ResolveRigPartBindingsJob` comply: every interpolation hole is a built-in `int` or `uint` (`Entity.Index`, `Entity.Version`, `authoringPathHash`, `targetId`), no format specifiers, no `+` concatenation, no conditionals in holes, no struct `ToString()`. Burst lowers these to a native logging call — no `System.String` is materialised, no allocation occurs, nothing is GC-tracked. The §4.1 purity clause holds, and the shipped strings also satisfy this repo's own hard-won BC1343/BC1016/BC1352 rules.

**Is §4.1 self-contradictory in demanding both Burst purity and rich error reporting? Only partly — and the part that *is* achievable was not achieved.** §4.1 requires errors *"reported via `Debug.LogError` with entity + asset context"*. What a licensee actually gets is:

> `[DOTS Animation Toolkit] Rig part entity 42:1 (authoring path hash 2748291043) references target id 999, which the actor's rig does not declare. The part is skipped.`

A baking-world entity index:version is not surfaced anywhere in the Unity Editor, and a 32-bit FNV hash cannot be inverted into a GameObject. The user is told a part is broken and given no way to find it — in a hierarchy that may hold dozens. That is not "entity + asset context"; it is entity + an opaque number.

Only **one** half of §4.1's demand is genuinely impossible: the clickable `UnityEngine.Object` context argument (`Debug.LogError(object, Object)` is not on Burst's supported list and could not be, since it takes a managed reference). The other half is entirely achievable **without giving up one byte of Burst purity**: `FixedString` is Burst's documented mechanism for exactly this, and `RigPartBakeLink` is already a `[BakingType]` whose own XML notes that baking types never reach the built entity scene — so carrying a `FixedString128Bytes authoringPath` alongside the hash costs bake-time memory and nothing else. The message would then name the object.

So the contradiction is real but narrow, and C3 resolved it by silently downgrading the requirement rather than escalating the narrow part and fixing the rest.

**Remedy (both halves):**
1. Add a `FixedString128Bytes authoringPath` (or `FixedString64Bytes` name) to `RigPartBakeLink`, populate it in `RigTargetBaker` from the transform path, and interpolate it into all four messages. Keep the hash if it is wanted for correlation; it is not a substitute for a name.
2. Amend §4.1 with one sentence: a Bursted baking system reports context as a `FixedString` authoring path, not as a clickable `UnityEngine.Object` context, because the latter is a managed reference Burst cannot pass.

### B4 — Four spec/reality conflicts resolved silently in code, no amendment filed

§9: *"any §8 contract change discovered mid-build is a **stop-the-line** doc amendment (this file), not a silent divergence."* C1 and C2 were each rejected for breaching this. It recurred:

1. **`RigTargetAuthoring` ships three inspector fields §8 M2's EXPOSES sketch does not list.** The sketch is `{ RigAsset rig; uint targetStableId; TargetKind kindOverride; Material expectedMaterial; }`. Shipped: `useKindOverride`, `restSliceIndex`, `vatDrivingLayerIndex` in addition. Each is *justified* — `TargetKind` has no "unset" member so the override needs a switch; `TargetRestPose.restSliceIndex` and `VatDriven.layerIndex` are §5.2-mandated and have no other source — but EXPOSES is a normative shared table and this is the largest unrecorded divergence in the module.
2. **Two public package types absent from §8 M2's OWNS list:** `RigPartBakeLink` (public, shipped, a `[BakingType]` on the public API surface) and `StartingLayerState` (public, `[Serializable]`, part of `ActorAuthoring`'s inspector contract). OWNS names exactly five types plus the VAT four.
3. **`[System.Serializable]` added to an M3-owned type from C3** — adjudication (c). Correct, unrecorded.
4. **`SampleSettings.phase01`'s derivation rule invented** — adjudication (a). Correct, unrecorded.

A fifth, lower-grade item belongs here too: **§4.6's final paragraph says the entity-baking step produces actor-space bounds "by combining `offsetBounds` with the rest-pose positions… That result is carried by `ActorRestBounds`."** §5.8 and §8 M2 both say the opposite — that `ActorRestBounds` carries the *rest frame only* and `RenderBoundsUpdateSystem` performs the union at runtime. The code follows §5.8/§8 M2 (correctly — it is the only reading under which `BoundsDirty` and per-clip unions make sense), so two of three normative statements agree with the implementation and §4.6's sentence is the outlier. But C3 picked a winner between contradicting normative sections in a code comment instead of amending the loser.

**Remedy:** one amendment pass covering all five. §8 M2 EXPOSES gains the three fields; OWNS gains the two types; §5.2 or §5.6 gains the `phase01` derivation paragraph and the `[Serializable]` note; §4.6's last sentence is reworded to match §5.8. Doc only, no code change, perhaps thirty minutes.

### B5 — The PlayMode asmdef declares all platforms for an editor-only suite

Adjudication (d). **Remedy:** `includePlatforms: ["Editor"]` on `StitchPunk.AnimationToolkit.Tests.PlayMode`; amend §1.3's row for that assembly; update C0's `PackagingConformanceTests` platform/reference assertion in the same commit; one Documentation~/CHANGELOG line stating the baking suite runs from the Test Runner's PlayMode tab in-editor and cannot run in a player.

### B6 — Shipped package metadata now misdescribes the shipped package

Neither commit touched `CHANGELOG.md` or `package.json`. Both still assert, of a package that now contains a full entity-baking module:

- `package.json` `description`: *"version 0.3.0 contains the data and sampling layer plus the authoring assets, validation, and the deterministic clip-registry builder (build steps C1 and C2), and **drives no entities yet**; feature modules land in subsequent 0.x versions."*
- `CHANGELOG.md` `## [0.3.0]`: *"Phase C build step C2… **Entity baking**, systems, shaders, and editor tooling **still do not ship**; those land in build steps C3 through C8."* — and still *"66 EditMode tests"* against an actual 192 (this half is C2's carried **D9**, now compounded).

These are the two files a licensee reads first. §9 requires each step to land complete; M6 owns the files but C3 is the step that invalidated them.

**Remedy:** bump to `0.4.0` (or add a `## [0.4.0] - Unreleased` section), write the C3 entry, correct the test counts, and correct the `description`. Fold in the (b) and (d) disclosure lines while there.

---

## Advisories (non-blocking)

| # | Item |
|---|---|
| **A1** | **Nothing proves the store *hit* path is ever taken.** `TwoActorsSharingAClipSet_ShareOneRegistryBlob` is satisfied identically by (i) the second baker probing and short-circuiting and (ii) both bakers building and `AddBlobAssetWithCustomHash` deduping the second. Only (i) is the §4.5 pattern C2 added `TryComputeContentHash` (A16) specifically to enable, and only (i) avoids building the blob twice. A counting `IStableIdMintReporter`-style seam, or asserting on `ClipRegistryBuilder` invocation count, would pin it. As written, the API C2 was rejected-and-reworked to expose could be bypassed entirely without a red test. |
| **A2** | **`RigPartRef` ordering determinism is claimed in comments and tested nowhere.** `RigBindingBakingSystem`'s XML argues single-threading is required so "the resulting `RigPartRef` order must be the same on every machine for a given input". `IJobEntity.Schedule` iterates in chunk order, which follows entity-creation order, which follows baking order — deterministic in practice, but an emergent property of Entities, not a guarantee, and unasserted. Low impact: §5.3's `RigBindingSystem` rebuilds the buffer from `LinkedEntityGroup` at spawn anyway, so nothing durable depends on the baked order. Worth either a test or a softening of the comment. |
| **A3** | **Inactive parts may be bound but excluded from `ActorRestBounds`.** `RigBindingBakingSystem`'s queries carry `EntityQueryOptions.IncludeDisabledEntities | IncludePrefab`, so a disabled part *is* bound and will animate. `ComputeActorRestBounds` enumerates via `GetComponentsInChildren<RigTargetAuthoring>()` with default include-inactive semantics. If those defaults exclude inactive children, a bound, animating part contributes nothing to the culling box. Untested in either direction; worth one fixture with a disabled part. |
| **A4** | **The two bakers disagree about how transform dependencies are declared.** `ActorBaker.TryGetRestPoseInActorSpace` routes every node through `GetComponent<Transform>(...)` and documents that it does so *"taking a bake dependency on every transform it multiplies"*. `RigTargetBaker.CaptureRestPose` reads `authoring.transform` directly (`:171`). Either the dependency is needed — in which case moving a part in the Editor may not refresh its `TargetRestPose`, a silent incremental-baking staleness bug — or it is not, in which case `ActorBaker`'s rationale overstates. One of the two files is wrong; resolve and make them consistent. |
| **A5** | **Rest-bounds inflation ignores rotation.** Half-extents are scaled by `max(\|scale.x\|, \|scale.y\|, 1)` and translated, but a part rotated about z grows its axis-aligned footprint by up to √2 and the box does not account for it. This mirrors §4.6's own key-space rule, so it is an inherited spec gap rather than a new defect — but §4.6's rule was written for *offsets*, where rotation is small, and here it applies to a part's authored rest rotation, where it need not be. Flagging for a future §4.6 sweep. |
| **A6** | **`SampleSettings` and `VatTextureBinding` are presence-checked only.** No test asserts `rateHz` survives the `math.max(0f, …)` clamp, that `phase01` lands in `[0,1)`, that two differently-pathed actors get *different* phases (the whole point of the value), or that `VatTextureBinding.setKey`/textures mirror the set. `phase01` distinctness in particular is a one-line assertion for a property §5.6 depends on. |
| **A7** | **`AuthoringPathHash` takes the low 24 bits** of an FNV-1a hash, where FNV avalanche is weakest. `(pathHash >> 8) & 0x00FFFFFF` costs nothing and spreads short sibling names better. See adjudication (a). |
| **A8** | **`sampleOverride.phase01` is inspector-visible and unconditionally discarded** by the baker. The tooltip says so; a custom inspector hiding it is the real fix. C7 / M5. |
| **A9** | **A misconfigured actor produces N+1 errors.** If `clipSet` is null, `ActorBaker` logs once and returns, then every part under it logs *"has no baked actor to bind to"* from the Bursted pass. Correct behaviour, noisy presentation; consider suppressing the per-part message when the actor entity exists but has no `ClipRegistry` (already distinguishable at `RigBindingBakingSystem.cs:106`). |
| **A10** | **`TargetRestPose`'s captured values are never asserted** — notably the quaternion-derived signed `rotationZ`, which exists specifically because `localEulerAngles` would report −30° as +330°. That is a deliberate, subtle correctness choice with no test behind it. One fixture with a part rotated −30° would pin it. |
| carried | C2's open advisories are untouched and remain open: **D9** (CHANGELOG drift — now escalated into **B6**), **D10**, **D11**, **A2**, **A3**, **A8**, **A10a**, **A12a** (`AnimTechnique` still has zero references package-wide). |

---

## Can C4 build on this cleanly?

Yes. The entity data C4 needs exists, is shaped correctly, and the two contracts C4 depends on most — `RigBindingUninitialized` baked enabled and `RigPartBinding.targetIndex` as plain instantiation-surviving data — are both right and both tested. **B1**–**B6** are quality and paperwork blocks on the C3 gate, not structural blocks on C4; none of them changes an entity layout C4 would have to re-target, with the possible exception of **B2**'s `PostTransformMatrix` finding, which C4 needs resolved *before* it writes `TransformApplySystem`.

### C4 handoff notes

1. **Resolve B2's `PostTransformMatrix` question first.** `TransformApplySystem` is specified to write scale/flip through `PostTransformMatrix`, and this module does not add that component — it relies on `TransformUsageFlags.NonUniformScale` causing Entities to add it, including for unit-scaled parts. Assert it exists on a unit-scaled baked part before building on it. If it does not, the audit §2.1 dead-scale regression is back and the fix belongs in `RigTargetBaker`, not in C4.
2. **`ActorRestBounds` carries the rest frame only, not a combination.** `RenderBoundsUpdateSystem` must union it with the `offsetBounds` of every clip still referenced (current + blending previous) and write *that* into `RenderBounds`. Never write `offsetBounds` alone. §4.6's closing sentence reads as though `ActorRestBounds` already holds the union — it does not; follow §5.8 and §8 M2. (See **B4** item 5.)
3. **`targetIndex == -1` and `actorRoot == Entity.Null` are live states, not impossible ones.** A part whose target id does not resolve keeps them and still carries `TargetPose`, `AnimVisible` and its technique components. Every C4 system that iterates parts must skip `targetIndex < 0`, and `RigBindingSystem`'s `LinkedEntityGroup` rebuild must not add such parts to `RigPartRef` — otherwise a single content typo puts a `−1` index into the buffer that the baking pass carefully kept out.
4. **Do not rely on baked `RigPartRef` order.** It is chunk-iteration order (see **A2**). §5.3 rebuilds the buffer at spawn anyway; treat the baked order as arbitrary.
5. **`AnimLod` is opt-in and frequently absent.** `ActorBaker` adds it only when `addDistanceLod` is set, so `AnimLodDistanceSystem` and every LOD-reading path must query for it rather than assume §5.2's inventory is unconditional.
6. **An empty actor bakes a zero-extent `ActorRestBounds`, not an inverted one.** `MinMaxAABB.Empty` is deliberately converted to `{ Center = 0, Extents = 0 }`. `RenderBoundsUpdateSystem` will therefore see a legitimately degenerate box for a partless actor — handle it as "no extent", not as a sentinel.
7. **The baked `phase01` comes from a hierarchy-path hash and must be re-derived per instance at spawn** (§5.3/§5.6). Every ECB-instantiated copy inherits one identical baked value, so without the re-derivation an entire crowd samples on the same frame — which is the precise failure the field exists to prevent.
8. **`PlaybackLayer.previousLoop` is still unwritten** (carried verbatim from C2's handoff, still true). `CommandApplySystem` must set it at every crossfade start, before `layer.loop` is overwritten, or the outgoing clip reverts to its authored default mid-blend.
9. **Adjudication (d) restricts the PlayMode assembly to Editor.** C4's M3 PlayMode suite lands in that same assembly and is therefore in-editor only. This is intended; do not reopen it.

---

## Required before the C3 gate can close

1. **B1** — make branch (iv) of `ValidateVatMaterial` testable and test it, **and** add the negative case: a correctly-configured VAT material must produce zero warnings.
2. **B2** — assert the §5.2 part archetype: `PostTransformMatrix` on a unit-scaled part, `AnimVisible`, and the technique components for a `FlipbookPlane` part and a `VatMesh` part. Fix `RigTargetBaker` if the `PostTransformMatrix` assertion fails.
3. **B3** — carry a `FixedString` authoring path on `RigPartBakeLink` and name the object in all four Bursted messages; amend §4.1 to say that a Bursted baking system reports context as a `FixedString` path, not a clickable `Object`.
4. **B4** — one doc-amendment pass: §8 M2 EXPOSES (three `RigTargetAuthoring` fields), §8 M2 OWNS (`RigPartBakeLink`, `StartingLayerState`), the `phase01` derivation rule, `SampleSettings`'s `[Serializable]`, and §4.6's closing sentence.
5. **B5** — Editor-restrict `Tests.PlayMode`, amend §1.3's row, update C0's conformance test in the same commit.
6. **B6** — correct `package.json`'s description and `CHANGELOG.md`; add the (b) reflection-dependency and (d) editor-only disclosures.
7. Then: the product owner's compile + Test Runner run, and a short re-check of the changed surface.

**A1**–**A10** and the carried C2 advisories may be taken into C4 as tracked backlog at the product owner's discretion. **A1**, **A4** and **A6** are the three worth doing in the same pass — each is a handful of lines, and **A4** is a latent incremental-baking bug rather than a tidiness item.

---

## Closing note

Two things deserve to be said plainly, because a gatekeeper who only lists defects gives the coordinator no signal about what to keep doing.

`ComputeActorRestBounds` and its fixture are the best work in the phase. The spec asked for "a part offset well away from the origin"; the fixture supplies that *and* a part parented under another part, and the `Min.x ≤ −0.85` assertion catches a whole class of half-right implementations that the specified test would have passed. That is a fixture written by someone trying to fail his own code.

And the two deliberate escalations — (b) and (d) — are exactly the behaviour §9 asks for and exactly what C1 and C2 failed to do. The problem is that four *other* conflicts (**B4**) were resolved silently in the same commits. The instinct is clearly present; it needs to fire on the small divergences too, because those are the ones that accumulate into a document nobody trusts.

---

## Rework record — advisory pass (2026-07-31)

Taken in the same pass as the **B1**–**B6** blocking rework. Each entry names what
changed and, where an advisory was closed by argument rather than by code, why.

| # | Disposition | What changed |
|---|---|---|
| **A1** | **Closed — code + test** | `ClipRegistryBuilder.BuildInvocationCount` (internal, with `ResetBuildInvocationCount`) is the seam the reviewer asked for. `TwoActorsSharingAClipSet_BuildTheRegistryOnce_NotOncePerActor` bakes three actors on one set and asserts exactly one build. This is the only assertable difference between the two paths: blob equality is satisfied identically by probe-and-skip and by build-thrice-and-dedup, so without the counter the probe could stop matching `Build`'s key and every crowd would silently triple its bake work. The complementary unit-level guarantee — that the probe's key *is* `Build`'s key — is already pinned by `ContentHashGoldenTests.TryComputeContentHash_MatchesTheKeyBuildProduces_ForTheSameAsset`. |
| **A2** | **Closed — comment softened** | The claim that `RigPartRef` order "must be the same on every machine" was overstated: it is emergent from Entities' chunk-iteration order, not guaranteed, and §5.3 rebuilds the buffer from `LinkedEntityGroup` at spawn so nothing durable reads it. The XML now states the two reasons single-threading *is* required (cross-entity buffer append, duplicate-claim read), records the ordering as repeatable-in-practice-but-unspecified, and tells the reader not to rely on it. No test added, because there is now no claim to test. |
| **A3** | **Closed — real defect, fixed** | `GetComponentsInChildren<RigTargetAuthoring>()` excluded inactive children while the binding pass carried `IncludeDisabledEntities`, so a disabled part was bound and animated but contributed nothing to the culling box — wrong only after the part is re-enabled at runtime. Now `GetComponentsInChildren<RigTargetAuthoring>(true)`, pinned by `ADisabledPart_IsStillCoveredByTheRestBounds_BecauseItIsStillBound`. |
| **A4** | **Closed — real defect, fixed** | `RigTargetBaker.CaptureRestPose` read `authoring.transform` directly, registering no bake dependency. `ActorBaker` was the correct one. Moving a part in the Editor would move its rendered position while `TargetRestPose` kept the last full bake's value, and §5.6 composes animated pose as an offset from that value — so the part animated around a stale origin. Now routed through `GetComponent<Transform>(authoring)`, with the reasoning in the method's XML. |
| **A5** | **Deferred — spec gap, not a C3 defect** | Rest-bounds inflation ignoring rotation is inherited from §4.6's own key-space rule. Correctly flagged for a §4.6 sweep; changing it here would put the baker out of step with the spec it implements. Carried to C4 backlog. |
| **A6** | **Closed — tests** | `SampleSettingsAndVatTextureBinding_CarryTheAuthoredValues_NotJustTheComponents` asserts the `math.max(0f, …)` clamp *and* that a legitimate rate survives it (clamping everything to 0 would pass a one-sided test), plus `setKey` and both texture refs. `AnActorWithNoVatTextureSet_GetsADefaultBinding_RatherThanNone` pins the absent-set case. `phase01` distinctness and range were already covered by `TwoActorsFromOneClipSet_GetDifferentSamplePhases_AndTheSameOneOnRebake`. |
| **A7** | **Closed — applied** | `ComputeSamplePhase` now takes `(pathHash >> 8) & 0x00FFFFFF`. |
| **A8** | **Deferred — C7/M5** | Correct as filed: the fix is a custom inspector that hides `phase01`, which is M5's surface. |
| **A9** | **Closed — applied** | `ResolveRigPartBindingsJob` stays silent when the actor entity exists but carries no `ClipRegistry`, which is exactly the shape of an actor whose own bake already reported the actionable message. A genuinely null `actorRoot` still reports. `AnActorOnAClipSetWithValidationErrors_LogsEveryRuleCode_AndBakesNoRegistry` bakes a three-part actor and asserts exactly one error, so the N+1 regression cannot come back unnoticed. |
| **A10** | **Closed — test** | `APartRotatedNegatively_CapturesASignedRestRotation_NotAWrappedOne` uses the reviewer's own −30° fixture and asserts −π/6 on both `TargetRestPose` and the seeded `TargetPose`. |

The same test also closes the **`DescribeValidationFailure`** coverage gap named in
the test-integrity section: it breaks two rules (V01 and V02) rather than one,
because a formatter that reports only the first failure is the likely bug and a
single-error fixture cannot catch it.

**Not closed, carried to C4:** **A5**, **A8**, and the carried C2 advisories
(**D10**, **D11**, **A10a**, **A12a**).
