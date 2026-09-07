# Amendment A68 — Cutscene Docs, API Reference, Sample, Release

> **Status:** ✅ spec, not built. Written 2026-09-04.
> **Roadmap:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` — read its §4 protocol first.
> **Depends on:** everything (A61–A67, G1–G3). Last in the queue.
> **Session budget:** one Sonnet session. Documentation, one compile-checked sample, a version bump. No runtime changes — if you find a bug here, log it in §7 and stop; do not fix it inside a docs amendment.

## 1. Why

A feature is not shipped in a sellable package until a stranger can integrate it from the docs alone. `Documentation~/cutscenes.md` describes the Phase G/A58 surface; A61–A67 changed the data model, the runtime contract, and the editor three times over. The owner asked for **well-detailed API docs** as a deliverable in its own right.

## 2. Read first

- Every cutscene file under `Runtime/`, `Authoring/`, `Editor/ClipEditor/Cutscene/` — the reference is written from the code, not from the amendments. `Documentation~/index.md`, `cutscenes.md`, `animation-events.md`, `sharing-clips.md` (voice and structure to match). `CHANGELOG.md` `[Unreleased]`, `package.json`.
- `Samples~/QuickStartActor/` — the sample layout and its asmdef; `Assets/_Vault/Memories/Code/AnimationToolkit.md` and the memory note "Toolkit Samples Not Compiled" (compile-check `Samples~` through a temporary assembly).
- `Tests/EditMode/PackagingConformanceTests.cs` — `Conformance_C` (no `UnityEditor` text in `Authoring/`), `Conformance_D` (no host asset-folder paths in package docs).

## 3. Deliverables

### 3.1 `Documentation~/cutscenes.md` — rewrite

Structure: Concept model (slots, lanes, holds, segments) → Authoring walkthrough per lane (clip, root, facing, part tracks, attach, marks, camera, events incl. holding events, holds) → Stage baking and scene binding → Editor workflow (viewport, cast, selection, clipboard, Auto Key, curves, transport) → Playing a cutscene (the host recipe, ten lines of code) → Runtime contracts a host consumes (`CutsceneCameraPose`, `CutsceneMoveToMark`, `CutsceneDetachSignal`, `CutsceneFacing`, `AnimEventOutput` on the request entity, `CutsceneHoldRelease`) → Known limitations (only what is still true). Every claim checked against the code the day it is written.

### 3.2 `Documentation~/cutscene-api.md` — new reference

One entry per public runtime type and member in `Runtime/Components/CutsceneComponents.cs`, `Runtime/Api/CutscenePlaybackApi.cs`, `Runtime/Blobs/CutsceneBlob.cs`, `Runtime/Sampling/CutsceneBlobSampler.cs`, `Runtime/Sampling/CutsceneBlockTiming.cs`, the two systems, and the authoring surface (`CutsceneAsset` and nested types, `CutsceneStageAuthoring`, `CutsceneBlobBuilder`, `ICutsceneEventInspectorProvider`). Per entry: signature, what it is for in one sentence, who writes / who reads, lifecycle (when added, enabled, cleared), ordering (group and edges), threading (main thread or Burst-safe), and the file it lives in. Then a **Host integration checklist** (bind, apply camera, move to marks, react to detach, map facing, release holds, destroy the request) and a **Frame order** diagram of `CutsceneTimelineSystem` → host movement → `TransformSampleSystem` → `CutscenePartOverrideSystem` → `TransformApplySystem` → `SocketResolveSystem`. Link it from `index.md` beside `cutscenes.md`.

### 3.3 `Samples~/Cutscene/` — a compile-checked sample

A minimal host: `CutsceneSampleHost.cs` (MonoBehaviour) that on a key press finds the stage by key, creates the request, applies `CutsceneCameraPose` to `Camera.main` while `isDriven`, walks entities to marks by lerping `LocalTransform` (a stand-in for pathfinding, clearly labelled), releases the `"Dialogue"` hold on a second key press, and destroys the request on completion. Plus a README and an asmdef mirroring `QuickStartActor`'s. **Compile-check it** by copying the folder to a temporary assembly under the host's asset tree, running the gate, then deleting the copy and confirming `git status` is clean — `Samples~` is invisible to the compiler and rots silently. Add the sample to `package.json` `samples`.

### 3.4 Release

- `CHANGELOG.md`: fold the `[Unreleased]` cutscene entries from A61–A67 into one `## [0.15.0]` section with Added / Changed / Fixed, one sentence per item, no narrative.
- `package.json` → `0.15.0`.
- `HANDOFF.md` §1 current-state paragraph rewritten; §4 loses every closed cutscene entry and gains one line: "Cutscenes: shipped in 0.15.0 — see `Documentation~/cutscenes.md`; open items in `Cutscene_Roadmap.md` §7 if any."
- `Assets/_Vault/Memories/Code/AnimationToolkit.md`: a "Cutscenes" section of traps only (stage must live in the bound objects' scene; hold ≠ pause; marks suspend the root lane; attached slots ignore root keys; skip applies attach markers). No feature list — write the `find` command that lists the files instead.

### 3.5 XML doc pass

Across the cutscene files touched by A61–A67: doc comments to one or two lines of *why*; delete multi-paragraph `<remarks>` that narrate the body (HANDOFF §2). Keep every remark that records a trap or a decision id.

## 4. Tasks

- [x] **T1 — `cutscenes.md` rewrite (§3.1).** **[parallel-safe with T2]**
- [x] **T2 — `cutscene-api.md` (§3.2).** **[parallel-safe with T1]** Every entry names its file; spot-check ten members against the code by opening them.
- [x] **T3 — Sample + compile check (§3.3).** Gate on the temporary copy; delete it; `git status` clean.
- [x] **T4 — Release + memory note (§3.4).**
- [x] **T5 — XML doc pass (§3.5).** Compile gate; `PackagingConformanceTests` (the doc-text scanners) green.
- [ ] **Full suites once.** Counts must not drop.
- [ ] **⏸ Owner checkpoint.** Read `cutscene-api.md` top to bottom with the acceptance cutscene running beside it. Anything the doc does not explain is a §7 entry.

## 5. Risks and traps

- `Conformance_D` flags `Assets/` + identifier in any package `.md`; write "your project's asset folder".
- `Conformance_C` scans comments — the sample host is *outside* `Authoring/` so it may mention the editor, but the doc pass must not add the token to `Authoring/` files.
- A sample that references `Camera.main` compiles without `Unity.Entities.Graphics`; do not add package references the sample's asmdef does not need.

## 6. Build log

- **Drift, spec vs. reality (found before T1, grounding pass):** §3.2 names `Runtime/Api/CutscenePlaybackApi.cs` — that file no longer exists. Amendment A69 (already landed, per HANDOFF §1/§4) renamed it to `Runtime/Api/CutsceneApi.cs` (`CutscenePlaybackApi` → `CutsceneApi`, table in `CHANGELOG.md`'s `[Unreleased]` "Changed — breaking (A69)" entry). Following the code per protocol §4.2: T2 documents `CutsceneApi.cs`, not a file named `CutscenePlaybackApi.cs`.
- **Drift, spec vs. reality (§3.4 release), corrected:** `package.json` is **already at `0.15.0`** (A69 bumped it — "Landed as 0.15.0" per its own HANDOFF §4 entry), but `CHANGELOG.md` was never folded to match: everything from Phase G / A58–A60 (pre-roadmap cutscene work) through A61–A67 (the roadmap) through A69 (unrelated static-class renames) all still sat under one `## [Unreleased]`. §3.4 asks specifically for the **A61–A67** entries — not A58–A60, not A69. Followed literally: extracted only the A61, A62, A63, A64, A65 (+ its untitled Fixed section), A66 (+ its Fixed(A66)), A67 entries into a new `## [0.15.0]` section (Added/Changed/Fixed, one sentence per item) placed above `## [0.13.0]`; left A69's "Changed — breaking" table and the Phase G/A58–A60 cutscene entries exactly where they were, still under `## [Unreleased]`. `package.json`'s version needed no change (already `0.15.0`). Net effect: `[Unreleased]` still contains real content (A69 + pre-roadmap cutscene work) after this amendment — that's expected, not a leftover mistake; a future release note should fold those separately. Flagging per protocol §4.8 rather than silently deciding scope — owner should confirm A58–A60/Phase G being left out of 0.15.0's cutscene note (despite being cutscene work) is the right call, since the roadmap itself only counts A61 onward as "the roadmap."
- **T5 already satisfied by prior work, verified not assumed:** Amendment A69 (landed 2026-09-06, after this spec's 2026-09-04 write date) already ran a package-wide doc-comment diet — `grep -n "<remarks>|<para>|<strong>|<em>|Amendment|Phase [A-Z]|§" **/*Cutscene*.cs` across the whole package matches **zero** hits in any `Runtime/`, `Authoring/`, or `Editor/` cutscene file; every match is in `Tests/` fixtures documenting *why* a regression test exists (an amendment/decision id), which is exactly the kind of remark T5 says to keep and which Conformance_F/C do not scan (Tests are not shipped). Nothing left to edit in shipped cutscene sources. `PackagingConformanceTests` confirmation deferred to the end-of-spec full-suite run — the Editor was in Play Mode when checked (the owner appeared to be running the verify-cutscene.md checklist), so no test job was started to avoid interrupting it.
- **Two doc-content drifts found by T1 (`cutscenes.md` rewrite), not blocking, recorded rather than silently fixed:** (1) `CutsceneHoldMarker.autoReleaseWhenMarksReached` defaults `true` and drives real bake/runtime behavior, but `CutsceneEditorPanel.BuildHoldInspector` exposes only **Time** and **Hold Id** in the Cutscene Editor tab — no in-tab toggle for it; the field is only reachable through the asset's default Unity inspector. (2) A cutscene's Events lane has no name/create picker on **Event Key** (a raw `PropertyField` on the `uint`), unlike a clip's own Events lane which uses a searchable `VocabularyPicker`. Both are documented as-is in `cutscenes.md`'s Known Limitations rather than treated as bugs to fix — T5/A68's budget is docs only, no runtime changes.
