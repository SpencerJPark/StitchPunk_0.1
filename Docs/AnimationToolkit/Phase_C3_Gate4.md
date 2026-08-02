# Phase C3 — Gate 4 (three-lens review)

**Run:** 2026-08-01 · **Scope:** `git diff 026a902..HEAD` over `Packages/com.stitchpunk.dotsanimationtoolkit` and `Docs/AnimationToolkit` (27 files, ~3,703 insertions)
**Shape:** three narrow agents in parallel, one lens each, each appending to its own file as it went. All three completed — the first gate of the four attempted to do so.

## Verdict: **FAIL** — 8 blocking items, 0 requiring rework of shipped behaviour

| Lens | Verdict | Blocking | Advisory |
|---|---|---|---|
| A — spec conformance | FAIL | 7 | 6 |
| B — test integrity | FAIL | 1 | 6 |
| C — code correctness | PASS | 0 | 5 |

Full findings, verbatim:
- [`Phase_C3_Gate4_ReviewerA_Spec.md`](Phase_C3_Gate4_ReviewerA_Spec.md)
- [`Phase_C3_Gate4_ReviewerB_Tests.md`](Phase_C3_Gate4_ReviewerB_Tests.md)
- [`Phase_C3_Gate4_ReviewerC_Code.md`](Phase_C3_Gate4_ReviewerC_Code.md)

**The code is sound. The paperwork around it is not.** Every blocking item is a text fix or a single test input. Nothing shipped behaves wrongly.

---

## The four "do this first" items — all answered

These had zero coverage across the entire third attempt. Reviewer C verified each against `Library/PackageCache` sources with citations, not from memory.

1. **Can `ActorBakeFailed` go stale? — No. Proven, not assumed.**
   `Baker.AddComponent` records the tag in `BakerState.AddedComponents` (`Baker.cs:1595-1607` → `:1403-1407`); `BakerState.Revert` (`BakerState.cs:53-60`) removes every recorded type index; `BakedEntityData.cs:538-558` reverts every `instructions.BakeComponents` entry *before* re-baking; `:903-910` plays `revertEcb` back before the baker ECB ("The state reverts have to be applied before the state changes"). Asset-only dependency changes — assigning the missing `RigAsset` without touching `ActorAuthoring` — enter the same list via `IncrementalBakingContext.cs:434-466`. **The single most likely place a defect was hiding is clean.**

2. **Did the `IBaker` refactor change the hash? — The hash, no. The call site, yes.**
   `AuthoringPathHash.Of` is byte-for-byte input-identical: `Baker.GetParents` (`Baker.cs:613-628`) yields parent→root excluding the leaf, matching the old chain exactly; constants, separator, sibling index, `^` overload resolution and null handling all match. But the *derivation* changed — `(pathHash & 0x00FFFFFFu)` → `(pathHash >> 8)` — so baked `phase01` values shift. **Not a defect: that is amendment A18, ratified deliberately, and the package has never been released** (version 0.4.0, no git tags, no consumers). Verified independently.

3. **`TakeTrailingBytes` — correct.** No surrogate pair or UTF-8 sequence can split; `125 − 4 = 121` is exact; `Unicode.Utf16ToUtf8` truncates on rune boundaries so the `CopyFromTruncated` backstop is safe; .NET and Unity agree on byte counts even for lone surrogates (both 3), so the budget cannot under-count.

4. **`ComponentLookup<ActorBakeFailed>.HasComponent` on a zero-sized tag in a Bursted `IJobEntity` — legal.** Reads only `LookupCache.IndexInArchetype` (`EntityComponentStore.cs:1325-1335`, `:3183-3189`), never data or size. Burst 1.8.29 `FixedStringNBytes` interpolation also confirmed via its CHANGELOG (ICE fixed in 1.8.21).

---

## Blocking items

### From Reviewer A — the documentation layer (7)

The recurring defect is one claim, stated in three places, contradicted by amendment A22 sitting twenty lines away in the same documents.

1. **F1** — `Phase_B_Architecture.md:358` (A21): binding pass has "four diagnostics". It has three (`RigBindingBakingSystem.cs:155,172,183`); A22 says two were deleted.
2. **F11** — `CHANGELOG.md:37`: "all four of `RigBindingBakingSystem`'s diagnostics"; `:104` in the same entry says two were deleted. **Third occurrence of the CHANGELOG-count defect across three gates.**
3. **F7** — `RigTargetAuthoring.cs:17-18`, a public inspector-facing type, still attributes the unknown-target-id error to `RigBindingBakingSystem`. A22 moved it to `RigTargetBaker` and claimed the doc comments were reconciled.
4. **F9** — A18 (`:491`) calls the `>> 8` shift "the load-bearing part"; `ActorBaker.cs:536-541` says it has "no observable behaviour". A-4 was answered in a code comment and never recorded in the spec.
5. **F12** — `CHANGELOG.md:48-51` claims sibling names "landed on adjacent phases" — a defect `ActorBaker.cs:536-541` records as disproved at 200 container positions.
6. **F17** — `Documentation~/index.md`: "Nothing that ships to a consumer uses reflection", refuted by `CHANGELOG.md:118-120` (Tests/ ships) and by index.md's own "contract tests … by reflection".
7. **F18** — `Phase_B_Architecture.md:936` justifies public `RigPartBakeLink`/`ActorBakeFailed` with "Entities requires baking types to be reachable". **False here** — same assembly, and the querying jobs are themselves `internal`. Widens a sold package's public API on a bad premise.

### From Reviewer B — one non-discriminating test (1)

8. **F1** — `AuthoringPathTests.cs:151-184`, `RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate` **is a fresh instance of the exact A-4 failure mode, inside the module the gate was convened over.**
   Traced byte by byte: delete the low-surrogate step-back at `AuthoringPathText.cs:106-109` and every assertion still passes. With the test's two-even-astral-node input, a lone surrogate encodes to 3 replacement bytes, the naive scan halts on the `'/'` at index 40, and `Substring(40)` returns `"/"` plus 20 intact pairs. **No other test in the tree touches surrogates**, so the step-back is entirely unpinned.
   Fix: an input whose retained region begins at an *odd* offset inside a pair.

### Promoted to blocking during consolidation (1)

9. **`RigBindingSystem` does not exist, and two load-bearing claims rest on it.**
   Reviewer C filed this as advisory; I am promoting it, because three of A's seven blocking items are the same defect class — a doc comment asserting what the tree does not do — and here it is load-bearing in two places. `Runtime/Systems/` is an empty directory; verified independently. Seven doc comments across five files reference `RigBindingSystem` in the **present tense** ("re-derives", "rebuilds", "rewritten by"), and it is:
   - the sole justification that changing baked `phase01` is harmless (`ActorBaker.cs:507-508`), and
   - the sole argument that `RigPartRef` order is unspecified (`RigBindingBakingSystem.cs:39-42`).

   Both conclusions happen to hold anyway — nothing in `Runtime/` writes `SampleSettings`, and the package is unreleased — but they hold for *different reasons than stated*. This is C4's system; the comments should be marked forward-looking, not present-tense.

---

## Verified clean — do not re-litigate

Recorded so the next gate does not spend budget here.

- **Test counts are correct for the first time in four gates.** 192 at `ec44226`, **205 EditMode + 27 PlayMode = 232** at HEAD. Counted by attribute via `git show` by Reviewer A and independently recounted by Reviewer B; zero `[TestCase]`/`[Values]` in the package, so no multiplication applies. Matches `index.md:72` exactly.
- **Amendments A18, A22, A23, A24 all match the tree** — A22's five-way ownership split lands where the table says; A23's thirteen-component baseline counted in `ActorBaker.Bake`; A24's rest-bounds formula correct.
- **§1.3's `Unity.Entities.Hybrid` prohibition holds** — EditMode asmdef clean; `AuthoringPathText` imports only Collections, reached via `InternalsVisibleTo`.
- **The error harness is sound** — correct callback (`logMessageReceivedThreaded`, load-bearing for the Bursted `ResolveRigPartBindingsJob` diagnostics), per-instance counter that cannot leak, and **no late-delivery race**: `BakingStripSystem.OnUpdate` calls `EntityManager.RemoveComponent(query, …)`, forcing complete-all-jobs inside `BakeGameObjects` before the unsubscribe.
- **The rebake test genuinely rebakes** — second `World` + `BlobAssetStore`, fresh instances (so it does discriminate against an instance-id derivation), sibling indices really pinned.
- **The stray-bounds "exact box" is independently derived** — Reviewer B re-derived all six numbers by hand from the fixture.
- **No `LogAssert.ignoreFailingMessages` in `[SetUp]`** anywhere — explicitly avoided and documented.
- **Hard-rule conformance clean across all eight code files** — no `var`, no single-letter names, no `.Run()`, `[ReadOnly]` from `Unity.Collections`, `[DisallowMultipleComponent]` on both authoring types.

## A-4 — ruled, closed

**Keep the `>> 8` shift.** It is never worse than the masked derivation, and it is the derivation that stays correct if a future caller of this general-purpose helper mixes its discriminator last. Do not write the discriminating test — it is not constructible, for the reason already recorded.

**But cut the 14-line comment at `ActorBaker.cs:524-541` to two lines.** Narrating a deleted fixture and shouting "do not write one" is gate correspondence, not documentation, in a file customers will open. Record the reasoning in the spec (which also closes A's F9).

## Remaining advisories worth acting on

- The error harness compares only the **count** of toolkit errors, so N *different* errors pass silently. `AStrayPartDoesNotEnlargeTheRestBounds_…` declares a count and inspects nothing.
- **A22's headline deliverable is unpinned** — the `ActorBakeFailed` tag's *causal* effect on `RigBindingBakingSystem`'s silence has no test; deleting the check and the error under it passes both assertions. (Reviewer C proved the mechanism is correct, so this is a coverage gap, not a defect.)
- Two of `ActorBaker`'s three `MarkBakeFailed` bail-outs have zero coverage.
- `MarkBakeFailed` can be called without logging — re-creates the exact unenforced coupling the tag was added to eliminate. Fold the log into the helper.
- A failed actor's entity now always survives baking: `TransformUsageFlags.None` marks an entity *used*, not unused.
- `Supplementary_NoShaderUsesTheBuiltInPipeline` passes vacuously on an empty file scan.
- Malformed doc XML at `ClipRegistryBuilder.cs:71`; `MaximumPathBytes = 125` is a hand-copied literal worth binding to `FixedString128Bytes.UTF8MaxLengthInBytes`.

---

## Note in the module's favour

The prior gate's non-discriminating phase test was **deleted with an honest "no test can cover this" note** rather than replaced with a weaker one that would have looked like coverage. That is the standard Reviewer B's F1 fails to meet — and the standard the next fix should be held to.
