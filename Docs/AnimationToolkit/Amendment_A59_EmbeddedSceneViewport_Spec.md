# Amendment A59 — Embedded Scene Viewport

Raised 2026-08-30, the owner's third correction on the same axis: *"there still is no scene visual
in the cutscene editor tab, it needs the scene there to actually work."* A58 made the preview
real but left the tool assembled out of docked windows; the owner's requirement is that **the
Cutscene Editor tab contains the scene visual itself**. This supersedes A58 §2.1's "embedded
scene-rendering viewport stays out of scope" — that deferral is revoked by the owner. Everything
else in A58 (animated preview, transport, cast panel, staging) stands and is what the viewport
displays.

## 1. Why this is cheap now (and why it was mis-priced twice)

Both earlier deferrals priced "embedded viewport" as *loading a scene into an isolated preview
world* — the `PreviewRenderUtility` model, where URP lighting genuinely is a serious lift. That
is not the situation. The remembered scene is **already open in the editor**, and since G3/A58
the preview **poses the real scene objects**. So the tab needs no world of its own: it renders
the open scene with its own utility camera into a `RenderTexture` shown in an `Image`. Real
lighting, real URP, actors already animating — all free, because it is the real scene.

**Render mechanism, verified against this project's package cache 2026-08-30** (not from memory):
`com.unity.render-pipelines.universal@0c18adc4ff89` implements
`UniversalRenderPipeline.SingleCameraRequest` (`{ destination, mipLevel, face, slice }`) in its
`ProcessRenderRequests` path — render one camera to a destination RT on demand. Confirm the exact
submit-side call (`RenderPipeline.SupportsRenderRequest` / `SubmitRenderRequest` overload shape)
in `com.unity.render-pipelines.core` before writing the call site. Fallback if the request path
misbehaves in edit mode: an enabled, `HideAndDontSave` camera with `targetTexture` set, rendered
by the pipeline's own loop.

## 2. The requirement

Open a cutscene → the tab shows the scene, the cast standing in it, and the timeline under it.
Scrub or play → the actors animate **in the tab**. No other window is required for the tool to
make sense. (The Scene view keeps working alongside — same real objects, same preview poses — it
just stops being mandatory.)

## 3. Design

### 3.1 Layout — the tab becomes a whole tool

Clip-Editor-shaped: **cast panel left · viewport center · inspector right · timeline bottom.**
The A58 cast panel and inspector move to flank the viewport rather than the timeline. When the
remembered scene is not the open scene, the viewport itself hosts the state: a centered
"Open Dock.unity" button (the existing remember/open flow), never a blank pane with a toolbar
warning somewhere else.

### 3.2 Rendering

- One utility `Camera` (`HideAndDontSave`, `enabled = false`, destroyed on tab hide/panel
  detach — it must never be saved into the scene or survive the tab) rendered on the panel's
  editor tick into an RT sized to the viewport element (resize recreates the RT).
- Tick only while `activeTab == CutsceneEditor` — the same gating that keeps the Clip Editor and
  Direction Sets panes from fighting; this renderer is additionally not `ClipPreviewController`,
  so it coexists with them by construction.
- Camera settings mirror a Scene view's defaults (skybox clear, everything-mask, pipeline-asset
  HDR); no gizmo/grid drawing in v1.

### 3.3 Viewport camera

Two modes on the viewport toolbar:

- **Free** — an orbit rig (focus + yaw/pitch + distance), the `CameraNavigation` gesture family:
  orbit, pan, dolly/zoom, fly, `F` to frame the cast or selection. Persisted per cutscene.
- **Shot** — locks the viewport to the camera lane's sampled pose (`SampleCameraWithCuts`), so
  scrubbing shows the framed shot exactly. This is the in-tab replacement for the Scene-view
  "Preview Shot" toggle, which stays for anyone using the Scene view.

### 3.4 Interaction, phased

- **T1 — see** (§3.2 + §3.3): render, scrub, play transport, navigation, open-scene flow in the
  pane. *This alone answers the owner's sentence.*
- **T2 — touch**: click-select in the viewport (camera ray against the cast's bound renderers;
  nearest wins), selection syncing to cast row, timeline group, and Unity's own
  Hierarchy/Inspector. Double-click frames.
- **T3 — move**: in-viewport translate/rotate gizmo on the selected slot/part at the playhead,
  plus the Key button — the `PreviewTransformGizmo`/`PreviewGizmoMath` approach re-hosted over a
  scene render. Until T3 lands, moving things still happens via the Scene view or inspector
  fields; that is a recorded gap, not a silent one.

## 4. Decisions (recorded; flag before T1 lands if wrong)

- **A59-D1** Render the open scene with a utility camera; never a preview world. If someone
  proposes loading the scene into a `PreviewRenderUtility` again, §1 is the answer.
- **A59-D2** The Scene-view preview path (posing, camera preview-shot, gizmo keying) is kept, not
  retired — it is the same real objects and costs nothing to keep; the viewport is additive.
- **A59-D3** The viewport renders on the preview tick (~30Hz cap, matching
  `ClipPreviewController`'s cadence), and only repaints when dirty (playhead moved, playing,
  camera moved, viewport resized) — an idle open tab must not re-render the scene every tick.
- **A59-D4** No `GUIUtility.hotControl` writes from the tick, ever (the A54 scar).

## 5. The queue

1. **A59-T1 — the viewport renders and plays** (§3.2, §3.3, open-scene flow, layout rehome of
   cast/inspector). Owner's eyes immediately after — before T2 starts.
2. **A59-T2 — click-select + sync.**
3. **A59-T3 — in-viewport gizmo + Key.**
4. **A59-T4 — docs** (`cutscenes.md` workflow section now leads with the tab, not the docked
   layout; CHANGELOG).

Gate per HANDOFF §3 cadence. UI wiring gets zero fixtures; the one testable seam is ray-pick math
if T2 extracts any (judge then — do not pre-build a fixture for Unity's own raycast).

## 6. Risks

- Edit-mode `SubmitRenderRequest` behavior is the load-bearing unknown; prove it renders the open
  scene from a disabled hidden camera *first*, in isolation via `execute_code`, before building
  layout around it. If it needs the fallback (§1), the design is unchanged.
- The RT + tick must not leak: camera and RT destroyed on tab hide, panel detach, and domain
  reload (the window rebuilds via `CreateGUI` — session-state discipline applies, see the
  AnimationToolkit memory note on the reload trap).
- A 2.5D URP scene may render differently without the Scene view's own SceneViewState effects
  toggles (fog, post-processing); if the tab's image looks wrong next to the Scene view, compare
  those flags before suspecting the request path.
