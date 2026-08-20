# Reviewer B — Test Integrity — DOTS Animation Toolkit C3 Gate

STATUS: complete — VERDICT: **FAIL** (1 blocking, 6 advisory). See `## VERDICT` at the end.

Scope: `git diff 026a902..HEAD` restricted to `Packages/com.dotsanimationtoolkit` and `Docs/AnimationToolkit`.
Lens: **Would this test fail if the code were wrong?**

Findings are appended incrementally as they are formed. Verdict is written last.

---

## Findings

### F1 — BLOCKING — `RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate` is non-discriminating (direct sibling of the deleted A-4 test)

`Tests/EditMode/AuthoringPathTests.cs:151-184`

```csharp
List<string> emojiPath = new List<string>
{
    Repeat(AstralCharacter, 20),
    Repeat(AstralCharacter, 20)
};
```

The production behaviour it names is `AuthoringPathText.TakeTrailingBytes`
(`Authoring/Baking/AuthoringPathText.cs:105-109`):

```csharp
int candidateIndex = startIndex - 1;
if (candidateIndex > 0 && char.IsLowSurrogate(text[candidateIndex]))
{
    candidateIndex--;
}
```

**Concrete mutation this test fails to catch: delete those three lines** (i.e. revert to
stepping back one UTF-16 code unit at a time — precisely the bug the test's own comment
says it guards against).

Trace with the test's input. Rendered full path = 20 astral (indices 0..39) + `'/'`
(index 40) + 20 astral (indices 41..80); length 81 UTF-16 units, 161 UTF-8 bytes, so it
truncates with `byteBudget = 125 - 4 = 121`.

- **Correct code:** pairs cost 4 bytes. 20 leaf pairs = 80 bytes (startIndex 41), `'/'`
  = 1 byte (used 81, startIndex 40), then 10 root pairs = 40 bytes (used 121,
  startIndex 20). Next pair would be 125 > 121 → break. `Substring(20)` starts on a
  **high** surrogate. Aligned.
- **Mutated code:** a lone surrogate encodes via the replacement fallback to 3 bytes.
  40 leaf units × 3 = 120 bytes (startIndex 41), `'/'` = 1 byte (used 121, startIndex
  40). Next unit would be 124 > 121 → break. `Substring(40)` = `"/"` + the leaf's 20
  **intact** pairs.

Result under the mutation: `".../" + "/" + 20 emoji`, 85 bytes. Every assertion in the
test holds — `DoesNotThrow` ✓, `≤ 125 bytes` ✓, no unpaired high surrogate ✓, no lone low
surrogate ✓. The test passes identically with the behaviour it names removed.

This is not an accident of one number: the input is built from two nodes of *even*
astral-character counts, so the naive scan can only ever land on `'/'` or a pair
boundary. Any input where the retained region begins at an odd offset inside a
pair-region would discriminate (the test needs a node with an odd unit offset relative to
the budget, e.g. mixing a 1-byte ASCII run into the leaf node so the byte arithmetic does
not land on a pair boundary).

This is the same failure mode as the A-4 test
(`TwoActorsWhoseNamesDifferOnlyInTheLastCharacter_GetWellSeparatedPhases`) that was
deleted for passing under both hash derivations.

---

### F2 — CONFIRMED OK — item 5, the stray-bounds "exact box" is independently derived

`Tests/PlayMode/ActorBakingAcceptanceTests.cs:1284-1292` and `:807-828`.

Re-derived independently from `ActorBakeFixture.cs:79-94`:

- Torso local `(0.4, 1, 0)`, extents `(0.5, 0.75, 0.1)` → x `[-0.10, 0.90]`, y `[0.25, 1.75]`, z `[-0.10, 0.10]`
- LeftArm is a child of Torso, local `(-0.6, 0.3, 0)` → actor space `(-0.2, 1.3, 0)`, extents `(0.25, 0.5, 0.1)` → x `[-0.45, 0.05]`, y `[0.80, 1.80]`
- Head local `(0, 12, 0)`, extents `(0.4, 0.4, 0.1)` → x `[-0.40, 0.40]`, y `[11.60, 12.40]`
- Union: x `[-0.45, 0.90]`, y `[0.25, 12.40]`, z `[-0.10, 0.10]`

That matches the asserted constants exactly. The numbers are hand-derived, not
observation-copied, and the fixture's off-axis torso x (`0.4`) plus nested left arm make
the chain-walk observable on both axes. **No finding.** (Minor: the stray variant at
`:1284-1292` omits the z asserts the no-stray variant makes; harmless.)

---

### F3 — ADVISORY — `AssertNoUnexpectedToolkitErrors` compares only the *count*, so N different errors are silently accepted

`Tests/PlayMode/BakingTestWorld.cs:241-257`

```csharp
if (recordedErrors.Length == expectedToolkitErrorCount)
{
    return;
}
```

The brief's question "a test that expects N errors but produces N *different* errors is
not silently accepted" — it **is** silently accepted by the harness itself. The harness
is a pure count. Whether that matters is then per-test:

- `APartWithAnUnknownTargetId_IsSkippedWithAnError_AndTheBakeStillSucceeds`
  (`:950-984`) pins content hard — `"Stray"`, the id, and `"does not declare"`. ✓
- `AnActorOnAClipSetWithValidationErrors_LogsEveryRuleCode_AndBakesNoRegistry`
  (`:986-1049`) pins `'Set'`, `V01`, `V02`. ✓
- `AVatPartWhoseMaterialLacksTheTextureSlot_LogsExactlyOneWarning` (`:1055-1126`) pins
  `"already claims"` and a path fragment. ✓
- **`AStrayPartDoesNotEnlargeTheRestBounds_AndTheValidPartsStillFillIt` (`:1262-1293`)
  calls `bakingWorld.ExpectToolkitErrors(1)` at `:1272` and never inspects the error at
  all.** Mutation it would not catch: `RigTargetBaker` stops emitting the A22
  unknown-target message and the Bursted `RigBindingBakingSystem` emits its
  registry-disagreement message instead — one error either way, bounds unchanged, test
  green. The A22 emitter-identity claim is pinned only by its sibling at `:950`.

Advisory rather than blocking because the sibling test does pin it; but the harness
being count-only is a standing false-assurance hazard for every future test that only
declares a count.

---

### F4 — CONFIRMED OK — the error-expectation harness: subscription, per-test scoping, TearDown, and the threaded race

`Tests/PlayMode/BakingTestWorld.cs:113-153`, `Tests/PlayMode/ActorBakingAcceptanceTests.cs:31-68`

- **Genuinely fails.** `AssertNoUnexpectedToolkitErrors` throws `AssertionException`
  (`BakingTestWorld.cs:252`) from `[TearDown]`; NUnit reports a TearDown throw as a test
  failure. ✓
- **Per-test, no leak.** `expectedToolkitErrorCount` is an *instance* field
  (`BakingTestWorld.cs:187`) and `[SetUp]` constructs a fresh `BakingTestWorld`
  (`ActorBakingAcceptanceTests.cs:35`). It cannot survive into the next test. ✓
- **Correct callback.** `Application.logMessageReceivedThreaded += RecordToolkitLog`
  (`BakingTestWorld.cs:135`), not the main-thread-only `logMessageReceived`. This is
  load-bearing: `ResolveRigPartBindingsJob` is `.Schedule()`d
  (`RigBindingBakingSystem.cs:80`) and its three `Debug.LogError` calls
  (`:155`, `:172`, `:183`) run on a worker thread. ✓
- **No late-delivery race.** I traced this rather than assuming. `Bake()` unsubscribes in
  its `finally` (`:150`), so a message arriving after the reflection call returns would be
  dropped *silently and in the unsafe direction* (a real error counted as zero). It
  cannot happen: `BakingUtility.BakeGameObjects` ends with `PostprocessBake`
  (`Library/PackageCache/com.unity.entities@e30ad8d00609/Unity.Entities.Hybrid/Baking/BakingUtility.cs:194-202`),
  which runs `PostBakingSystemGroup` and then `BakingStripSystem`, whose `OnUpdate` is
  `EntityManager.RemoveComponent(query, componentType)` per registered baking type
  (`BakingStripSystem.cs:36-45`) — a structural change, which forces
  `BeforeStructuralChange` → complete-all-tracked-jobs. The resolve job is therefore
  joined before `BakeGameObjects` returns. ✓
- **`[TearDown]` is *not* unconditional** (`ActorBakingAcceptanceTests.cs:62-65`): it is
  skipped when the body already failed. Documented and defensible — it can only suppress
  a follow-on complaint on an already-red test, never turn a red test green.

**No finding.** This part of C3 is sound. (See F3 for the count-only weakness that
remains.)

---

### F5 — ADVISORY — `ExpectToolkitErrors` is only reset by constructing a new world, and `Bake()` clears the recorded lists, so a two-bake test measures only the last bake

`Tests/PlayMode/BakingTestWorld.cs:115-119` and `:232-235`

```csharp
lock (toolkitLogLock)
{
    toolkitWarnings.Clear();
    toolkitErrors.Clear();
}
```

`ExpectToolkitErrors` sets a sticky field; `Bake` resets the *observations* but not the
*expectation*. No test in the tree bakes twice on one world today
(`TwoActorsFromOneClipSet_…` uses a second world at `ActorBakingAcceptanceTests.cs:566`),
so this is latent rather than live — but a future test that bakes twice would have the
first bake's errors discarded with no signal. Worth a one-line reset in `Bake()`.

---

### F6 — CONFIRMED OK — item 4, the rebake test genuinely re-bakes and the pinning is real

`Tests/PlayMode/ActorBakingAcceptanceTests.cs:513-587`

```csharp
Object.DestroyImmediate(firstActor);
Object.DestroyImmediate(secondActor);

GameObject rebuiltFirstActor = fixtureAssets.CreateStandardActor("FirstActor", clipSet, false);
ActorBakeFixture.PlaceUnder(rebuiltFirstActor, phaseRoot, 0);
...
using (BakingTestWorld rebakeWorld = new BakingTestWorld("RebakeWorld"))
{
    rebakeWorld.Bake(phaseRoot);
```

- It is a **new `World` + new `BlobAssetStore` + a fresh `BakingUtility.BakeGameObjects`
  invocation**, not a re-read of the first world. ✓
- The instances are genuinely fresh (`DestroyImmediate` then `CreateStandardActor`), so
  `Object.GetInstanceID` differs — which is what makes the assertion discriminate against
  an instance-id derivation. ✓
- The pinning is real: `PlaceUnder` calls `SetParent` then `SetSiblingIndex`
  (`ActorBakeFixture.cs:226-230`), and the hash folds the sibling index in
  (`AuthoringPathHash.cs:82`). The container `phaseRoot` is the *same* object across both
  bakes so its own name and scene-root sibling index are held constant — which matters,
  because `ComputeSamplePhase` hashes the actor's whole ancestor chain
  (`ActorBaker.cs:542`). ✓
- `rebakeWorld.AssertNoUnexpectedToolkitErrors()` is called explicitly at `:585`, since
  `[TearDown]` only covers `bakingWorld`. ✓

**No finding.**

---

### F7 — CONFIRMED — item 7, the test-count claim is accurate

Counted by attribute over the shipped tree
(`Packages/com.dotsanimationtoolkit/Tests/`):

- **EditMode: 205** `[Test]` (AuthoringPathTests 12, ClipRegistryBuilderTests 18,
  ClipRegistryDeterminismTests 14, ClipRegistryUtilTests 10, ClipSamplerTests 12,
  ClipValidationTests 32, ContentHashGoldenTests 5, DataContractTests 9, EasingTests 7,
  EventWrapMathTests 16, LayerCompositionTests 14, LoopTimeMappingTests 12,
  PackagingConformanceTests 9, RuntimeContractTests 13, SampleQuantizationTests 5,
  StableIdentityTests 17)
- **PlayMode: 27** (`ActorBakingAcceptanceTests` 26 + `PlayModeAssemblySmokeTest` 1)
- **Total 232.**

There are **zero** `[TestCase]`, `[TestCaseSource]`, `[Values]`, `[ValueSource]`,
`[Repeat]`, `[Combinatorial]`, `[Theory]` or `[UnityTest]` attributes anywhere in the
package tests, so no multiplication applies and the attribute count equals the case
count. The CHANGELOG's 205 + 27 claim is **accurate**.

---

### F8 — CONFIRMED OK — item 8, no `LogAssert.ignoreFailingMessages` in `[SetUp]`

Grep over `Packages/com.dotsanimationtoolkit/Tests/` and `Assets/_Scripts/Tests/`:
the only assignments are `BakingTestWorld.cs:133-134` and `:151`, inside `Bake()`, with
save/restore around the reflection call. `ActorBakingAcceptanceTests.cs:44-47` documents
why it is *not* in `[SetUp]` (UTF's `BeforeAfterTestCommandBase` disposes the setup
LogScope before the body runs). The trap named in the brief is explicitly avoided.
**No finding.**

---

### F9 — ADVISORY (borderline blocking) — the `ActorBakeFailed` tag's *causal* effect on `RigBindingBakingSystem`'s silence is asserted nowhere

`Authoring/Baking/RigBindingBakingSystem.cs:151-156`

```csharp
if (actorBakeFailedLookup.HasComponent(bakeLink.actorRoot))
{
    return;
}
Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' belongs to an actor that has no usable clip registry, and no earlier message explained why. ...");
```

`AnActorOnAClipSetWithValidationErrors_LogsEveryRuleCode_AndBakesNoRegistry`
(`Tests/PlayMode/ActorBakingAcceptanceTests.cs:986-1049`) asserts (a) exactly one error
with three parts, and (b) `HasComponent<ActorBakeFailed>` on the actor. It never
establishes that (b) *causes* (a).

**Concrete mutation it fails to catch: delete the `actorBakeFailedLookup.HasComponent`
check and the `Debug.LogError` beneath it — i.e. make the job unconditionally silent
about a missing registry.** Error count is still 1, the tag is still added by
`ActorBaker.MarkBakeFailed` (`ActorBaker.cs:128-131`), both assertions still pass. That
mutation restores exactly the pre-A22 defect the CHANGELOG says was fixed ("an actor that
lost its registry could fail silently").

The test's own comment at `:1035-1038` states the causal claim — "The binding pass's
silence about the three unbound parts is conditional on that tag (A22): without it the
pass reports an unexplained missing registry" — which the test does not constrain. The
missing fixture is the inverse: an actor entity with no `ClipRegistry` and **no**
`ActorBakeFailed`, asserting the error *does* fire. That is constructible in the harness
(bake, then remove `ActorBakeFailed` / `ClipRegistry` and re-run the group, or add a
throwaway baking system).

Advisory rather than blocking only because the mutation loses a diagnostic rather than
producing wrong baked data — but it is the single named deliverable of amendment A22 and
it is unpinned.

---

### F10 — ADVISORY — `ActorBaker`'s other two `MarkBakeFailed` bail-outs have no test at all

`Authoring/Baking/ActorBaker.cs:41-49` (null clip set) and `:57-65` (null rig).

Grep over the whole test tree for `MarkBakeFailed`, `"has no clip set assigned"`,
`"which has no rig assigned"`, `"has no Actor component"`, `"quotes its target id"`,
`"has no rig: neither"`, `"seeds layer"` returns **zero hits**. Only the third bail-out —
the `ClipValidationException` catch at `:199-206` — is exercised.

This matters because the A22 doc comment on `MarkBakeFailed` (`:116-119`) says "**Every**
early return above must call this", and only one of the three is verified to. It is also
where the behaviour is least obvious: with a null clip set, `RigTargetBaker` resolves
`effectiveRig == null` for every part and emits its own error per part
(`RigTargetBaker.cs:58-65`), so the real output is 1 + N errors — arguably correct, but
nobody has looked, and "one actionable message" is the claim C3 makes.

Also untested C3 error branches: `RigTargetBaker`'s no-actor-in-parents error
(`:44-52`), its rig-mismatch error (`ResolveEffectiveRig`, `:138-146`), and
`ActorBaker.SeedStartingLayers`' two error branches (`:285-293`, `:298-310`).

---

### F11 — ADVISORY — `Supplementary_NoShaderUsesTheBuiltInPipeline` passes vacuously if the file scan returns nothing

`Tests/EditMode/PackagingConformanceTests.cs:351-378`, helper at `:400-412`

```csharp
List<string> scannedFiles = EnumeratePackageFiles(
    new string[] { "*.shader", "*.hlsl", "*.cginc" });
foreach (string scannedFile in scannedFiles) { ... }
Assert.IsEmpty(violations, ...);
```

There is no assertion that `scannedFiles` is non-empty. If `PackageRootPath` resolution or
the glob ever broke, the test would report green while scanning zero files — the
"asserting a collection is non-empty when the interesting property is its contents"
failure mode, inverted. It is non-vacuous *today* only because
`Tests/PlayMode/VatMaterialProbe.shader` happens to exist; the package ships exactly one
shader, so this is one deletion away from being a permanently-green no-op.

Secondary: the regex `\bCGPROGRAM\b|\bCGINCLUDE\b|UnityCG\.cginc` does not detect a
built-in-pipeline shader written with `HLSLPROGRAM` and non-URP includes. The shader it
guards (`VatMaterialProbe.shader:32-39`) is correctly retargeted — URP `RenderPipeline`
tag, `HLSLPROGRAM`, URP `Core.hlsl`. Fix is one line: assert `scannedFiles.Count > 0`.

---

### F12 — ADVISORY — `AssertToolkitComponentsAre` compares component **short names**, not types

`Tests/PlayMode/ActorBakingAcceptanceTests.cs:206-250`

```csharp
presentToolkitNames.Add(managedType.Name);
...
expectedNames.Add(expectedComponentTypes[expectedIndex].Name);
```

Two toolkit types with the same short name in different sub-namespaces (e.g. a
`Runtime` and an `Authoring` `AnimVisible`) would compare equal. Low likelihood, trivial
to harden by comparing `FullName`. Noted because this is the assertion carrying the word
"exactly" for both archetypes.

Credit where due: the namespace filter is `StartsWith("DotsAnimationToolkit")`,
which includes `…​.Authoring`, so the exact-archetype assertion *does* catch a stray
`ActorBakeFailed` or `RigPartBakeLink` landing on the root — that is real discrimination.

---

### F13 — NOTED, NO FINDING — the shift in `ComputeSamplePhase` is documented as untestable, honestly

`Authoring/Baking/ActorBaker.cs:531-541` carries an explicit "NO TEST COVERS THE SHIFT,
and none can — do not write one", explaining that the deleted A-4 fixture passed
identically under both derivations at 200 container positions. That is the correct
disposition of the prior gate's finding: a known-non-discriminating test was removed and
the gap was recorded rather than papered over. This is the standard F1 fails to meet.

---

## VERDICT

**FAIL**

The C3 test work is, on the whole, unusually good: the error-expectation harness is
correctly built (right callback for Bursted diagnostics, per-instance counter, no
late-delivery race — I traced the completion through `BakingStripSystem`), the rebake test
genuinely re-bakes from fresh instances with real sibling pinning, the stray-bounds "exact
box" is hand-derived and I re-derived it independently to the same six numbers, the
`LogAssert.ignoreFailingMessages`-in-`[SetUp]` trap is explicitly avoided and documented,
the CHANGELOG's 205 + 27 test claim is exactly right, and the prior gate's
non-discriminating phase test was deleted with an honest "none can be written" note rather
than replaced with a weaker one. But the module ships one new test that passes identically
with the behaviour it names removed — `RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate`
— and I traced the mutation byte by byte rather than suspecting it. That is the precise
failure mode this gate exists to catch and the precise reason the A-4 test was deleted, so
shipping a fresh instance of it inside the same module cannot pass. The fix is small (an
input whose retained region starts at an odd offset inside a surrogate pair), which is why
this is a fail-and-return rather than a rework.

### Blocking items

1. **F1** — `Tests/EditMode/AuthoringPathTests.cs:151-184`,
   `RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate` is non-discriminating.
   Deleting the low-surrogate step-back at `Authoring/Baking/AuthoringPathText.cs:106-109`
   leaves every assertion green: with the test's two-even-astral-node input a lone
   surrogate costs 3 replacement bytes, the naive scan stops on the `'/'` at index 40, and
   `Substring(40)` returns `"/"` plus 20 intact pairs. Needs an input where the retained
   region begins at an odd offset inside a pair (e.g. an ASCII run inside the leaf node so
   the budget arithmetic does not land on a pair boundary), plus — ideally — a direct
   assertion that the byte budget is respected under the *correct* 4-byte accounting rather
   than the fallback 3-byte one.

### Advisory items

- **F3** — `BakingTestWorld.AssertNoUnexpectedToolkitErrors` is count-only; N different
  errors are silently accepted. `AStrayPartDoesNotEnlargeTheRestBounds_…`
  (`ActorBakingAcceptanceTests.cs:1262-1293`) declares `ExpectToolkitErrors(1)` and
  inspects nothing.
- **F5** — `ExpectToolkitErrors` is sticky across bakes on one world while `Bake()` clears
  the observations; latent, not live.
- **F9** — the `ActorBakeFailed` tag's causal effect on `RigBindingBakingSystem`'s silence
  is unpinned; deleting the check *and* the error beneath it passes every assertion. This
  is amendment A22's headline deliverable.
- **F10** — two of `ActorBaker`'s three `MarkBakeFailed` bail-outs, plus four other C3
  error branches in `RigTargetBaker` / `SeedStartingLayers`, have zero coverage.
- **F11** — `Supplementary_NoShaderUsesTheBuiltInPipeline` passes vacuously on an empty
  scan; one `Assert.Greater(scannedFiles.Count, 0)` away from safe.
- **F12** — `AssertToolkitComponentsAre` compares short type names, not full names.

### Independently counted test totals

| Suite | Count |
|---|---|
| EditMode `[Test]` | **205** |
| PlayMode `[Test]` | **27** (ActorBakingAcceptanceTests 26 + PlayModeAssemblySmokeTest 1) |
| **Total** | **232** |

No `[TestCase]`, `[TestCaseSource]`, `[Values]`, `[ValueSource]`, `[Repeat]`,
`[Combinatorial]`, `[Theory]` or `[UnityTest]` attributes exist anywhere in the package
test tree, so attribute count equals case count and no multiplication applies. The
CHANGELOG claim is accurate.

STATUS: complete

