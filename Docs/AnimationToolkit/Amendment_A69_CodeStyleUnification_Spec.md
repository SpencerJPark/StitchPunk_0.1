# Amendment A69 — Code Style Unification (naming + comment audit)

**Status:** SPECCED 2026-09-06, not started. Owner-requested. Package version after this lands: **0.15.0** (breaking renames on the public API).
**Scope:** `Packages/com.dotsanimationtoolkit/` — `Runtime/`, `Runtime.Physics/`, `Authoring/`, `Editor/`, `Documentation~/`, `README.md`, `Samples~/`. Tests change only where a rename forces it. Game-side consumers under `Assets/_Scripts/` are updated for renames and nothing else.
**Execution protocol:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4, unchanged. Its rule 6 applies and overrides HANDOFF §2's older "do not spawn subagents" bullet: subagents may take any task marked **[parallel-safe]**, and comment-stripping is embarrassingly parallel, so use them. Subagents never touch `mcp__UnityMCP__*`; only the parent gates and commits.

---

## 1. Why this exists (owner's words, 2026-09-06)

> "Naming conventions to be stable — some are called utils, some are query, some api. A universal naming would be easier to work with downstream."
>
> "Remove the sheer amount of extra comments. Why is there page-length summaries? Summary comments should only exist at the top of files and the code functions should be written where just reading the code makes it obvious what is happening. Play is self-explanatory."
>
> "The blobs have an insane amount of comments, one for every field. The variable name should be self-explanatory enough."

The package's own rulebook already says this — `Docs/AnimationToolkit/HANDOFF.md` §2, first bullet: *"Doc comments: one or two lines, and only where the why is not obvious… no multi-paragraph `<remarks>` essays."* The code ignored it. Measured on 2026-09-06, non-test sources only:

| Measure | Count |
|---|---|
| `///` lines / total lines | 18,578 / 74,608 (25%) |
| `<remarks>` blocks | 871 |
| `<para>` | 796 |
| `<strong>` / `<em>` | 544 / 255 |
| "architecture section N" citations in code | 231 |
| "amendment ANN" citations in code | 267 |
| "Phase X" citations in code | 188 |
| Files where `///` is more than half of all lines | 7 (`ValidationMessage.cs` is 72%) |

A customer who installs this package has none of `Docs/AnimationToolkit/`. Every "section 5.4" and "amendment A28" in a doc comment is a dangling reference to them. Every `<remarks>` essay pushes the code it describes off the screen.

**Measure it yourself before and after** (Git Bash, from repo root):

```bash
cd Packages/com.dotsanimationtoolkit
SRC="Runtime Runtime.Physics Authoring Editor"
echo "/// lines: $(grep -rh --include=*.cs '///' $SRC | wc -l)  of  $(find $SRC -name '*.cs' | xargs cat | wc -l)"
for t in '<remarks>' '<para>' '<strong>' '<em>' 'architecture section' 'amendment A[0-9]' 'Phase [A-G]' '§'; do
  echo "$t: $(grep -rhE --include=*.cs "$t" $SRC | wc -l)"; done
```

---

## 2. Decisions (recorded; do not re-ask the owner)

### 2.1 Naming — static classes

One suffix per role. The role is what the class **does to data**, not where it lives.

| Suffix | Role | Where |
|---|---|---|
| `Api` | The public surface a host game calls. Read and write side of one subject live in **one** class. | `Runtime/Api/` only, and everything in `Runtime/Api/` ends in `Api`. |
| `Builder` | Bake time: authoring assets in, blob out. | `Authoring/Build/` |
| `Sampler` | Pure: blob + time in, pose/value out. Burst-safe, no `EntityManager`. | `Runtime/Sampling/` |
| `Resolver` | Pure: id/tag/angle in, index or variant out. | `Runtime/Sampling/`, `Authoring/Baking/` |
| `Math` | Pure numeric functions on plain values. No ECS types in signatures. | `Runtime/Sampling/`, `Editor/…/Preview/` |
| `Validation` | Rule checks that produce `ValidationMessage`s. | `Authoring/Validation/` |
| `Utility` | Editor-only asset surgery on `UnityEngine.Object`s. | `Editor/ClipUtilities/` **only** |
| `Editing` | Editor-only edit operations on a clip under the undo system. | `Editor/ClipEditor/Editing/` |

**Banned anywhere:** `Util`, `Utils`, `Helper`, `Helpers`, `Query`, `Manager`, `Common`, `Misc`, `Ext`, `Extensions`. Also banned: `Utility` outside `Editor/ClipUtilities/`.

A static class that is none of the above roles is named as a plain noun for the thing it models (`EasingPresets`, `ClipKeyClipboard`, `RestPoseCapture`, `AnimEventMask`, `ConstantsGenerator`) — these already comply and are not touched.

**Renames this amendment performs** (every consumer in the table in §2.4 is updated in the same task):

| Old | New | Notes |
|---|---|---|
| `AnimationCommandUtil` + `PlaybackQuery` | **`PlaybackApi`** | Merge: `Play`, `Queue`, `Stop`, `SetSpeed`, `SetTime`, `IsPlaying`, `NormalizedTime`, `HasFinishedThisFrame`. One file `Runtime/Api/PlaybackApi.cs`; delete both old files + `.meta`. |
| `BillboardQuery` | **`BillboardApi`** | Methods unchanged: `TryGetFrame`, `ToBillboardSpace`. |
| `ClipRegistryUtil` | **`ClipRegistryApi`** | `TryResolveClip` unchanged. `ResolveTargetIndex` → **`TryResolveTarget`** (it returns `bool` with an `out`; the name must say so). |
| `CutscenePlaybackApi` | **`CutsceneApi`** | Methods unchanged. |
| `ToolkitWorldControl` | **`ToolkitWorldApi`** | Move file from `Runtime/Systems/` to `Runtime/Api/`. `SetEnabled`/`IsEnabled` unchanged. |
| `StableIdUtility` | **`StableIdMinting`** | Runtime, not editor, so `Utility` is banned here. Stays in `Runtime/Identity/`. |
| `RagdollTransformUtil` | **`RagdollTransformMath`** | Two pure `ComputeWorldTransform` functions on values. Move to `Runtime/Sampling/`. |
| `AnimationLodPolicy` | **`AnimationLodResolver`** | Pure level/rate lookups. `LevelForDistanceSq` → `ResolveLevelForDistanceSq`. |
| `CutsceneSceneBindingUtility` | **`CutsceneSceneBinding`** | Editor, but not in `ClipUtilities/`. |

Not renamed (already conform): `ClipSampler`, `CutsceneBlobSampler`, `FacingResolver`, `SpriteIndexResolver`, `BillboardRootResolver`, `RagdollBodyResolver`, `BillboardMath`, `EventWindowMath`, `EventWrapMath`, `PreviewGizmoMath`, `ClipRegistryBuilder`, `SocketRegistryBuilder`, `CutsceneBlobBuilder`, `ClipValidation`, the seven `Editor/ClipUtilities/*Utility` classes, the five `Editor/ClipEditor/Editing/*Editing` classes, `CutsceneBlockTiming`, `CutsceneFacingVariants` (plain nouns, pure; leave).

### 2.2 Naming — methods

- A method that returns `bool` and has an `out` parameter is `Try…`. A `bool` with no `out` is a predicate: `Is…`, `Has…`, `Can…`, `Contains…`, `Snaps…`, `Freezes…` are all fine. **`FinishedThisFrame` → `HasFinishedThisFrame`.** `ResolveTargetIndex` → `TryResolveTarget`. `SetEnabled` returning `bool` stays (Unity convention: returns previous state).
- Everything else is unchanged. This amendment is not a vocabulary rewrite of method names; only the `Try`/predicate rule is enforced.

### 2.3 Comments — the rule

Replace HANDOFF §2's first bullet with this, verbatim, and apply it:

> **One `<summary>` per file, on the file's primary type, at most three lines, stating what the type is and the one contract a caller must know. Nothing else in the file gets a `<summary>` unless the name cannot carry it.** No `<remarks>`. No `<para>`, `<strong>`, `<em>`, `<list>`. No citations of the architecture doc, amendments, phases or spec sections — customers do not have those documents. A `<param>` only for a sentinel or a unit (`NaN = clip default`, `seconds`, `−1 = none`). A field comment only for a sentinel, a unit, or an ordering/aliasing trap, as **one** line. An inline `//` only for a *why* the code cannot express — a trap, an ordering constraint, a reason a reader would otherwise "fix" — at most two lines. If a method needs a paragraph, the method is wrong: split it or rename it.

Concretely, in priority order:

1. **Delete** every `<remarks>` block. If it contained a genuine trap (an ordering constraint, a silent-failure mode), keep the trap as one `//` line at the exact statement it protects — not at the top of the type.
2. **Delete** every `<summary>` that restates the member name or signature. `Play` gets none. `clipIndex` with "Dense registry index of the current clip" becomes `public int clipIndex; // -1 = unresolved`.
3. **Delete** every citation: "architecture section 5.4", "(amendment A28)", "Phase D §5.2", "rule V08", "C10". Where the sentence around it carried a reason, keep the reason, drop the citation. `grep -rE 'section [0-9]|amendment A[0-9]|Phase [A-G]|§' --include=*.cs` must return zero lines in non-test sources when done.
4. **Strip markup** from what survives: `<c>`, `<see cref>`, `<paramref>` are allowed only inside a surviving one-liner; `<strong>`/`<em>`/`<para>`/`<list>` never.
5. **Rename instead of commenting** when a name is what makes the comment necessary. Examples the survey found: `advanceStartTime` needs six paragraphs today; `timeAtFrameStart` needs none. `vatFrameStart` "or −1 when the clip has no VAT range" → keep the field, one trailing `// -1 = no VAT range`. Field renames on blobs are allowed but each one bumps `ClipRegistryBlob.schemaVersion` / the golden hash exactly as any layout change would — so **do not rename blob fields in this amendment**; comment them with one line and log the wanted rename in §6 for a later schema bump. Component and asset fields may be renamed freely (assets: check `[FormerlySerializedAs]` is added so saved `.asset` files survive).
6. **ScriptableObject fields**: a doc comment is invisible to the person editing the asset. Any explanation an author needs becomes a one-sentence `[Tooltip]`; anything longer goes to the matching `Documentation~/*.md` page. The XML comment is deleted either way.
7. **Systems**: `OnCreate`/`OnUpdate`/job structs get no `<summary>`. The system's one file-level summary says which group it is in and the one ordering fact that matters (e.g. "clears last frame's events before applying commands; EventEmissionSystem runs after this and would otherwise erase resolve-failure events").
8. **Editor UI code** (`Editor/ClipEditor/**`): same rule. UI wiring never needs prose.
9. **Tests**: leave comments as they are. Out of scope except where a rename forces an edit.

**What to keep, so the audit does not go too far:**

- The copyright header line on every file.
- The file-level `<summary>` (rewrite it to ≤3 lines if it is longer).
- One-line sentinel/unit/trap comments.
- `// why` lines on non-obvious statements: the `RequireAnyForUpdate` reason in `CommandApplySystem.OnCreate` is a correct example — it says why `RequireForUpdate` would be wrong, in four lines that could be two. Keep it, tighten it.
- The `previousLoop` copy-before-overwrite ordering in `CommandApplySystem` is a real trap. Keep it as one `//` line on the assignment, delete the paragraph.

**Success bar** (the conformance test in T1 enforces the hard zeros; the ratio is a target, not a gate):

| Measure | Now | Target |
|---|---|---|
| `<remarks>`, `<para>`, `<strong>`, `<em>`, `<list` | 871 / 796 / 544 / 255 / 11 | **0** each |
| Citations (`section N`, `amendment ANN`, `Phase X`, `§`) | ~700 | **0** |
| `///` lines / total | 25% | **≤ 6%** |
| Files where `///` > 25% of lines | 30+ | **0** |

### 2.4 What this breaks, and who is updated in the same commit

Package public API names change. Consumers found on 2026-09-06 (`grep -rlwE 'AnimationCommandUtil|PlaybackQuery|BillboardQuery|ClipRegistryUtil|CutscenePlaybackApi|ToolkitWorldControl|StableIdUtility|RagdollTransformUtil|AnimationLodPolicy' --include=*.cs --include=*.md Assets Packages Docs`):

- **Game:** `Assets/_Scripts/Utils/BehaviorCommands/AnimationCommands.cs`, `Assets/_Scripts/Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitAnimationAssignmentSystem.cs`, `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackSystem.cs`, `Assets/_Scripts/Systems/SpawnInitSystemGroup/SpawnStateInitSystem.cs`, `Assets/_Scripts/Systems/CutsceneSystemGroup/CutsceneStartSystem.cs`, `Assets/_Scripts/Systems/CutsceneSystemGroup/CutsceneDialogueCueSystem.cs`, `Assets/_Scripts/Components/Cutscene/CutsceneComponents.cs`, `Assets/_Scripts/Data/SOs/NarrativeEventSO.cs`, `Assets/_Scripts/Data/Enums/AnimationToolkitLayer.cs`.
- **Package internals:** `ActorBaker.cs`, `CutsceneStageAuthoring.cs`, `RigBindingBakingSystem.cs`, `ClipRegistryBuilder.cs`, `ClipPreviewController.cs`, every `Runtime/Systems/*` that calls the renamed classes, `Runtime/Components/*.cs` doc comments naming them (those comments are being deleted anyway).
- **Package tests:** `Tests/EditMode/ClipRegistryUtilTests.cs` → rename file + class to `ClipRegistryApiTests`; `Tests/PlayMode/PlaybackQueryTests.cs` → `PlaybackApiTests`; `StableIdentityTests.cs`, `TestBlobFactory.cs`, `PlaybackTestActor.cs`, `PlaybackTimeSystemTests.cs`, `CutsceneAttachTests.cs`, `CutsceneFacingTests.cs`, `CutsceneMarkTests.cs`, `CutsceneTimelineSystemTests.cs`, `ClipRegistryBuilderTests.cs`.
- **Docs:** `Documentation~/getting-started.md`, `index.md`, `billboarding.md`, `cutscenes.md`, `ragdoll.md`, `README.md`, `CHANGELOG.md` (add a **Changed — breaking** entry listing every rename, old → new), and `Samples~/**/*.cs` (not compiled by Unity — compile-check via the temp-assembly trick in `Assets/_Vault/Memories/Code/Gotchas.md`, or at minimum grep them).
- **Vault:** `Assets/_Vault/Memories/Code/Contracts.md`, `Gotchas.md`, `Systems.md`, `Systems_Animation.md` — update the names. The specs under `Tasks/NewPlans/` and `Docs/AnimationToolkit/Amendment_A6*.md` are historical and are **not** edited; add one line to `Cutscene_Roadmap.md` §5 saying "A69 renamed `CutscenePlaybackApi` → `CutsceneApi`, `AnimationCommandUtil`/`PlaybackQuery` → `PlaybackApi`".

---

## 3. Read first

1. Repo root `CLAUDE.md`.
2. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6.
3. `Assets/_Vault/Memories/Code/RULES.md`.
4. This spec, in full.
5. `Packages/com.dotsanimationtoolkit/Tests/EditMode/PackagingConformanceTests.cs` — the source-scanning conformance tests you extend in T1 (`Conformance_E_NoImguiApis_InEditorSources` is the pattern: `PackageRootPath` + `Directory.GetFiles(..., "*.cs", SearchOption.AllDirectories)` + a banned-substring list + a failure message that lists every offending `file:line`).
6. `Packages/com.dotsanimationtoolkit/Runtime/Systems/CommandApplySystem.cs` and `Runtime/Components/PlaybackLayer.cs` — read them once as the "before". §2.3 describes their "after".

---

## 4. Tasks

Tick each checkbox as you land it. Commit message prefix: `A69-Tn:`.

### T1 — The gate first: conformance tests that fail on today's code

- [ ] In `PackagingConformanceTests.cs` add:
  - **`Conformance_F_NoDocEssaysOrSpecCitations_InSources`** — scans `Runtime`, `Runtime.Physics`, `Authoring`, `Editor` for `*.cs` and fails listing every `file:line` containing any of: `<remarks>`, `<para>`, `<strong>`, `<em>`, `<list `, `architecture section`, regex `amendment A[0-9]`, regex `\bPhase [A-G]\b`, `§`, regex `\brule V[0-9]{2}\b`. It must fail today with thousands of lines; **cap the message at the first 50 hits plus a total count** so the Test Runner stays usable.
  - **`Conformance_G_StaticClassSuffixVocabulary`** — every `static class` declared in the same four folders must either end in one of `Api Builder Sampler Resolver Math Validation Utility Editing` or appear in a `PlainNounStaticClasses` allowlist inside the test (`EasingPresets`, `ClipKeyClipboard`, `RestPoseCapture`, `AnimEventMask`, `ConstantsGenerator`, `CutsceneBlockTiming`, `CutsceneFacingVariants`, `AuthoringPathHash`, `AuthoringPathText`, `CutsceneDerivedHolds`, `CutsceneDirectionVariants`, `CutsceneKeySampler`, `CutsceneMarkMerge`, `CutsceneAssetOpener`, `DirectionSetAssetOpener`, `TimelineRangeShading`, `VocabularySettingsProvider`, `RagdollPreviewSceneryProvider`, `VocabularyRegistryProvider`, `CutsceneEventInspectorProviders`, `BindingReconciler`, `ClipEditorDocking`, `PrefabAuthoringBridge`, `RigStructureEditor`, `ClipComponentModel`, `GizmoDragRouting`, `EventLaneAddressing`, `PreviewLineMaterial`, `PreviewScenePicker`, `RagdollPreviewProbe`, `VatMeshPreparer`, `VatTentacleRigBuilder`, `VatTextureBaker`, `ClipKeyConversion`, `CutsceneSceneBinding`). Additionally fails on any static class whose name ends in a banned token (`Util`, `Utils`, `Helper`, `Helpers`, `Query`, `Manager`, `Common`, `Misc`, `Ext`, `Extensions`), and on `Utility` outside `Editor/ClipUtilities/`. Must fail today on the nine §2.1 renames.
  - **`Conformance_H_ApiFolderClassesEndInApi`** — every `public static class` in `Runtime/Api/` ends in `Api`. Fails today on four of five.
- [ ] Compile gate. Run `Conformance_F`, `Conformance_G`, `Conformance_H` — **all three must fail**. Paste the failure counts into §6. Commit the tests red. This is the one commit in the package allowed to leave a red test, and it is red on purpose.

### T2 — Renames (one commit, package + game + tests + docs together) — not parallel-safe

- [ ] Perform every rename in §2.1 and §2.2. Merge `AnimationCommandUtil` + `PlaybackQuery` into `Runtime/Api/PlaybackApi.cs`; delete the two old files and their `.meta`. Move `ToolkitWorldControl.cs` → `Runtime/Api/ToolkitWorldApi.cs`, `RagdollTransformUtil.cs` → `Runtime/Sampling/RagdollTransformMath.cs` (move with `git mv` so the `.meta` GUID travels).
- [ ] Update every consumer in §2.4: game, package, tests, `Documentation~`, `README.md`, `Samples~`, vault notes. Rename the two test files/classes.
- [ ] `CHANGELOG.md` `[Unreleased]` → add `### Changed — breaking (A69)` with the full old → new table. `package.json` version → `0.15.0`.
- [ ] Compile gate on **both** the package and the game (`Assets/_Scripts` consumers are in the game assemblies; a package-only green is not enough). Run `Conformance_G`, `Conformance_H` → green. Run `PlaybackApiTests`, `ClipRegistryApiTests`, `StableIdentityTests`, `CutsceneTimelineSystemTests` → green, discovered counts unchanged from before the rename.
- [ ] Commit `A69-T2: one suffix per role — PlaybackApi, CutsceneApi, BillboardApi, ClipRegistryApi`.

### T3 — Comment audit, Runtime **[parallel-safe with T4, T5, T6]**

Folders: `Runtime/Api`, `Runtime/Blobs`, `Runtime/Components`, `Runtime/Identity`, `Runtime/Sampling`, `Runtime/Systems`, `Runtime.Physics`. Apply §2.3 to every file. This is the folder the owner named; do it to the letter.

- [ ] `Runtime/Blobs/*.cs` — every field comment goes unless it is a sentinel/unit/order trap in one line. `ClipRegistryBlob`'s type summary keeps exactly one fact: "dense clip index = position in `clips` = position in `sortedClipIds`; both are id-sorted". **Do not rename blob fields** (§2.3 item 5).
- [ ] `Runtime/Components/*.cs` — same. `PlaybackLayer.advanceStartTime` → rename to `timeAtFrameStart`, one trailing line `// written only by PlaybackTimeSystem, before it advances time`. `RagdollComponents.cs` (68% comment) loses all six essays; `RagdollBodyElement` keeps one line: `// buffer order = hierarchy depth, shallowest first; the solver reads parents through it`.
- [ ] `Runtime/Api/*.cs` — `PlaybackApi.Play` gets no summary. The class summary keeps two facts, one line each: commands are appended *and* `AnimationCommandPending` is enabled by every method; `blendDuration = NaN` means the clip's authored default. `<param>` on `blendDuration` only.
- [ ] `Runtime/Systems/*.cs` — §2.3 item 7. `CommandApplySystem` keeps: the `RequireAnyForUpdate` why (two lines) and the `previousLoop` ordering line.
- [ ] Compile gate. Run `DotsAnimationToolkit.Tests.PlayMode` in full (Runtime is what it exercises) — green, discovered count unchanged. Commit `A69-T3: Runtime reads as code`.

### T4 — Comment audit, Authoring **[parallel-safe with T3, T5, T6]**

Folders: `Authoring/Assets`, `Authoring/Baking`, `Authoring/Build`, `Authoring/Validation`.

- [ ] `Authoring/Assets/*.cs` — §2.3 item 6: SO field XML → `[Tooltip]` one sentence or nothing; long explanations (e.g. the `ClipAsset.frameRate` essay) move to `Documentation~/clip-editor.md` if not already there, else are deleted. `RigAsset.cs` (59%), `ClipAsset.cs` (61%), `CutsceneAsset.cs` (44%) are the targets.
- [ ] `Authoring/Validation/ValidationMessage.cs` (72%) — the rule-id enum keeps one line per rule stating the rule, nothing more. That is the one place a per-member one-liner is the documentation.
- [ ] `Authoring/Build/ClipRegistryBuilder.cs`, `Authoring/Baking/ActorBaker.cs` — essays go; the bake-order traps (a baker writes only its own entity; `IBaker.GetName`/`GetParents` for dependency tracking) stay as `//` lines at the statements they protect.
- [ ] Compile gate. Run `ClipRegistryBuilderTests`, `ClipValidationTests`, `CutsceneBlobBuilderTests` (EditMode) and `ActorBakingAcceptanceTests`, `RigBindingSystemTests`, `CutsceneStageBakingTests` (PlayMode) — green. Commit `A69-T4: Authoring reads as code`.

### T5 — Comment audit, Editor **[parallel-safe with T3, T4, T6]**

Folders: `Editor/ClipEditor/**`, `Editor/ClipUtilities`, `Editor/Inspectors`, `Editor/VatBaking`. Largest by volume (`ClipEditorWindow.cs` alone has 1,947 `///` lines). Split across two subagents by folder if you like; both are parallel-safe with each other.

- [ ] Apply §2.3. UI element classes get one summary line at most. Partial-class files (`ClipEditorWindow.*.cs`) get a summary only on the file that declares the type.
- [ ] Compile gate. Run `ClipEditorAuthoringTests`, `ClipEditorLayoutTests`, `ClipEditorAddEventTests`, `ClipEditorHierarchySelectionTests`, `BillboardPreviewParityTests`, `RagdollPreviewParityTests`, `SocketPreviewParityTests`, `VatTextureBakerTests` — green. Commit `A69-T5: Editor reads as code`.

### T6 — Docs and samples sweep **[parallel-safe with T3, T4, T5]**

- [ ] `Documentation~/*.md`, `README.md`: replace every old API name (T2 already did the mechanical rename; this task reads each page once and fixes any sentence the rename made false, e.g. "read it back through `PlaybackQuery`").
- [ ] `Samples~/**/*.cs`: same, then compile-check through a temp assembly (the `Samples~` entry in `Assets/_Vault/Memories/Code/Gotchas.md`) — Samples~ is not compiled by Unity and rots silently.
- [ ] Commit `A69-T6: docs and samples name the new API`.

### T7 — Close the gate

- [ ] Run `Conformance_F`, `Conformance_G`, `Conformance_H` → **green**. If `Conformance_F` still lists hits, fix them; do not widen the allowlist.
- [ ] Run the measurement script from §1; paste before/after into §6. `///` ratio ≤ 6% or explain in §6 which files are over and why.
- [ ] Full suites once: `DotsAnimationToolkit.Tests.EditMode`, `DotsAnimationToolkit.Tests.PlayMode`, `StitchPunk.Tests`, `StitchPunk.Tests.PlayMode`. Discovered totals must match the pre-A69 counts recorded in §6 at T1 (a dropped count is a lost fixture, not a pass).
- [ ] Replace HANDOFF §2's doc-comment bullet with §2.3's rule verbatim, and add "Static-class suffixes: §2.1 of Amendment A69" as a hard convention beneath it. Update HANDOFF §4 with one paragraph.
- [ ] Commit `A69-T7: gate closed; HANDOFF carries the rule`.

### ⏸ owner checkpoint

Open `Runtime/Api/PlaybackApi.cs`, `Runtime/Blobs/ClipRegistryBlob.cs`, and `Runtime/Systems/CommandApplySystem.cs` side by side with the previous commit. The question for the owner is one of taste, not correctness: is the surviving comment volume what you meant, or still too much? Report which files were left over 6% and why.

---

## 5. Traps

- **`git mv` for moved files**, never delete + create: a new `.meta` GUID breaks any `.asset` or scene that references an Editor script by GUID (none should for static classes, but the habit is what keeps the next move safe).
- **Blob field renames are layout changes** only when the type/order changes; a pure rename is layout-neutral, but `schemaVersion`/golden-hash discipline in this package is "any edit to a blob struct bumps both" and the hash test will tell you. That is why §2.3 forbids blob field renames here — keep the audit and the schema bump in separate commits.
- **`[FormerlySerializedAs]`** on any renamed SO/MonoBehaviour field, or every saved asset silently reverts that field to default on next open.
- **`ConstantsGenerator.EscapeXmlDocText`** generates `///` lines into host-side constants files on purpose (they are IntelliSense for vocabulary ids). Leave the generator's *output* alone; the generator's own comments follow the rule.
- **Conformance_F's substring `§`** is a non-ASCII byte. Read files as UTF-8 explicitly (`File.ReadAllLines(path, Encoding.UTF8)`) or the scan misses it.
- **The `<inheritdoc/>` tags (21)** are not essays; leave them.
- **Subagents never touch `mcp__UnityMCP__*`.** Three processes on one Editor once grew `Logs/Editor.log` to 2.2 GB. Parent gates, parent commits.
- **Do not "improve" logic while stripping comments.** If reading a method makes you want to change it, note it in §6 and leave it. A style commit that also changes behaviour cannot be reviewed.

---

## 6. Log (append as you go)

- 2026-09-06 — specced. Baseline: EditMode / PlayMode discovered counts to be recorded at T1.
