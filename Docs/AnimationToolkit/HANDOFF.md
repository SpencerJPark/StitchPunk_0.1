# Handoff — DOTS Animation Toolkit

Paste this whole file as the first message of a new chat.

---

You are continuing a sellable UPM package at
`C:\Users\spenc\Documents\GitHub\Stitch_Punk\Packages\com.dotsanimationtoolkit` (version 0.60.0).
**§4** is newest first: A100 (Stats tab, built 2026-09-15, T12 closed under the standing rule with its question kept; roadmap Phase 2 complete), the 0.52.1 Flipbooks rename, then A101 (editor chrome consistency, built 2026-09-15 unattended, three ⚠ interpretations for the owner), then A99, A97F and A96F (built 2026-09-15 unattended; their checkpoints closed under the standing rule, the ⚠ questions kept in each paragraph), then A98, A97 and A96 (built 2026-09-14). The older editor checkpoints
further down §4 (A71–A81 era) were closed as accepted on 2026-09-14 under the owner's "assume they pass unless something
is game breaking" rule; the in-game G5-P10 and ragdoll RG-T4/T7/T10 checks stay open (A99 re-asks the ragdoll ones).

## 1. Read first, in this order

1. `CLAUDE.md` (repo root) — project conventions.
2. `Docs\AnimationToolkit\Phase_F_RigCentric_Spec.md` — the active spec. Its §10 decisions are
   recorded architecture calls, and §12 records three amendments raised while building it (the T6
   rule number, V39, V40). The code is written; **nothing in it has compiled or run yet.**
3. `Docs\AnimationToolkit\Amendment_A55_EventAuthoring_Spec.md` — closed apart from its §4 Task 5
   visual pass.
4. `Docs\AnimationToolkit\Phase_E_TargetTags_Spec.md` — both A55 and Phase F build on its tag
   rules. §4.2.1, §4.2.2, §4.2.3 and §6.1 are owner directives, not suggestions.
5. This file's §5 and §6 — standing owner directives, and what you may not decide alone.

Read `Docs\AnimationToolkit\Phase_D_Ragdoll_Spec.md` §9 only if you touch ragdoll code.
The Phase A/B/C review docs were deleted in the 2026-08-29 cleanup — closed history, recoverable
from git if a decision ever needs tracing. What remains in `Docs\AnimationToolkit\` is live.

**Current state (2026-09-08):** cutscenes are a shipped feature, now on v0.19.0 (Amendment A73,
profile-driven) — see `Documentation~/cutscenes.md` for the concept model and authoring workflow and
`Documentation~/cutscene-api.md` for the full runtime/authoring member reference. `Samples~/Cutscene`
is a compile-checked host sample. `CHANGELOG.md`'s `## [0.19.0]` section is the one-paragraph-per-item
summary of everything A73 shipped; see §4 for A73's own status and its open owner checkpoint.

**0.16.0 shipped the actor profile (Amendment A70, T1-T10 all landed and gated)** — layers moved off
the rig onto `ActorProfileAsset`, animations are played by name (`PlaybackApi.PlayAnimation`), a
directional entry resolves per-facing, and ragdoll can be triggered from an animation; see
`Documentation~/actor-profiles.md`, now with a "cutscenes play your profile" paragraph added by A73.

Two things carried forward from the pre-cutscene era, still true: **every existing `ActorAuthoring`
still needs its `clipSet` re-pointed by hand** where Phase F's migration-free field removal (2026-08-29)
was never followed by a bulk fix — `MaleCitizen` (the game's first toolkit actor, built for G0) is
re-pointed and working; `PlayerUnit`/`BaseUnit`'s separate body-part tree is not. And rig billboards
and distance LOD still need a host-written `AnimationToolkitCameraData` singleton (both
`BillboardResolveSystem` and `AnimLodDistanceSystem` `RequireForUpdate` it) — the game has one
(`AnimationToolkitCameraBridge.cs`, `Assets/_Scripts/MonoBehaviours/Managers/`, placed only in
`TestArea.unity`). Without a writer, billboarded rigs do not turn at all (spherical is the zero-`forward`
case, not this one) and distance LOD does not run; since 0.37.0 (A90) the console says so once after
120 frames and names the Camera Sync sample.

## 2. How to work here

**Write less prose than the surrounding code does.**

- **Doc comments (Amendment A69, replaces the older one-or-two-line rule verbatim):** One
  `<summary>` per file, on the file's primary type, at most three lines, stating what the type is
  and the one contract a caller must know. Nothing else in the file gets a `<summary>` unless the
  name cannot carry it. No `<remarks>`. No `<para>`, `<strong>`, `<em>`, `<list>`. No citations of
  the architecture doc, amendments, phases or spec sections — customers do not have those
  documents. A `<param>` only for a sentinel or a unit (`NaN = clip default`, `seconds`, `−1 =
  none`). A field comment only for a sentinel, a unit, or an ordering/aliasing trap, as **one**
  line. An inline `//` only for a *why* the code cannot express — a trap, an ordering constraint, a
  reason a reader would otherwise "fix" — at most two lines. If a method needs a paragraph, the
  method is wrong: split it or rename it. Enforced by `Conformance_F` in
  `PackagingConformanceTests.cs`.
- **Test only what matters.** The behaviour the feature exists for, and any regression you fix.
  That is the ceiling — roughly two tests per task, often zero for pure UI wiring.
  **Before keeping a test, revert the fix and watch it fail.** If it passes either way, delete it;
  it costs maintenance and proves nothing. A guard clause whose behaviour is obvious from reading
  it does not get a fixture. Watch for `LogAssert.NoUnexpectedReceived()` — Unity fails only on
  unexpected *errors*, never warnings, and it inspects logs received *before* the call, so it is
  the most common way to write a test that cannot fail.
- **Small increments, recompile often.** Sessions here have been cut off mid-edit; one left the
  package uncompilable. Save and gate after each coherent piece, not at the end of a task.
- **Do not spawn subagents.** Three processes driving one live Unity Editor caused MCP lock
  contention that grew `Logs/Editor.log` to 2.2 GB and broke test runs. Work sequentially.

**Hard conventions (from CLAUDE.md — non-negotiable):**

- Never `var`. Never single-letter names. Explicit types; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` into `state.Dependency`.
- **UI Toolkit only** in editor sources. `Conformance_E` bans IMGUI and the test enforces it, so
  `AdvancedDropdown` is unavailable.
- **`Authoring/` must never reference `UnityEditor`** — it ships to players and `Conformance_C`
  scans raw file text including comments. Editor-only machinery lives in the Editor assembly.
- An `EnabledRefRW`/`RO` parameter is named *component name* + `Enabled`.
- **Static-class suffixes: §2.1 of Amendment A69.** One suffix per role — `Api` (the public surface
  a host game calls, `Runtime/Api/` only), `Builder` (bake-time authoring→blob), `Sampler`
  (pure blob+time→pose), `Resolver` (pure id/tag/angle→index), `Math` (pure numeric functions),
  `Validation` (rule checks), `Utility` (editor-only asset surgery, `Editor/ClipUtilities/` only),
  `Editing` (editor-only clip-editing operations). Banned anywhere: `Util`, `Utils`, `Helper`,
  `Helpers`, `Query`, `Manager`, `Common`, `Misc`, `Ext`, `Extensions`. A static class that is none
  of these roles is named as a plain noun for the thing it models. Enforced by `Conformance_G`/
  `Conformance_H`.

## 3. Verification gate

Unity MCP only works while the Editor is open. If `mcp__UnityMCP__*` is unreachable, say so and
stop rather than claiming a change compiles.

**Cadence (owner directive, 2026-08-28): the full suites do NOT run after every edit.** The owner
closed the Editor over exactly this — sessions re-running ~700 tests per change. Per edit: steps
1–2 (the compile gate) plus only the fixtures the change touches, by `test_names`/`group_names`.
Steps 3–4 in full run **once, at the commit point** for the task. And stop growing the suite —
most UI wiring gets zero tests (§2).

1. `mcp__UnityMCP__refresh_unity` (`compile: "request"`, `wait_for_ready: true`)
2. `mcp__UnityMCP__read_console` (`types: ["error"]`) — some tests log errors on negative paths
   deliberately; judge by `error CS####` / Burst `BC####`
3. `mcp__UnityMCP__run_tests` EditMode `["DotsAnimationToolkit.Tests.EditMode"]` → poll
   `get_test_job` (`wait_timeout: 90`)
4. `mcp__UnityMCP__run_tests` PlayMode `["DotsAnimationToolkit.Tests.PlayMode"]`
   (`init_timeout: 120000`) → poll (`wait_timeout: 90`)

**Check the discovered total, not just pass/fail.** `resultState: "Passed"` with `total: 0` is the
shape of a suite that silently stopped compiling. Counts must not drop.

**For anything that saves, prove the write persists**: drive the path with
`mcp__UnityMCP__execute_code` against a real asset, save, reload from disk, assert. "The field
displays" is not proof. Delete scratch assets and confirm `git status` afterwards.

## 4. The queue

**Phase 6 close - A105, A106 and A107 (0.58.0, 0.59.0, 0.60.0), built 2026-09-19 in three parallel worktrees.** The last three editor UI passes, run together and integrated in one sitting; Phase 6 is complete. **The premise of the run was that all three specs were stale** - written 2026-09-15 against pre-A104 captures, then overtaken by eight trunk passes and A108 without anyone updating them. Fifteen read-only verifiers graded all 79 findings with `file:line` evidence and the stage cross-checked every DONE against a fresh capture: **22 were already built**, so running a spec as written would have rebuilt finished work. Residual was A105 19, A106 26, A107 20, so all three kept a worktree and A107's "runs alone" was overturned as parallel-safe. **The capture cross-check earned its keep three times:** the first capture pass was taken with nothing selected, so Rigs and Ragdoll photographed empty states rather than the rows the findings describe (re-taken after setting `ClipEditorWindow.selection` by reflection); **RD5 passed in code and failed on screen** - all three Ragdoll columns called `MakePaneHeader`, yet the headers sat at three heights, because `.toolkit-pane-header` carried no height at all and `RagdollViewportElement` was not a `.toolkit-column` either (fixed in the shared sheet with `min-height: 32px`, so no tab patches it from C# again); and **AP6 was graded DONE from the wrong file** - the duplicated direction text lives in the preview transport row, not the inspector column the verifier searched. Nine findings were new, five of them on Ragdoll from holding its capture against `StyleGuideReferenceImage.png`. Two verdicts were later overturned *in the toolkit's favour* by measurement: **RD9** (the flat-looking Bodies list is genuinely two-tone - column `#383838`, list `#282828`) and **RD11** (the scenery control already is a segmented control; it shows one segment because `RagdollPreviewScenery.Props` is empty in this scene, while the reference image was captured with props authored). EditMode **968/968** after integration, up from 962; PlayMode 304/304 at the baseline. Captures: `Library/UIAudit/before-phase6-close/` and `after-phase6-close/` (plus `10_ActorEditor_loaded.png` and `11_Ragdoll_selected.png`, the only shots where the layer rows and the Body card are visible at all). **For the owner:** (1) Texture Packer's unsaved-setup flow - is "Save as recipe" where you would look for it, beside Bake? (2) Rigs chips at 34 target rows - right density? (3) Health's filter segments, or should the counts be badges that toggle? (4) **CT3 needs your call, not a pass:** the cutscene rail's opacity is deliberate, with a design comment in both the C# and the USS, and R20 wants one translucent rail everywhere - change it or keep the exception?

**The Cutscenes magenta is closed, and it was never the toolkit's (2026-09-19).** `Assets/Materials/UnitsLegacy/Faceware.mat` named shader guid `e5e6305b...`, which no `.meta` in the project claims, so Unity substituted `Hidden/InternalErrorShader`. It was repointed to `Shader Graphs/2DShader` (`dd0290a2...`) - the *same* `fileID`, and the shader the other 27 `UnitsLegacy` materials already use, so it was a one-guid edit rather than a guess. Verified in the Editor and photographed before and after (`12_CutsceneEditor.png` vs `12_CutsceneEditor_facewareFixed.png`). **Still magenta and still not ours:** two root-level `Quad` renderers in `TestArea` carry a genuinely null material - an owner call, since nothing says what those quads are meant to be.

**A108 Chrome consistency pass 2 (0.57.0), built 2026-09-17, one owner checkpoint open.** The owner's four complaints from the A105 review, all closed and all measured. **Gutters:** Actor Profiles' layers column and the Actor Inspector carry `toolkit-column--inset` (12px sides, 8px top) on the column *views*, inside `toolkit-list-surface` so the dark tone still reaches the column edge; putting it on the hosts instead is the double-inset trap. **One button family:** `ToolkitIcons.MakeIconTextButton` applies Secondary unless the caller named a variant, so no call site can produce an unstyled control again; `MakePrimaryAction` goes through `StyleButton` (without that one line every primary carries two variant classes — proven by revert); the icon-only base takes the shared 5px radius and a ghost look; and `.toolkit-action-run` gives a header run one height, 28px when it carries the primary. Bake and Save now measure height 28.0 / radius 5.0 / one family. `MakeSecondaryAction`, `MakeGhostAction` and `MakeDestructiveAction` join `MakePrimaryAction`. **One tone:** the sheet's `-unity-background-image-tint-color` rule for this never worked and could not — the IL of `Image.OnGenerateVisualContent` reads `Image.tintColor` and never the resolved background tint — so `ToolkitIcons.ApplyIconTone` writes `tintColor` from the control's own resolved `color` (no colour literal in C#); the Bake glyph reads at 15.17:1 on the light fill. Because a tint multiplies and cannot desaturate, the six multi-hue call sites (four Bake buttons, both Ragdoll preview toggles) use drawn glyphs — this widens D6, which only meant to avoid redrawing every icon. **Fifteen drawn tab glyphs:** new `ToolkitGlyphs` rasterises all fifteen from signed distance fields on one grid at one stroke weight; Rigs is a bone, Ragdoll is a body. Its five shape files register through **erasable `static partial void` hooks**, which is what let the framework and its shapes be written in the same parallel wave. Compact tabs are now centred to 0.000px because the toggle's *input* leaves the row (the `Clickable` is on the Toggle itself, and `panel.Pick` still routes from all five points of a 28x24 tab); an `--icon-unresolved` tab keeps its input so it keeps its word. Three of fifteen shapes were rejected from a rendered contact sheet and reworked (a crescent read as a moon, a half-disc as a prohibition sign, one aperture notch as a dial, a crossed pair as a node graph). **Two defects five green gates could not see and the drive did:** `AttachToPanelEvent` is too early to read `resolvedStyle.color` — it returns opaque black with alpha 1, past any alpha guard, and tinted 14 of 15 glyphs invisible (now deferred through `schedule.Execute` plus `GeometryChangedEvent`); and `flex-basis` beats `width`, so the Health tab stayed 104px in compact mode and the strip ended in a gap. Six fixtures, each proven by reverting its own fix in three rounds. EditMode **962/962**, PlayMode **304/304**. Captures: `Library/UIAudit/before-a108/` and `after-a108/` (plus `02b_BakeSaveRun.png` at 3x and `Library/UIAudit/a108-glyphs/contact_sheet.png`). **⚠ For the owner:** (1) do Rigs and Ragdoll read the way you meant at 16px? (2) a run carrying the primary is 28px throughout, so Save grew to match Bake rather than Bake shrinking — right call? (3) D6 widened as above. **Observed, not caused here:** at 974pt with all four Actor Profiles columns open the 260px inspector runs ~1.2px past the window edge (zero elements overflow the column itself), and the 12px inset costs its fields 24px, which truncates the `Rig` field's label at that width. **Carried to A107:** the four cutscene-panel icon+word buttons, which now inherit the Secondary default.

**A104 Style foundation (0.56.0), built 2026-09-15 through `/worktree-run`, S5 open for the owner.** The shared layer the per-tab passes build on, to the style guide approved the same day (`Docs/AnimationToolkit/EditorStyleGuide.md`). Two new stylesheets under `Editor/ClipEditor/Shared/` — `ToolkitTokens.uss` (tokens) and `ToolkitComponents.uss` (component classes) — load after the window sheet through one `ToolkitChrome.AddToolkitStyleSheets(root)` call in the Clip Editor and the VAT Bake window, so all 15 tabs change at once. A stage probe settled the open question: a custom property defined as `var(--unity-colors-window-background)` **does** resolve in a live editor panel (#383838), so the tokens alias Unity's theme variables and the light skin needs no second palette; a probe element in a docked window that is not the visible dock tab has no panel and reads transparent, which is a false negative, so probe in a floating utility window. The tab strip is a tab list that no longer clips descenders, in-pane modes are a segmented control, the primary is a neutral light fill with secondary/ghost/destructive variants and a visible disabled state, list rows are flat 22px lines with the full text in the tooltip, and `PreviewSurfaceMaterialResolver` replaces `Default-Diffuse` so proxies stop rendering magenta under URP. `Conformance_J` ratchets the window sheet down (151 → 139 literals, 5 → 0 off-scale font sizes). Gates: W1 17/17, W2 28/28, W3 30/30, the revert-to-fail failing exactly its three fixtures; after the merge EditMode 876, PlayMode 285. Before and after captures of all 15 tabs: `Library/UIAudit/before/` and `Library/UIAudit/after-a104/`. **Audit findings kept for A105–A107 (A104 §7 S3, F1–F9), none fixed here by A104-D11:** Texture Packer's image list still uses the old boxed two-line row (`ImageCatalogColumn`, not the shared column); Flipbooks truncates every name because its name column is too narrow; Events' key rows sit four row-heights apart; Clip Sets repeats the full asset path on every row; Rigs' target rows are still boxed; empty panes are plain sentences rather than designed empty states; VAT Bake, Capture and Health stretch their primary to the full column width; Ragdoll's body picker is a tall empty box; and **Cutscenes still renders magenta** — that viewport draws the open scene through a hidden camera, so A107 must confirm whether the source is scene materials rather than a toolkit proxy. **⚠ For the owner (S5):** **Q1** does the tab list read right at this window width, or should tabs shrink or scroll when the window is narrow? **Q2** is the neutral light primary clear enough as "the main action"? **Q3** anything in the shared layer to change before A105/A106 build on it? **Unverified:** the replaced proxy material is unproven by eye (every preview viewport was empty in the captures), and no Play mode was run.

**A103 Unified authoring and the whole-animation VAT preview (0.55.0), built 2026-09-15 through `/worktree-run`, S6 closed under the standing rule.** One spec replaced Unified Clip Authoring P1–P5 and A79. `RegistryTargetPoser` owns the preview registry blob and the per-target pose loop: `ClipPreviewController` writes it into its flat mirror with root-relative rest, and `VatPreviewPartPoser` writes it into the VAT Bake source copy's real nodes with local rest, skipping VAT-part targets. A rig with no targets now builds a registry (the `PartCount == 0` return is gone) and says, as information, when nothing in the set will move. The Clip Editor gains a default-off **Baked VAT** rail toggle that draws baked parts at the skeleton root in place of the skinned mesh, a **VAT binding** component on each node the bake resolves (writes `vatTracks` by target or `vatSource`; no remove button, clearing Source removes it), and read-only imported-clip lanes placed by the clip's duration. The VAT Bake preview plays every part, names clips, and splits **VAT parts** / **Other parts** / Ghost. Five new EditMode fixtures (F1–F5) each failed on their revert; EditMode 873, PlayMode 285. The stage drives found and fixed one defect the fixtures could not see: the two new VAT Bake toggles were created off and *Other parts* had no change callback, so the posed quad half stayed hidden (both-on rendered identical to VAT-only) — fixed in `37258750` and re-driven (four states, four images, playhead still). ⚠ Kept for the owner (S6, closed under the standing rule): **Q1** Baked VAT is off by default and *replaces* the skinned mesh rather than overlaying it — right view for spotting bake drift, or default on, or ghosted? **Q2** do the two drawn glyphs read at 16 px, and is *Other parts* the right name? **Q3** one read-only row per animated bone for imported clips, or a row per channel? Captures in `Library/A103Captures/` (`b_vat_bake_preview_*.png`, `c_baked_vat_*.png`). Pre-existing, not A103's: `VatSampleTentacleClips.vatTextures` points at a deleted asset (no baked tentacle set exists; rebake it), the Clip Editor's proxy quad for a VAT-mesh target renders magenta (`PreviewRigMirror`'s material has no working shader under URP), and `VatBakeWindow` has no `OnDisable`, so closing the floating window leaks its preview's source copy until the next domain reload. Unverified: nothing was driven inside the docked Clip Editor window itself (the rail toggle, the binding row in the inspector and the imported lanes under live zoom were proven on detached controllers and elements only), and no Play mode.

**A102 Release readiness (0.54.0), built 2026-09-15 in the A102 / Despawn / Minion Orders parallel batch, no owner checkpoint.** The three "before 1.0" checks the package description carried since 0.9.0 are done: the four `Samples~` compiled clean as copied assemblies, the game's Windows64 development player build finished with zero errors, and a batch-mode clean project importing the package by `file:` path compiled Runtime, Authoring and Editor with zero errors. Conformance_A is green (the Editor row now lists URP, recorded as Amendment A102 in the architecture doc), so a package EditMode run reports zero failures for the first time since July, and any red conformance test is now real. `SamplesCompileConformanceTests` guards sample asmdef references and `using` directives on disk (revert-to-fail proven for both); it does not replace a real sample compile. Getting-started documents importing into another project. Unverified: the owner's A91 player-build check stays his; no Play mode was run.

**A100 (0.53.0), built 2026-09-15, T12 closed under the standing rule.** The Clip Editor has a **Stats** tab (`ClipEditorTab.Stats = 14`, after Ragdoll, before Health) that reads `World.DefaultGameObjectInjectionWorld` four times a second in Play mode and only reads: actors, layers, ragdolling, cutscene players, a LOD 0–3 histogram, events this frame with a 60-poll sparkline, actors with pending events and with windows open (`AnimEventMask` enabled), VAT parts, distinct textures and their runtime memory, and the times of the toolkit group and its Binding/Logic/Presentation/Ragdoll groups; Snapshot copies the same rows as Markdown. Everything lives in `Editor/Stats/` (`ToolkitStatsCollector`, `ToolkitStatsSample`, `SparklineElement`, `StatsSnapshotFormatting`, `StatsPanel`). Probed in T0: a group's `ProfilerRecorder` must be named `"<World.Name> <Type.FullName>"` and then samples without the Profiler window; bucketing 1,000 actors' `AnimLod` costs 0.005 ms, so nothing is sampled below 100,000. Drive on a detached panel in `DOTSTestScene` (which has no toolkit actors, so 12 were created in the Play world): panel 12 = world query 12, toolkit group 0.059 ms, snapshot read back from the clipboard (in the spec's §7), dashes and no exceptions after exiting Play. This completes the roadmap's Phase 2. **Not seen by anyone:** the tab itself (no captures, by instruction), and VAT/ragdoll/cutscene numbers on real actors. **⚠ for the owner (T12):** enter Play and open Stats — which of these numbers do you actually want to watch, and which are noise? Should Snapshot also save a file under `Library/`?

**0.52.1 rename, 2026-09-15.** Sprite Sheets is now **Flipbooks** in the tab, the data (`FlipbookAsset`, `FlipbookFrame`, `SpriteTrack.flipbook` with `[FormerlySerializedAs("sheet")]`, `ClipEditorTab.Flipbooks` = 1) and the code (`Editor/Flipbooks/`, `Flipbook*` classes and tests, `flipbooks.md`); script GUIDs kept. Cutscene Director is now **Cutscenes** (label only). Older paragraphs below keep the old names.

**A101 (0.52.0), built 2026-09-15 unattended, T33 closed under the standing rule.** Every tab of the Clip Editor window is now built from one chrome: a subject bar across the top (Retarget, Capture, VAT Bake, Materials, Cutscene Director, Ragdoll, Health), `toolkit-column` columns with a titled pane header each, one accent-filled primary action per tab, dim hints, and results in a status footer — through four shared elements in `Editor/ClipEditor/Shared/` (`ToolkitChrome`, `ViewportFrameElement`, `CatalogSidebarElement`, `PathPickerRowElement`) rather than per-tab copies. `Conformance_I` (`EditorStyleConformanceTests`) fails on any inline colour/opacity/font/border write in `Editor/` outside a shrink-only 15-file allowlist unless the line ends `// colour from data`. The cutscene timeline draws its ruler and striped ghost lanes when nothing is loaded and fills below the last row; its three dividers are remembered; its transport reads Length · core · Time · Speed · Zoom. The Clip Editor and Texture Packer (the owner's reference tabs) look as before apart from the Texture Packer's Bake taking the accent fill. Before/after captures of all fourteen tabs are in `Library/A101Captures/`. **⚠ for the owner (spec D7, D11, D13):** (1) "key rows visible even when nothing is selected" was read as the Clip Editor's ghost lanes — if you meant one override row per rig part always listed, that is a different amendment; (2) Sprite Sheets' two catalogs became Sheets | Images sidebar modes like the Texture Packer's; (3) Materials gained Rig and Clip Set pickers in its bar. Unverified by hand: Retarget's Space toggle, the cutscene dividers after a hide/show, dragging an image onto Frames from the Images mode.

**A99 (0.51.0), built 2026-09-15, T12 closed under the standing rule.** The Ragdoll tab landed: bodies, box handles, limits and the drop test have one screen - a bodies column over the rig's `ragdollBodies`, a viewport hosting its own `ClipPreviewController` with the box handles and a Drop / Reset transport over the scenery props, and an inspector for the selected body's collider, mass, damping and limits above the rig-wide settings - all following the window's shared Rig, with the rig asset's own inspector reduced to an "Edit in the Ragdoll tab" button (which focuses the tab) and its validation badges. The box-handle drag math moved verbatim out of the Clip Editor's window partial into `RagdollBoxDragSession`, which both viewports share, so the Clip Editor's ragdoll toggle behaves as before. Nothing about limit defaults, the solver or launch changed. Drive on a scratch copy of `NewRig`: 11 bodies · 8 joints, a hinge limit persisted to YAML and undone, Drop then Reset left the rig byte-identical. **Still for the owner's eyes (RG-T4/T7/T10, re-asked here):** (1) the ±45° default hinge limits - right or wrong? (`NewRig`'s Pelvis carries 0); (2) the drop from a mid-animation pose - the pose row is not wired from the window yet, so the drop uses the rest pose; (3) launch feel from an animation in Actor Profiles.

**A97F (0.50.0), built 2026-09-15, T7 closed under the standing rule.** The Retarget tab's Skipped rows now fix in both directions: the row menu gains `Add tag to rig part…`, a second dropdown of the rig's parts that writes the track's tag onto the part you choose as one undo step, turning the row Bound without touching the clip; parts that already wear a tag are offered behind a confirm naming the tag they would lose and its clip-reference count, and the one-wearer rule is enforced in `RetargetRemapEditing.AddTagToRigPart` (`RigAssetUtility.SetTargetTag` never had it). `GenericDropdownMenu` has no submenus on 6000.5, hence the two-stage menu. Drive on scratch copies of `Walk` and `NewRig` with `LeftUpperLeg`'s tag cleared: 15/1 → 16/0 Bound, YAML carried the tag, undo cleared it. **⚠ for the owner:** should parts that already wear another tag be offered at all (today: yes, behind the confirm)?

**A96F (0.49.0), built 2026-09-15, T7 closed under the standing rule.** The Materials tab's Create button became **Create and assign**: `MaterialTemplateUtility.TryAssignToTargetRenderer` opens the rig's Source Prefab with `LoadPrefabContents`, resolves the target's Source Node Path, takes the Renderer on that node only, replaces one material slot and saves the prefab asset - the package's non-undoable rig-structure write, never `AssetDatabase.SaveAssets()`. One slot is replaced outright; several slots mean the slot holding the catalog's selected material when it is on this renderer, else slot 0, named in the result line; when assignment cannot happen the material is still created and the line says why. Drive on scratch copies of `NewRig` and `MaleCitizen.prefab`: BaseHead's renderer took `M_A96FScratchRig_BaseHead`, the real prefab untouched. **⚠ for the owner:** the multi-slot rule (selected material's slot, else slot 0) has no fixture and was not driven.

**A98 (0.48.0), built 2026-09-14, T13 accepted 2026-09-14.** The Capture tab renders a clip (shared Clip Set and Rig), a profile
animation or a cutscene through the preview camera to a PNG sequence (optionally transparent) or a looping GIF, framed with the
orbit camera. Three `ICaptureSource` adapters own their preview controllers and are disposed with `CapturePanel`;
`ICaptureSource.RenderFrame` replaces the spec's `PreviewCamera` (the controller exposes none), and
`ClipPreviewController.RenderCaptureFrame` hides the grid, selection, bone handles and socket markers. `FrameCaptureRunner` takes
one frame per editor update, with Cancel and one refresh at the end; `GifEncoding` is an in-package GIF89a encoder (T0 timing
1661 ms for 30 frames at 512²). Output defaults to `Assets/Generated/DotsAnimationToolkit/Captures/<name>`; stills import
uncompressed without mips. Drive: Walk gave 12 transparent PNGs and a 33,774-byte GIF; a cancel at frame 3 of 60 kept 3 files.
In this project the preview draws `NewRig`'s part quads magenta (the viewport's own `Render` path gives the same image).

**A97 (0.47.0), built 2026-09-14, T11 answered 2026-09-14: reworked as A97F (`0.50.0`, specced).** The Retarget tab shows a clip against a rig as one row per track:
Bound, Skipped (no part wears the tag) or Dangling (the tag left the registry), in `ClipValidation.ValidateTrackBindingInto`'s
order. A row's remap writes that track's tag in this clip with undo, merging onto a track that already carries the tag;
"Remap in every clip…" runs A92's replace. A roster strip shows bound/total on every rig and switches the shared rig, and a
preview poses the clip on the picked rig. Drive: Walk on `NewRig` 16/16; a rig copy missing `UpperLeftLeg`'s tag gave one
Skipped row and roster 15/16; the remap persisted in the scratch clip's YAML and undid.

**A96 (0.46.0), built 2026-09-14, T13 answered 2026-09-14: reworked as A96F (`0.49.0`, specced).** The Materials tab lists every material on the shared rig's source
prefab, the targets each serves, and the shader contract as data (`MaterialContractValidation`): Flipbook Plane needs
`_ImageIndex` or `_AtlasFrame`, VAT Mesh `_VatFrameA/B` and `_VatBlend`, every kind GPU instancing; plus a sheet check. There
is no example `.shader`: Create makes a material from the kind's shader graph beside the prefab and does not assign it. The
entity baker runs the contract on VAT Mesh parts once the VAT slot exists. Drive: `NewRig`'s prefab carries 30 materials (16
mapped, 14 on unmapped nodes); Create wrote a `ToolkitSpriteUnlit` material with instancing on.

**Batch totals (2026-09-14, integration `e7ae55f9`):** EditMode 857 (Conformance_A the standing failure), PlayMode 285. The
same day the owner accepted A93F T10, A94F T12 and A95F T10 ("these look good for now"). Answers the same day: A98 T13 accepted; A96 T13 and A97
T11 "yes", reworked as A96F (`0.49.0`) and A97F (`0.50.0`). Next: A96F, A97F and A99 (`0.51.0`) in parallel, then A100 (`0.52.0`)
(`Assets/_Vault/Spencer/next-session-parallel-a96f-a97f-a99-prompt.md`).

**A95F (0.45.0), built 2026-09-14, T10 accepted 2026-09-14.** Sprite Sheets is a names layer over the project's existing
Texture2DArrays. Every array appears in the catalog; opening one shows GPU-copied layer thumbnails with frames numbered
0…n-1, and renaming then Save writes `<Array>_Sheet.asset` beside it. The Clip Editor's Sheet field takes an array
directly and reuses that sheet. Baking separate images now composes a grid PNG imported as a Texture2DArray with a
chosen reference array's importer settings (or the project arrays' shared defaults), replacing A95's uncompressed
`.asset`; re-bakes keep the GUID. The catalog column gained a per-row rename/delete predicate. Drive: a baked grid of
opaque swatches imports as DXT1 where `EyeArray` is DXT5, because AutomaticCompressed follows the source's alpha.

**A94F (0.44.0), built 2026-09-14, T12 accepted 2026-09-14.** Health is two panels: a big Scan project button with last-scan
status, severity chips that count and filter, a findings list of two-line rows, and a detail panel with the
explanation, every affected asset and one button per fix. H01, H05 and H06 gain Delete behind a confirmation that
names the path and what still references it (VAT deletes take only part files no other set uses). H11 (profile bind
validation, V08/V36/V41 skipped) and H12 (shared clip binding) replace the Clip Editor's toolbar badge, which is gone;
the tab reads "Health (n)" in red from window open, and all eight former badge refresh sites request a debounced
rescan. Deletes were exercised on scratch copies only.

**A93F (0.43.0), built 2026-09-14, T10 accepted 2026-09-14.** Event routing is removed from the package (asset, authoring and
baker, blob, API, asset utility, stub generator, Routes column and three fixtures), with no migration because no
routing data existed. The Events tab's right column is Used by: boxed Clips, Cutscenes and Profiles groups whose rows
ping on click and open the owner through `EventsPanel.OpenOwnerRequested`, which the window routes to
`FocusClip`, `FocusCutsceneTab` and `FocusWithActorEditorTab`. Runtime delivery stays on the per-actor
`AnimEventOutput` buffer; `AnimEventBufferApi` (`ContainsEvent`, `TryFindEvent`, `TryFindNextEvent`) saves consumers
the key-filter loop, and the owner's untracked damage system now reads `AnimEvents.Damage` through it.

**Batch totals (2026-09-14, integration `f67b47e3`):** EditMode 851 (Conformance_A the standing failure), PlayMode
285. Next: A96, A97 and A98 at `0.46.0`–`0.48.0` (`Assets/_Vault/Spencer/next-session-parallel-a96-a98-prompt.md`).

**Owner answers (2026-09-14, later).** A87's follow-up, A88 T9 and A92 T10 are accepted without a look ("assume they
pass unless something is really game breaking"). A94F now also gives the tab a "Health (n)" error count and removes the
Clip Editor's error badge, with rules H11/H12 covering its checks. A95F's baked sheets become grid PNGs imported with a
reference array's settings (the project's arrays look right that way; other methods look bad). The game's unused
`TextureArrayBuilder.cs`/`TextureArrayConfig.cs` were retired.

**Owner answers (2026-09-14) to A93 T16, A94 T14 and A95 T15, reworks specced, not built.** Events: routing is removed
from the package and the right column becomes the usage list; no event entities (runtime events stay on the
`AnimEventOutput` buffer, plus an `AnimEventBufferApi` helper); the owner's generated damage stub is converted, not
deleted (`A93F_EventsTabRework_Spec.md`, `0.43.0`). Health: a big Scan button, a findings list beside a detail panel
with fix actions, Delete behind confirmations for H01, H05 and stale H06 textures (`A94F_HealthTwoPanel_Spec.md`,
`0.44.0`). Sprite Sheets: every project `Texture2DArray` listed, frames named by number until renamed, thumbnails by
GPU `CopyTexture` (`A95F_SheetsFromArrays_Spec.md`, `0.45.0`). Batch prompt:
`Assets/_Vault/Spencer/next-session-parallel-a93f-a95f-prompt.md`; A96–A98 follow at `0.46.0`–`0.48.0`.

**Built (2026-09-14): Amendment A95 — Sprite Sheets tab — 0.42.0**, **T15 owner checkpoint open.** Spec:
`Assets/_Vault/Tasks/AnimationPackage/A95_SpriteSheetsTab_Spec.md` (§7: drifts, integration, drive). Built in a
parallel worktree batch with A93 and A94, integrated in `ffdc6754`. The tab (`Editor/SpriteSheets/`) lists
`SpriteSheetAsset`s over the Images catalog; Frames is a reorderable list whose order is the layer order; the
contact sheet shows every layer. Bake (`SpriteSheetBaker`) stacks same-size frames into one uncompressed RGBA32
`Texture2DArray`, decoding sources without Read/Write, and re-bakes over an existing array in place
(`CopySerialized`, same GUID). The sheet is edited as a working copy and written only by Save. The Clip
Editor's sprite inspector binds a sheet per track (`SpriteTrack.sheet`, authoring-only, never baked) and picks
Frame and Base Frame by name. No atlas builder (owner call). Drive-proven on scratch: bake, reload, byte-equal
layers, a reordered re-bake, and name picks stored as `sliceIndex` on disk for Absolute and RelativeToBase keys.
Not run: the Play-mode material step and any capture (batch cautions). No double-click opener.

**Built (2026-09-14): Amendment A94 — Health tab — 0.41.0**, **T14 owner checkpoint open.** Spec:
`Assets/_Vault/Tasks/AnimationPackage/A94_HealthTab_Spec.md`. One project-wide findings list: ten rules H01–H10
in `Editor/Health/HealthRules/` (six `…Validation` classes) run by `HealthScan` (plain noun, allowlisted), stale
or unbaked VAT bakes pinned above everything. Rows ping their asset; H02 Remove missing
(`ClipAssetUtility.RemoveClipFromSet`), H06 Rebake (the Clip Sets tab's jump, `OnClipSetRebakeRequested`) and
H09 Save (`SaveAssetIfDirty` on that asset) are the one-click fixes. The panel rescans 500 ms after the new
`AssetReferenceIndex.Dirtied` (`Rebuilt` never fires on an import alone). `VatSourceHashResolver.FindRigByStableId`
replaces the Clip Sets tab's private rig lookup. This project scans to 3 findings: H06 `VatSampleTentacleClips`
unbaked, H02 `NewClipSet` lists 3 missing clips, H05 `VatSampleTentacleRig` unused. H09's Save was proven
through a reload on a scratch clip; Remove missing and Rebake were not clicked.

**Built (2026-09-14): Amendment A93 — Events tab — 0.40.0**, **T16 owner checkpoint open.** Spec:
`Assets/_Vault/Tasks/AnimationPackage/A93_EventsTab_Spec.md`. Keys (`EventKeyCatalogColumn`) is the project
event registry with its 64-key maskable budget, New (`CreateVocabularyEntry`), Rename, Delete, Generate
Constants and Merge into…; the middle column edits the entry, payload schema and preview clip and lists usage
from the Asset Reference Index; Routes edits the key's rows in the project `AnimEventRoutingAsset`, created on
the first route at `Assets/Generated/DotsAnimationToolkit/AnimEventRouting.asset` (moved from the spec's
`Assets/Settings/…` by Conformance_D at integration). `AnimEventRoutingAuthoring` bakes it to the
`AnimEventRouting` singleton (`AnimEventRoutingBlob`, routes sorted by key, kind and route id); hosts read it
with `AnimEventRoutingApi.TryGetRoutes(ref blob, …)`, `ref` and never `in`. Generate consumer stub… writes a
host `ISystem`. The package never handles a route. Drive-proven on a registry copy and scratch assets (mint,
routes on disk, blob build, stub compiled as an `ISystem`); the Play-mode singleton read was not run. Batch
suites: EditMode 850 (standing Conformance_A only), PlayMode 285.

**Built (2026-09-14): Amendment A92 — project-wide refactor operations — 0.39.0**, **T10 owner checkpoint
open.** Spec: `Assets/_Vault/Tasks/AnimationPackage/A92_RefactorOperations_Spec.md`; its §7 logs thirteen T0
drifts and the close. `RefactorEditing` re-keys an event (clip markers, cutscene markers, ragdoll triggers that
have a trigger), merges one event into another (re-key, then remove the old registry entry), and moves clip
transform/sprite tracks and cutscene part tracks from one tag to another (rig targets never retagged). Each is
one collapsed undo group, saves only the touched assets with `SaveAssetIfDirty`, and is previewed from the
Asset Reference Index. `RefactorPromptEditing` is the shared pick → `DisplayDialog` → run flow behind four
entry points: event pin right-click "Change key everywhere…" (clip and cutscene timelines), event registry row
"Merge into…", tag registry row "Replace in clips with…" (both project instance only), and the Rigs tab Tag
button right-click "Move clip tracks to another tag…". Owner calls 2026-09-14: cutscene part tracks included;
a merge leaves payloads raw and warns. Suites: EditMode 840 (838 + 2, standing Conformance_A only), PlayMode
285. Operations drive-proven on scratch assets (apply, disk, one undo); the UI entry points were not driven
(modal dialog, docked window) and not captured.

**Built (2026-09-13): Amendment A88 — layered event preview in the Actor Editor — 0.38.0**, **T9 owner
checkpoint open.** Spec: `Assets/_Vault/Tasks/AnimationPackage/A88_LayeredEventPreview_Spec.md`; its §7
logs thirteen T0 drifts. A collapsible **Layer Events** strip (`LayerEventStripElement`) sits under the
Preview column's transport: one 22 px row per layer with the playing clip's pins, a playhead and
`t / duration`, a stopped layer dimmed to 35%, a crossfade source's pins hollow; crossings flash the pin
(120 ms, 3 px) and play the A87 preview sound. The rule lives in `LayerEventRowResolver`: a layer emits
when `Active` **or** `FinishedThisFrame` (the spec said Active only), the ghost keys on the `Blending`
flag, and time goes through `ClipSampler.ResolveLoopMode` + `MapTimeNormalized`. `ActorPreviewComposer`
gained `LayerCount` and `Layer(int)`. The panel ticks the strip from both `Tick()` and `Step(int)`.
Suites: EditMode 838 (835 + 3, standing Conformance_A only), PlayMode 285. Not driven in the docked window
and not captured; mount proven on a detached panel. On MaleCitizen only `MeleeContinuous` carries a marker
(`Attack`, no preview clip), and there is no Override animation to play.

**Built (2026-09-13): Amendment A90 — missing camera data warning and the Camera Sync sample — 0.37.0**,
**T7 answered 2026-09-13: accepted** (the sample stays singleton-only). Spec: `Assets/_Vault/Tasks/AnimationPackage/A90_CameraDataWarning_Spec.md`;
its §7 logs ten T0 drifts.
- **`CameraDataMissingWarningSystem`** (top group, no declared order, not Burst). It warns once per world
  when billboard roots, or `AnimLod` actors with `distanceLodEnabled`, have waited 120 frames for
  `AnimationToolkitCameraData`, then disables itself. It disables silently once the singleton exists.
- **Message:** "DOTS Animation Toolkit: no AnimationToolkitCameraData singleton after 120 frames, so
  billboarded rigs are not turning to face the camera. Write the singleton from your camera every frame,
  or import the Camera Sync sample and add ToolkitCameraSync to a scene object." The clause after "so"
  names distance LOD instead ("distance LOD is not running, so AnimLod levels stay where they are"), or both.
- **Sample:** `Samples~/CameraSync` (`ToolkitCameraSync`), compile-checked through a scratch assembly.
- **Drift worth knowing:** without the singleton, rig billboards do not turn at all; spherical is the
  zero-`forward` case. The shader path reads `_ToolkitCameraForward`, which nothing in the project writes.
- **Suites:** EditMode 835 (standing `Conformance_A` only), PlayMode 285 (283 + 2). No Play-mode drive,
  on the owner's instruction. `CutsceneA64Checkpoint.unity` places a toolkit actor with no bridge, so it
  may start warning.

**Built (2026-09-13): Amendment A91 — profile animation name errors (P2) at save and at player build —
0.36.0** (A88 and A90 are unbuilt; A91 took the next free minor). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A91_ProfileP2AtBuild_Spec.md`; its §7 logs nine T0 drifts.
- **One scan, `ProfileP2Scan`** (`ScanProfile`, `ScanProject`, `FormatForConsole`). A null registry
  resolves to the project one and is never passed through, because `Validate` treats null as "skip
  the membership check". A94's H03 calls it.
- **Save:** `ActorProfileSaveValidation` (`OnWillSaveAssets`) logs one warning per P2 finding and
  never blocks. It fires on `SaveAssetIfDirty`, not on `CreateAsset`.
- **Build:** `ActorProfileBuildValidation` (`IPreprocessBuildWithReport`) throws
  `BuildFailedException` listing every finding. The per-machine EditorPrefs toggle sits on the
  Animation Names settings page and defaults to on.
- **Drift worth knowing:** the bake already failed a zero `animationKey`; only registry membership
  was missing. The builder comment and `actor-profiles.md` said otherwise and are corrected.
- **Suites:** EditMode 835 (833 + 2, standing `Conformance_A` only), PlayMode 283. The build hook
  was proven one level down, with no real player build, on the owner's instruction.
- **T7 answered 2026-09-13, accepted.** The save warning stays; it fires only while a profile has
  P2 findings. The toggle stays per machine. A real player build was attempted 2026-09-13
  but stopped on unrelated game compile errors (`StitchPunk.Editor` builds into the player), so the
  hook is still proven one level down only. Accepted as working for now; check tracked in
  `Assets/_Vault/Spencer/verify-a91-player-build.md`.

**Built (2026-09-13): Amendment A89 — stale VAT bake detection — 0.35.0** (A88 is not built and
takes the next free minor). Spec: `Assets/_Vault/Tasks/AnimationPackage/A89_StaleVatBakeDetection_Spec.md`.
Its §7 logs five drifts and the design settled at T0.
- **One source hash, computed only in `VatSourceHashResolver`.** It folds every VAT-bound clip
  plus the rig's structure. `VatTextureSetBuilder.WriteSet` stamps it set-wide, where before it
  stored only part 0's hash, together with the new `sourceRigStructureHash`, so a stale set can
  say "rig changed" or "clips changed".
- **Unchanged or newly consumed:** `sourceRigKey` stays identity, so V40 is untouched. V08 was
  dormant and now receives the recomputed hash in `ValidationBadgeElement`.
- **UI:** `VatFreshnessBadgeElement` (Fresh / Stale / Unbaked) sits beside the VAT Bake tab's
  resolved-parts line and on a new VAT Textures row in the Clip Sets tab.
- **When it refreshes:** on selection, after a bake, and through `VatSourceImportWatcher`, an
  `AssetPostprocessor` relay. The drive found that `EditorApplication.projectChanged` does not
  fire when an existing asset is saved.
- **One-time staleness:** every set baked before 0.35.0 reads Stale once.
- **Suites:** EditMode 833 (831 + 2, standing `Conformance_A` only), PlayMode 283.
- **Drive:** a floating `VatBakeWindow` plus a detached `ClipSetsPanel`. A SaveAssets guard kept
  the owner's dirty assets unwritten.
  - Results: bake → Fresh; a Fin-only edit → Stale "clips changed" and V08; flip a Kind → Stale
    "rig changed"; rebake → Fresh; add a clip → Stale.
  - The VAT Bake tab was captured in both states. The Clip Sets tab was not, because it lives
    only in the owner's docked window.
- **T8 answered the same day. Its follow-ups are built at the same version:**
  - A **Rebake** button on the Clip Sets VAT Textures row. It jumps to VAT Bake with the set and its
    baked rig selected, and does not bake.
  - The VAT Bake resolved-parts line refreshes on import. It skips on an unchanged key, and the
    preview is rebuilt only when the subject renderer changes.
  - V08 is obvious: its row sorts first, the summary is prefixed "VAT stale · ", the text names set,
    rig and reason, and the Actor Editor badge feeds V08 too.
  - A94's spec gained D8: H06 is an Error for stale and unbaked, pinned above every finding, with
    Rebake and Locate.
  - EditMode 833 (standing `Conformance_A` only). The drive log is in spec §7.

**Built (2026-09-13): Amendment A87 — scrub crossings and sound on scrub — 0.34.0.** Spec
`Assets/_Vault/Tasks/AnimationPackage/A87_ScrubEventCrossings_Spec.md`. Its §7 carries the D3 audio
probe and the drifts: the playhead write lives in `ClipEditorWindow.SetPlayheadTime`, not the pane;
`FlashPin` is lane-local; wraps are inferred from the delta, so reverse play works; `Play` has no
volume parameter; no Conformance_G entry was needed; and D6 (cutscenes) is deferred because it
needs three files. `ClipEditorWindow.SetPlayheadTime` calls `TimelinePane.ReportPlayheadMoved`,
which resolves crossings with `ScrubEventCrossingResolver`, flashes the pin (`TrackLaneElement.FlashPin`,
own-colour 3 px outline for 120 ms) and plays `AnimEventKeyRegistry.FindPreviewClip` through
`EditorEventPreviewPlayer` (`AudioUtil.PlayPreviewClip` by reflection; one voice, same clip at most
once per 40 ms). A clip switch and a key drag fire nothing; a paused jump over half the clip is a
seek. `AnimEventKeyEntry.previewClip` is edited under each Event Names row's Payload foldout.
Suites: EditMode 831 (829 + 2, standing `Conformance_A` failure only), PlayMode 283. The drive
ran one level down and never touched the owner's live window. It proved the flash and the preview
start on a scrub, one firing per frame step, one per loop in both play directions, nothing on seek,
drag or clip switch, and the JSON write to disk. The registry file was restored afterwards. Sound
itself is not verifiable from a session. **T10 answered 2026-09-13, accepted:** sound plays; D1 kept (a seek fires nothing); D5 kept, with one fix — a selected pin now flashes too (selection used to hide it; CHANGELOG `0.37.0` Fixed).

**Built (2026-09-13): Amendment A86 — one event editing surface for clips and cutscenes — 0.33.0.**
Spec `Assets/_Vault/Tasks/AnimationPackage/A86_UnifiedEventEditing_Spec.md`; its §7 carries seven
spec-vs-code drifts (the cutscene inspector lives in `CutsceneEditorPanel`, there was no cutscene
validator, the pulse-only-window rule was already V20, the cutscene lane is USS elements, D5 had no
task), the build log and the drive. `EventMarkerInspectorElement` edits both marker types through
`IEventMarkerAccessor` (`ClipEventMarkerAccessor`, `CutsceneEventMarkerAccessor`); a host
`ICutsceneEventInspectorProvider` still owns the payload for its keys via `PayloadOverride`.
`EventLaneStyle` draws the pin on both lanes (a holding cutscene event is a Holding-coloured
outline); `EventMarkerContextMenu` is the right-click menu on both. `AnimEventValidation` owns
V09/V19/V20 and the new V41 (key not in the registry, only when a registry is passed) and V42 (int
outside the value names); `ClipValidation` delegates, both inspectors show the marker's findings.
Suites: EditMode 829 (827 + 2, standing `Conformance_A` failure only), PlayMode 283. Drive proved
field edits, the schema dropdown, copy/paste payload and disk persistence on scratch copies, and
found one bug (a literal "V09 ·" prefix on every finding, fixed); no capture of the live window.
`G1CheckpointCutscene.asset` stores reserved event key 1 and now shows V09. Owner follow-up the same day:
cutscene events are added through the event picker (**+** on the Events group row, right-click, or
double-click), split into one row per event name, and every Director row takes the Clip Editor's
lane look (EditMode 829 again). **T10 answered 2026-09-13: accepted ("events are unified").**

**Built (2026-09-13): Amendment A85 — event payload schema — 0.32.0.** Spec
`Assets/_Vault/Tasks/AnimationPackage/A85_EventPayloadSchema_Spec.md`; its §7 carries the drifts,
two ⚠ interpretations and the drive log. `AnimEventKeyEntry` gained `intParamLabel`,
`intParamValueNames`, `floatParamLabel`, `floatParamUnit`, edited under a "Payload" foldout per row
in `AnimEventKeyRegistryEditor` (the Quick Edit window hosts that editor, so it inherits the
foldout). `EventPayloadFieldBuilder` (`Editor/ClipEditor/Components/`) renders the Clip Editor's
payload fields from the schema: a dropdown over value names, a labelled field, or hidden; a key
with no schema keeps the raw fields. `ConstantsGenerator` writes the schema into each constant's
XML doc and a nested `<Key>Values` class, fed by two optional closures on
`VocabularyConstantsSection`. Suites: EditMode 827 (826 + 1, standing `Conformance_A` failure
only), PlayMode 283. Drive proved persistence through a domain reload and the generated
`AnimEvents.SoundValues`; the live window was not driven and nothing captured (Editor unfocused).
**For A86:** the cutscene event inspector still binds raw fields (`CutsceneEditorPanel.cs`
`AddBoundField(eventProperty, "intParam", …)`) and should call the same builder. **T10 owner checkpoint answered 2026-09-13: kept as built
(Payload foldout stays collapsed; both interpretations confirmed). A85 closed.**

**Built (2026-09-12): Amendment A84 — asset reference index, "where is this used" — 0.31.0.**
Spec `Assets/_Vault/Tasks/AnimationPackage/A84_AssetReferenceIndex_Spec.md`; its §7 carries the D1
measurement, the six spec-vs-asset drifts and the drive log. `AssetReferenceIndex`
(`Editor/ClipUtilities/`) scans every toolkit asset with one combined `FindAssets` (65 ms; six
separate calls cost 250+ ms), is dirtied by an `AssetPostprocessor` on any `.asset` change, and
answers seven `ReferencesTo…` queries plus `CountTracksBoundToTarget` and `SummarizeForDialog`.
`TrackTargetMatchResolver.TrackBindsTarget` is the one tag-first binding rule; the hierarchy
pane's "animated" bold and `RigTargetReferenceResolver` both go through it. The Rig, Clip Set and
Actor Profile delete dialogs open with "Referenced by N …" and up to ten names. Suites: EditMode
826 (824 + 2, standing `Conformance_A` failure only), PlayMode 283. Drive proved the three
behaviours against scratch copies; no capture (Editor unfocused). **T11 owner checkpoint answered
2026-09-13: delete-dialog wording and the ten-name cap kept as built. A84 closed.**

**Built (2026-09-12): Amendment A83 — the Clip Editor window is four panes — 0.30.0.** Spec
`Assets/_Vault/Tasks/AnimationPackage/A83_WindowDecomposition_Spec.md`; its §7 carries the range
map, the per-extraction logs and every decision (D8–D10). `ClipEditorWindow.cs` went from 9,365
lines to 4,268; the clip list, hierarchy, inspector and timeline are elements under
`Editor/ClipEditor/Panes/`, bound to `ActiveAssetSelection` and the new `ClipEditorSession`, built
and wired in `OnEnable` and rooted in `CreateGUI`. No UXML, rename or behaviour change; methods
kept their names, and a scripted check compared every moved body to the original. Suites equal to
the baseline (EditMode 824 with the pre-existing `Conformance_A` failure, PlayMode 283). The owner
chose the orchestrator-slices execution model after the first worker measurement; no captures
exist (Editor unfocused), so the T8 owner checkpoint — open the Clip Editor, pick a clip,
scrub, add an event, add a key, drag a hierarchy row — was the acceptance; **answered "everything
works" 2026-09-12, A83 closed.** D7's 2,500-line target
was not reached; the viewport/gizmo block is the next lift, for a later amendment.

**Built (2026-09-11): Amendment A82 — one catalog column, remembered dividers — 0.29.0.** Spec
`Assets/_Vault/Tasks/AnimationPackage/A82_SharedCatalogColumn_Spec.md`; its §7 carries the build
log. `ToolkitCatalogColumn<TAsset>` and `CoverPaneSplitView` (both `Editor/ClipEditor/Shared/`)
now sit behind the Rigs, Clip Sets, Actor Profiles and Recipes catalogs and all eight cover-pane
splits (VAT Bake gained one) (`DotsAnimationToolkit.Split.<tab>.<pane>` in `EditorPrefs`); the three named catalog
classes are thin subclasses with unchanged public surfaces, `ClipSetsPanel` lost its inline copy,
and the Images sidebar deliberately stays on its own element (its rows are not assets). Live probe
on 6000.5 corrected the vault: the public `fixedPaneInitialDimension` setter DOES repair a
collapsed split; writing the pane's style width alone leaves the drag line behind. Wave 1 is
gated and its fixture proven to fail; **wave 2 landed while the Editor was closed and has had
static review only** — run the compile gate, the four catalog/layout fixtures and the T10
hide/show drive before the T11 owner checkpoint. Three of four wave-2 workers were killed by a
Sonnet rate limit mid-task; their partial output was verified and completed rather than resumed.

**Built (2026-09-10): Amendment A81 — Texture Packer tab — 0.28.0.** Spec
`Docs/AnimationToolkit/Amendment_A81_TexturePacker_Spec.md`; its §7 carries the build log. The
game's channel packer is now the DOTS Animator's **first tab** (`ClipEditorTab.TexturePacker = 0`,
every other value shifted up): a `TwoPaneSplitView` of a segmented `Images | Recipes` sidebar (280
px, boxed rows with 48 px thumbnails, search, drag-out via `DragAndDrop.StartDrag`, double-click
adds at the visible centre, ✓ + an eye toggle for rows already on the canvas; the recipe catalog
with New-by-name, click-to-load, right-click Rename/Delete and the **only** Save) over the moved
node graph (`Editor/TexturePacker/`, twelve files; `TexturePackRecipeAssetUtility` in
`ClipUtilities/`). New over the game-side tool: drop on an output channel row auto-wires the
source's R, drop on a source node swaps its image with wires intact, R G B A chips preview one
channel per source, a Presets ▾ menu with Match Largest Source, and — owner directive — a bake
never writes a recipe; the header shows ` ●` while the canvas is unsaved and switching away asks.
`Assets/_Scripts/Editor/TexturePacker/` is `git rm`'d; no recipe asset ever existed, so nothing
migrated. Built by fourteen parallel `worker` subagents (peak 90k tokens, none capped) after the
orchestrator drove the old window once and wrote `PackRequest.cs`; one wave, one gate (one missing
`using`), then three orchestrator fixes found by the drive (sidebar header grouping, a trashed
recipe marks the canvas unsaved, the rename refreshes the header label). Gated **EditMode 823/823**
(820 + 3 `TexturePackMathTests`, invert proven revert-to-fail; only the standing `Conformance_A`
drift) and **PlayMode 283/283**. Driven for real against generated 64² greyscale PNGs in
`Assets/A81Scratch`: 75 sidebar rows for 75 `Assets/`-rooted textures, sidebar activation → node +
✓ + hidden by the eye toggle, two drops into G → exactly one edge, bake → `RGB24`, sRGB off,
uncompressed, pixel `(0, 200, 0)`, same GUID after a re-bake; a recipe created, loaded, wired and
**baked without the on-disk recipe changing**, then Save wrote it (1 wired, path set, ` ●` gone),
rename kept the graph, `AssetDatabase.OpenAsset` landed on the tab with it loaded, trash left the
canvas; a G chip produced a 64² greyscale preview and restored the source on release; replace kept
the R wire and recorded the new GUID, a duplicate was refused with the prefixed warning. **No
capture** — `EditorApplication.isFocused` was false throughout (the owner was elsewhere), so the
tab has not been seen by anyone; that and the ⚠ D6 header look are the owner's checkpoint.
**Observation for him:** the sidebar's `toolkit-pane-header` wraps its actions onto a second row at
280 px (the mode toggles plus three Recipes buttons do not fit on one), so the header is two rows
tall in Recipes mode.

**Built (2026-09-09): Amendment A80 — one clip set, one rig, every tab — 0.27.0.** Spec
`Docs/AnimationToolkit/Amendment_A80_SharedAssetSelection_Spec.md`; its §7 carries the build log.
The top bar is tabs and the validation badge; the Clip Set and Rig fields sit under the Clips and
Rig Hierarchy pane headers (same element names). One `ActiveAssetSelection` per window is written
by every tab — a catalog click on Clip Sets or Rigs is the pick (reversing A76-D3 on the owner's
instruction), VAT Bake's two fields are live pickers with the "change them in the top bar" hint
gone, the standalone VAT window owns a selection of its own, and Actor Profiles is four resizable
columns: the two shared fields over a searchable profiles catalog (New/Refresh, right-click
Rename/Delete via `ActorProfileAssetUtility`), Layers, Preview, Actor Inspector — the header
Profile field is gone. Built by fifteen parallel `worker` subagents (peak 79k tokens, none capped)
then two, gated once per wave; the orchestrator did `VatBakeWindow`, the scaffolding removal and
the string sweep. Gated **EditMode 820/820** (814 + 6; only the standing `Conformance_A` drift)
and **PlayMode 283/283**. Driven for real against `Assets/A80Scratch` copies: a Rigs-tab click and
a Clip Sets-tab click both landed in the Clip Editor's pane fields (39 hierarchy rows, 13 clips);
nulling the rig on VAT Bake emptied the hierarchy with the new "Pick a rig above the hierarchy."
hint; New on Actor Profiles wrote a profile whose **reloaded** asset carried Base first and
Override last; picking a profile set the shared rig in every field; `TrashProfile` removed it from
the catalog; a reflected `RestoreView` put both values into all five surfaces. Captures in
`Library/A80Captures/` (three tabs, looked at). **Three ⏸ interpretations are the owner's to judge**
(spec §2 ⚠, T13): both shared fields at the top of the Profiles column; VAT Bake's fields kept and
made live rather than removed; New on either catalog makes the empty asset active everywhere.
One observation for him: after a tab hide/show the Profiles column settles at its 200px floor
rather than the 260px it opens at — the same settle-to-floor A75/A76 recorded, not new.
**Next-build candidates** (2026-09-09 audit, five ranked ideas):
`Docs/AnimationToolkit/Handoff_NextFive_2026-09-09.md`.

**Built (2026-09-08): Amendment A78 — the rig says what to bake — 0.26.0.** Spec
`Docs/AnimationToolkit/Amendment_A78_VatBakeSourceFromRig_Spec.md`; its §8 carries the full build log.
The VAT Bake tab's Skinned Mesh field is gone: `VatBakeSourceResolver` reads the rig's Source Prefab
and decides the part list, the panel shows what it resolved to on a read-only line that pings the
prefab, and the bake poses one throwaway `Object.Instantiate` copy — so a character no longer has to
be dragged into an open scene and a bake can no longer write a sampled pose into the `.prefab` on
disk. Every VAT part of a rig now bakes in one run into its own texture and runtime mesh;
`VatTextureSetAsset` carries a `parts` list, `TryGetPart` mirrors `TryGetTrackRange`'s
exact-then-untargeted fallback, and `clipRanges` deliberately stays flat and set-level so no runtime
system changed. `Bake`'s 84-line middle split into `VatBakeClipBuilder` (pure, five fixtures) and
`VatTextureSetBuilder`, and the panel came out shorter than it went in. The Rigs tab gained a **Kind**
button per target row (A78-D17) — without it a baked VAT part renders as a motionless clump with no
error, so it is what makes the rest usable on an actor. `VatTextureBinding` keeps its set key and
loses its textures to a new per-part `VatPartTextureBinding`; `ValidateVatMaterial` is now per-part
and names the part. `schemaVersion` is stamped at last (it was `0` on every set ever produced).

Gated **EditMode 814/814** (801 + the 13 new; only the standing `Conformance_A` asmdef drift) and
**PlayMode 283/283**. All five load-bearing new fixtures were revert-to-fail proven in one pass and
restored. Driven for real, not just compiled: the sample tentacle re-baked to **byte-identical
filenames** with `targetId == 0`, `schemaVersion == 1`, a 16×183 texture and 61 frames, and
`git status` confirmed `VatSampleTentacle.prefab` **unmodified** — the A78-D7 assertion; a new
two-part sample baked two distinct textures and runtime meshes with both ranges starting at their own
frame 0; flipping a clip so nothing animated the fin skipped it by name and still baked the other;
an empty clip set wrote **no asset at all**; and a Kind write was proved by reloading the rig from
disk and reading the raw YAML, with `Undo.PerformUndo()` restoring it.

**Three things a later session should not rediscover**, all in the vault note's new "The VAT bake
asks the rig" section: sockets are sampled on the **first** part's call only; the bake instance is
destroyed once after the last `Bake` returns, never per part; and `StopAnimationMode()` does not
revert a pose, which is why the baker snapshots TRS itself. Two escalations are recorded in the
spec's §8 rather than quietly taken — §5.5's consumer list was incomplete (`VatPreviewElement` and
`VatPreviewMaterial` read the deleted fields and had to be migrated mechanically, adding no preview
feature; fold this into A79's reading), and §5.8's create-mode paragraph was stale because A77 had
already removed the Rigs tab's create form. **T8 (⏸ owner checkpoint) is open** — the spec's §6 T8
names exactly what to open, press and look at, and asks for an eye on three things before A79 builds
the preview toggles. Nothing is queued behind it.

**Built (2026-09-08): A77 — create-and-rename on both catalog tabs — 0.25.0.** Owner-driven, no
spec document; the reasoning lives in `Assets/_Vault/Memories/Code/AnimationToolkit.md` under
"Both catalog tabs create, rename and delete in place". Clip Sets' New now creates an empty set and
selects it, the same shape Rigs got in 0.24.0, and its create form is gone. Rig rows gained
right-click Rename and Delete, clip set rows gained Rename beside their Delete, and clip rows in the
picker gained Rename — all through one shared `InlineRenameEditing` control that edits the row title
in place.

Two decisions reversed on the owner's instruction, both previously recorded the other way: **A76-D8
(no delete in the rig catalog)** is now a delete, confirmed and routed to the OS trash rather than a
hard delete, because the reference sweep that would price it still does not exist; and creating an
asset on either tab no longer loads it into the Clip Editor. EditMode 801/801, PlayMode 283/283,
with the standing `Conformance_A` asmdef drift still the only failure. **Nobody has looked at any of
this yet** — the A76 ⏸ owner checkpoint is still open and now covers A77 too.

**Built (2026-09-08): Amendment A76 — Rigs tab — 0.23.0.** Spec
`Docs/AnimationToolkit/Amendment_A76_RigsTab_Spec.md`; §7 carries the full build log. The Rigs tab
is now catalog | targets | preview over two draggable dividers, the left pair starting at 640px of
which `RigCatalogColumn` (a deliberate mirror of A75's catalog, not an extraction of it) takes 280.
Selecting a rig lists every renderer-bearing node in its prefab with that rig's targets ticked;
tick, untick, retag and repointing Source Prefab each write immediately through four new
`RigAssetUtility` methods, undoable. **T8 (⏸ owner checkpoint) is open** — nobody has looked at
this tab yet except through one capture.

Three things a later session should not have to rediscover. `RigTargetDefinition.stableId` is
`internal`, so only `RigAsset.EnsureStableIds()` can mint a target id and it must run *after* the
target is in the list — hence `Undo.RecordObject` + direct mutation rather than the
`SerializedProperty` route clip sets use. A panel that writes on interaction must subscribe to
`Undo.undoRedoPerformed` or Ctrl+Z changes the asset while its ticks stay put; that was found by
looking at the capture, not by a test, and fixed. And undo cannot be verified across an
`AssetDatabase.Refresh()` — the reload makes a working undo look broken. All three are in the vault
note's new "Rigs tab (A76)" section.

`NewRigPanel` is now `RigsPanel` and `ClipEditorTab.NewRig` is `ClipEditorTab.Rigs`; the UXML names
`tab-new-rig` / `new-rig-pane` deliberately did not change, since `ClipEditorLayoutTests` asserts
them and nothing user-visible reads them. Deleting a rig from the catalog is deliberately absent —
a rig is referenced by actor profiles and every clip track, and there is no sweep wide enough to
price that yet. EditMode 794/794, PlayMode 283/283, with the standing `Conformance_A` asmdef drift
still the only failure.

**Built (2026-09-08): Amendment A75 — Clip Sets tab — 0.22.0.** Spec
`Docs/AnimationToolkit/Amendment_A75_ClipSets_Spec.md`. T1–T7 all landed and gated across four
subagent waves (T1/T2/T3, then T4/T6a/T6c, then T5, then T6b) plus the orchestrator's own T7
drive/capture pass; T8 (⏸ owner checkpoint) is open. `ClipAssetUtility` gained
`AddExistingClipToSet`/`RemoveClipFromSet(ClipSetAsset, ClipAsset)`, both routed through the
existing private undo-wrapped cores so a clip added or removed through the new tab is
indistinguishable from one made by hand. `ClipPickerModel` (pure, no `UnityEditor`) backs a new
`ClipPickerListElement` — a searchable, check-boxed `ListView` whose row toggle reads its live
index from `userData` rather than a closure, so a recycled row can't write another row's clip.
`ClipSetSaveLocation` wraps the `EditorPrefs`-remembered save folder, validated against
`AssetDatabase.IsValidFolder` on every read with a project-relative-path boundary check (an
`AssetsBackup` sibling folder must not pass as a subfolder of `Assets`). `ClipSetsPanel` mirrors
`NewRigPanel`'s two-column shape — a 280px catalog of every `ClipSetAsset` in the project, and an
editor column that switches between Create (name, folder picker, live "will create" path hint,
starting ticks) and Edit (ticks apply immediately through the T1 utility, reported to the window
via `SetClipsChanged` rather than the panel ever touching `clipSet` itself, matching the
panel-reports/window-acts split A71/A74 already established). `ClipEditorTab` gained
`ClipSets = 1`, shifting every tab after it up one; the toolbar's "New Set" button and the
window's own `CreateClipSet()` are deleted, not hidden. Driven for real: created a probe set with
one ticked clip, confirmed the write on a reloaded `ClipSetAsset` reference after
`AssetDatabase.Refresh()`, unticked the same clip in Edit mode and confirmed the removal on
another reload, and confirmed the `EditorPrefs` save-folder key updated — every named UI element
(`clip-sets-catalog-column`, `clip-sets-list`, `clip-picker`, etc.) queried with a real non-zero
`resolvedStyle`/`layout`. **The pixel capture step could not be completed**: `GrabPixels` returned
a byte-identical stale frame across repeated `RepaintImmediately`/`RepaintAllViews` attempts, and
`EditorApplication.isFocused` was `false` for the whole session — another application held OS
focus, and Unity appears not to re-render a docked view's actual backbuffer while unfocused
regardless of internal repaint requests. `InternalEditorUtility.ReadScreenPixel` was tried as a
fallback and, exactly as this doc's "capture the window" trap warns, captured the other
application instead. No screenshot exists to hand the owner; **the ⏸ checkpoint below is the
owner's first real look**, not a confirmation of one already taken. Full suites re-gated clean
after fixing a `Conformance_D` violation two subagent-authored test fixtures introduced (arbitrary
`Assets/A`-style test data read as a host asset folder path — renamed or split via string
concatenation, matching the pattern the conformance test uses on its own source): EditMode
784/784 (784 = 777 + 6 new T1–T3 fixtures + 1 new T5 fixture; only the pre-existing
`Conformance_A` asmdef drift), PlayMode 283/283 unchanged.

**Built (2026-09-08): New Rig source preview — 0.21.0.** Owner request, built directly rather than
specced: "if I select a game object for new rig, I would like to see what that prefab looks like
somewhere on the screen most likely to the right and then all the info to fill out is on the left."
`NewRigPanel` took the `VatBakePanel` two-column shape (420px form column, viewport pane), and a new
`RigSourcePreviewElement` renders an inert preview-scene copy of the assigned prefab on the shared
`PreviewOrbitCameraRig`/`PreviewCameraNavigation`/`PreviewSceneGizmos`. The list and the picture are
one choice: a ticked node draws as authored, an unticked one greys to a translucent shell (a rail
toggle hides them outright), and clicking a row boxes that node and retargets `F`. Driven for real
against `Assets/Prefabs/Units/MaleCitizen.prefab` — 34 nodes, copy confirmed in the Preview Scene,
untick/re-tick verified to swap and restore materials, focus box verified active; captures at
`Library/A75Captures/new-rig.png` (before the row-truncation fix) and `new-rig-after.png` (not
committed — Library is generated). **⏸ Owner checkpoint: the visual pass on `new-rig-after.png`.**
Two fixes rode along: neither host disposed `VatBakePanel`, leaking a `PreviewRenderUtility` per
window close, and a long node path pushed the tag button out of its row. Unrelated finding, game
side not package: `MaleCitizen`'s `Faceware` material (`Assets/Materials/UnitsLegacy/Faceware.mat`)
has a missing shader and renders magenta — the preview is reporting it faithfully.

**Built (2026-09-08): Amendment A74 — Preview Viewports — 0.20.0.** Spec
`Docs/AnimationToolkit/Amendment_A74_PreviewViewports_Spec.md`. T1–T10 all landed and gated across
four subagent waves plus the orchestrator's own T10 drive/capture pass; T11 (⏸ owner checkpoint) is
open. The gesture state machine left `ClipEditorWindow.CameraNavigation.cs` for a shared
`PreviewCameraNavigation` over an `IPreviewCameraRig`, with `ClipPreviewController` implementing
that interface directly and the window reduced to a thin adapter keeping its old method names
(`Grep`-confirmed zero stray references to the deleted `CameraGesture` enum/fields outside the new
class). The Actor Editor viewport now carries the Clip Editor's camera (orbit, pan, look + fly,
dolly, zoom, `F`, double-click reset) and a matching rail — Reset Camera, Billboard, Ragdoll, the
last now a manual override beside the composer's own trigger — with no gizmo modes. The VAT Bake
panel gained a second pane: `VatPreviewElement`, its own `PreviewRenderUtility` playing the baked
`runtimeMesh` through the shipped VAT shader, a transport, a clip picker, a frame readout, and a
translucent source ghost (a preview-scene copy, posed independently) so a drift between bake and
source shows as a double image. `VatSampleTentacleUtility` writes a procedural sample rig/clip/set
so the preview has something to show on a project with no VAT content, behind a **Create Sample
Tentacle** button — driving it for real caught a Conformance_D violation (the button's own fallback
folder hardcoded a host asset path) and, separately, a rail-sizing bug found only by capturing the
window (`Reset Camera`/`Ghost` collapsed to ~10×2px — missing `clip-editor__overlay-tool-button` on
the controls themselves, not just their icons); both fixed and re-verified before this note was
written. Full suites re-measured clean at EditMode 777 (was 767, +10 new fixtures; the standing
Conformance_A asmdef drift is still not this amendment's) / PlayMode 283 (unchanged). Captures at
`Library/A74Captures/actor-editor.png` and `vat-bake.png` (not committed — Library is generated).
Independent of A73, which had already shipped 0.19.0 when this landed, so this took 0.20.0.

**Built (2026-09-08): Amendment A73 — Profile-Driven Cutscenes — 0.19.0.** T1–T8 all landed and
gated across two sessions. The slot now carries `profile`/`locomotion`/`layerStops` in place of
`rig`/`clipSets`/`directionSet`; blocks play by animation name against the bound actor's own
`ActorProfile` (schema 6); one Editor row per profile layer replaces the single clip lane, with
**+**/**■** header buttons and an Unresolved row for a block whose key the profile lacks; auto
locomotion plays the Moving/Standing entry from real displacement unless a block already claims that
layer; facing is auto unless keyed (`CutsceneFacingKey.mode`), with an arrival latch, and writes
`ActorFacing.facing` besides `CutsceneFacing`; `CutsceneMarkKey.waitUntilReached` derives a
rendezvous hold with a one-click Marks-row **+** button and a Set From Scene View Pivot button; the
Editor preview reconstructs a `PlaybackLayer` array per scrub through the runtime's own
`ClipSampler.CompositeLayers`. Session 1 (T1–T3) shipped with PlayMode unverified (the game side was
deliberately red); the T4–T8 continuation session first unblocked and fully verified T1–T3 (finding
and fixing three real bugs the first completed full-suite run surfaced — see the spec's §7 build
log), then built T4–T6 (two parallel subagents for T4/T5, one for T6 — each caught a bug of its own
at the compile gate, also in §7) and found T7 needed no code changes at all (the sample host and the
three named tests were already correct; logged as drift, not invented work). Gated:
`DotsAnimationToolkit.Tests.EditMode` 767/767 (only the pre-existing, unrelated `Conformance_A`
asmdef drift) and `.PlayMode` 283/283, both green. **One ⏸ owner checkpoint is open** — spec §5's
final task names exactly what to open, press and look at (`NewCutscene.asset` with `MaleCitizen.profile`
in `DOTSTestScene.unity`); nothing is queued behind it. Game plan **G6**
(`Assets/_Vault/Tasks/NewPlans/CutsceneProfileCutover_System.md`) re-points the game's seven
cutscene assets by script and gates `UnitAnimationAssignmentJob` on `CutsceneActor`, and must follow
in its own session. Spec `Docs/AnimationToolkit/Amendment_A73_ProfileDrivenCutscenes_Spec.md`,
session prompt `Amendment_A73_ProfileDrivenCutscenes_Prompt.md`.

**The Actor Editor roadmap is built (2026-09-07): A70 (0.16.0), A71 (0.17.0) and the game cutover G5
are in, gated green (EditMode 823 / PlayMode 291; the one standing `Conformance_A` asmdef drift
remains). Three ⏸ owner checkpoints are open and nothing is queued behind them:**

- **A71-T10** — open `Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset` (double-click)
  and walk the spec's §5 T10 list; the layout is the owner's to change.
- **G5-P10** — `TestArea.unity`, Play: idle sway, blink, walk, a rotter's punch via
  `DebugZombifyMenu`, a death with the death face + ragdoll, a resurrection; F9 cutscenes still play
  (on the top layer now).
- **RG-T4/T7/T10** (`Assets/_Vault/Tasks/NewPlans/RagdollTuning_System.md`) — judge the eleven
  bodies' hinge limits in the Clip Editor's Ragdoll preview, then the in-game drop and launch feel.
  One toolkit question from the machine sample: a settled ragdoll never flags `Sleeping`.

Index: `Assets/_Vault/Tasks/NewPlans/ActorEditor_Roadmap.md`. Both content recipes live in
`Assets/_Scripts/Editor/ContentAuthoring/` and are re-runnable.

**Built (2026-09-07): Amendment A72, editor visual unification — 0.18.0.** T0–T12 are in
(commits `A72-T1/T2`, `A72-T3`, `A72-T4/T5/T8`, `A72-T6/T7`, `A72-T9/T10/T11`, `A72-T12`).
Gated: 0 compile errors; EditMode 760/760 bar the standing `Conformance_A` asmdef drift,
PlayMode 277/277 (those are the counts the runner reports for the two package assemblies —
earlier entries quoted 823 / 291 from a different tally; the attribute count in `Tests/EditMode`
is 760, so nothing dropped). **One ⏸ owner checkpoint is open: the visual pass.** BEFORE and
AFTER captures of the three tabs are in `Library/A72Captures/before-*.png` / `after-*.png`; the
owner judges cohesion, the boxed layers and whether the eye reads as `defaultActive`. Nothing is
queued behind it. Two spec items landed as documented fallbacks: the Cutscene Editor's `F` centres
on the playhead rather than the earliest selected item (`CutsceneItemAddress` is positional, so
"earliest selected time" needs a per-lane switch nobody has asked for yet), and the layer eye's
test drives `ToggleLayerDefaultActive` directly because an unattached `VisualElement` drops
`SendEvent` (no panel, no dispatcher). Traps found on the way are in
`Assets/_Vault/Memories/Code/AnimationToolkit.md` ("Shared editor chrome").
(`Docs/AnimationToolkit/Amendment_A72_EditorVisualUnification_Spec.md`; index
`Assets/_Vault/Tasks/NewPlans/EditorVisualUnification_Roadmap.md`.)

## 5. Standing owner directives — binding, do not lose

- **Names, never numbers**, in downstream game code and in every editor surface. Game code uses
  generated constants (`TargetTags.Jaw`, `AnimEvents.Footstep`). Sole exception: an unresolvable id
  after a delete. (§4.2.3)
- **"I shouldn't have to manually assign any assets for this."** Both vocabularies auto-create under
  `ProjectSettings/`. No asset creation, no wiring.
- **The tag and event lists are editable from the clip editor.**
- **The tag is a keyed track's one identifier (2026-08-28, A56).** A keyed row with no tag must be
  impossible to create; track creation tags the part automatically. "(no tagged part)" remains a
  legal display state — a clip may name a tag the open rig doesn't wear — but "(untagged)" may not
  come back. Kind prefixes (`T `/`S `) stay gone from row names.
- **New rigs are created fresh — build no migration paths** for old rigs with empty fields.
- **Events are authored loosely; downstream systems read and redirect.** They will eventually drive
  sound, ragdoll triggers, damage, shader alt-views and dialogue. **Do not build downstream
  consumers unless asked.**
- **T2 is lenient (§6.1):** a tag-bound track whose tag is absent from the rig is *skipped with a
  warning*, not an error, so one clip covers a roster of differing rigs. Safe only because of the
  three mitigations — dropdown-only selection, the case-insensitive duplicate guard, and a warning
  naming clip + track + tag + rig that surfaces in the validation badge.
- **T3 stays an error**, reported differently from T2: a tag missing from a *rig* is a roster fact;
  a tag missing from the *registry* is a dangling reference.
- The owner eventually wants to hear sound while scrubbing. Note the Clip Editor's scrub path poses
  through `ClipSampler` and never runs `EventEmissionSystem`/`EventWindowSystem` (ECS, play-time
  only), so that needs its own crossing detection comparing playhead-before against playhead-after.
  ~~**Not on the queue — do not start it.**~~ Lifted on the owner's 2026-09-10 instruction and built
  as Amendment A87 (0.34.0, Clip Editor only; cutscene timeline deferred). Sound *mixing* stays out of
  the package.

## 6. Do not decide these alone

- **The bone-reparent guard.** Hierarchy drag-to-reparent permits dragging skinned bones;
  `RigStructureEditor.ValidateReparent` guards cycles and self-parenting only.
  `worldPositionStays: true` makes the drag look fine, and the corruption surfaces later with no
  error — when the clip plays, or baked into every VAT frame. `ClipPreviewController.IsSkinnedBone(int)`
  is public and makes the guard cheap. Recommendation on file: guard it. The owner asked for
  hierarchy dragging, so excluding bones is their call.
- **Q1 — the Spatial3D twist axis.** `twistLimitDegrees` has no defined axis; D2 provisionally used
  the child's rest-local +Y. Owner said "ignore for now"; it blocks D8 only.

If you hit a spec/reality conflict, **escalate a written amendment** — never quietly edit a doc so
it agrees with your code. That habit sank three earlier gates.

## 7. Known gaps and caveats

- ~~`ClipEditorWindow.CountTracksForTarget` matches by raw `targetId`, so it undercounts tag-bound
  tracks in delete confirmations.~~ Fixed by A84 (0.31.0): `RigHierarchyPane.CountTracksForTarget`
  calls `AssetReferenceIndex.CountTracksBoundToTarget`. `ClipSpriteEditing.CollectTracksForTarget`
  still matches raw ids and still has no production callers (audit 2026-09-15).
- The ragdoll preview derives `restRelativeRotation`/`parentAnchorOffset` from the on-screen pose,
  not the authored rest pose, so toggling it on mid-animation can show a first-frame limit
  correction the runtime would not produce.
- The Unity Physics probe casts along gravity only — a wall a body drifts sideways into is missed.
- `PreviewRigMirror` has no notion of `HierarchyPath`; such addresses do not resolve on a
  pure-cutout preview.
- The ±45° default hinge limits on ragdoll bodies were invented by an agent and never judged by eye.
  Prime suspect if a drop looks wrong.
- Restart the Editor to rotate `Logs/Editor.log` if it has grown huge.
- `Docs/AnimationToolkit/shader-contract.md` and `Documentation~/shader-contract.md` are near-identical
  mirrors that differ only in host-specific paths. They will drift; neither is marked authoritative.
- `facesDirection` is a **mirror point**, and a mirror point flips its whole subtree (owner rule
  2026-09-06). A part under one is tagged `PartMirrorFromAncestor` at bake and skips its own mirror,
  so ticking the flag on a descendant as well is redundant rather than cancelling. The slot
  inspector says so when it finds one. Ticking *nothing* is still the failure that turns nothing,
  and the bake warns about that.

## 8. Not yet judged by eye

Test-clean but visually unverified, because the owner has been away from the PC: the ragdoll drop,
Rig Edit gizmos, the New Rig wizard, the amber event pin shape, and event stacking. Anything
on-screen needs the owner to look — never report a visual result as verified.

## 9. Lessons this package keeps re-teaching

1. **Closure is a property of the code, not of the note saying the code changed.** Verify against
   the shipped tree — never a CHANGELOG, a review doc's closure table, or a previous session's
   summary. Two of three reworks asserted closures the diff did not contain.
2. **Reading the diff is not enough either. Run the thing.** The worst bug found here was invisible
   to three independent static reviewers and took ninety seconds of execution to surface.
3. **Features have repeatedly shipped with tooltips and docs describing behaviour that did not
   work**, passing tests that checked wiring existed rather than that the feature did what it
   claimed. Every toggle that writes a pose must be asked: does it un-write?
4. **A suite that builds all its inputs in memory has no coverage of the serializer** — and the
   serializer is part of the authoring contract. Two shipping-blocking defects appeared the instant
   something built real, saved assets instead of fixtures.
