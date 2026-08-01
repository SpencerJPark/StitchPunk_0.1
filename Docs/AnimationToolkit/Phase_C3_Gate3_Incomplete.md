# Phase C3 — third gate attempt (INCOMPLETE)

**Date:** 2026-07-31 / 2026-08-01
**Status: ABANDONED MID-REVIEW.** All three reviewers were killed by a session usage
limit within ~2 minutes of starting. Reviewer A and Reviewer B each had written
exactly one finding; Reviewer C (code correctness) wrote nothing at all.

**This is not a gate verdict. C3 has not been reviewed.** The two findings below
are real — both were independently re-verified by the coordinator against the tree
before being acted on — but they are the first thing each reviewer happened to look
at, not a survey. Reviewer C's lens (code correctness, Burst legality, the
`ActorBakeFailed` staleness question) has had no coverage whatsoever.

Both findings below have since been FIXED. See the commit following this file.
The remaining scope is untouched and must be re-reviewed from scratch.

---

## Reviewer A — spec conformance (1 finding, then killed)

# C3 Gate Review (3rd pass) — Reviewer A, spec conformance

Scope: `git diff 026a902..HEAD` over `Packages/com.stitchpunk.dotsanimationtoolkit` + `Docs/AnimationToolkit`.
Rework commits under examination: `880a8e3`, `6612382`, `6772503`, `149956d`, `4221485` (on top of the previously-FAILed `026a902..3f08feb`).

Findings appended as confirmed. Verdict at the bottom.

---
## FINDING 1 — B6 test counts: index.md now CORRECT (verified by count); CHANGELOG's 0.3.0 running total is still wrong — BLOCKING (narrow)

**I counted the shipped suite myself, not from any table.**
`grep -rno "\[Test[A-Za-z]*\]\|\[UnityTest\]\|\[TestCase\|\[Ignore\|\[Explicit" Tests/` over the package returns
**233 occurrences, all of them `[Test]`** — zero `[TestCase]`, zero `[UnityTest]`, zero `[Ignore]`,
zero `[Explicit]`, zero `#if` fencing inside the test tree. Split:

- EditMode = **205** (AuthoringPath 12, ClipValidation 32, ClipRegistryBuilder 18, StableIdentity 17,
  EventWrapMath 16, LayerComposition 14, ClipRegistryDeterminism 14, RuntimeContract 13,
  ClipSampler 12, LoopTimeMapping 12, ClipRegistryUtil 10, DataContract 9, PackagingConformance 9,
  Easing 7, ContentHashGolden 5, SampleQuantization 5)
- PlayMode = **28** (ActorBakingAcceptanceTests 27, PlayModeAssemblySmokeTest 1)

`Documentation~/index.md:72-74` — *"205 EditMode tests … plus 28 PlayMode tests, 27 of which bake
real GameObject hierarchies"*. **Exactly right, including the 27-of-28 split.** The previous
review's Finding 2 (index.md said 164, actual 192) is **genuinely closed**; I verified by
independent count, not by reading the claim.
`README.md` carries no test count at all (only `Unity: 6000.5 minimum` on line 10) — nothing to correct.

**Still wrong:** `CHANGELOG.md:160`, rewritten *by this rework*
(`git diff 026a902..HEAD -- CHANGELOG.md` line 133: `-66 EditMode tests` → `+70 new EditMode tests — 176 in the suite`).
The 0.3.0 entry is `## [0.3.0] - Unreleased`, i.e. it covers **all** of C2. Measured suite size at
each release boundary (`git ls-tree -r <rev> -- Tests/EditMode | git show | grep -c "\[Test\]"`):

| rev | meaning | EditMode `[Test]` |
|---|---|---|
| `e5eba17` | C0 / 0.1.0 | 8 |
| `19b89ca` | C1 end / 0.2.0 | 106 |
| `2163dd7` | C2 **first** commit | 176 |
| `ec44226` = `026a902` | C2 end / 0.3.0 | **192** |
| `HEAD` | 0.4.0 | 205 |

So `CHANGELOG.md:141-143`'s 0.2.0 line (*"98 new — 106 in the suite"*) is **correct**, and
`:160`'s 0.3.0 line is **wrong in both numbers**: the release added **86**, not 70, and left
**192** in the suite, not 176. The rework read the count at C2's opening commit and ignored the
three C2 rework commits (`1aca683` +4, `239cb2f` +9, `86c6455` +3) that ship inside the same
unreleased 0.3.0. The running total 8 → 106 → 176 → (205) is internally tidy and externally false,
which is the same defect shape as before: a number that reconciles against a note rather than
against the tree.

**Blocking, narrowly.** B6's written remedy is *"correct the test counts"*, this is the third gate
at which a CHANGELOG count is wrong, and the rework touched this exact line. The licensee-facing
`index.md` figure is right, so the practical harm is small — but "the number I edited is still not
the number in the tree" is precisely the class of claim this gate exists to reject.

---

---

## Reviewer B — test integrity (1 finding, then killed)

# Gate C3 (attempt 3) — Reviewer B — test integrity

Scope: `git diff 026a902..HEAD -- Packages/com.stitchpunk.dotsanimationtoolkit/Tests`,
read against the production code it exercises.

Findings appended as confirmed. Verdict at the bottom.

---
(in progress)

## FINDING 1 — BLOCKING — the new phase-adjacency test cannot fail the way its own comment says it can (closes nothing; A-4 is still open)

**File:** `Tests/PlayMode/ActorBakingAcceptanceTests.cs:589-623`
`TwoActorsWhoseNamesDifferOnlyInTheLastCharacter_GetWellSeparatedPhases`

Its comment states the reason it exists:

> *"the point of taking bits 8–31 rather than 0–23 (amendment A18) is that the low bits of a
> multiply-terminated hash are the least mixed. The existing phase test uses
> "FirstActor"/"SecondActor" … so the property that motivated the shift had no coverage."*

**It still has no coverage.** I re-implemented `AuthoringPathHash.Of`
(`Authoring/Baking/AuthoringPathHash.cs:66-88`) exactly — FNV-1a over, per node **leaf first**,
each name character, then the sibling index, then `'/'` — and evaluated the fixture
(`Actor1` at sibling 0 and `Actor2` at sibling 1, both under `AdjacencyFixtureRoot`) under both
derivations:

| container sibling index at scene root | `abs(dPhase)` with shipped `(h>>8)*2^-24` | `abs(dPhase)` with pre-A18 `(h & 0x00FFFFFF)*2^-24` |
|---|---|---|
| 0 | 0.8137 | 0.2978 |
| 1 | 0.6887 | 0.7030 |
| 2 | 0.3113 | 0.7030 |
| 3 | 0.3113 | 0.7030 |
| 4 | 0.3113 | 0.2971 |
| 5 | 0.3113 | 0.2971 |

**Concrete regression this test does not catch:** revert `ActorBaker.cs:531` from
`return (pathHash >> 8) * (1f / 16777216f);` to the pre-A18 `return (pathHash & 0x00FFFFFFu) * (1f / 16777216f);`
— i.e. undo amendment A18's derivation entirely — and this test is **green under every container
sibling index**. The whole suite stays green.

**Why it cannot work as designed.** `AuthoringPathHash.Of` walks **leaf first**, so the character
that differs (`1` vs `2`) is hashed *first*, and ~22 further FNV rounds follow it (the leaf's
sibling index, `'/'`, all 20 characters of `AdjacencyFixtureRoot`, its sibling index, `'/'`). The
low-bit weakness the shift exists to dodge only shows when the differing character is the **last**
thing mixed, i.e. when the near-identical names are on the **outermost ancestor**, never on the
leaf. A fixture that could discriminate would have to put `Actor1`/`Actor2` at the top of the path
and hash a short or empty tail — which this hash function's node ordering makes impossible for a
single-node difference at the leaf.

**Secondary (advisory) defect in the same test:** the separation depends on
`AdjacencyFixtureRoot`'s own sibling index in the scene root, which the test does not control —
`CreateContainer` is a bare `new GameObject(name)` (`ActorBakeFixture.cs:210-215`). Scanning
S = 0..199 the shipped derivation's minimum separation is **0.0594** (S = 79), against a threshold
of 0.05. Small in a clean test scene, but the threshold and the uncontrolled input are within a
factor of 1.2 of each other, and nothing in the test says so.

Net: the `>> 8` change made in this rework is still covered by nothing, and the test written to
cover it asserts a property that holds under the code it was written to distinguish from.

---

## Reviewer C — code correctness

Produced no output. Killed before writing its scratchpad file.
