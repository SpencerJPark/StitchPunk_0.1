# Amendment A98 — Capture tab: PNG sequences and GIFs from the preview camera

> **Status:** ✅ built 2026-09-14 as `0.48.0` in the A96–A98 parallel worktree batch (merged `e07c91dd`, integrated `e7ae55f9`); ⏸ T13 owner checkpoint open. The specced `0.45.0` went to A95F; see §7.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** A74 (shared `PreviewCameraNavigation`, `IPreviewCameraRig`,
> `PreviewCameraPose`), A71 (`ActorPreviewComposer` plays profile animations), the cutscene
> `CutscenePreviewController`.
> **Executor:** one orchestrator; `worker` subagents in **one wave of seven**, each ≤ 2 files.
> **T0 contains a platform probe** (D3) that decides whether the GIF task runs.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A98 — Capture tab** on the DOTS Animation Toolkit package (head
`0.44.0` or later). Spec: `Assets/_Vault/Tasks/AnimationPackage/A98_CaptureTab_Spec.md`. Read it,
the roadmap §3 protocol, then only what §3 here names. **Run T0's probe before the wave.** T0 and
T1 yours; one wave (T2–T8); one gate; T9–T12 yours. Stop at T13.

---

## 1. Goal

Store-page screenshots, devlog shots, regression captures and bug reports all need "render this
animation to files", and today that is a screen recorder pointed at the editor. The preview already
renders any clip, any profile animation and any cutscene through a `PreviewRenderUtility`. After
this amendment a Capture tab renders a chosen source over a frame range at a chosen size and rate to
a PNG sequence (transparent background optional) and, if the platform probe passes, to an animated
GIF, with the camera framed by the same orbit navigation every preview uses.

```
┌ Capture ─────────────────────────────────────────────────────────────────────────────────────────┐
│ Source [Clip ▾] Clip Set [CitizenClips ▾] Clip [Walk ▾]   Rig [MaleCitizen ▾]                     │
│ ┌ Viewport (framed with the orbit camera) ──────────────┐ ┌ Settings ─────────────────────────┐ │
│ │                                                        │ │ Size 512 x 512  [presets ▾]        │ │
│ │                        (actor)                         │ │ FPS 30    Range [0 ─────── 1.0]     │ │
│ │                                                        │ │ Background ○ transparent ● colour  │ │
│ │                                                        │ │ Format ● PNG sequence ○ GIF        │ │
│ └────────────────────────────────────────────────────────┘ │ Out Assets/Captures/Walk/  [Capture]│ │
│                                                            └────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A98-D1 — `ClipEditorTab.Capture`, after Retarget.** Toggle `tab-capture`, text "Capture", pane
  `capture-pane`. Joins the shared selection for Clip Set and Rig; Source and Clip/Animation/
  Cutscene are local.
- **A98-D2 — Three sources through one interface.** `ICaptureSource { float DurationSeconds;
  void PoseAt(float seconds); Camera PreviewCamera; }` with adapters over `ClipPreviewController`
  (clip), `ActorPreviewComposer` + presenter (profile animation, direction chosen in the settings),
  and `CutscenePreviewController` (cutscene). Each adapter owns its controller and is disposed with
  the panel.
- **A98-D3 — GIF is probed, not assumed.** The package has no encoder dependency and must not add
  one. Candidate: a self-contained LZW GIF89a encoder (`GifEncoding`, pure, ~250 lines, global
  palette by median-cut to 256 colours, one frame per capture frame, frame delay from FPS). T0 does
  not probe *availability* (there is nothing to probe) — it **times** a hand-written encode of 30
  frames at 512² via `execute_code` using a throwaway implementation: if it is under 3 s the GIF
  task (T5) runs; if not, GIF ships as "PNG sequence only" with a docs sentence, and T5 is dropped.
  Record either way. ⚠
- **A98-D4 — Rendering goes through `PreviewRenderUtility` to a `RenderTexture` of the chosen
  size**, `ReadPixels` into a `Texture2D` (`RGBA32` for transparent, `RGB24` otherwise),
  `EncodeToPNG`. Transparent background = camera clear colour alpha 0 with the preview's scenery
  hidden. Rendering happens in an `EditorApplication.update`-driven loop, one frame per tick, with
  a progress bar and Cancel — never a blocking loop (a 300-frame capture must not freeze the Editor).
- **A98-D5 — Files are `<Out>/<name>_0001.png` …** and `<Out>/<name>.gif`; the folder is created;
  existing files with the same names are overwritten after one confirm. `AssetDatabase.Refresh`
  once at the end; imported PNGs get `textureCompression = None`, no mipmaps — they are stills, not
  game textures. ⚠ (or leave import defaults — the checkpoint asks).
- **A98-D6 — The camera is the orbit rig**, framed by the user in the viewport with
  `PreviewCameraNavigation`; `CapturePose` is stored per source in `EditorPrefs` so re-captures
  match. Presets: 256², 512², 1024², 1920×1080.
- **A98-D7 — No audio, no overlays, no gizmos**; `PreviewSceneGizmos` are off during capture.

---

## 3. Read first

- `Editor/ClipEditor/Preview/ClipPreviewController.cs` lines 220–240 (`CapturePose`,
  `RestorePose`), 960–1060 (`SamplePose`, `SampleCompositedPose`); grep `PreviewRenderUtility` for
  how it renders and what camera it exposes.
- `Editor/ClipEditor/Preview/IPreviewCameraRig.cs`, `PreviewOrbitCameraRig.cs`,
  `PreviewCameraNavigation.cs` — public surfaces.
- `Editor/ClipEditor/ActorEditor/ActorPreviewComposer.cs` lines 60–150; `IActorPosePresenter.cs`.
- `Editor/ClipEditor/Cutscene/CutscenePreviewController.cs` — grep `public` for the seek call.
- `Editor/VatBaking/VatBakePanel.cs` — the cover-pane preview hosting idiom (same as A97).
- Vault `AnimationToolkit.md` "The viewport camera is an orbit rig with no position of its own".

---

## 4. Design

### 4.1 `Editor/Capture/ICaptureSource.cs` + adapters (T1 interface; T2 adapters).
### 4.2 `Editor/Capture/CaptureSettings.cs` (T1) — plain serializable class (size, fps, range,
background, format, out folder, name) remembered in `EditorPrefs` as JSON.
### 4.3 `Editor/Capture/FrameCaptureRunner.cs` (T3) — the D4 loop: `Start(ICaptureSource,
CaptureSettings, Action<int,int> progress, Action<string> finished)`, `Cancel()`.
### 4.4 `Editor/Capture/PngSequenceWriter.cs` (T4) — file naming, folder creation, import settings.
### 4.5 `Editor/Capture/GifEncoding.cs` (T5, conditional) — `Encode(IReadOnlyList<Color32[]>
frames, int width, int height, int delayCentiseconds) → byte[]`. `Math`-like purity; plain-noun
allowlist.
### 4.6 `Editor/Capture/CaptureViewportElement.cs` (T6), `CapturePanel.cs` (T7).

---

## 5. Tasks

- [x] **T0 — Baseline + probe (orchestrator).** Gate; totals. D3 timing probe. Confirm how the
  preview controller's camera can render to an arbitrary `RenderTexture` (grep `targetTexture`;
  if the `PreviewRenderUtility` owns it, `BeginPreview(rect)` with the capture size and
  `EndPreview` as a `Texture` is the path — record which).
- [x] **T1 — Interface + settings (orchestrator).** §4.1's interface, §4.2. Gate. Commit `A98-T1`.
- [x] **T2 — Three adapters [parallel-safe]** — Files: new `ClipCaptureSource.cs`, new
  `ProfileAnimationCaptureSource.cs`; the cutscene adapter is a **second worker** (T2b) with new
  `CutsceneCaptureSource.cs` only.
- [x] **T3 — Runner [parallel-safe]** — Files: new `FrameCaptureRunner.cs`. Brief carries T0's
  render-path finding.
- [x] **T4 — PNG writer + fixture [parallel-safe]** — Files: new `PngSequenceWriter.cs`, new
  `Tests/EditMode/PngSequenceWriterTests.cs` (`FrameFileName_PadsToFourDigits`: frame 7 →
  `Walk_0007.png`; a pure naming function). Revert-to-fail: drop the padding.
- [x] **T5 — GIF encoder + fixture [parallel-safe, conditional on T0]** — Files: new
  `GifEncoding.cs`, new `Tests/EditMode/GifEncodingTests.cs` (`Encode_TwoFrames_HasHeaderAndTwoImageDescriptors`:
  bytes start `GIF89a`, contain two `0x2C` image-descriptor separators, end `0x3B`). Revert-to-fail:
  write one frame.
- [x] **T6 — Viewport element [parallel-safe]** — Files: new `CaptureViewportElement.cs`.
- [x] **T7 — Panel [parallel-safe]** — Files: new `CapturePanel.cs`.
- [x] **T8 — Docs [parallel-safe]** — Files: new `Documentation~/capture-tab.md`,
  `README.md` (one bullet in the feature list).
- **Gate the wave.** `PngSequenceWriterTests`, `GifEncodingTests` (if T5). Commit `A98-T2..T8`.
- [x] **T9 — Window wiring (orchestrator).** Tab; `index.md`; `CHANGELOG.md` `## [0.45.0]`;
  `package.json`; `Conformance_G`. Gate.
- [x] **T10 — Drive.** Full suites. Capture Walk at 256², 12 fps, full range → N PNGs exist with
  alpha where transparent was chosen (read one pixel off the actor with `execute_code`); GIF opens
  in a browser (SendUserFile it to the owner); Cancel mid-capture leaves the Editor responsive and
  the partial files present. Capture the tab itself.
- [x] **T11 — Vault + HANDOFF.** Vault note "Capture tab (A98)": the render path from T0, the
  D3 timing.
- [x] **T12 — Close.** Roadmap checkbox.
- [ ] **T13 — ⏸ owner checkpoint.** Message: "Capture tab: frame Walk, press Capture, open the
  folder. A GIF is attached [or: GIF was dropped because encoding took N s — see §7]. ⚠ Should
  captured PNGs import uncompressed (as now) or with project defaults?"

---

## 6. Deliberately out of scope

- Video (MP4) — no encoder without a dependency.
- Capturing the game view or play mode.
- Sprite-sheet output from captures (A95 packs; a "send frames to Sprite Sheets" button is a
  natural follow-up once both exist).

## 7. Build log

### Phase 0 (stage, 2026-09-14)

- **Version:** the status line said `0.45.0`; A93F–A95F took `0.43.0`–`0.45.0`, so this spec takes `0.48.0`
  (roadmap rule). CHANGELOG's top section is `## [0.45.0]` at `7d036585`.
- **Baseline at `7d036585`:** compile clean; `DotsAnimationToolkit.Tests.EditMode` 851 run, 850 passed, the one failure
  the standing `Conformance_A` (asmdef reference list); `DotsAnimationToolkit.Tests.PlayMode` 285 run, 285 passed.
- **Registry sha256:** `DotsAnimationToolkitAnimEventKeyRegistry.asset`
  `3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset`
  `dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f`. Drives must leave both unchanged.
- **Stage untracked files:** none. Nothing on the stage names a type this spec removes or renames.
- **Runs as** a spec-lead (opus) with sonnet workers in a Worktree Toolkit batch beside the other two of A96–A98; the
  stage owns window wiring, `index.md`, CHANGELOG, `package.json`, the conformance pin, drives and the close.
- **T0 D3 GIF timing probe (execute_code, CodeDom C# 6, unoptimised):** 30 synthetic 512×512 frames (a gradient with a
  moving shaded disc). The throwaway encoder built a global 256-colour median-cut palette from every 7th pixel of every
  3rd frame, mapped pixels through an RGB555 lookup and LZW-encoded each frame (open-addressing hash, 12-bit codes, clear
  at 4096). Total **1661 ms** (palette 1496 ms, mapping 36 ms, LZW the rest; 620,818 bytes); a replay took **1781 ms**.
  Under the 3 s bar: **T5 (GIF) runs.** The median-cut sort dominates, so a shipped encoder should sample, not sort
  every pixel. A per-pixel-noise worst case was not timed.
- **T0 render path:** `ClipPreviewController` owns a private `PreviewRenderUtility`; `Render(int pixelWidth, int
  pixelHeight)` (line ~1495) poses the scene, then `renderUtility.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none)`,
  `renderUtility.camera.Render()` and `return renderUtility.EndPreview()`. The utility owns the target, so a capture
  calls `Render(captureWidth, captureHeight)`, blits the returned `Texture` (a RenderTexture) into a temporary
  RenderTexture and `ReadPixels` from that. The controller exposes no `Camera`, so `ICaptureSource.PreviewCamera` (D2)
  is drift: the lead decides between a `Texture RenderFrame(int, int)` member and exposing the camera, and logs it.
  `CutsceneViewportElement.Render` uses its own `utilityCamera` with a URP `SingleCameraRequest` into `renderTarget`
  (legacy `targetTexture` + `Render()` fallback); `VatPreviewElement` uses the `BeginPreview` path too.
- **Conformance_D drift:** D5/§1 show `Assets/Captures/Walk/`. Package files may not name `Assets/<Folder>` except
  `Assets/Generated`, so the default output folder is `Assets/Generated/DotsAnimationToolkit/Captures/<name>/`.

### Spec-lead (opus, worktree `spec/a98`, 2026-09-14)

**T0 grounding (grep only, no Unity):**
- **Drift 1 — D2 `Camera PreviewCamera` is replaced by `Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour)`** on `ICaptureSource`. `ClipPreviewController` keeps its `PreviewRenderUtility` private and exposes no camera; each source renders its own frame, and the runner blits the returned texture into a temporary sRGB RenderTexture and `ReadPixels` from that. The interface also carries `CaptureName`, `CameraPoseKey`, `NotReadyReason` (null = ready), `CameraRig` (`IPreviewCameraRig`), `CaptureCameraPose()` / `RestoreCameraPose(in PreviewCameraPose)` and `IDisposable`.
- **Drift 2 — `ClipPreviewController` gained `RenderCaptureFrame(int, int, bool transparentBackground, Color backgroundColour)`**: sets the clear colour (alpha 0 when transparent) and, as the last step before the camera pose in `Render`, deactivates the grid, selection box, bone handles and socket markers (D7), restoring them after the render. One hunk inside `Render` plus the new members after it; the stage should expect a textual neighbour if A96/A97 touch `Render`.
- **Drift 3 — the range is normalised with an exclusive end.** `CaptureSettings.rangeStartNormalized` / `rangeEndNormalized` are fractions of the source duration (they survive a source switch); `FrameCountFor` = `ceil(span * fps)`, so a 1 s Walk at 12 fps is 12 frames and a looping GIF never repeats its first pose. T10's "N PNGs" is 12 for a 1 s clip.
- **Drift 4 — the cutscene source has no fixed duration** (`CutsceneAsset` has none, by design). The adapter copies `CutsceneEditorPanel.ComputeContentEndSeconds` (minimum 1 s). A cutscene capture poses the open scene's bound objects through `CutscenePreviewController.EnterPreview` / `ExitPreview` and renders through a hidden utility camera on a `PreviewOrbitCameraRig` (URP `SingleCameraRequest`, legacy fallback); Transparent clears only the sky.
- **Drift 5 — output folder** `Assets/Generated/DotsAnimationToolkit/Captures/<name>` (Phase 0's Conformance_D note), held as `CaptureSettings.DefaultOutputRoot`; an explicit `outputFolder` wins.
- **Drift 6 — GIF fixture counts descriptors by walking blocks**, not raw `0x2C` bytes (that byte occurs in palettes and LZW data).
- **Drift 7 — `GifEncoding` and `PngSequenceWriter` are static classes without a role suffix** and need the Conformance_G plain-noun allowlist (see For integration).
- **D6 pose storage:** `CaptureSettings.TryLoadCameraPose` / `SaveCameraPose` keyed by `ICaptureSource.CameraPoseKey` (`Clip.<clip guid>.<rig guid>`, `Profile.<profile guid>.<key X8>`, `Cutscene.<guid>`).

**T1** (`A98-T1`): `ICaptureSource`, `CaptureSettings` (EditorPrefs JSON, presets, frame math, GIF delay = max(2, round(100 / fps)) centiseconds), `RenderCaptureFrame`, and API-pinned stubs for every wave file. Gate (`PackagingConformanceTests`): compile clean, 10 of 12 passed; failures the standing Conformance_A and the expected Conformance_G allowlist.

**Wave (T2–T8, `A98-T2..T8`):** nine sonnet workers. The first T2 worker (both adapters) capped at 40 turns having written nothing; two fresh single-file workers finished it with the facts pre-grounded (`SamplePose` takes normalised 0..1 time — the window clamps with `Clamp01`; the skinned source is `rig.sourcePrefab`; `PlayAnimation` starts a blend-in, so the profile source ticks `blendDuration` before scrubbing the layer). Lead fixes: the GIF encoder widened its code size after inserting a code (decoders run one code behind, so frames past 512 codes would corrupt) — now widens after the emit, before the insert, and clears before code 4095; the GIF fixture's block walker stepped 9 bytes past the image separator instead of 10.
- Wave gate 1 (`PngSequenceWriterTests`, `GifEncodingTests`, `PackagingConformanceTests`): compile clean, 11 of 14 passed; `GifEncodingTests` failed on the walker's 9-byte step (fixture bug, encoder bytes verified by reading), plus the standing Conformance_A and the expected Conformance_G.

### For integration

**CHANGELOG `## [0.48.0]`:**
> ### Added
> - **Capture tab** in the Clip Editor window: renders a clip (shared Clip Set and Rig), a profile animation (by name, at a chosen facing) or a cutscene (in its open scene) over a frame range to a PNG sequence (optionally transparent) or a looping GIF, framed with the preview orbit camera. Size presets 256, 512, 1024 square and 1920 x 1080; FPS 1–60; range as a fraction of the source with an exclusive end; output `<folder>/<name>_0001.png` or `<folder>/<name>.gif`, default folder `Assets/Generated/DotsAnimationToolkit/Captures/<name>`; one overwrite confirm; Cancel keeps written PNGs; PNGs import uncompressed without mipmaps. The camera pose is remembered per source.
> - `ICaptureSource`, `ClipCaptureSource`, `ProfileAnimationCaptureSource`, `CutsceneCaptureSource`, `FrameCaptureRunner` (one frame per editor update, usable without the panel), `PngSequenceWriter`, `GifEncoding` (self-contained GIF89a encoder, no dependency), `CaptureSettings`, `CaptureViewportElement`, `CapturePanel`.
> - `ClipPreviewController.RenderCaptureFrame` renders without grid, selection, bone handles or socket markers, cleared to a capture background.
> - Documentation: `capture-tab.md`.

**Conformance_G plain-noun allowlist:** `"GifEncoding"`, `"PngSequenceWriter"`.

**Wiring:** `ClipEditorTab.Capture` after `Retarget`; UXML toggle `tab-capture` (text "Capture"), pane `capture-pane`. `CapturePanel capturePanel = new CapturePanel(); capturePanel.Bind(sharedSelection); capturePane.Add(capturePanel);` and `capturePanel?.Dispose();` in the window's teardown (it owns the source adapters, each a `PreviewRenderUtility` or hidden camera, and cancels a running capture). `index.md` link: `capture-tab.md`. Unity must generate `.meta` files for `Editor/Capture/` (folder plus ten scripts) and the two test scripts on the stage refresh.

**Drive notes:** detached use is `FrameCaptureRunner.Start(new ClipCaptureSource(set, rig, clip), settings, progress, finished)` with `settings.outputFolder = "Assets/A98Scratch/..."`; poll `IsRunning` / `FramesCaptured`; `Cancel()`. The overwrite confirm exists only in `CapturePanel`. Nothing calls `AssetDatabase.SaveAssets`; the writer runs one `AssetDatabase.Refresh` and `SaveAndReimport` per PNG inside `StartAssetEditing`.

**Unverified (static only):** transparent alpha surviving `PreviewRenderUtility` / URP into `EndPreview`; the sRGB temporary target giving viewport-matching bytes; `CaptureViewportElement` reuses `ClipEditorWindow.uss` classes (unstyled when detached); cutscene capture (EnterPreview on the open scene, framing bound renderers); profile blend-in finish; GIF playback in a browser (only the container structure is fixture-covered).

**Vault traps (AnimationToolkit.md "Capture tab (A98)"):** the render path from Phase 0 (a source renders its own frame; copy out of the utility's texture before the next render); `SamplePose`'s parameter is normalised despite the window's `playheadTime` name; `PlayAnimation` starts a blend, so a scrubbed capture must finish the blend first; GIF LZW widens before the insert (giflib order) or large frames corrupt silently; a cutscene capture writes and restores the open scene's bound transforms, so it must not run while the Cutscene tab is previewing the same scene; D3 timing 1661 ms for 30 frames at 512.

**HANDOFF draft:** A98 Capture tab (0.48.0) renders clips, profile animations and cutscenes to PNG sequences or looping GIFs through the preview camera. Three `ICaptureSource` adapters own their preview controllers (disposed with `CapturePanel`); `FrameCaptureRunner` captures one frame per editor update with Cancel and a single refresh; `GifEncoding` is an in-package LZW GIF89a encoder (Phase 0 timing 1661 ms for 30 frames at 512). Default output is under `Assets/Generated/DotsAnimationToolkit/Captures`. Open: T13 owner checkpoint (uncompressed vs project-default PNG import), and the drive's alpha, sRGB and cutscene checks.

### Final gate and revert-to-fail (spec-lead)

- Wave gate 2 (same three fixtures, after the walker fix): compile clean, **12 of 14 passed** - `PngSequenceWriterTests` 1/1, `GifEncodingTests` 1/1, `PackagingConformanceTests` 10/12 with only the standing Conformance_A and the expected Conformance_G (allowlist above).
- **Revert-to-fail:** one commit mutated both (writer dropped the `D4` padding; encoder wrote only the first frame) and gated: `FrameFileName_PadsToFourDigits` failed (`Walk_7.png`) and `Encode_TwoFrames_HasHeaderAndTwoImageDescriptors` failed (1 descriptor, expected 2). A hard reset to the previous commit restored both; sha256 matches the committed files (`PngSequenceWriter.cs` `283b9d294b61ef420dbe0c2b1cc1d2b0ae2163fb8ed489db10a2260667f099cb`, `GifEncoding.cs` `5ced203e55d4659b9e033af31ee517e65f1de3d19dade48eb69bddcbe3d429fa`).

### Close (stage, 2026-09-14)

- **Merge:** rebased onto trunk (head `e07c91dd`), pushed. `remove` failed WinError 32 on the finished lead's folder;
  `spec/a98` deleted after `git merge-base --is-ancestor`. The branch tracked no `.meta` files: Unity generated the folder
  and eleven script metas on the stage, committed at integration. `restore-trunk` after a98's gates left an empty
  `Editor/Capture/` folder and a Unity-written `Capture.meta` on the stage twice; the first was deleted, the second
  kept for the merge. `GifEncoding` and `PngSequenceWriter` went on the Conformance_G allowlist at integration.
- **Integration `e7ae55f9`:** `ClipEditorTab` Materials 10, Retarget 11, Capture 12; toggles and cover panes after Health;
  `tabToggles` sized 13; `MaterialsPanel` built with the window (`Bind(selection)`, `Refresh()` on show), `RetargetPanel`
  and `CapturePanel` built lazily on first show with `Bind(selection)`; all three disposed in teardown; layout test lists,
  `index.md`, CHANGELOG 0.46.0–0.48.0, `package.json` and the conformance pin at 0.48.0. Not driven in the real window (the
  docked Clip Editor is the owner's).
- **Gates:** integration fixtures 24 of 25 (`MaterialContractValidationTests`, `RetargetBindingResolverTests`,
  `PngSequenceWriterTests`, `GifEncodingTests`, `ClipEditorLayoutTests`, `PackagingConformanceTests`; Conformance_A the only
  failure); full EditMode 857 run, 856 passed (Conformance_A only; 851 + the batch's six new tests); PlayMode 285 of 285.
- **T10 drive (scratch only, `Assets/A98Scratch/`, preview camera only, no Play mode):** one `ClipCaptureSource(NewClipSet, NewRig,
  Walk)` (read-only on the real assets; Walk is 1.0 s) driven through `FrameCaptureRunner` from `execute_code`, the runner kept in
  `AppDomain` data between calls and its `finished` callback logging to the console.
  - **PNG sequence, 256×256, 12 fps, full range, transparent:** 12 frames (the exclusive-end range, as drifted) in about 11 s of
    wall time including MCP polling; `Walk_0001.png` … `Walk_0012.png`, 7.0–7.7 KB each, finish message "Captured 12 frames to
    Assets/A98Scratch/Walk.". Read back in a separate call by decoding the file bytes: 51,396 of 65,536 pixels alpha 0 (corner
    `RGBA(0,0,0,0)`), 13,887 alpha 255, 253 partial; frames 1 and 6 differ. Importer: `textureCompression` Uncompressed, mips off,
    `alphaIsTransparency` on.
  - **GIF, 256×256, 12 fps, solid colour:** `Walk.gif` 33,774 bytes, starts `GIF89a`, ends `0x3B`; 12 of 12 frames. Sent to the
    owner as `A98_Walk_capture.gif`.
  - **Cancel:** a 1024² 60-frame run finished all 60 before a separate cancel call arrived (`Cancel()` was a no-op, `WasCancelled`
    false). A 1920×1080 60-frame run cancelled from its progress callback at frame 3 stopped with `WasCancelled` true, 3 of 60
    captured, `Walk_0001`–`0003.png` and their `.meta` files kept, "Cancelled after 3 of 60 frames; 3 files kept in
    Assets/A98Scratch/Cancel2."; the Editor stayed idle (no compile, no asset update pending).
  - **What the frames show:** the actor's part quads render solid magenta (11,474 of 14,140 opaque pixels exactly `255,0,255`) with
    thin white rectangle outlines and a dark red bar above the head. The same `ClipPreviewController.Render(256, 256)` path the
    viewport uses gives the same image (11,435 magenta pixels) plus the grid and floor line, so the capture matches the preview minus
    its scenery. The magenta is how this detached preview draws `NewRig`/`NewClipSet` here (legacy `Shader Graphs/2DShader` part
    materials), not a capture fault; whether the owner's docked viewport shows the same is not known.
  - Capture source disposed; scratch deleted; registry sha256s unchanged; `NewClipSet`, `NewRig` and `Walk` untouched.
- **Not seen by eye:** the drawn tab, the viewport element's styling, and the profile-animation and cutscene sources (not driven).
