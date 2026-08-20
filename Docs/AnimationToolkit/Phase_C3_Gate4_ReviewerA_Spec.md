# Reviewer A — Spec Conformance — DOTS Animation Toolkit C3 Gate

STATUS: complete — VERDICT: FAIL (7 blocking, see bottom)

Scope: `git diff 026a902..HEAD` restricted to `Packages/com.dotsanimationtoolkit` and `Docs/AnimationToolkit`.
Normative spec: `Docs/AnimationToolkit/Phase_B_Architecture.md`.
Evidence rule: verified against the shipped tree, not the author's notes.

## Findings

### F1 — BLOCKING — Amendment A21 says the binding pass has "four diagnostics"; the shipped pass has three. A21 and A22 contradict each other.

`Docs/AnimationToolkit/Phase_B_Architecture.md:358` (A21):
> "`RigPartBakeLink` therefore carries `authoringPath` … while **every one of its four diagnostics** names the offending object."

`Packages/com.dotsanimationtoolkit/Authoring/Baking/RigBindingBakingSystem.cs` has exactly **three** `Debug.LogError` calls — lines 155, 172, 183 (`grep -c Debug.LogError` = 3). At `026a902` the same file had four (lines 109/116/125/136 of the old file).

A22 (`:360`, two paragraphs later, same C3 gate, same date) explicitly states the reduction:
> "Rows 1 and 2 of the pre-amendment binding pass — 'has no baked actor to bind to' and 'clip registry failed to build' — were unreachable by construction and have been **deleted**"

and its own ownership table assigns the binding pass exactly three rows. So A21 as shipped asserts a count that A22, twenty lines later in the same document, refutes, and that the code refutes. This is precisely the failure mode the owner flagged: an amendment written against the author's mental model of the code rather than the tree. Fix: A21's "four" → "three", or drop the count.

### F2 — VERIFIED CLEAN — §1.3 EditMode/Unity.Entities.Hybrid constraint holds

`Packages/com.dotsanimationtoolkit/Tests/EditMode/DotsAnimationToolkit.Tests.EditMode.asmdef` references exactly: Runtime, Authoring, Editor, UnityEngine.TestRunner, UnityEditor.TestRunner, Unity.Entities, Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions, Unity.Burst — **no `Unity.Entities.Hybrid`**, matching §1.3's row verbatim, and `includePlatforms: ["Editor"]`.

`AuthoringPathText.cs:1-6` imports only `System.Collections.Generic`, `System.Text`, `Unity.Collections` — no `Unity.Entities`, no `UnityEngine`. The split genuinely achieves what §8 M2 claims. `AuthoringPathText` is `internal` (`AuthoringPathText.cs:43`) and reachable from the suite via `Authoring/AssemblyInfo.cs:9` `[assembly: InternalsVisibleTo("DotsAnimationToolkit.Tests.EditMode")]`. Confirmed against the files, not the claim.

A17's PlayMode claim also holds: `Tests/PlayMode/DotsAnimationToolkit.Tests.PlayMode.asmdef` has `"includePlatforms": ["Editor"]`.

### F3 — VERIFIED CLEAN — A18's normative phase derivation matches the code

Spec `:491`: "**`phase01 = (AuthoringPathHash.Of(actorTransform) >> 8) × 2⁻²⁴`**".
`ActorBaker.cs:542-543`:
```
uint pathHash = AuthoringPathHash.Of(this, authoring.transform);
return (pathHash >> 8) * (1f / 16777216f);
```
2⁻²⁴ = 1/16777216. Matches. `AuthoringPathHash.Of` (`AuthoringPathHash.cs:76,97`) reads names through `baker.GetName` and the chain through `baker.GetParents`, as A18 mandates; the sibling index is read off `pathNode.transform.GetSiblingIndex()` (`:82`), which A18 explicitly documents as the accepted dependency gap. No `GetInstanceID` / `GetEntityId` anywhere in the package (grep clean).

### F4 — VERIFIED CLEAN — A23's "thirteen components" is arithmetically true against the tree

§5.2's root block lists 14 types (ClipRegistry, PlaybackLayer, AnimationCommand, AnimationCommandPending, AnimEventOutput, AnimEventsPending, RigPartRef, RigBindingUninitialized, AnimVisible, BoundsDirty, ActorRestBounds, SampleSettings, AnimLod, VatTextureBinding). `ActorBaker.Bake` (`ActorBaker.cs:75-106`) adds exactly those 13 unconditionally and `AnimLod` only under `if (authoring.addDistanceLod)` (`:103-106`). A23's count and its opt-in claim both check out.

### F5 — VERIFIED CLEAN — A24's ActorRestBounds formula matches `ActorBaker`

A24 (`:507`) requires "the union over every *bound part* of that part's rest position relative to the actor root, inflated by its target's `boundsExtents` scaled by `max(|scale.x|, |scale.y|, 1)`", never reading `offsetBounds`, with unresolved parts contributing nothing and a zero-extent box for an actor with no bound parts.
`ActorBaker.cs:416-429`:
```
float scaleFactor = math.max(math.max(math.abs(restScale.x), math.abs(restScale.y)), 1f);
float3 halfExtents = registry.Value.targetBoundsExtents[denseTargetIndex] * scaleFactor;
...
if (!anyPartBounded) { return new AABB { Center = float3.zero, Extents = float3.zero }; }
```
and `:400-406` skips unresolved target ids. `offsetBounds` is not referenced in `ActorBaker.cs`. Conformant.

### F6 — ADVISORY — A24's "every *bound part*" and `ActorBaker`'s notion of bound differ for duplicate-target parts

`ActorBaker.ComputeActorRestBounds` counts a part as contributing whenever its target id resolves in the registry (`ActorBaker.cs:400`). `RigBindingBakingSystem` additionally *drops* a part whose target another part already claimed (`RigBindingBakingSystem.cs:177-185`), so that part is never in `RigPartRef` and is not "bound". The rest box therefore includes a part the binding pass rejected. Harmless (bounds only ever grow, and the duplicate is an error case that already logs), but A24's phrase "every bound part" is not literally what the code computes.

### F7 — BLOCKING — A22 claims to fix "three doc comments"; one of them is still wrong, and it is on a customer-facing public type

A22 (`Phase_B_Architecture.md:360`) states the problem it exists to fix:
> "The rework that closed B3 moved the first of those into `RigTargetBaker` without recording it, leaving the row, **three doc comments** and the code all stating different things."

`Packages/com.dotsanimationtoolkit/Authoring/Baking/RigTargetAuthoring.cs:17-18` — the XML `<remarks>` on the **public inspector-facing MonoBehaviour** — still says:
> "A part whose id is not a target of the actor's rig is reported by `RigBindingBakingSystem` and skipped; the rest of the actor still bakes."

That is the pre-A22 ownership, verbatim. Under A22 the unknown-target-id error is owned by `RigTargetBaker` (`RigTargetBaker.cs:80-96`), and `RigBindingBakingSystem` never sees such a part at all because the baker withholds `RigPartBakeLink` (`RigTargetBaker.cs:99-105`). `RigTargetAuthoring.cs` was **not** touched in `026a902..HEAD`, so this is the fourth doc comment the amendment did not find. Blocking because A22's own stated deliverable was reconciling the doc comments with the code, this is the one type a consumer reads in the inspector/IntelliSense, and the sentence is false as shipped.

### F8 — ADVISORY — §4.1's `RigTargetBaker` row still says `RigPartBinding` carries a bake-time `targetId`; it does not

`Phase_B_Architecture.md:352`:
> "| `RigTargetAuthoring` + `RigTargetBaker` | … | Part entity: `RigPartBinding` (**bake-time `targetId`**; dense index resolved by the baking system), …"

§5.2 (`:678`) and the shipped code disagree: `RigPartBinding` is `{ Entity actorRoot; int targetIndex; }` and the bake-time `targetId` lives on `RigPartBakeLink` (`RigPartBakeLink.cs:35`), a separate `[BakingType]`. Predates C3 (the split is older), so not a C3 regression — but A22 edited the row directly beneath it while leaving this stale, and it is the same ownership confusion A22 exists to settle.

### F9 — BLOCKING — A18 asserts the `>> 8` shift is "the load-bearing part"; the shipped implementation comment says it has "no observable behaviour". The normative spec and the code it governs contradict each other.

`Phase_B_Architecture.md:491` (A18, normative):
> "The **shift is the load-bearing part**. FNV-1a ends on a multiply, and a multiply propagates carries only upward, so the low bits of the finished hash are the least mixed whichever input contributed them; bits 8–31 are exactly the 24 the phase needs, so discarding the bottom byte costs no range."

`ActorBaker.cs:531-541` (shipped source, added in this same C3 window):
> "NO TEST COVERS THE SHIFT, and none can — do not write one. … A fixture was written for this and deleted at the third C3 gate after it was shown to pass identically **with the shift and with the pre-A18 mask**, at every one of 200 container positions — it discriminated nothing while claiming to pin A18. … The shift is therefore **a defensible micro-improvement with no observable behaviour**, and that is the honest description of it."

Both cannot stand. Either A18's rationale paragraph is overclaimed and must be softened to match what the code's own author concluded, or the code comment is wrong. As shipped, the normative spec's justification for a normative formula is refuted by the implementation of that formula. This is the open **A-4** finding (`Docs/AnimationToolkit/Phase_C3_ReReview.md:550`, `Phase_C3_Gate3_Incomplete.md:90`) resolved in the code by a comment rather than in the spec — A18 was never amended to record the outcome, and nothing in `Phase_B_Architecture.md` mentions A-4 at all (grep: zero hits).

### F10 — ADVISORY — internal gate process leaks into shipped, customer-visible source

`ActorBaker.cs:531-541` ships, in the source of a sellable asset, an all-caps instruction to a future maintainer ("NO TEST COVERS THE SHIFT, and none can — **do not write one**") and a reference to "the third C3 gate" — a private review artefact the customer has never seen and cannot look up. Same class of leak, milder: `RigBindingBakingSystem.cs:155` tells the end user "if none does, this is a toolkit defect worth reporting" without saying *where* to report it.

### F11 — BLOCKING — CHANGELOG says "all four of `RigBindingBakingSystem`'s diagnostics"; there are three, and the same entry says two were deleted

`Packages/com.dotsanimationtoolkit/CHANGELOG.md:36-42`:
> "`RigPartBakeLink` carries the authoring object's hierarchy path … and **all four** of `RigBindingBakingSystem`'s diagnostics **now** name the part"

`CHANGELOG.md:103-104`, same 0.4.0 entry, 60 lines later:
> "**Two guards** that had become unreachable by construction were **deleted**."

The shipped file has three `Debug.LogError` calls (`RigBindingBakingSystem.cs:155,172,183`). "now name the part" is present tense about the shipped 0.4.0, so "all four" is a false statement about the release it documents, and it is self-contradicted inside the same entry. Same root defect as F1 (A21), reproduced in the customer-facing file. The brief notes the CHANGELOG has been wrong twice before; this is a third instance of the same class.

### F12 — BLOCKING — CHANGELOG asserts a phase-collision bug the author's own investigation disproved

`CHANGELOG.md:48-51`:
> "`ActorBaker`'s sample phase takes bits 8–31 of the path hash rather than the low 24. FNV-1a's last step is a multiply, so its low bits carry the least avalanche and **sibling names differing in one character landed on adjacent phases** — the opposite of what the phase is for."

`ActorBaker.cs:536-541` (same commit range):
> "A fixture was written for this and deleted at the third C3 gate after it was shown to **pass identically with the shift and with the pre-A18 mask, at every one of 200 container positions** — it discriminated nothing while claiming to pin A18. … The shift is therefore a defensible micro-improvement with **no observable behaviour**."

The CHANGELOG describes a concrete user-visible defect ("landed on adjacent phases") that the shipped code's own comment says was measured and did not exist. A changelog for a sold package must not claim a fix for a symptom that was never demonstrated. Pair with F9: the same overclaim sits in normative A18.

### F13 — VERIFIED CLEAN — test counts check out against the tree

Counted `[Test]/[TestCase]/[TestCaseSource]/[UnityTest]` attributes via `git show <sha>:<path>` (no arithmetic on the CHANGELOG):

| commit | EditMode | PlayMode |
|---|---|---|
| `ec44226` (C2 end) | **192** | — |
| `026a902` (C3 base) | **192** | — |
| `HEAD` | **205** (192 + `AuthoringPathTests` 12 + `PackagingConformanceTests` 8→9) | **27** (`ActorBakingAcceptanceTests` 26 + `PlayModeAssemblySmokeTest` 1) |

`Documentation~/index.md:72-75` claims "205 EditMode tests … plus 27 PlayMode tests, 26 of which bake real GameObject hierarchies" — **all three numbers are correct**. `CHANGELOG.md:160`'s historical "192 in the suite" for 0.3.0 is also correct against `ec44226`. The stale "164 EditMode tests" line in `index.md` was removed. No `[TestCase]`-with-arguments attributes exist, so attribute count equals NUnit case count. The count defect that failed the prior gates is fixed.

### F14 — VERIFIED CLEAN — version numbers are consistent

`package.json:4` `"version": "0.4.0"`; `README.md:9` "**Version:** 0.4.0 (pre-release)"; `Documentation~/index.md:24` "Current status: pre-release 0.4.0, build step C3"; `CHANGELOG.md:8` `## [0.4.0] - Unreleased`. All four agree.

### F15 — ADVISORY — every CHANGELOG release header is `- Unreleased`, and the 0.4.0 entry has two `### Changed` sections

`CHANGELOG.md:8,124,196,240` — `[0.4.0] - Unreleased`, `[0.3.0] - Unreleased`, `[0.2.0] - Unreleased`, `[0.1.0] - Unreleased`. The file declares adherence to Keep a Changelog (`:5-6`), under which only the topmost section may be Unreleased and shipped versions carry dates. The 0.4.0 entry also has `### Changed` twice (`:34` and `:96` "Changed (C3 re-review)"), and the second section documents churn against an *unreleased intermediate* state — e.g. `:109-112` says `BuildInvocationCount` "previously shipped", but the `git diff 026a902..HEAD` shows the member being **added** in this same range, so it has never shipped in any version. A consumer reading 0.4.0's changelog is told about regressions in code they never had.

### F16 — ADVISORY — `ClipRegistryBuilder.BuildInvocationCount`'s doc block is malformed XML

`Authoring/Build/ClipRegistryBuilder.cs:60-108`: a `<summary>` is followed by **two sibling `<remarks>` elements** on one member, and the first `<remarks>` opens with prose then closes a `</para>` that was never opened (`:73` `</remarks>` … the block at `:60-77` contains `</para>` with no matching `<para>`). With `/doc` enabled this is CS1570 ("XML comment has badly formed XML"); Unity does not enable it by default, so it is cosmetic today — but it is the doc comment on a sellable package's API file. The *substance* of the change is correct and matches `CHANGELOG.md:109-112`: the member and its increment are both inside `#if UNITY_EDITOR` (`:105`, `:152-154`) and the increment uses `Interlocked.Increment`.

### F17 — BLOCKING — `Documentation~/index.md` claims "Nothing that ships to a consumer uses reflection" in the same paragraph that names the shipping file that does

`Documentation~/index.md` (C3-added block):
> "That suite drives a bake through `BakingUtility.BakeGameObjects`, which Entities exposes only to its own test assemblies — the harness reaches it by reflection. This is disclosed rather than hidden because it is a real maintenance risk … **Nothing that ships to a consumer uses reflection; the dependency is confined to `Tests/PlayMode/BakingTestWorld.cs`.**"

`Tests/` **does** ship. The package's own CHANGELOG argues exactly that, in this same release: `CHANGELOG.md:118-120` — "the file ships in the tarball **unless `Tests/` is excluded** — so a consumer project imports and variant-compiles a built-in-pipeline shader out of a URP-only package". There is no `Tests/` exclusion in `package.json` (no `files`/`.npmignore` mechanism present), so `BakingTestWorld.cs` ships to the consumer and it is the reflection user. And `index.md`'s own "Running the tests" section says "the contract tests assert the shipped blob and component layouts **by reflection**". The disclosure paragraph, whose stated purpose is honest risk disclosure, closes with a sentence the package elsewhere refutes twice.

### F18 — BLOCKING — §8 M2's justification for making `RigPartBakeLink` and `ActorBakeFailed` public is not true, and it permanently enlarges a sellable package's support surface

`Phase_B_Architecture.md:936` (§8 M2 EXPOSES, added this gate):
> "`RigPartBakeLink` and `ActorBakeFailed` are **public because Entities requires baking types to be reachable from the system that queries them**; they carry no contract for a consumer and never reach a built entity scene."

The premise is false as applied here. Both types and the only system that queries them live in **the same assembly** (`DotsAnimationToolkit.Authoring`): `RigPartBakeLink.cs:29`, `ActorBakeFailed.cs:35`, and `RigBindingBakingSystem.cs:48`, all in namespace `DotsAnimationToolkit.Authoring`. "Reachable" is satisfied by `internal`. The file proves it: the two jobs that actually declare the queries are themselves `internal partial struct` — `RigBindingBakingSystem.cs:91` `internal partial struct ClearRigPartRefsJob` and `:123` `internal partial struct ResolveRigPartBindingsJob` — and IJobEntity codegen emits into the same assembly. The package already uses this pattern for `AuthoringPathHash.cs:52` and `AuthoringPathText.cs:43` (`internal static class`), reached from the test assembly via `Authoring/AssemblyInfo.cs:9-10` `InternalsVisibleTo` — the same grant would cover these two.

Consequence: `DotsAnimationToolkit.Authoring` is `autoReferenced: true`, so both types appear in every consumer's IntelliSense as public API of a sold asset, on a justification that does not hold. Remedy is either making them `internal` or replacing the sentence with the real reason. Flagged BLOCKING under the brief's rule that "anything newly public in the package is a support commitment" and because the normative spec records a technical claim that the tree contradicts.

*(UNVERIFIED caveat: I cannot compile in this environment, so "internal would compile" rests on the same-assembly evidence above rather than on a build. The false-justification half of the finding does not depend on that.)*

### F19 — ADVISORY — C3 added the "well formed rather than half built" principle to one `RigTargetBaker` branch and left two sibling branches half built

`RigTargetBaker.cs:168-176` (new this gate) argues:
> "the part simply falls back to `TargetKind.Quad` **so its entity is well formed rather than half built**."

But `RigTargetBaker.Bake` has two earlier bail-outs that `return` **before** `GetEntity` is ever called — `:43-52` (no `ActorAuthoring` on self or any parent) and `:58-65` (no rig from either the component or the actor's clip set). A part taking either path receives no `RigPartBinding`, no `TargetRestPose`, no `TargetPose`, no `AnimVisible` and no technique components — precisely the half-built part entity the new comment says the design avoids. Both are logged, so nothing is silent; the inconsistency is in the stated principle, not in diagnostics.

### F20 — ADVISORY — the branch A22 calls "the substance of this amendment" has no fixture

A22 (`Phase_B_Architecture.md:368`): "**The `ActorBakeFailed` tag is the substance of this amendment, not bookkeeping.** … rows 3 and 5 above are the branches that survive because of it", and it criticises the pre-amendment state as "a coupling nothing asserted, commented **or tested**."

Grepping `Tests/PlayMode/*.cs` for `ActorBakeFailed` / the new message text: only the **suppressed** side is asserted (`ActorBakingAcceptanceTests.cs:1015,1033` — tag present, binding pass silent). The unsuppressed branch — no registry **and** no tag, which is the branch the tag exists to create and the only one that emits `RigBindingBakingSystem.cs:155` — has no fixture, and neither does row 5 (`:172`). Understandable (both need a third-party baking system to strip `ClipRegistry`), but the amendment's own complaint about the old design applies unchanged to the new one. Not a §8 M2 ACCEPTANCE obligation, so ADVISORY; depth is Reviewer B's.

Related, same class: A23 (`:657`) promises "a second fixture pinning its presence when the authoring asks"; the tree has one fixture doing both halves (`ActorBakingAcceptanceTests.cs:412 BakingAnActor_AddsAnimLodOnlyWhenTheAuthoringAsksForIt`). Substantively equivalent, wording drift only.

### F21 — VERIFIED CLEAN — remaining checked claims

- **§8 M2 EXPOSES field lists match the shipped MonoBehaviours exactly.** `ActorAuthoring { ClipSetAsset clipSet; List<StartingLayerState> startingLayers; SampleSettings sampleOverride; bool addDistanceLod; }` → `ActorAuthoring.cs:32,41,51,61`. `RigTargetAuthoring { RigAsset rig; uint targetStableId; bool useKindOverride; TargetKind kindOverride; int restSliceIndex; int vatDrivingLayerIndex; Material expectedMaterial; }` → `RigTargetAuthoring.cs:31,38,47,54,63,72,82`. `StartingLayerState` is public and now named in M2 OWNS (`ActorAuthoring.cs:76`).
- **A22's ownership split is implemented as written.** Unknown target id → `RigTargetBaker.cs:89-95` with `authoring` as log context; link withheld at `:97-105`. Actor bail-out tag → `ActorBaker.cs:47,63,69` → `MarkBakeFailed()` at `:128-131`. Suppression → `RigBindingBakingSystem.cs:151-154`. Duplicate claim → `:177-185`. Registry/rig disagreement → `:160-173`. All five A22 rows land where the table says.
- **A20 / `SampleSettings` inspector field** — consumed at `ActorAuthoring.cs:51` as claimed.
- **VAT probe shader really is URP.** `Tests/PlayMode/VatMaterialProbe.shader:32` `Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }`, `:36` `HLSLPROGRAM`; no `CGPROGRAM`/`UnityCG.cginc` in the file. Matches `CHANGELOG.md:117-122`.
- **§5.8 (unamended body) does not contradict A24** — `:756` already required the runtime union "translated into actor space by `ActorRestBounds`". A24 is consistent with it, and A13's superseded clause was removed from `:503-505`.
- **Burst log-string sizes are within budget.** The three interpolated `Debug.LogError` messages measure 318 / 314 / 134 literal chars plus at most 125 path bytes and a uint, i.e. ≤ 449 bytes — under `FixedString512Bytes`. No format specifiers are used, so no BC1343 risk. *(UNVERIFIED: not compiler-checked; margin is comfortable.)*

## VERDICT

**FAIL**

The module's *code* is in good shape and conforms to the amendments in every substantive respect I could check against the tree: A18's phase formula, A22's five-way ownership split, A23's thirteen-component baseline, A24's rest-bounds formula, the §1.3 `Unity.Entities.Hybrid` constraint, the URP probe shader, and — for the first time across three gates — the test counts (205 EditMode / 27 PlayMode, verified by counting attributes at `ec44226` and `HEAD`, not by arithmetic on the CHANGELOG). What fails is the *documentation layer this gate was specifically about*. Three of the four amendments C3 introduced or ratified are internally inconsistent or overclaimed, and the same errors are mirrored into the customer-facing CHANGELOG and manual: A21 asserts a diagnostic count that A22 refutes twenty lines later and that the code refutes; A18 asserts a rationale that the shipped implementation's own comment calls "no observable behaviour"; the CHANGELOG repeats both and additionally claims a fix for a phase-collision bug the author measured and disproved; A22's stated deliverable — reconcile the doc comments — missed the one comment that sits on a public inspector-facing type; and §8 M2 records a false technical reason for enlarging the public API surface. This is the exact failure mode the owner named: claims reconciled against the author's notes rather than against the shipped tree.

Blocking items:

1. **F1** — A21 (`Phase_B_Architecture.md:358`) says the binding pass has "four diagnostics"; it has three (`RigBindingBakingSystem.cs:155,172,183`), and A22 (`:368`) says two were deleted.
2. **F7** — `RigTargetAuthoring.cs:17-18` still tells consumers the unknown-target-id error comes from `RigBindingBakingSystem`; A22 moved it to `RigTargetBaker` and A22's own preamble claims the doc comments were reconciled.
3. **F9** — A18 (`:491`) calls the `>> 8` shift "the load-bearing part"; `ActorBaker.cs:536-541` says it was measured to have "no observable behaviour". The open A-4 finding was answered in a code comment and never recorded in the spec.
4. **F11** — `CHANGELOG.md:37` "all four of `RigBindingBakingSystem`'s diagnostics" — there are three, and `CHANGELOG.md:104` in the same entry says two were deleted.
5. **F12** — `CHANGELOG.md:48-51` claims sibling names "landed on adjacent phases", a defect `ActorBaker.cs:536-541` records as disproved at 200 container positions.
6. **F17** — `Documentation~/index.md` "Nothing that ships to a consumer uses reflection" is refuted by `CHANGELOG.md:118-120` (Tests/ ships) and by index.md's own "contract tests … by reflection".
7. **F18** — `Phase_B_Architecture.md:936` justifies public `RigPartBakeLink` / `ActorBakeFailed` with a constraint that does not apply (same assembly; the querying jobs are themselves `internal`), permanently widening the public API of a sellable package.

Every blocking item is a text fix except F18, which is a text fix or a two-word visibility change. None requires reworking shipped behaviour.




