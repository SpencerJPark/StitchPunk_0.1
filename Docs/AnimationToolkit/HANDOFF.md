# Handoff — DOTS Animation Toolkit, continue the work

Paste this whole file as your first message in the new chat.

---

You are continuing work on a sellable UPM package at
`C:\Users\spenc\Documents\GitHub\Stitch_Punk\Packages\com.dotsanimationtoolkit`.

**Read first, in this order:**
1. `C:\Users\spenc\Documents\GitHub\Stitch_Punk\CLAUDE.md` — project conventions.
2. `Docs\AnimationToolkit\Phase_E_TargetTags_Spec.md` — the active feature spec (target tags / shared clips). §4.2.1, §4.2.2, §4.2.3, §6.1 are owner directives.
3. `Docs\AnimationToolkit\Phase_D_Ragdoll_Spec.md` — the finished ragdoll feature, for its §9 gotchas which still apply.

**Current state (verified): EditMode 682/682, PlayMode 240/240, console clean.** The tree compiles.

---

## Standing owner directives — do not lose these

These were given across a long session and are binding on all future work.

### Naming and identity

- **"I will only ever want to use and reference the tag and event NAMES, never the numbers — in both the downstream code I write and the editor."** Ids are storage only. Every editor surface shows a name. Game code uses generated constants (`TargetTags.Jaw`, `AnimEvents.Footstep`). The single permitted exception is an unresolvable id after a delete, where `(unresolved 0x1A2B3C4D)` is correct because the number is all that survives. This is spec §4.2.3.
- **"I'm fine with a registry, but I don't want to manually create and wire it — it should just exist."** No asset creation, no assignment. Both vocabularies auto-create under `ProjectSettings/`.
- **"I want to be able to edit the list of tags and events from the clip editor."**

### Rigging workflow

- **New rigs will be created fresh; do not build migration paths** for old rigs with empty fields. The owner decided this explicitly.
- **Tagging should feel native to rigging** — it belongs on the physical rig hierarchy rows where the owner is already looking, not in a separate inspector section below a list. *(Not yet done — current implementation is a "Target Tags" section under the Targets list.)*
- **"I shouldn't have to manually assign any assets for this."**

### Events

- **Author loosely; downstream systems read and redirect.** Events will eventually drive sound, ragdoll triggers, damage, alt views for shaders, and dialogue. Do not build downstream consumers unless asked.
- **The owner eventually wants to test sound from the editor.** Note: the Clip Editor's scrub path poses through `ClipSampler` and does **not** run `EventEmissionSystem`/`EventWindowSystem` (ECS, play-time only). A scrub-preview needs its own crossing detection in the editor, comparing playhead-before against playhead-after.
- **Each event needs its own timeline lane.** *(Not yet done — currently same-time events stack vertically in one lane with click-cycling, which the owner rejected.)*
- **"Edit Events" must be immediately available after adding an event.** *(Not yet done.)*

### UI standardisation

- **Add/Remove tag functions must be paired exactly where "Set Tag" appears**, via a searchable dropdown that also allows editing the list.
- **One popup style.** The tag-edit popup and the create-new-event-name popup must be the same UI, not parallel implementations.

### Validation policy

- **T2 is lenient (owner decision):** a tag-bound track whose tag is absent from the rig is **skipped with a warning**, not an error, so one clip can cover a roster of differing rigs. This is only safe because of three mandatory mitigations in §6.1 — dropdown-only selection, the case-insensitive duplicate guard, and a warning that names clip + track + tag + rig and surfaces in the Clip Editor's validation badge.
- **T3 stays an error** and must be reported differently from T2. A tag missing *from a rig* is an ordinary roster fact; a tag missing *from the registry* is a dangling reference.

---

## Hard conventions (from CLAUDE.md — non-negotiable)

- **Never `var`. Never single-letter names.** Explicit types; names read like documentation.
- **Never `.Run()` a job** — `.Schedule()` / `.ScheduleParallel()` into `state.Dependency`.
- **UI Toolkit only in editor sources.** `Conformance_E` bans IMGUI APIs and the test enforces it. `AdvancedDropdown` is therefore unavailable.
- **`Authoring/` must never reference `UnityEditor`** — it ships to players, and `Conformance_C` scans raw file text including comments. Editor-only machinery goes in the Editor assembly. (This was violated and fixed; do not reintroduce.)
- **XML doc comments explain *why*, not *what*.** They are a selling point of this package, not decoration. Match the surrounding density.
- **An `EnabledRefRW`/`RO` parameter is named** *component name* + `Enabled`.

---

## Verification gate — required after every change

1. `mcp__UnityMCP__refresh_unity` (`compile: "request"`, `wait_for_ready: true`)
2. `mcp__UnityMCP__read_console` (`types: ["error"]`) — some tests deliberately log errors on negative paths; judge by `error CS####` / Burst `BC####`
3. `mcp__UnityMCP__run_tests` EditMode `["DotsAnimationToolkit.Tests.EditMode"]` → poll `get_test_job` (`wait_timeout: 90`)
4. `mcp__UnityMCP__run_tests` PlayMode `["DotsAnimationToolkit.Tests.PlayMode"]` (`init_timeout: 120000`) → poll (`wait_timeout: 90`)

**Check the discovered total, not just pass/fail** — a suite that silently stopped compiling a fixture reads as green. A phase that adds logic and returns a flat test count is not done; that was sent back once already.

**Prove writes persist.** For anything that saves, drive the write path via `mcp__UnityMCP__execute_code` against a real asset, save, reload from disk, and assert. Do not settle for "the field displays". Clean up scratch assets and confirm `git status` afterwards.

### Process lessons paid for in this session

- **Do not let subagents spawn their own subagents.** Three processes driving one live Unity Editor caused MCP lock contention that spammed `Logs/Editor.log` to 2.2 GB and broke test runs. Phases run sequentially through the gate.
- **Save in small increments and recompile often.** Several agents were cut off mid-edit by usage limits; one left the package uncompilable.
- **Features have repeatedly shipped with tooltips and docs describing behaviour that did not work**, passing tests that checked wiring existed rather than that the feature did what it claimed. Every toggle that writes a pose must be asked "does it un-write?"
- **A test that cannot fail is worse than no test — and the owner does not want volume.** Before keeping a regression test, revert the fix and watch it fail; if it passes either way, delete it. Two per fix is the target: the behaviour the feature exists for, and the regression itself. A guard clause whose behaviour is obvious from reading it needs no fixture. Watch for `LogAssert.NoUnexpectedReceived()` — Unity fails tests only on unexpected *errors*, never warnings, and it checks logs received *before* the call, so it is a common way to write a test that can never fail.

---

## What is done

**Ragdoll (Amendment A50, phases D0–D14)** — complete except D8. Solver, bake, five runtime systems, Clip Editor component, preview simulation with box handles, optional Unity Physics probe, docs, 0.11.0 bump.

**Target tags (Phase E)** — E0 verified the footprint claim (clip tracks reach the runtime as dense indices only; no blob or runtime change needed). E1 `TargetTagRegistry`. E1.5 shared `VocabularyPicker` + `VocabularyQuickEditWindow` on a `PickerOverlay` base. E2 `RigTargetDefinition.tagId` + rule T1 (`V34`). E3/E4 landed **and are now verified end to end**: `TransformTrack.tagId` and `SpriteTrack.tagId` with a documented sentinel convention, bake resolution against the set's rig, and T2/T3/T4 as `V35`/`V36`/`V37`. A shareable clip is one whose `rig` is null — that null is the V06 exemption, and it is what lets the clip join sets whose rigs differ.

**E3/E4 proof (2026-08-25).** `ClipRegistryBuilderTests.Build_ResolvesOneSharedClip_ToDifferentDenseIndices_InTwoSetsWithDifferentRigs` — one `ClipAsset`, one tag-bound track, two sets whose rigs differ in target names, count and stable ids; dense index 0 on one, 1 on the other. Verifying it found a real defect, now fixed: **T2 (V35) was judged against `clip.rig`**, which on a shareable clip is null by design, so every tag-bound track of every shared clip warned always — including on rigs that did carry the tag, the feature's healthy path. Both binding checks now take a `resolutionRig`, the rig the clip will actually play on, which from `ValidateSet` is the **set's** rig. The bake path was always correct (`ClipRegistryBuilder` used `clipSet.rig`), so this was validation noise, never wrong animation. Commit `d65dfc94`.

**E6 Task 2 — generated name constants (2026-08-25).** `ConstantsGenerator`
(`Editor/ClipUtilities/`) is now the one code path behind all three "Generate … Constants"
buttons; `ClipSetAssetEditor`'s private copies of the sanitise / unique / keyword-escape /
XML-escape rules were deleted and repointed at it. `VocabularyConstantsSection` is the shared
UI block on both registry inspectors — and therefore reachable from any picker's "Edit Target
Tags…" / "Edit Events…", since `VocabularyQuickEditWindow` hosts those inspectors verbatim.
The destination is asked for **once** through a save dialog and then remembered on the registry
itself (`IVocabularyRegistry.GeneratedConstantsPath`, persisted through the existing
`ProjectSettings/` JSON round-trip), after which the button reads *Regenerate* and rewrites in
place with no dialog. It is stored with the vocabulary rather than in `EditorPrefs` on purpose:
re-picking a path per machine is how a project ends up with a second constants file that nobody
regenerates. The class name is derived from the chosen file name, so `TargetTags.cs` gives
`TargetTags.Jaw`. Names that cannot be written as C# are logged as warnings naming both forms
(`'Eye L' … emitted as 'Eye_L'`), because §4.2.3's promise is that the owner works in names.

**Proof it persists (2026-08-25).** Driven through `execute_code` against the live project
vocabulary: a row and a path were written, the `ProjectSettings/` file was re-read from disk
into a **fresh** instance, and the generator run off *that* — the constant, its minted id, the
derived class name and the substitution report all came back correct. Scratch artefacts removed
and `git status` confirmed clean.

**One doc correction on the way through.** `MakeUniqueName`'s comment claimed a `_2`, `_3`, …
suffix sequence; the code has always produced `_1`, `_2`, …. The docs were corrected to match
the code, not the reverse — changing the sequence would rename constants in any project that
has already generated them. (The logic itself is sound: a row literally named "Foot step 1"
cannot collide with a generated `Foot_step_1`, in either arrival order.)

**Recent recovery:** an agent died mid-refactor having deleted `TargetTagPicker`/`TargetTagQuickEditWindow` after writing generalised replacements. Call sites were repointed at `VocabularyPicker`, `AnimEventKeyRegistry` gained its missing `IVocabularyRegistry.ContainsId`, and the `ProjectSettings/` singleton machinery was moved out of `Authoring/` into `Editor/ClipUtilities/VocabularyRegistryProvider.cs` to satisfy `Conformance_C`.

---

## What is left, highest value first

1. **E6 Task 4 — sweep raw numbers out of every editor surface.** Event markers in particular still identify by `eventKey`. Show names everywhere a name resolves.

2. **One timeline lane per event name.** Replaces the current vertical stacking. `Footstep` gets a row, `Damage` gets a row; three events on one frame land on three rows automatically. Adding an event with a new name creates its lane.

3. **Move tagging onto the rig hierarchy rows**, per the owner's "it should live in the rig setup so I can adjust it directly on the physical rig hierarchy."

4. **"Edit Events" button available immediately after adding an event.**

5. **E5 — docs, CHANGELOG, Amendment A51** into `Phase_B_Architecture.md`. Note amendments run to A50; check `grep -nE "^## Amendment A[0-9]+"` before claiming a number — "A45" was claimed once while taken and had to be renumbered across ~55 places.

---

## Open decisions the owner must make (do not decide these unilaterally)

- **The bone-reparent guard.** Hierarchy drag-to-reparent permits dragging **skinned bones**, and `RigStructureEditor.ValidateReparent` guards only cycles and self-parenting — not skin-binding integrity. Because `TryReparent` uses `worldPositionStays: true` it looks fine at the moment of the drag; the corruption is delayed and silent, surfacing when the clip plays or when VAT bakes it into every frame with no error. `ClipPreviewController.IsSkinnedBone(int)` already exists and is public, so the guard is cheap. **Recommendation: guard it.** The owner asked for hierarchy dragging, so excluding bones is their call.
- **Q1 — the Spatial3D twist axis.** `twistLimitDegrees` has no defined axis. D2 provisionally used the child's rest-local +Y. The owner said "ignore this for now"; it blocks **D8 (3D ragdoll polish)** only.

---

## Known gaps and caveats

- `ClipEditorWindow.CountTracksForTarget` matches by raw `targetId`, so it **undercounts tag-bound tracks** in delete confirmations. `ClipSpriteEditing.CollectTracksForTarget` has the same flaw but no production callers.
- The ragdoll preview derives `restRelativeRotation`/`parentAnchorOffset` from the **on-screen pose**, not the authored rest pose, so toggling on mid-animation can show a first-frame limit correction the runtime would not produce.
- The Unity Physics probe casts **along gravity only** — a wall a body drifts sideways into does not register.
- `PreviewRigMirror` has no notion of `HierarchyPath`, so such addresses do not resolve on a pure-cutout preview.
- **The ±45° default hinge limits** on ragdoll bodies were invented by an agent, never judged by eye. Prime suspect if a drop looks wrong.
- `Logs/Editor.log` reached 2.2 GB from lock contention. Restarting the Unity Editor rotates it.

---

## Not yet judged by eye

The owner has been away from the PC. These are all test-clean but visually unverified: the ragdoll drop, Rig Edit gizmos, the New Rig wizard, the amber event pin shape, and event stacking.
