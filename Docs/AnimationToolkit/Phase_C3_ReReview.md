# Phase C3 Re-Review — after the gate rework

**Date:** 2026-07-31
**Scope:** `git diff 026a902..HEAD` — commits `aacde42`, `d288342`, `d237fa2`, `f60a4fa`.
**Method:** three independent reviewers run in parallel, each confined to one lens so no single agent had to hold the whole surface. Each verified claims against shipped code and against Unity package sources in `Library/PackageCache`, not against the coordinator's rework notes.

## Consolidated verdict: **FAIL**

| Reviewer | Lens | Verdict |
|---|---|---|
| A | Spec conformance | FAIL — 4 blocking |
| B | Test integrity | FAIL — 4 blocking |
| C | Code correctness | FAIL — 3 blocking |

All three independently found the same live bug (`AuthoringPathHash.PathOf` throwing at bake on non-ASCII paths), which is the strongest signal in the set.

The three full reports follow verbatim.

---

# Reviewer A — spec conformance

# C3 Re-review — Reviewer A (spec conformance)

Scope: `026a902..HEAD` (aacde42, d288342, d237fa2, f60a4fa) over
`Packages/com.dotsanimationtoolkit` + `Docs/AnimationToolkit`.

Findings appended as confirmed. Verdict at the bottom.

---
## FINDING 1 — BLOCKING — B4 is NOT closed (3 of its 5 items untouched)

`Docs/AnimationToolkit/Phase_C3_Review.md:~381` (closure-audit table) claims:
> **B4** | Closed | Amendments A17–A20 recorded; §8 M2 EXPOSES/OWNS, the `phase01`
> derivation rule, `SampleSettings`'s `[Serializable]` and §4.6's closing sentence
> all amended.

`git diff 026a902..HEAD -- Docs/AnimationToolkit/Phase_B_Architecture.md` touches
exactly four places: line 96 (A17), 356/358 (A19 rewrite + A21), 475 (A18),
917 (A20 addendum under **M3**). **§8 M2 was never edited. §4.6 was never edited.**

| B4 item | Required | Actual |
|---|---|---|
| 1. §8 M2 EXPOSES gains `useKindOverride`, `restSliceIndex`, `vatDrivingLayerIndex` | amend | **OPEN.** `Phase_B_Architecture.md:910` still reads `RigTargetAuthoring { RigAsset rig; uint targetStableId; TargetKind kindOverride; Material expectedMaterial; }`. `RigTargetAuthoring.cs` ships all seven fields; `RigTargetBaker.cs:174` reads `authoring.useKindOverride`, `:206/:224` read `authoring.restSliceIndex`, `:246` reads `authoring.vatDrivingLayerIndex`. |
| 2. §8 M2 OWNS gains `RigPartBakeLink`, `StartingLayerState` | amend | **OPEN.** `Phase_B_Architecture.md:909` OWNS is byte-identical to pre-rework: `ActorAuthoring, ActorBaker, RigTargetAuthoring, RigTargetBaker, RigBindingBakingSystem` + the VAT four. `grep RigPartBakeLink` over the spec returns only the A21 paragraph — which *mentions* the type but does not add it to any OWNS list. |
| 3. `SampleSettings` `[Serializable]` | amend | Closed (A20, line 917). |
| 4. `phase01` derivation rule | amend | Partially closed. A18 (line 475) says only "`SampleSettings.phase01` is derived this way" — it never states the derivation (`((pathHash >> 8) & 0x00FFFFFF) * 2^-24`), which is the invented rule the gate asked to be recorded. Advisory-grade on its own. |
| 5. §4.6's closing sentence reworded to match §5.8 | amend | **OPEN.** `Phase_B_Architecture.md:484` still reads verbatim: *"the entity-baking step … is responsible for producing actor-space bounds by combining `offsetBounds` with the rest-pose positions of the targets each clip touches. That result is carried by `ActorRestBounds`."* This still contradicts §5.8 and §8 M2 and still contradicts the shipped `ActorBaker.ComputeActorRestBounds`, which unions rest poses only and never sees `offsetBounds`. |

**Why it matters.** Items 1 and 2 are the exact class of silent divergence §9 calls
stop-the-line, and B4 was raised *because* the same failure had already been
rejected in C1 and C2. Item 5 is worse than paperwork: it is a live contradiction
between two normative sections that C4 must read to build `RenderBoundsUpdateSystem`,
and the C3 handoff note 2 exists only to warn C4 not to believe §4.6 — a warning in
a review doc is not an amendment to the spec.

**Compounding:** the closure audit was written specifically as the antidote to
"closure is a property of the code, not of the note" — and itself asserts a closure
that the diff does not contain.

---
## FINDING 2 — BLOCKING — B6 is only partly closed: the shipped test count is still wrong

`Phase_C3_Review.md` B6 requires: *"bump to `0.4.0` … write the C3 entry, **correct the
test counts**, and correct the `description`."* Closure audit records B6 as "Closed".

- `package.json:4` — version `0.4.0`, description rewritten. **Correct.**
- `CHANGELOG.md:8-73` — `## [0.4.0] - Unreleased` C3 entry present. **Correct.**
- `README.md:9` — `0.4.0`. **Correct.**
- `Documentation~/index.md:71` — *"Packaging conformance tests plus **164** EditMode
  tests…"*. **WRONG.** The shipped EditMode assembly contains **192** `[Test]` methods
  (`grep -rh "^\s*\[Test\]" Tests/EditMode/*.cs | wc -l` = 192; per-file:
  ClipValidation 32, ClipRegistryBuilder 18, StableIdentity 17, EventWrapMath 16,
  LayerComposition 14, ClipRegistryDeterminism 14, RuntimeContract 13,
  LoopTimeMapping 12, ClipSampler 12, ClipRegistryUtil 10, DataContract 9,
  PackagingConformance 8, Easing 7, SampleQuantization 5, ContentHashGolden 5).
  Reviewer B named 192 explicitly in B6. This line was not touched by the diff — the
  rework edited index.md above and below it and left the count alone.
- `CHANGELOG.md:107` — `## [0.3.0]`'s *"66 EditMode tests"* is likewise untouched.
  (Defensible if read as a per-release delta, but 96 + 66 + C0's 8 = 170 ≠ 192, so it
  is not reconcilable under either reading.)
- The new PlayMode suite (31 `[Test]` methods) is given no count anywhere.

**Why it matters.** This is C2's carried advisory **D9**, escalated to **B6**
*because* it had already been left open once. It is now open for the third gate
running. `Documentation~/index.md` is the file a licensee reads; a package that
undercounts its own test suite by 15% is exactly the "misdescribes the shipped
package" defect B6 was raised to fix. Everything else in B6 is genuinely done, which
makes this a narrow miss — but the required item was "correct the test counts", and
the counts are not corrected.

---

## FINDING 3 — CLOSED, verified — B1

Genuinely closed, and closed well. `Tests/PlayMode/VatMaterialProbe.shader` declares
real `_VatBoneTex` / `_VatPosTex` slots; `ActorBakeFixture.CreateVatCapableMaterial`
(`ActorBakeFixture.cs:321`) builds from it and `Assert.IsNotNull`s the `Shader.Find`,
so a silent import failure cannot degrade the test back to branch (iii).

- Branch (iv): `ActorBakingAcceptanceTests.cs:932`
  `AVatPartBoundToTheWrongTexture_LogsTheSection44Mismatch` — asserts the warning
  names `StaleBoneTex`, which only branch (iv) can produce.
- Negative case: `:969` `AVatPartBoundToTheBakedTexture_WarnsAboutNothing` — asserts
  zero material warnings for a correctly configured part, plus `VatDriven` presence.
- `AssertToolkitWarnings` (`:54`) pins an exact count, so double-warning regressions fail.

Advisory only (see A-list below): the probe shader is `CGPROGRAM`/`UnityCG.cginc`
built-in-pipeline code shipping inside a URP-only package.

---

## FINDING 4 — CLOSED, verified — B2

- `PostTransformMatrix` on a **unit-scaled** part: `ActorBakingAcceptanceTests.cs:313`
  `BakingAQuadPart_ProducesPostTransformMatrix_SoScaleIsNotDead` (`ActorBakeFixture.AddPart`
  sets only `localPosition`, so scale is 1). The `TransformUsageFlags.NonUniformScale`
  assumption is now pinned, which was the C4 handoff blocker.
- `AnimVisible` on a part: `:210`.
- `FlipbookPlane` positive + VAT absence: `:233`.
- `VatMesh` positive (all three `Vat*Property`) + sprite absence + `VatDriven.layerIndex`
  against the authored value: `:273`.
- Root archetype "exactly" (extra components fail too) via `AssertToolkitComponentsAre`: `:143`.

---
## FINDING 5 — BLOCKING — a §4.1 error ownership was moved between systems silently, and the file now contradicts itself

**Spec.** `Phase_B_Architecture.md:351` (§4.1, `RigBindingBakingSystem` row, unchanged
by this rework): *"Cross-entity pass: … Errors (**unknown targetId**, duplicate binding)
are reported via `Debug.LogError` … and the part is skipped."* The unknown-targetId
error is normatively owned by the binding pass.

**Code.** `Authoring/Baking/RigTargetBaker.cs:79-96` — new in `aacde42`:

```csharp
if (targetDefinition == null)
{
    // Report it here rather than leaving it to the binding pass. …
    // The part is then left without a RigPartBakeLink, so the binding pass never sees it
    Debug.LogError(… "references target id " … "which rig '" + effectiveRig.name + "' does not declare." …);
}
else
{
    AddComponent(partEntity, new RigPartBakeLink { … });
}
```

The error moved from the Bursted system to the managed baker, **and** the part is
now withheld from `RigPartBakeLink` entirely, which makes
`RigBindingBakingSystem.cs:140` (`!ClipRegistryUtil.TryResolveTarget → "references
target id {n}, which the actor's rig does not declare"`) unreachable for the ordinary
case. `ActorBakingAcceptanceTests.cs:816` confirms it: the single expected error names
`"Stray"` in the baker's wording, not the job's.

**No amendment records this.** A19 and A21 govern *how the binding pass phrases a
message*; neither reassigns *which system owns the unknown-targetId error*, and
neither mentions withholding `RigPartBakeLink`. §4.1's row is untouched.

**The file now states both positions.** `RigTargetBaker.cs:169-174`, the XML on
`ResolveTargetKind`, still reads:

> *"A target id the rig does not declare is **not** reported here: architecture
> section 4.1 gives that error to `RigBindingBakingSystem`, which is the one place
> that can see whether the id resolves against the actor's baked registry. Reporting
> it in both places would double every message."*

That is a direct contradiction of the comment at `:82` and of the code at `:87`,
100 lines apart in the same file, both shipped. A maintainer reading `ResolveTargetKind`
would "restore" the old behaviour and reintroduce the double report.

**Why it matters.** This is the exact §9 stop-the-line failure mode B4 was raised for,
committed *in the same rework that was supposed to close B4*. It is also a real
semantic change, not cosmetics: the baker validates against `effectiveRig` (the
`RigAsset`), the job validated against the actor's **baked `ClipRegistry`**. Those are
different sources; the binding pass was specified as the check *"which can see whether
the id resolves against the actor's baked registry"* — the case where a rig declares a
target that the registry did not end up carrying is now checked by nothing.

---

## FINDING 6 — advisory — B3's only test pin depends on an ordering the package documents as unspecified

`ActorBakingAcceptanceTests.cs:918` (`AVatPartWhoseMaterialLacksTheTextureSlot_…`)
asserts `StringAssert.Contains("VatBody", bakingWorld.ToolkitErrors[0])` — the review's
sole evidence that A21's path naming works.

The duplicate-claim branch (`RigBindingBakingSystem.cs:143-155`) reports whichever part
is iterated **second**. The fixture creates `Torso` first and `VatBody` fourth, so the
assertion only holds if `Torso` is visited first — and `RigBindingBakingSystem.cs:35-42`,
rewritten in this same rework to close **A2**, now says:

> *"chunk iteration order follows entity creation order … but that is an emergent
> property of Entities, not a guarantee this package can make … **Treat the order as
> unspecified.**"*

The suite therefore asserts on the one thing the package just finished declaring it
does not guarantee. If Entities ever reorders, the test fails naming `Torso` and reads
as an A21 regression. Assert `Contains("VatBody") || Contains("Torso")`, or better, make
the fixture's two claimants distinguishable from the valid parts so either ordering
produces a determinate expected name.

---

## FINDING 7 — advisory — three of the four Bursted diagnostics A21 covers are now unreachable

A21: *"every one of its four diagnostics names the offending object."* True textually.
But after this rework:

1. `RigBindingBakingSystem.cs:127` `"has no baked actor to bind to"` fires only when
   `bakeLink.actorRoot == Entity.Null` (`:123` returns silently otherwise). `RigTargetBaker.cs:89`
   is the only writer of `RigPartBakeLink` and always sets `actorRoot = GetEntity(actorAuthoring, …)`
   with `actorAuthoring` already null-checked at `:44`. **Unreachable.**
2. `:134` `"clip registry failed to build"` requires a `ClipRegistry` present with
   `!Value.IsCreated`. `ActorBaker.cs:72` adds `ClipRegistry` only after a successful
   build. **Unreachable.**
3. `:143` unknown target id — unreachable per Finding 5.
4. duplicate claim — the only live one, and the only one tested.

Not a defect on its own (defensive branches are fine), but A21's claim reads as though
four diagnostics were improved when one was, and the review's B3 evidence line
(*"the one Bursted branch a fixture can reach"*) understates *why* only one is reachable —
it is not a fixture limitation, it is that the other three are dead.

---
## FINDING 8 — BLOCKING — B3's remedy throws at bake for non-ASCII hierarchy names, and its comment argues the inverse

`Authoring/Baking/AuthoringPathHash.cs:91-98`:

```csharp
// FixedString128Bytes holds 125 UTF-8 bytes. Trimming by character count is conservative
// for any non-ASCII name — the safe direction to be wrong in, since overflowing throws.
const int MaximumPathCharacters = 110;
if (fullPath.Length > MaximumPathCharacters)
{
    fullPath = ".../" + fullPath.Substring(fullPath.Length - MaximumPathCharacters);
}
return new FixedString128Bytes(fullPath);
```

**The stated reasoning is backwards.** `String.Length` counts UTF-16 code units;
`FixedString128Bytes` capacity is 125 **UTF-8 bytes**. Bytes ≥ chars for every
non-ASCII character (2 for Latin-1 accents and Cyrillic, 3 for CJK, 4 per surrogate
pair). Trimming by character count is therefore conservative **only for ASCII** and
anti-conservative for everything else — the opposite of what the comment claims.

**It throws.** Verified in the shipped Collections source, not from recall:
`Library/PackageCache/com.unity.collections@a43cabe808ca/Unity.Collections/FixedString.gen.cs:4106-4111`
— `FixedString128Bytes(String)` calls `Initialize` → `CopyFromTruncated`, then
`CheckCopyError` (`:4705-4710`), which is
`[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]`
and `throw new ArgumentException($"FixedString128Bytes: {error} …")` on
`CopyError.Truncation`. `ENABLE_UNITY_COLLECTIONS_CHECKS` is defined in the Editor —
which is the only place baking runs.

**Trigger threshold.** A path of ~42 CJK characters, or ~63 accented-Latin
characters, exceeds 125 bytes while staying under the 110-char guard, so the guard
never fires and the constructor throws inside `RigTargetBaker.Bake`
(`RigTargetBaker.cs:104`). Even when the guard *does* fire it produces a 114-**char**
string, i.e. up to 342 bytes.

**Why it matters.** B3's remedy was supposed to make a diagnostic *more* useful. As
shipped it converts a diagnostic path into a bake-time exception for any licensee
whose GameObject names are not ASCII — a first-run failure for a large share of a
commercial package's market, on a code path that only exists to print a nicer error
message. Nothing in the suite covers it: every fixture name is ASCII.

**Fix is one line:** use `CopyFromTruncated` (which returns `CopyError` instead of
throwing) or budget by `Encoding.UTF8.GetByteCount` rather than `String.Length`, and
correct the comment.

**Meta-point for question 2/4:** this is a comment that describes *intended* safety
rather than *shipped* behaviour — the exact failure the brief flags as having
recurred three times, committed inside the fix for it.

---

## FINDING 9 — §8 M2 acceptance mapping, re-derived independently

Re-derived from `Phase_B_Architecture.md:913` (M2 ACCEPTANCE), not from the review's table.
C3's scope excludes the `VatTextureBaker` EditMode bullets — correct, `§9`'s build-plan
row puts `VatTextureBaker` in **C6**, not C3 (`Phase_B_Architecture.md:958`).

| §8 M2 bullet | Assertion | Verdict |
|---|---|---|
| §5.2 root archetype **exactly**, component-by-component incl. the five enableable initial states | `ActorBakingAcceptanceTests.cs:86` + `AssertToolkitComponentsAre` (`:143`), which fails on an *extra* toolkit component too | **Mapped, both directions** |
| `ActorRestBounds` = actor-space union of bound parts' rest poses scaled by `boundsExtents` (A13); far-offset part contained | `:675` — all six bounds asserted exactly, hand-derived, incl. the nested `LeftArm` chain walk and the y=12.4 head | **Mapped** |
| Two actors sharing a set share one blob (reference equality via content hash) | `:601` reference equality; `:650` negative case; `:626` build-once counter (A1) | **Mapped** |
| Part entities carry `RigPartBinding` with correct dense indices for a 3-target fixture rig | `:513` + `AssertPartBound` (`:542`) — asserts *which* GameObject each dense index resolved to, and that the root buffer agrees | **Mapped** |
| Unknown-target part logs error, is skipped, bake still succeeds | `:802` | **Mapped**, but see Finding 5 — the error now comes from the wrong system |
| Material↔texture-set mismatch fixture logs **exactly one warning from `RigTargetBaker`** (§4.4) | `:932` `AssertToolkitWarnings(2, "StaleBoneTex")` | **Mapped with a gap** — advisory below |

**Advisory gap on the last bullet.** `AssertToolkitWarnings` counts *all* toolkit
warnings from any source. In `AVatPartBoundToTheWrongTexture_LogsTheSection44Mismatch`
the expected 2 are one `RigTargetBaker` mismatch plus one unrelated `ActorBaker`
"seeds no starting clip" notice. A `RigTargetBaker` that emitted the mismatch **twice**
while `ActorBaker` emitted nothing would satisfy `AreEqual(2, …)` and the
`Contains("StaleBoneTex")` probe. The bullet's load-bearing words are "exactly one
**from `RigTargetBaker`**", and the assertion cannot distinguish sources. Either seed a
starting layer in this fixture so the expected count drops to 1, or filter the recorded
messages by their emitting text.

---
## FINDING 10 — B5 closed in code; §1.3's table cell still contradicts its own amendment

Code side verified:
- `Tests/PlayMode/DotsAnimationToolkit.Tests.PlayMode.asmdef` — `"includePlatforms": ["Editor"]` ✓
- `Tests/EditMode/PackagingConformanceTests.cs:156` — `expectedIncludePlatforms = new string[] { "Editor" }` ✓, comment corrected ✓
- `CHANGELOG.md:30` and `Documentation~/index.md:86` disclose it ✓

Doc side: `Phase_B_Architecture.md:94`, the §1.3 row's **Platforms** cell, still reads
`All (test framework standard)`. Amendment **A17** two lines below (`:96`) overrides it.
This matches the doc's established convention (A11/A12/A13/A14 are all appended
paragraphs, not in-place edits), so I do not treat it as blocking — but B5's written
remedy was *"amend §1.3's **row**"*, and a normative table cell that states the
opposite of the paragraph beneath it is the mechanism by which this document becomes
untrustworthy. Same shape applies to §4.1's row vs A19/A21.

---

## FINDING 11 — advisory list

| # | Item | Location |
|---|---|---|
| **a1** | `§5.2`'s root inventory lists `AnimLod` unconditionally; `ActorBaker` adds it only when `addDistanceLod` is set. The **new** `AssertToolkitComponentsAre` expectation (`ActorBakingAcceptanceTests.cs:136`) now normatively pins its *absence* as the conformant archetype, and `:336` asserts the opt-in behaviour is correct. A spec/reality conflict newly codified in a test rather than amended. (The C3 gate recorded this as handoff note 5, not as a defect — but the rework has since hardened it into an assertion.) | `Phase_B_Architecture.md:630`; `ActorBakingAcceptanceTests.cs:132,336` |
| **a2** | `AuthoringPathHash` is named normatively by both **A18** and **A21** but appears in no §8 OWNS list — a third missing OWNS entry alongside Finding 1's two. | `Phase_B_Architecture.md:475,358,909` |
| **a3** | **A18** records *that* `phase01` uses `AuthoringPathHash` but never states the derivation, so the A7 `(pathHash >> 8) & 0x00FFFFFF` change is normatively unrecorded. B4 item 4 is half-closed. | `Phase_B_Architecture.md:475` vs `ActorBaker.cs:496` |
| **a4** | `ClipRegistryBuilder.BuildInvocationCount` — shipped mutable static state added to an **M1-owned** type purely as an M2 test seam, incremented on every `Build` call in every context including a player. Not recorded anywhere. (No race: Entities 6.5 invokes `Baker.Bake` on the main thread — checked in `Library/PackageCache/com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/`.) Preferable: `[Conditional("UNITY_EDITOR")]` guard, or an injectable counter. | `ClipRegistryBuilder.cs:72-79,121` |
| **a5** | `BakingTestWorld.Bake` sets `LogAssert.ignoreFailingMessages = true` for the whole bake. Of the 31 PlayMode tests only 8 assert on `ToolkitErrors`/`ToolkitWarnings`; in the other 23 an unexpected package `LogError` now passes silently, where pre-rework `LogAssert` would have failed the test. Consider a default `Assert.IsEmpty(ToolkitErrors)` in `[TearDown]` with an opt-out. | `BakingTestWorld.cs:120-124` |
| **a6** | `Tests/PlayMode/VatMaterialProbe.shader` is built-in-pipeline `CGPROGRAM` + `UnityCG.cginc` shipping inside a package whose §6/M4 contract is URP-only, and it lands in the published tarball. It is never rendered, so it works — but M4's C5 acceptance is "all shaders compile for the URP target with zero warnings-as-errors" and this file will be swept up by any such grep/compile gate. | `VatMaterialProbe.shader:30-32` |
| **a7** | `Documentation~/index.md:26-28` still summarises 0.4.0 as *"the data and sampling layer plus the authoring layer"* with no mention of entity baking, immediately above a bullet list that now includes it. | `Documentation~/index.md:26` |
| **a8** | A part's `AnimVisible` is asserted **present** but never asserted **enabled**. §5.2 makes the root's baked-enabled state contractual and calls the part's "propagated"; a part baked disabled would freeze that part on frame 1 with the whole suite green. One line. | `ActorBakingAcceptanceTests.cs:210` |
| **a9** | See Finding 6 (order-dependent B3 pin), Finding 7 (three dead diagnostics), Finding 9's gap (`AssertToolkitWarnings` cannot attribute a warning to its emitter). | |

**Verified-good, for the record** (checked against sources, not notes):
- **A3**'s ruling is correct. `Baker.GetComponentsInChildren<T>` uses
  `private const bool kDefaultIncludeInactive = true` and offers no bool overload —
  `Library/PackageCache/com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/Baker.cs:31,492`.
- **A4** was a real bug and the fix is right (`RigTargetBaker.cs:202`
  `GetComponent<Transform>(authoring)`).
- Burst **does** support `FixedStringNBytes` in interpolated strings despite the doc's
  "built-in types only" list — `com.unity.burst@6bb9aca3ef38/CHANGELOG.md:150` (fixed
  in 1.8.21; package pins 1.8.29). A19/A21's Burst-purity claim holds.
- **A18** and **A20** are true of the shipped code (`grep GetInstanceID` returns only
  XML prose; `ActorStateComponents.cs:63` carries `[System.Serializable]`).
- `VatTextureBinding` matches §4.4's sketch field-for-field.
- §9's build plan puts `VatTextureBaker` in **C6**, so C3 omitting M2's VAT EditMode
  acceptance bullets is correct, not a gap.

---

## Verdict

| Gate item | Status |
|---|---|
| **B1** | Closed — verified (Finding 3) |
| **B2** | Closed — verified (Finding 4) |
| **B3** | **Partially closed** — the `FixedString` path lands, but the implementation throws at bake for non-ASCII names (Finding 8), and a §4.1 error was relocated in the same change without an amendment (Finding 5) |
| **B4** | **NOT CLOSED** — 3 of 5 items untouched; the closure audit's claim is false (Finding 1) |
| **B5** | Closed in code; doc cell still contradicts its amendment (Finding 10, advisory) |
| **B6** | **Partially closed** — version/description/changelog correct, test counts still wrong (Finding 2) |
| §8 M2 bullets | All six map to real assertions; one has an attribution gap (Finding 9) |
| Silent divergences | One new instance found (Finding 5), plus a1/a2/a3/a4 |

VERDICT: FAIL — 4 blocking items (Findings 1, 2, 5, 8)

---

# Reviewer B — test integrity

# Re-review B — Test Integrity (C3 rework)

Scope: `git diff 026a902..HEAD -- Packages/com.dotsanimationtoolkit/Tests`
Commits: aacde42, d288342, d237fa2, f60a4fa

---

## VERIFIED GOOD (the two things the gate specifically asked about)

### G1 — `VatMaterialProbe.shader` genuinely reaches the section 4.4 comparison branch. CONFIRMED.
Chain traced end to end:
- `VatMaterialProbe.shader:19` declares `_VatBoneTex` as a real `2D` property.
- `ActorBakeFixture.cs:321-337 CreateVatCapableMaterial` does `Shader.Find(VatProbeShaderName)` +
  `Assert.IsNotNull` (fails loudly if the shader did not import) + `SetTexture("_VatBoneTex", ...)`.
- `ActorBakeFixture.cs:353` sets `flavor = VatFlavor.BoneMatrix`, so
  `RigTargetBaker.cs:294-300` selects `_VatBoneTex` as `texturePropertyName`.
- `RigTargetBaker.cs:302` `material.HasProperty("_VatBoneTex")` is now TRUE, so the early-return
  short-circuit that sank the original C3 gate is skipped and execution reaches
  `RigTargetBaker.cs:312-321`, the actual `GetTexture(...) != expectedTexture` comparison.
- `AVatPartBoundToTheWrongTexture_LogsTheSection44Mismatch` (`ActorBakingAcceptanceTests.cs:932`)
  asserts the fragment `"StaleBoneTex"`, which is emitted **only** by the mismatch message
  (`RigTargetBaker.cs:317` `DescribeTexture(boundTexture)`); the no-slot message at
  `RigTargetBaker.cs:305-307` never prints a texture name. So the fragment does pin the branch.
- `AVatPartBoundToTheBakedTexture_WarnsAboutNothing` (:969) supplies the false-positive guard with
  a total warning count of 1 (the unrelated seeds-no-clip notice).

B2 is genuinely closed. This is the one item I tried hardest to break and could not.

### G2 — `BuildInvocationCount` test can fail in both directions.
`TwoActorsSharingAClipSet_BuildTheRegistryOnce_NotOncePerActor` (:626) resets the counter
immediately before its own bake, uses a private `BakingTestWorld` (hence a private
`BlobAssetStore`), and a probe regression yields 3 while a total bake failure yields 0. Not
tautological, and not dependent on which of the three actors bakes first. See A-5/A-6 for caveats.

---

## BLOCKING

### B-1 — `LogAssert.ignoreFailingMessages = true` around every bake removes the only failure signal 21 of the 26 acceptance tests have for unexpected toolkit errors.
**File:** `Tests/PlayMode/BakingTestWorld.cs:125-126` (restore at :143).

Verified against UTF source, not recall:
- `Library/PackageCache/com.unity.test-framework@f3e6c9a02477/UnityEngine.TestRunner/Assertions/LogAssert.cs:100-114`
  — `ignoreFailingMessages` writes `LogScope.Current.IgnoreFailingMessages`.
- `.../Assertions/LogScope/LogScope.cs:123-126` —
  `if (IsFailingLog(type) && !IgnoreFailingMessages) FailingLogs.Add(log);`
- `.../LogScope.cs:138-149` — `IsFailingLog` covers `Assert`, `Error`, `Exception`.
- `.../LogScope.cs:178-192 EvaluateLogScope` throws `UnhandledLogMessageException` only out of
  `FailingLogs`.

Net effect: for the duration of every `Bake()`, **no** `Debug.LogError` / `LogException` /
`Debug.Assert` from any source — including this package — can fail the test.

Only 5 of the 26 acceptance tests assert on `bakingWorld.ToolkitErrors` at all:
`APartWithAnUnknownTargetId...` (:812), `AnActorOnAClipSetWithValidationErrors...` (:853),
`AVatPartWhoseMaterialLacksTheTextureSlot...` (:913), `AVatPartBoundToTheWrongTexture...` (:960),
`AVatPartBoundToTheBakedTexture...` (:994). The other 21 — every archetype, dense-index,
rest-bounds, sample-settings, blob-sharing and VAT-property test — assert nothing about errors and
are now immune to them.

**How a test passes while the code is wrong:** introduce a regression that makes
`RigBindingBakingSystem` log the duplicate-claim or no-baked-actor error on every part, or makes
`ActorBaker.SeedStartingLayers` log "seeds layer N but rig defines only M" for every actor.
`BakingAnActor_ProducesTheSection52RootArchetype` still produces a correct archetype, so it stays
green — and so do the other 20. Before this rework UTF failed all of them on the first unexpected
error. That safety net was traded away wholesale to fix a narrow host-log problem.

**Fix:** keep the suppression (the host-log rationale is sound) but replace the net the suite lost.
Give `BakingTestWorld` an opt-in — e.g. `bakingWorld.ExpectToolkitErrors(1)` — and assert in
`ActorBakingAcceptanceTests.TearDown`:

    Assert.AreEqual(expectedToolkitErrorCount, bakingWorld.ToolkitErrors.Count,
        "Unexpected toolkit error(s) during bake: " + string.Join(" | ", bakingWorld.ToolkitErrors));

defaulting to 0. That restores the guarantee for all 21 tests in one place and, unlike `LogAssert`,
stays host-independent — which was the whole point of the harness.

### B-2 — `AStrayPartDoesNotEnlargeTheRestBounds` is a one-sided assertion satisfied by a bounds pass that produces nothing at all.
**File:** `Tests/PlayMode/ActorBakingAcceptanceTests.cs:1058-1076`, assertion at :1072-1075.

The entire test is `Assert.Less(restBounds.Max.x, 50f)`.

**How it passes while the code is wrong:** any implementation that, on hitting an unresolvable
part, bails out of `ActorBaker.ComputeActorRestBounds` and returns the `anyPartBounded == false`
zero box (`ActorBaker.cs:397-401`) passes — `0 < 50`. So does one returning `default(AABB)`, or one
that dropped all three legitimate parts. That is precisely the shipped bug this test claims to
prevent, in the opposite direction: an actor with one typo'd target id silently gets a zero-extent
culling box and vanishes at any distance.

No other test closes the hole. `BakingAnActor_ProducesActorSpaceRestBounds...` (:675) and
`ADisabledPart...` (:764) both use fixtures with **no** stray part, so neither exercises the mixed
resolvable/unresolvable case.

**Fix:** the fixture is `CreateStandardActor` plus one stray — the same three real parts as the
exact-bounds test. Assert the same exact box:

    Assert.AreEqual(-0.45f, restBounds.Min.x, Tolerance);
    Assert.AreEqual( 0.90f, restBounds.Max.x, Tolerance);
    Assert.AreEqual( 0.25f, restBounds.Min.y, Tolerance);
    Assert.AreEqual(12.40f, restBounds.Max.y, Tolerance);

That pins both halves at once: the stray contributed nothing, and the three real parts still did.

### B-3 — `AuthoringPathHash.PathOf` is introduced by this rework, runs on every successfully-bound part, has zero tests, and throws on non-ASCII hierarchy names.
**File:** `Authoring/Baking/AuthoringPathHash.cs:76-99` (new); called from `RigTargetBaker.cs:104`.

**No test anywhere touches it.** The only nearby assertion is
`StringAssert.Contains("VatBody", bakingWorld.ToolkitErrors[0])`
(`ActorBakingAcceptanceTests.cs:918-923`), which checks a 7-character ASCII leaf name and nothing
about path assembly, truncation, or encoding.

The bug the missing test hides, verified against Collections source
(`Library/PackageCache/com.unity.collections@a43cabe808ca/Unity.Collections/FixedString.gen.cs`):
- `FixedString128Bytes.utf8MaxLengthInBytes = 125`; the `FixedString128Bytes(String)` ctor calls
  `CheckCopyError`, which does `throw new ArgumentException("FixedString128Bytes: {error} ...")` on
  overflow — guarded by `ENABLE_UNITY_COLLECTIONS_CHECKS` / `UNITY_DOTS_DEBUG`, i.e. active in the
  Editor, which is exactly where baking runs.
- `AuthoringPathHash.cs:93-97` trims by **character** count (`MaximumPathCharacters = 110`), and
  the comment at :91-92 claims that is "conservative for any non-ASCII name — the safe direction to
  be wrong in, since overflowing throws." The reasoning is inverted. Character trimming is
  conservative only for ASCII. 110 characters of 2-byte accented Latin is 220 bytes; 110 characters
  of 3-byte CJK is 330 bytes. And a path under 110 characters is not trimmed at all, so a hierarchy
  of ~63 CJK characters throws without truncation ever being involved.
- The throw surfaces inside `RigTargetBaker.Bake`, so a consumer with non-ASCII GameObject names
  gets an exception out of a Baker on every rig part. For a package sold to a general audience this
  is a first-day bug.

**How a test would catch it:** an EditMode test on `PathOf` asserting (a) `Root/Child/Leaf`
rendering; (b) a >110-character ASCII path truncates to `.../<last 110>` and keeps the leaf;
(c) a transform named with 100 CJK characters returns a valid `FixedString128Bytes` rather than
throwing — this one fails today; (d) `PathOf(null)` returns `default`. The production fix is to
trim by UTF-8 byte budget, or use `CopyFromTruncated` as `ClipRegistryBuilder.cs:424-426` already
does for `debugName`.

### B-4 — `TwoActorsFromOneClipSet_GetDifferentSamplePhases_AndTheSameOneOnRebake` cannot detect what its own assertion message says it detects.
**File:** `Tests/PlayMode/ActorBakingAcceptanceTests.cs:458-471`.

The assertion message reads: *"Re-baking unchanged source must reproduce the same phase. A
session-local id would give a different value here every run."*

That is false. The rebake bakes **the same `GameObject` instances** into a second
`BakingTestWorld`. `Object.GetInstanceID()` is stable for an object's lifetime, so a
`ComputeSamplePhase` implemented as `hash(authoring.GetInstanceID())` produces the identical value
in both worlds and this test stays green. The test pins only "the phase is not randomised per
bake" — it does **not** pin amendment A18 (no session-local id), the property the comment at
:458-459 and `ActorBaker.cs:481-488` claim it guards.

Structurally the same defect that sank the original C3 gate: the fixture cannot reach the condition
the test claims to discriminate.

**Fix:** rebuild the hierarchy from *fresh* `GameObject` instances with the same names and the same
sibling indices (parent them under a container and pin with `SetSiblingIndex`, so the path hash is
genuinely identical while the instance ids are not), bake that, and assert the same `phase01`. An
instance-id derivation then fails; a path hash passes.

---

## ADVISORY

**A-1 — `AVatPartWhoseMaterialLacksTheTextureSlot_LogsExactlyOneWarning` does not pin which material
branch fired.** `ActorBakingAcceptanceTests.cs:912` asserts the fragment `"_VatBoneTex"`, but that
string appears in *both* `RigTargetBaker.cs:305-307` (no-slot) and `:316-319` (mismatch). If
`HasProperty` were inverted, execution would fall through to the compare branch, `GetTexture` would
return null != boneTexture, and a warning still containing `_VatBoneTex` would be emitted — test
green. Assert `"declares no '_VatBoneTex' slot"` instead. (Contrast G1, where the sibling mismatch
test uses a genuinely discriminating fragment.)

**A-2 — `ADisabledPart_IsStillCoveredByTheRestBounds_BecauseItIsStillBound` asserts only the bounds
half of its own claim.** `:764-795` asserts `restBounds.Max.y == 12.40` and nothing about binding.
The "because it is still bound" half lives in `EntityQueryOptions.IncludeDisabledEntities` on
`ClearRigPartRefsJob` / `ResolveRigPartBindingsJob` (`RigBindingBakingSystem.cs:86, :102`). Delete
that flag and this test still passes. Add: the disabled head's
`RigPartBinding.targetIndex == HeadDenseIndex`, and a `RigPartRef` entry for it on the root.

**A-3 — The part archetype is never actually compared exactly.**
`BakingAQuadPart_ProducesTheSection52PartArchetype` (:196-230) does presence checks plus four
absence checks but never calls `AssertToolkitComponentsAre`. Only the root (`:131-141`) gets the
exact comparison. An extra toolkit component on a part changes its chunk layout exactly as much as
one on the root. Reuse the helper.

**A-4 — The `ComputeSamplePhase` change made in this rework has no test that can see it.**
`ActorBaker.cs:496` went from `(pathHash & 0x00FFFFFFu)` to `((pathHash >> 8) & 0x00FFFFFFu)`. The
stated justification (`:490-493`) is that two siblings whose names differ only in the last
character land on adjacent phases. The only phase test uses "FirstActor"/"SecondActor", which
differ in far more than the last character, so both formulas pass. Add actors named
`"Actor1"`/`"Actor2"` and assert their phases are separated by more than a small epsilon.

**A-5 — The `BuildInvocationCount` documentation materially oversells what the short-circuit
saves.** `ClipRegistryBuilder.cs:66-70` and `ActorBakingAcceptanceTests.cs:628-631` both say a
broken probe "would cost every actor a full canonicalisation-and-hash pass". It already does:
`ActorBaker.TryAcquireRegistry` (`ActorBaker.cs:162`) calls `TryComputeContentHash` for every actor,
and that method runs `BuildValidatedBlob` + `HashRegistry` in full (`ClipRegistryBuilder.cs:177-186`)
— only with `Allocator.Temp`. The store hit saves one persistent allocation and one store insert
per duplicate actor, not the canonicalisation. The *test* is fine; the documentation will mislead
anyone reasoning about crowd bake cost in a package they paid for.

**A-6 — `BuildInvocationCount` is unguarded mutable static state.** `ClipRegistryBuilder.cs:73-79`.
No reset in `[SetUp]`/`[TearDown]`; the single consumer resets immediately before its own bake
(`ActorBakingAcceptanceTests.cs:638`), so no order dependence exists **today** — but nothing
enforces it, and the next test that reads the counter without resetting inherits whatever the
previous test left. Reset it in `SetUp` so correctness does not depend on every future author
remembering. Also `BuildInvocationCount++` is a non-atomic read-modify-write; bakers run on the
main thread in Entities today, but that is an implementation detail, not a contract —
`Interlocked.Increment` costs nothing. On the "legitimate seam" question: yes, acceptable —
`internal`, never read at runtime, zero effect on baked data. Production code lightly bent for
testing, not distorted.

**A-7 — Foreign log pollution is possible in principle (question 4a).**
`BakingTestWorld.cs:197-214` filters only on the literal `"[DOTS Animation Toolkit]"` and is
subscribed to the *global*, cross-thread `Application.logMessageReceivedThreaded`. Anything
carrying that prefix logged from any thread inside the `Bake()` window is counted — including the
toolkit's own **runtime** systems in the default PlayMode world (a job scheduled on an earlier
frame still executing), or a consumer's renamed fork. Risk in this repo is currently nil (no
toolkit actor in the PlayMode test scene), but the harness comment at `:150-157` presents
host-independence as a guarantee and it is not one. Downgrade the comment to a documented
limitation, or additionally gate recording on a bake being in flight.

**A-8 — `ToolkitWarnings` / `ToolkitErrors` allocate a fresh snapshot per access.**
`BakingTestWorld.cs:174-195` returns `list.ToArray()` each time; `AssertToolkitWarnings`
(`ActorBakingAcceptanceTests.cs:54-78`) reads the property three to four times, so the count it
asserts and the list it searches are different snapshots. Harmless today, O(n^2) allocations, and
it quietly defeats the lock's purpose. Take one snapshot into a local at the top of the helper.

**A-9 — `StringAssert.Contains("Set", ...)` at `ActorBakingAcceptanceTests.cs:858` is
near-vacuous.** `"Set"` is both the fixture asset name and a substring of many plausible message
words. Assert `"'Set'"` with the quotes so it actually pins the asset the message names.

**A-10 — `AVatPartWhoseClipSetHasNoTextureSet_LogsExactlyOneWarning` (:1003) asserts *two*
warnings** (`AssertToolkitWarnings(2, ...)` at :1018). Rename it. As written, the next reader
"fixes" the assertion to match the name and silently loosens it. It also asserts nothing about
`ToolkitErrors` — see B-1.

**A-11 — `RigBindingBakingSystem.cs:123-127` — the "genuinely absent actor root still reports"
branch is untested and appears unreachable.** `RigTargetBaker.cs:102` fills `actorRoot` from
`GetEntity(actorAuthoring, ...)`, which never yields `Entity.Null` for a live authoring component,
and the baker returns early when `actorAuthoring` is null (`:43-52`).
`AnActorOnAClipSetWithValidationErrors...` pins only the *silent* direction, so a regression that
made this pass unconditionally silent would go unnoticed. Either delete the branch or reach it with
a fixture.

**A-12 — Stale doc contradicting the behaviour now under test.** `RigTargetBaker.cs:169-174` still
states "A target id the rig does not declare is *not* reported here: architecture section 4.1 gives
that error to `RigBindingBakingSystem`." This rework moved exactly that error **into** this baker
(`:90-96`), which is what `APartWithAnUnknownTargetId...` (:802) now pins. A reader cannot tell from
the source which behaviour is intended.

**A-13 — `CaptureRestPose`'s new `partTransform == null` fallback** (`RigTargetBaker.cs:203-212`) is
unreachable — a `Component` always has a `Transform` — and untested. Dead defensive code in a
shipped package.

**A-14 — Packaging note (for Reviewer A, not test integrity).** `VatMaterialProbe.shader` ships
inside `Tests/PlayMode/` of the package. Unless the `Tests` folder is excluded from the published
tarball, every consumer project imports and variant-compiles a hidden test shader. Separately, the
PlayMode asmdef becoming `includePlatforms: ["Editor"]` (f60a4fa) is what makes `Shader.Find` on a
package-local shader safe here; `ActorBakeFixture.cs:323-328` fails loudly if that ever changes,
which is the correct guard.

**A-15 — Count discrepancy.** The gate brief says 27 acceptance tests;
`ActorBakingAcceptanceTests.cs` contains 26 `[Test]` methods.

---

## Answers to the specific questions asked

**Q2 — does `VatMaterialProbe.shader` genuinely fix the C3 defect?** Yes. See G1. The section 4.4
comparison branch is reached and the assertion fragment discriminates it. Note A-1: the *sibling*
no-slot test does not discriminate its own branch.

**Q4a — can a foreign toolkit-prefixed message pollute the counts?** Yes in principle, no in this
repo today. See A-7.

**Q4b — does `ignoreFailingMessages` hide real regressions?** Yes, broadly. See B-1. Verified
against `LogScope.cs:123` and `LogAssert.cs:100-114`, not from memory. The harness comment's claim
that a `[SetUp]`-set flag never reaches the bake is **correct** — `BeforeAfterTestCommandBase.cs:149`
and `:200` wrap each before-action in its own `using (var logScope = new LogScope())`, and
`LogScope.Current` is `s_ActiveScopes[0]` (`LogScope.cs:26-34`), so a flag written during setup dies
with that scope. Placing the suppression around the bake is the right mechanism; what is missing is
the replacement assertion.

**Q5 — is `BuildInvocationCount` a legitimate seam?** Yes (A-6). The test is robust and not
bake-order dependent (G2), but the surrounding documentation is wrong about what it measures (A-5)
and the static needs a `[SetUp]` reset to stay order-independent as the suite grows.

**Q6 — coverage gaps.** With nothing at all: `AuthoringPathHash.PathOf` (B-3); the `>> 8`
sample-phase change (A-4); binding of disabled parts (A-2); the absent-actor-root diagnostic
(A-11); the `CaptureRestPose` null fallback (A-13); and the bake-dependency change from
`authoring.transform` to `GetComponent<Transform>(authoring)` (`RigTargetBaker.cs:202`) — that last
one is genuinely hard to test in this harness and I would accept it as untestable.

---

VERDICT: FAIL

Blocking items: 4
- B-1 log suppression removes the unexpected-error net from 21 of 26 tests
- B-2 one-sided stray-bounds assertion
- B-3 `AuthoringPathHash.PathOf` untested and throws on non-ASCII names
- B-4 rebake-determinism test cannot discriminate a session-local id

---

# Reviewer C — code correctness

# Re-review C — code correctness & project-rule conformance
Scope: `git diff 026a902..HEAD -- .../Authoring .../Runtime` (aacde42, d288342, d237fa2, f60a4fa)

---

## C-1 BLOCKING — `AuthoringPathHash.PathOf` throws on non-ASCII paths; its own comment states the truncation logic backwards

**File:** `Packages/com.dotsanimationtoolkit/Authoring/Baking/AuthoringPathHash.cs:91-98`

```csharp
// FixedString128Bytes holds 125 UTF-8 bytes. Trimming by character count is conservative
// for any non-ASCII name — the safe direction to be wrong in, since overflowing throws.
const int MaximumPathCharacters = 110;
if (fullPath.Length > MaximumPathCharacters)
{
    fullPath = ".../" + fullPath.Substring(fullPath.Length - MaximumPathCharacters);
}
return new FixedString128Bytes(fullPath);
```

The comment is inverted. UTF-8 encodes one `char` as **1–3 bytes** (4 for a surrogate pair, i.e. 2 bytes/char), so a
character count is an *under*-estimate of the byte count for anything non-ASCII, never conservative. `char count <=
byte count` always; the guard is therefore only sound for pure ASCII.

Ground truth (read, not recalled):
- `com.unity.collections@a43cabe808ca/Unity.Collections/FixedString.gen.cs:3766` — `utf8MaxLengthInBytes = 125`.
- `FixedString.gen.cs:4106-4111` — `FixedString128Bytes(String source)` calls `Initialize` (=`CopyFromTruncated`)
  then `CheckCopyError`.
- `FixedString.gen.cs:4706-4710` — `CheckCopyError` **throws `ArgumentException`**, and is
  `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]`. Baking only ever runs in the
  Editor, where `ENABLE_UNITY_COLLECTIONS_CHECKS` is defined — so the throw is always live on the code path that
  matters.

**Failure scenario (inputs → wrong behaviour):**
1. A part whose hierarchy path is 42+ CJK/Cyrillic/emoji characters — e.g. `キャラクター/胴体/左腕` nested a few
   levels under a Japanese-named prefab root — is under the 110-character guard, so no trimming happens, but encodes
   to >125 UTF-8 bytes. `new FixedString128Bytes(fullPath)` throws `ArgumentException: FixedString128Bytes:
   Truncation while copying "…"` inside `RigTargetBaker.Bake`. The baker aborts mid-bake: `RigPartBakeLink`,
   `TargetRestPose`, `TargetPose`, `AnimVisible` and all technique components are never added, so the part is a
   half-built entity. This is a hard bake failure caused purely by the user's choice of GameObject names.
2. The truncation branch does not save it either: `".../" + 110 non-ASCII characters` is 4 + up to 330 bytes, so a
   *long* non-ASCII path throws too. The branch that exists to prevent overflow can itself overflow.

Non-Latin GameObject names are entirely ordinary in a shipped, sold package (any non-English studio). This is an
AAA-bar defect, not a theoretical one.

**Fix:** never construct the fixed string from an unmeasured managed string. Use the truncating API, which is public
(`FixedStringAppendMethods.cs:411 — public static CopyError CopyFromTruncated<T>(ref this T fs, string s)`):

```csharp
FixedString128Bytes authoringPath = default;
authoringPath.CopyFromTruncated(fullPath);   // returns CopyError.Truncation; never throws
return authoringPath;
```
Keep a character pre-trim if a `.../` marker is wanted, but the `CopyFromTruncated` call must be the thing that
enforces the capacity. Correct the comment: trimming by characters is *permissive*, not conservative.

**Related advisory (same site):** `fullPath.Substring(fullPath.Length - 110)` can slice through a UTF-16 surrogate
pair, leaving a lone surrogate at the start of the retained text. Trimming to a rune boundary, or letting
`CopyFromTruncated` do the work, avoids emitting a mojibake first character.

**Related advisory (same site):** the path is rebuilt by repeated `string` concatenation walking up the hierarchy
(`AuthoringPathHash.cs:87`), which is O(depth²) allocations per part. Harmless for shallow rigs, wasteful at bake
scale for deep ones; a `StringBuilder` or a two-pass build would be the shipped-package form.

---

## C-2 VERIFIED OK â€” Burst *does* support `FixedString128Bytes` interpolation in `Debug.LogError`

**Files:** `RigBindingBakingSystem.cs:127,134,143,154`

The scope note asked whether Burst supports interpolating a `FixedString128Bytes`. Read, not recalled:

- `com.unity.burst@6bb9aca3ef38/Documentation~/csharp-string-support.md:53-69` lists only built-in scalar and vector
  types as interpolation arguments and says structs print their full type name. **That doc page is stale.**
- `com.unity.burst@6bb9aca3ef38/CHANGELOG.md:150` (1.8.21): "Fixed internal compiler error when using a
  `FixedStringNBytes` value in an interpolated string". Installed Burst is **1.8.29** (`package.json:4`), so the fix
  is in.
- `com.unity.burst@6bb9aca3ef38/Runtime/DiagnosticId.cs:182` â€” `ERR_StringInternalCompilerFixedStringTooManyUsers`,
  a FixedString-specific diagnostic inside the `StringUsageTransform`, i.e. the string transform has dedicated
  FixedString handling.
- Unity's own Bursted code does exactly this:
  `com.unity.entities@e30ad8d00609/Unity.Entities/EntityCommandBufferDebug.cs:125,141,260` interpolate
  `FixedString64Bytes`/`FixedString128Bytes` values (`entityName`, `originSystemDebugName`,
  `typeIndex.ToFixedString()`) straight into `Debug.Log($"...")`.

Argument counts are 1 and 2 per message, well under the three-argument `string.Format` limit; no format specifiers are
used, so BC1343 does not apply; no `+` concatenation, so BC1016 does not apply. **Not a defect.**

---

## C-3 BLOCKING â€” three XML-doc/comment statements now assert the opposite of the code they document

The rework moved the "target id the rig does not declare" error from the Bursted binding pass into
`RigTargetBaker.Bake` (`RigTargetBaker.cs:90-96`). Three pieces of prose still say the old thing, and one of them sits
**twenty lines below the new code, in the same file, untouched by the same commit**:

1. **`Packages/com.dotsanimationtoolkit/Authoring/Baking/RigTargetBaker.cs:169-174`** â€” `<remarks>` on
   `ResolveTargetKind`:
   > "A target id the rig does not declare is *not* reported here: architecture section 4.1 gives that error to
   > `RigBindingBakingSystem`, which is the one place that can see whether the id resolves against the actor's baked
   > registry. Reporting it in both places would double every message."

   `Bake` now reports it exactly here. A maintainer reading this remark would conclude the `Debug.LogError` at line 90
   is a duplicate-message bug and delete it â€” restoring the very defect this rework was meant to close.

2. **`Packages/com.dotsanimationtoolkit/Authoring/Baking/ActorBaker.cs:370-371`** â€” "Unknown targets are
   reported once, by `RigBindingBakingSystem`; this pass stays silent about them." No longer true; they are reported
   by `RigTargetBaker`.

3. **`Packages/com.dotsanimationtoolkit/Authoring/Baking/RigBindingBakingSystem.cs:97-99`** â€” `<summary>` on
   `ResolveRigPartBindingsJob`: "A part that cannot be bound is reported once and left inert." After the new early
   return at lines 123-126 the commonest unbindable part is left inert and **not** reported (see C-4).

**Failure scenario:** the next person to touch either baker follows the documented contract rather than the code. Given
that a previous gate on this package rejected specifically on documentation accuracy, prose contradicting adjacent code
introduced by the same commit is a gate defect, not a nit.

**Fix:** rewrite all three to describe the current split â€” the *managed* baker reports an unknown target id (it can name
the rig and attach a click-to-select context object) and withholds `RigPartBakeLink`, so the Bursted pass never sees
such a part.

---

## C-4 BLOCKING â€” the rework left three of the four Bursted error paths unreachable and silenced the one real failure they covered

**File:** `Packages/com.dotsanimationtoolkit/Authoring/Baking/RigBindingBakingSystem.cs:114-145`

Tracing each guard against its only writer â€” `ActorBaker.cs:72` and `ActorBaker.cs:80` are the *only* places in the
package that add `ClipRegistry` or `RigPartRef` (verified by grep across `Authoring`, `Runtime`, `Editor`):

| Line | Guard | Reachable? |
|---|---|---|
| 127 | actor root missing **and** `actorRoot == Entity.Null` | **No.** `RigTargetBaker.cs:43-52` returns early when `actorAuthoring == null`, and `Baker.GetEntity(Component, flags)` (`com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/Baker.cs:995-1000`) returns `Entity.Null` *only* when the component is null. The guarded message can essentially never print. |
| 134 | `clipRegistry.Value.IsCreated == false` | **No.** `ActorBaker.TryAcquireRegistry` (lines 155-184) returns `true` only via a store hit (a valid blob) or a successful `BuildValidatedBlob`, and `ClipRegistry` is added at line 72 only after that returns true. A non-created blob is never stored. |
| 143 | target id not in the actor's registry | **No.** `ClipRegistryBuilder.BuildCanonicalTargets` (`ClipRegistryBuilder.cs:229-244`) puts *every* non-null `rig.targets` entry into `sortedTargetIds`, and `RigTargetBaker.FindTargetDefinition` searches that same list on the same `effectiveRig` â€” and now withholds `RigPartBakeLink` on a miss. The two sets are identical, so the binary search cannot fail. |
| 154 | duplicate target claim | Yes â€” the only live one. |

**The reachable failure is now silent.** The realistic case â€” actor entity exists, `ClipRegistry` absent because
`ActorBaker` bailed out â€” takes the `actorRoot != Entity.Null` early return at lines 123-126 and prints nothing. After
this change the branch prints only in a case that cannot occur, and stays silent in every case that can.

**Why the silence is a genuine risk, not a style point:** the suppression is unconditional on the *reason*. It is
correct today only because `ActorBaker`'s three bail-outs (lines 41-48, 56-63, 172-179) each happen to log. Nothing
enforces that coupling â€” it is not asserted, not commented at the `ActorBaker` end, not tested. Add a fourth bail-out to
`ActorBaker` without a log, or have any future baking system strip `ClipRegistry`, and every part under that actor
silently stops animating with **zero** diagnostic output anywhere in the project. That is exactly the "the part doesn't
animate and nothing says why" support ticket a sold package cannot afford.

Secondary: in the only way line 127 could ever fire (a hand-constructed link, i.e. from a test), it prints
`Rig part '' has no baked actor...` â€” `AuthoringPathHash.PathOf(null)` returns `default`, an empty string
(`AuthoringPathHash.cs:78-81`). So even the surviving message degrades to naming nothing.

**Fix:** either (a) keep the deduplication but make it explicit and enforced â€” have `ActorBaker` write a
`[BakingType] ActorBakeFailed` tag on the actor entity when it bails and suppress in the binding pass only when that tag
is present, so an *unexplained* missing registry still reports; or (b) drop the suppression and emit one message per
actor rather than per part. Either way delete the three dead guards, or demote 134/143 to
`[Conditional("UNITY_DOTS_DEBUG")]` invariant asserts â€” unreachable user-facing error strings are shipped dead code.

---

## C-5 ADVISORY â€” `RigTargetBaker.CaptureRestPose`: correct API, but over-invalidating, and its new null branch is dead

**File:** `Packages/com.dotsanimationtoolkit/Authoring/Baking/RigTargetBaker.cs:200-212`

**The API is correct.** `Baker.GetComponent<T>(Component)` exists (`Baker.cs:98-101`) and routes through
`GetComponentInternal<T>` (`Baker.cs:122-135`), which calls `DependOnGetComponent` and, for a `Transform`, additionally
`DependOnParentTransformHierarchy`. The stated purpose â€” registering a bake dependency that `authoring.transform` would
not â€” is genuinely achieved.

**Over-invalidation is real.** `CaptureRestPose` reads only `localPosition` / `localRotation` / `localScale`, which are
independent of every ancestor. But `GetComponentInternal` unconditionally adds the *whole parent hierarchy* as a
dependency (its own comment: "Transform component takes an implicit dependency on the entire parent hierarchy since
transform.position and friends returns a value calculated from all parents"). Dragging the actor root, or any
intermediate pivot, now re-runs `RigTargetBaker` for **every** part beneath it even though no baked byte can change. On
a deep rig this turns a one-transform edit into N baker invocations per drag. Acceptable â€” correctness beats speed â€” but
it is a cost the `<remarks>` does not mention while implying the change is free. Document it, or take the narrower
`DependsOn(authoring.transform)` dependency if it suffices.

**The null branch is dead code that fabricates data.** `GetComponentInternal` does
`gameObject.TryGetComponent<Transform>(out ...)`, and every GameObject has a Transform, so `partTransform` cannot be
null. Lines 204-212 never run â€” and if they somehow did, they would silently return an identity rest pose with no
diagnostic, which is worse than throwing. At the stated "no placeholders" bar, delete it.

---

## C-6 ADVISORY â€” `ComputeSamplePhase`'s `>> 8` is harmless and still normalised, but the mask is now a no-op and the rationale does not match the algorithm

**File:** `Packages/com.dotsanimationtoolkit/Authoring/Baking/ActorBaker.cs:492-497`

- **Normalisation is correct.** `pathHash >> 8` on a `uint` yields at most `0x00FFFFFF`, so the product with
  `1f / 16777216f` lands in `[0, 0.99999994]`, still inside `[0, 1)`. No regression.
- **The mask is dead.** `(pathHash >> 8)` is already `<= 0x00FFFFFF`; `& 0x00FFFFFFu` can never clear a bit. Drop it, or
  the code implies a constraint it is not applying.
- **The justification is directionally right but does not describe this hash.** FNV-1a's low bits genuinely mix poorly
  (multiply carries propagate upward only), so preferring bits 8-31 is defensible. But the specific claim â€” "two
  siblings whose names differ only in the last character can land on adjacent phases" â€” does not follow from this
  implementation: `AuthoringPathHash.Of` walks **leaf to root** (`AuthoringPathHash.cs:48-61`), so a leaf-name character
  is consumed in the *first* iteration and then passes through the sibling index, the separator and every ancestor â€”
  many further multiply rounds. The bits reaching the final multiply unmixed are the **root's**, not the leaf's. The
  cited scenario is not the one the shift fixes.

---

## C-7 ADVISORY â€” `ClipRegistryBuilder.BuildInvocationCount`: a test seam shipped unguarded, with an inaccurate doc claim

**File:** `Packages/com.dotsanimationtoolkit/Authoring/Build/ClipRegistryBuilder.cs:60-79, 121`

- **Thread safety is not a live problem.** Bakers are invoked from a single main-thread loop
  (`com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/BakedEntityData.cs:607-645` â€” a plain `for` over the
  baker array inside a managed try/catch), so `BuildInvocationCount++` is not raced during baking. It *is* an
  unsynchronised `++` reachable from a public entry point (`ClipRegistryBuilder` and `Build` are both `public`,
  lines 42 and 110) that the class doc advertises for editor preview as well as baking â€” a latent trap rather than a
  current bug.
- **The doc's runtime claim is wrong.** The `<remarks>` says "it is internal, so nothing outside the package â€” and
  nothing at runtime â€” can see or depend on it." The Authoring assembly has `"includePlatforms": []`
  (`Authoring/DotsAnimationToolkit.Authoring.asmdef`), i.e. it **is compiled into player builds**, and
  `Authoring/AssemblyInfo.cs:8-10` grants `InternalsVisibleTo` to the Editor and both test assemblies. The counter
  therefore exists at runtime in a shipped player and the public `Build` mutates it there on every call. What is true is
  only that no shipping code *reads* it.
- **Fix:** wrap the property, the reset and the increment in `#if UNITY_EDITOR` (baking and preview are both
  editor-only), or use `Interlocked.Increment` if it must stay unconditional. Either way correct the remark to claim
  only what holds.

---

## C-8 ADVISORY â€” `AuthoringPathHash.Of` is still needed, but the class doc no longer describes the class

**Files:** `Authoring/Baking/AuthoringPathHash.cs:8-33`, `Authoring/Baking/ActorBaker.cs:495`,
`Authoring/Baking/RigTargetBaker.cs:104`

Item 6 checks out: `Of` has exactly one live caller (`ActorBaker.ComputeSamplePhase`), which genuinely needs a `uint`,
and a repo-wide grep finds **no** remaining reference to the removed `authoringPathHash` field outside the historical
review doc `Docs/AnimationToolkit/Phase_C3_Review.md`. No dead field, no stale binding, no orphaned serialized data.

Two prose drifts:
- The class `<summary>` still describes the type as only "Hashes an authoring object's hierarchy path into a stable
  32-bit value". It now also hosts `PathOf`, which hashes nothing and returns text; the type name no longer covers its
  contents.
- The class `<remarks>` (lines 25-27) still says the value serves "the two things that use it â€” spreading sampling
  phase, and naming an object in a diagnostic". `Of` now serves the phase only; the diagnostic uses `PathOf`.

---

## C-9 ADVISORY â€” both path walks read ancestors they take no bake dependency on

**Files:** `Authoring/Baking/ActorBaker.cs:495`, `Authoring/Baking/RigTargetBaker.cs:104`, both calling into
`Authoring/Baking/AuthoringPathHash.cs:45-63` and `:76-99`

Both call sites pass `authoring.transform` (not `GetComponent<Transform>(authoring)`), and both `Of` and `PathOf` then
walk `currentNode.parent` to the root reading each ancestor's `name` and `GetSiblingIndex()`. None of that is a
registered bake dependency.

**Consequence:** rename or reorder an *ancestor* of a baked actor and no rebake is triggered, so
`SampleSettings.phase01` keeps the value from the last bake â€” an incremental bake and a clean bake of the same scene
produce different bytes. That is precisely the reproducibility property `ComputeSamplePhase`'s own `<remarks>` claims to
be protecting ("making the bake a pure function of the source"). Visually harmless, as that remark also notes, but the
claim as written is false under incremental baking. The same mechanism leaves `RigPartBakeLink.authoringPath` naming a
stale hierarchy in error messages after an ancestor rename â€” the exact "tells them where to look" value the field was
introduced for.

Note the inconsistency inside a single method: `RigTargetBaker.Bake` uses `authoring.transform` at line 104 and
`GetComponent<Transform>(authoring)` four lines later at 202, for the same object, in the same commit.

**Fix:** register the dependency (`DependsOn` per ancestor GameObject, or `GetComponent<Transform>` on each, which
already implies the parent chain), or soften both `<remarks>` to say the value is stable only against edits at or below
the actor.

---

## Project-rule conformance sweep (all changed files)

| Rule | Result |
|---|---|
| Never `var` | PASS â€” grep over `Authoring/Baking/*.cs` and `Authoring/Build/ClipRegistryBuilder.cs` returns nothing |
| No single-letter names | PASS |
| Never `.Run()` a job | PASS â€” `RigBindingBakingSystem.OnUpdate` uses `ScheduleParallel` then `Schedule`, both assigned to `state.Dependency` |
| `[ReadOnly]` from `Unity.Collections` | PASS â€” `using Unity.Collections;` present at `RigBindingBakingSystem.cs:4` |
| `ISystem` + `[BurstCompile]`, no managed allocation in Burst | PASS |
| `EnabledRefRW/RO` naming | N/A â€” none used |
| Burst BC1343 / BC1016 | PASS â€” no format specifiers, no `+` concatenation in Bursted log strings |

---

VERDICT: FAIL

Blocking items: 3 (C-1, C-3, C-4)

Advisories: 6 numbered (C-5, C-6, C-7, C-8, C-9) plus the two sub-advisories recorded under C-1 (surrogate-pair
splitting on `Substring`, O(depth^2) string concatenation in `PathOf`).

Verified and clear: C-2 (Burst FixedString interpolation is supported â€” the Burst doc page is stale, the changelog,
diagnostic ids and Unity's own Entities code all confirm support), item 6's dead-code check (see C-8), and the full
project-rule sweep above.

