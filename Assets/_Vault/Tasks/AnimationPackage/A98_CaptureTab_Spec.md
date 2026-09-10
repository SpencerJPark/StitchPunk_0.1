# Amendment A98 — Capture tab: PNG sequences and GIFs from the preview camera

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.45.0`.
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

- [ ] **T0 — Baseline + probe (orchestrator).** Gate; totals. D3 timing probe. Confirm how the
  preview controller's camera can render to an arbitrary `RenderTexture` (grep `targetTexture`;
  if the `PreviewRenderUtility` owns it, `BeginPreview(rect)` with the capture size and
  `EndPreview` as a `Texture` is the path — record which).
- [ ] **T1 — Interface + settings (orchestrator).** §4.1's interface, §4.2. Gate. Commit `A98-T1`.
- [ ] **T2 — Three adapters [parallel-safe]** — Files: new `ClipCaptureSource.cs`, new
  `ProfileAnimationCaptureSource.cs`; the cutscene adapter is a **second worker** (T2b) with new
  `CutsceneCaptureSource.cs` only.
- [ ] **T3 — Runner [parallel-safe]** — Files: new `FrameCaptureRunner.cs`. Brief carries T0's
  render-path finding.
- [ ] **T4 — PNG writer + fixture [parallel-safe]** — Files: new `PngSequenceWriter.cs`, new
  `Tests/EditMode/PngSequenceWriterTests.cs` (`FrameFileName_PadsToFourDigits`: frame 7 →
  `Walk_0007.png`; a pure naming function). Revert-to-fail: drop the padding.
- [ ] **T5 — GIF encoder + fixture [parallel-safe, conditional on T0]** — Files: new
  `GifEncoding.cs`, new `Tests/EditMode/GifEncodingTests.cs` (`Encode_TwoFrames_HasHeaderAndTwoImageDescriptors`:
  bytes start `GIF89a`, contain two `0x2C` image-descriptor separators, end `0x3B`). Revert-to-fail:
  write one frame.
- [ ] **T6 — Viewport element [parallel-safe]** — Files: new `CaptureViewportElement.cs`.
- [ ] **T7 — Panel [parallel-safe]** — Files: new `CapturePanel.cs`.
- [ ] **T8 — Docs [parallel-safe]** — Files: new `Documentation~/capture-tab.md`,
  `README.md` (one bullet in the feature list).
- **Gate the wave.** `PngSequenceWriterTests`, `GifEncodingTests` (if T5). Commit `A98-T2..T8`.
- [ ] **T9 — Window wiring (orchestrator).** Tab; `index.md`; `CHANGELOG.md` `## [0.45.0]`;
  `package.json`; `Conformance_G`. Gate.
- [ ] **T10 — Drive.** Full suites. Capture Walk at 256², 12 fps, full range → N PNGs exist with
  alpha where transparent was chosen (read one pixel off the actor with `execute_code`); GIF opens
  in a browser (SendUserFile it to the owner); Cancel mid-capture leaves the Editor responsive and
  the partial files present. Capture the tab itself.
- [ ] **T11 — Vault + HANDOFF.** Vault note "Capture tab (A98)": the render path from T0, the
  D3 timing.
- [ ] **T12 — Close.** Roadmap checkbox.
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

_(empty — must contain the D3 timing and the T0 render-path finding before the wave)_
