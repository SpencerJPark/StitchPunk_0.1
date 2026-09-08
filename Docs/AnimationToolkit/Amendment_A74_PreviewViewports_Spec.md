# Amendment A74 — Preview Viewports: Actor Editor camera and VAT Bake preview

> **Status:** ✅ T1–T10 built and gated 2026-09-08 (0.20.0). ⏸ T11 owner checkpoint open.
> **Executor:** one orchestrating session (Editor-connected, runs the gate, commits) plus **small
> Sonnet subagents that only edit files** — a subagent never touches `mcp__UnityMCP__*`. Every task
> below is sized for one subagent well under ~100k tokens: named files, named line ranges, one or
> two new files each, no re-reading of what the orchestrator already knows. Tasks marked
> **[parallel-safe]** may run at the same time as every other task carrying the same marker in the
> same wave; the orchestrator runs **one** compile gate over a wave.
> **Protocol:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 (binding) and
> `Docs/AnimationToolkit/HANDOFF.md` §2 (A69 comment rule, static-class suffixes, UI Toolkit only)
> and §3 (gate + discovered counts). Commit prefix `A74-Tn`.
> **Version:** the next unused minor — `0.19.0` if A73 has not shipped when this lands, otherwise
> `0.20.0`. One `CHANGELOG.md` entry under that version, headed "A74 — preview viewports".

---

## 1. What the owner asked for (2026-09-08, verbatim where it matters)

> "adjusting the actor editor preview, I would like to be able to move the camera around like in
> clip editor and have the same controls in clip editor except for the move, rotate, scale controls
> because those are more for setting up the scene and arent needed for this part. I also would like
> to add a similar window to the vat baker, somehow I want visual feed back showing me how the vat
> baked parts are working in that window to confirm that they baked correctly."

Three requirements, and every task in §5 serves one:

| # | Requirement | Where it lands |
|---|---|---|
| R1 | **The Actor Editor viewport has the Clip Editor's camera.** Left-drag orbit, middle-drag pan, right-drag look with W/A/S/D + Q/E flying (Shift faster), Alt + right-drag dolly, wheel zoom, `F` frames, double-click and a Reset Camera button return to head-on. | §4.1, §4.2, T1–T4 |
| R2 | **Same viewport controls as the Clip Editor, minus the gizmo modes.** The Clip Editor's left-edge tool rail has six buttons: Move / Rotate / Scale, Reset Camera, Billboard, Ragdoll. The Actor Editor gets the last three. `W`/`E`/`R` do nothing in the Actor Editor viewport (there is no gizmo to switch). | §4.2, T4 |
| R3 | **The VAT Bake panel shows the bake playing.** A viewport beside the bake form renders the baked `runtimeMesh` through the package's own VAT shader, driven by the texture set just written, with a transport, a clip picker and a frame readout — so a wrong bake is *seen* (a clump, a frozen pose, a wrong clip), not inferred from a log line. The same camera as R1. A translucent **source ghost** — the real skinned mesh posed at the same time — overlays it so a drift between bake and source shows as a double image. | §4.3, T5–T11 |

The vault's standing lesson applies to R3 in particular: for this owner an authoring tool without
live visual feedback "is not a useful tool at all" (`feedback_visual_first_editor_tools`). The
viewport is the deliverable; the log is not.

## 2. Decisions (recorded — do not re-ask)

- **A74-D1 — One camera navigation class, shared by three viewports.** The gesture state machine
  now inside `ClipEditorWindow.CameraNavigation.cs` (218 lines: `CameraGesture`, `heldFlyKeys`,
  `isFlyingFast`, `TryBeginCameraGesture` … `StepCameraFly`) moves into a plain class
  `PreviewCameraNavigation` in `Editor/ClipEditor/Preview/`. It drives any `IPreviewCameraRig`
  (new interface, §4.1). The window keeps its method names and becomes a thin adapter, because
  its pointer-down still has to try ragdoll handles and gizmo handles *between* the exclusive
  camera gestures and the plain left-drag orbit (`OnPreviewPointerDown`, `ClipEditorWindow.cs:2847-2910`).
- **A74-D2 — `ClipPreviewController` implements `IPreviewCameraRig` as-is.** It already has every
  member with the exact signatures (`Orbit`, `Zoom`, `Pan`, `LookAround`, `Dolly`, `Fly`,
  `ResetView`, `FrameSelection`, `ClipPreviewController.cs:1259-1378`). Its internal orbit maths is
  **not** refactored in A74 — the 1975-line controller is too much surface for a subagent without
  a compiler. A standalone `PreviewOrbitCameraRig` (§4.1) carries the same maths for viewports that
  have no controller (the VAT preview). Folding the controller onto that rig is a recorded
  follow-up, not part of this amendment.
- **A74-D3 — The Actor Editor borrows the whole camera pose, not just yaw and pitch.** Today
  `BorrowPreviewCamera` / `ReturnPreviewCamera` (`ActorEditorPanel.cs:231-261`) save `OrbitYaw` and
  `OrbitPitch` only; once a pan or a flight can move the focus, returning to the Clip Editor tab
  with a moved focus and distance would be a regression there. `ClipPreviewController` gains
  `CapturePose()` / `RestorePose(in PreviewCameraPose)` and the panel uses them. The borrow still
  starts head-on and framed (`OrbitYaw = OrbitPitch = 0`, `FrameRig()`), exactly as now.
- **A74-D4 — The Actor Editor rail is Reset Camera, Billboard, Ragdoll — in that order, built in
  C#, reusing the Clip Editor's overlay classes.** `clip-editor__viewport-frame`,
  `clip-editor__viewport-overlay`, `clip-editor__overlay-column`,
  `clip-editor__overlay-tool-button`, `clip-editor__overlay-tool-icon`,
  `clip-editor__overlay-run-break` (`ClipEditorWindow.uss:687-720, 1326-1420`). No class rename
  sweep: the panel is inside the window's tree and inherits the sheet (A72-D9). Icons are the
  window's: `d_FrameCapture` / "Reset Camera", `d_BillboardRenderer Icon` / "Billboard",
  `d_Avatar Icon` / "Ragdoll" (`ClipEditorWindow.cs:957, 972, 1012`).
- **A74-D5 — The Actor Editor's Ragdoll toggle is a manual override of the composer's trigger.**
  The composer still fires `RagdollStartRequested` / `RagdollStopRequested` from animation flags
  (A71-T7); those now *also* write the toggle with `SetValueWithoutNotify`. A user click does what
  the Clip Editor's toggle does: on → `TryEnableRagdollPreview`, off → `DisableRagdollPreview`.
  A refused enable snaps the toggle back off and puts the refusal in the status label (the field
  `ragdollRefusalReason` already exists for that).
- **A74-D6 — The VAT preview renders through the shipped graph, bone flavour only.** The one VAT
  shader the package ships is `Shaders/ToolkitVatCrowdUnlit.shadergraph`; its vertex stage is
  `ToolkitVatBoneSkin` reading bone data from **UV1** exactly as `VatMeshPreparer` writes it
  (`(index0, index1, weight0, weight1)`, `VatMeshPreparer.cs:55-63`). A `VatFlavor.Vertex` set has
  no `runtimeMesh` (the preparer refuses, `VatMeshPreparer.cs:41-45`) and no vertex-fetch graph
  exists, so the preview shows a status line naming both facts and draws nothing. A vertex-fetch
  preview shader is a separate authoring task; record it in §7 as a follow-up, do not attempt it.
- **A74-D7 — `_VatTexelParams` is set by the preview from the set's own fields.** Nothing in
  `Runtime/` or `Authoring/` writes this material-level property today (only `_VatBoneTex` is
  validated, `RigTargetBaker.cs:19`); the game's materials carry it by hand. The preview sets
  `(textureWidth, boneTexture.height, rowsPerFrame, boneCount)` per `Documentation~/shader-contract.md:249`.
  This exposes a documentation gap for customers — note it in §7; do not change the runtime.
- **A74-D8 — Frame maths mirrors `VatMaterialSystem.TryResolveGlobalFrame`.**
  `globalFrame = frameStart + clamp(time × fps, 0, frameCount − 1)`. The runtime maps time against
  the clip blob's `duration` and loop mode; the preview has no blob, so its clip length is
  `frameCount / fps`. A loop-safe range therefore holds its duplicated last frame for one extra
  frame-time — cosmetic, and documented in the class's one summary line.
- **A74-D9 — The VAT preview lives in `VatBakePanel`, so both hosts get it.** The panel is hosted by
  the standalone `VatBakeWindow` and by the Clip Editor's VAT Bake tab (`ClipEditorWindow.cs:1706-1727`).
  A72-D9 left the standalone window off the shared stylesheet; A74 puts it on (loads
  `ClipEditorWindow.uss`, adds the `clip-editor__root` class for the `--toolkit-color-*` tokens)
  because the transport and rail chrome need it. The panel's "styled inline, a host's sheet has no
  reason to know this panel" comment (`VatBakePanel.cs:31-33`) is rewritten to say the opposite.
- **A74-D10 — The VAT preview has its own `PreviewRenderUtility`, never the window's controller.**
  The vault already records that one `PreviewRenderUtility` cannot serve two viewports in a frame
  (`ClipEditorWindow.cs:4527` comment). The VAT viewport is a mesh + material + camera, so it
  needs none of the controller's rig machinery. Camera constants are copied from
  `ClipPreviewController.EnsureRenderUtility` (FOV 45, near 0.1, far 200, clear
  `(0.17, 0.17, 0.18)`, ambient `0.45`, two lights) so the three viewports clear to one colour.
- **A74-D11 — The source ghost is a preview-scene copy, never the scene object.** The baker poses
  the live scene hierarchy inside `AnimationMode` and restores it once per bake
  (`VatTextureBaker.cs:186-220`). Doing that every preview tick would fight the user's scene. The
  ghost instantiates `renderer.transform.root.gameObject` into the preview scene
  (`PreviewRenderUtility.AddSingleGO`), poses the **copy** with `AnimationClip.SampleAnimation` and
  `BoneTrackPoser.ApplyTracks`, and lets the copy's own `SkinnedMeshRenderer` skin it. Translucent
  tint, overlaid at the same origin: a correct bake shows one shape, a wrong one shows two.
- **A74-D12 — A sample subject ships behind a button, because the project has no VAT content.**
  No `.asset` in `Assets/` sets `vatSource.sourceClip`, and `VatTentacleRigBuilder.CreateTentacle`
  has no caller. A **Create Sample Tentacle** button writes the tentacle's clip, a VAT-bound
  `ClipAsset`, a `ClipSetAsset` and a `RigAsset` into the output folder and fills the form, so the
  owner can press Bake and see the preview inside a minute. The asset surgery is a static class in
  `Editor/ClipUtilities/` with the `Utility` suffix (HANDOFF §2, `Conformance_G`).
- **A74-D13 — Keys.** Space / Home / End / ← → (Shift ×10) reach the VAT preview on both hosts:
  the Clip Editor's `ResolveActiveTransportTarget` gains a `VatBake` case; the standalone window
  registers its own root `KeyDownEvent` with the same map. `F` and the fly keys are the viewport's
  own (it is focusable, as the Clip Editor's is, `ClipEditorWindow.cs:2348-2352`).
  `PreviewCameraNavigation` calls `StopPropagation` **only** for keys it consumes, so Space still
  bubbles to the root handler.

## 3. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`.
2. `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections **"The viewport camera is an orbit
   rig with no position of its own"** and **"Shared editor chrome"** — the three silent traps
   (look ≠ orbit, pan needs the pixel height, fly steps on the tick) and the one-root-KeyDown rule.
3. `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.CameraNavigation.cs` in
   full (218 lines). This is the code being lifted; read it before designing anything.
4. `ClipPreviewController.cs:24-60` (constants), `:195-225` (orbit fields and `OrbitYaw`/`OrbitPitch`),
   `:1255-1420` (every camera method), `:1462-1525` (`Render`), `:1914-1935` (`EnsureRenderUtility`),
   `:1787-1791` (`ApplyCameraPose`).
5. `ActorEditorPanel.cs:19-70` (fields), `:180-261` (`SetSource`, `SetTicking`, borrow/return),
   `:286-330` (transport row), `:360-395` (viewport column), `:488-517` (ragdoll hooks),
   `:518-600` (`Tick`, `RenderViewport`).
6. `VatBakePanel.cs` in full (494 lines) and `VatBakeWindow.cs` (26 lines).
7. `VatTextureSetAsset.cs:15-72, 220-240` (`VatClipRange` has `frameStart`, `frameCount`, `fps`,
   `bounds`), `VatMaterialSystem.cs:150-200` (`TryResolveGlobalFrame`), `VatMeshPreparer.cs`,
   `VatTentacleRigBuilder.cs:14-40, 180-189`.
8. `Shared/ITransportTarget.cs`, `Shared/TransportCoreElement.cs:89-135`, `Shared/ToolkitIcons.cs`
   (public surface at lines 46-110), and `ClipEditorWindow.cs:2734-2790` (`SetOverlayToolIcon`,
   both overloads — the Toggle one is what T4 lifts).
9. Tests to mirror: `Tests/EditMode/ActorEditorPanelTests.cs:24-50` (how a panel is constructed
   without a window), `ClipPreviewCompositeTests.cs:27-40` (`new ClipPreviewController()` is legal
   in EditMode and must be disposed), `VatTextureBakerTests.cs:60-120` (`CreateSkinnedRenderer`).

## 4. Design

### 4.1 Shared camera — `Editor/ClipEditor/Preview/`

**`IPreviewCameraRig.cs`** (new, public interface + one struct, namespace `DotsAnimationToolkit.Editor`):

```csharp
public interface IPreviewCameraRig
{
    void Orbit(Vector2 pixelDelta);
    void Zoom(float amount);
    void Pan(Vector2 pixelDelta, float viewportHeightPixels);
    void LookAround(Vector2 pixelDelta);
    void Dolly(Vector2 pixelDelta);
    void Fly(Vector3 localDirection, float deltaSeconds, bool fast);
    void ResetView();
    void FrameSelection();
}

public struct PreviewCameraPose
{
    public float yawDegrees;
    public float pitchDegrees;
    public float distance;
    public Vector3 focus;
}
```

**`PreviewCameraNavigation.cs`** (new, `public sealed class`). Owns the gesture state machine
lifted from `ClipEditorWindow.CameraNavigation.cs`; the rig is swappable and every call is a no-op
while it is null (the Actor Editor panel exists before its controller arrives).

```csharp
public sealed class PreviewCameraNavigation
{
    public enum Gesture { None, Orbit, Pan, Look, Dolly }

    public const float WheelZoomPerNotch = 0.3f;   // ClipEditorWindow.OnPreviewWheel's 0.3f

    public IPreviewCameraRig Rig { get; set; }
    public Gesture ActiveGesture { get; }
    public bool IsFlying { get; }                  // ActiveGesture == Gesture.Look
    public event Action CameraChanged;             // raised after any rig call that moved the camera

    public static Gesture ResolveExclusiveGesture(int button, bool altKey);  // 2→Pan, 1→Look, 1+alt→Dolly, else None
    public bool TryBeginExclusiveGesture(int button, bool altKey, bool shiftKey);
    public bool TryBeginExclusiveGesture(PointerDownEvent pointerEvent);     // forwards to the primitive overload
    public void BeginOrbit();                      // the left-drag path for hosts without picking
    public void ContinueGesture(Vector2 pixelDelta, float viewportHeightPixels);
    public void EndGesture();
    public bool TryHandleKeyDown(KeyDownEvent keyEvent);   // flying: record fly keys, swallow every key; F: FrameSelection; else false
    public void HandleKeyUp(KeyUpEvent keyEvent);
    public void HandleWheel(WheelEvent wheelEvent);        // Zoom(delta.y * WheelZoomPerNotch), StopPropagation
    public void StepFly(float deltaSeconds);
    public void ResetView();                               // EndGesture, then Rig.ResetView
    public void AttachTo(Image viewport);
}
```

`AttachTo` is the whole no-pick binding, so the Actor Editor and the VAT preview register nothing
themselves: sets `focusable = true`; `PointerDownEvent` → double-click with button 0 = `ResetView`
and return; else `CapturePointer`, `Focus`, `TryBeginExclusiveGesture` else `BeginOrbit` when
button 0; `PointerMoveEvent` → if captured, `ContinueGesture(deltaPosition, contentRect.height)`;
`PointerUpEvent` → `ReleasePointer`, `EndGesture`; `PointerCaptureOutEvent` → `EndGesture`;
`WheelEvent` → `HandleWheel`; `KeyDownEvent` → `TryHandleKeyDown` and `StopPropagation` only when it
returned true; `KeyUpEvent` → `HandleKeyUp`. Comment the two traps in one line each where they
live: the gesture is resolved at the press and never re-read (releasing Alt mid-drag must not turn a
dolly into a look), and `PointerCaptureOutEvent` ends the gesture because a lost capture never
delivers a release. `ContinueGesture` for `Gesture.Orbit` calls `Rig.Orbit`.

**`PreviewOrbitCameraRig.cs`** (new, `public sealed class PreviewOrbitCameraRig : IPreviewCameraRig`).
The controller's orbit maths (`ClipPreviewController.cs:1259-1416`) as a standalone object with no
rig knowledge. Constants copied verbatim from `ClipPreviewController.cs:24-56`
(`DefaultOrbitDistance`, `MinimumOrbitDistance`, `MaximumOrbitDistance`, `DegreesPerDragPixel`,
`DollyFractionPerPixel`, `FlySpeedUnitsPerSecond`, `FlyFastMultiplier`, `FrameFieldOfViewDegrees`,
`MinimumFrameRadius`). Adds what a controller-less host needs:

```csharp
public Vector3 CameraPosition { get; }      // focus - rotation * forward * distance
public Quaternion CameraRotation { get; }   // Quaternion.Euler(pitch, yaw, 0)
public void SetFrameTarget(Bounds bounds);  // what ResetView and FrameSelection frame
public void Frame(Bounds bounds);           // focus = center, distance = DistanceThatFrames(extents.magnitude)
public void ApplyTo(Camera camera);
public PreviewCameraPose CapturePose();
public void RestorePose(in PreviewCameraPose pose);
```

`ResetView` = yaw 0, pitch 0, `Frame(frameTarget)`; `FrameSelection` = `Frame(frameTarget)`
(no selection exists in a mesh preview). Keep `LookAround` exactly as the controller does it —
capture the camera position, rotate, put the focus back `distance` ahead — that is the trap the
vault note exists for.

### 4.2 Actor Editor viewport

`ActorEditorPanel` (fields `viewportColumn`, `viewportImage`, `viewportStatusLabel`, `previewController`
already exist; lines named in §3.5):

- New fields: `private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();`
  `private PreviewCameraPose restoredCameraPose; private bool hasBorrowedCamera;` (replacing
  `restoreOrbitYaw`, `restoreOrbitPitch`, `hasCapturedOrbit`), `private ToolbarToggle billboardToggle;`
  `private ToolbarToggle ragdollToggle;`.
- `SetSource(controller, rig)`: after assigning `previewController`, `cameraNavigation.Rig = controller`.
- Viewport column: the `Image` moves inside a `VisualElement viewportFrame` (class
  `clip-editor__viewport-frame`, `flexGrow = 1`) that also holds the overlay
  (`clip-editor__viewport-overlay`, `pickingMode = Ignore`) → rail column
  (`clip-editor__overlay-column`) → `ToolbarButton` named `actor-reset-camera-button`,
  `ToolbarToggle actor-billboard-preview-toggle` (`clip-editor__overlay-run-break`),
  `ToolbarToggle actor-ragdoll-preview-toggle`. Each has an `Image` child with
  `clip-editor__overlay-tool-icon` and `pickingMode = Ignore`, iconed through the new
  `ToolkitIcons.SetToggleIcon(Toggle, Image, string iconName, string fallbackText)` (T4 lifts the
  Toggle overload of `SetOverlayToolIcon` into `ToolkitIcons`; the Button case already exists as
  `ToolkitIcons.SetButtonIcon`). The reset button's tooltip is the window's gesture cheat-sheet
  text verbatim (`ClipEditorWindow.cs:1000-1006`). The validation badge's message panel still
  attaches to `viewportColumn` — do not move it.
- `cameraNavigation.AttachTo(viewportImage)` once, in the constructor after the column is built.
- `BorrowPreviewCamera`: `restoredCameraPose = previewController.CapturePose(); hasBorrowedCamera = true;`
  then the existing head-on start (`OrbitYaw = OrbitPitch = 0`, `BillboardPreviewEnabled = true`,
  `DisableRagdollPreview`, `FrameRig`), then `billboardToggle.SetValueWithoutNotify(true)`,
  `SetRagdollToggleWithoutNotify(false)`. `ReturnPreviewCamera`: `RestorePose(in restoredCameraPose)`
  and the billboard restore that exists today.
- `Tick`: `cameraNavigation.StepFly(elapsedSeconds)` immediately before `RenderViewport()`. The
  panel already renders every tick while paused, so an orbit shows without extra plumbing.
- Billboard toggle → `previewController.BillboardPreviewEnabled = newValue`.
- Ragdoll toggle per A74-D5; one private `SetRagdollToggleWithoutNotify(bool)` is called from
  `OnComposerRagdollStartRequested` (true on success), `OnComposerRagdollStopRequested`,
  `JumpToStart` and `SetTicking(false)`.
- `RenderViewport` status: when `ragdollRefusalReason != null` it is already shown; keep.

Nothing else in the panel changes. No `W`/`E`/`R` handling: `TryHandleKeyDown` returns false for
them when not flying, and they bubble away harmlessly.

### 4.3 VAT Bake preview — `Editor/VatBaking/`

**Layout.** `VatBakePanel` becomes a row: the existing form on the left at a fixed 420 px
(`style.width = 420f; flexShrink = 0`), a **Preview** pane on the right (`flexGrow = 1`,
`minWidth = 320`) with a `toolkit-pane-header` ("Preview", actions pushed right), the viewport frame
(image + overlay rail: Reset Camera, **Ghost** toggle), a status label, and a `toolkit-transport`
row: `TransportCoreElement` in a `toolkit-transport__group`, then a group with caption "Clip", a
`DropdownField` named `vat-preview-clip`, and a `Label` (`toolkit-transport__derived`) reading
`frame 12 / 60 · global 132 · 30 fps`. The form gains one field at the top of its **Output**
section: `ObjectField("Preview Set")` of type `VatTextureSetAsset` named `vat-preview-set-field`,
and a `Create Sample Tentacle` icon-text button (`ToolkitIcons.MakeIconTextButton`, icon
`d_Toolbar Plus`, text "Sample Tentacle") at the bottom of the **Source** section.

**`VatPreviewPlayback.cs`** (new, `public sealed class`, pure logic, no Unity objects beyond
`VatClipRange`):

```csharp
public void SetRange(VatClipRange range);          // resets time to 0
public bool HasRange { get; }
public float Time { get; set; }                    // seconds, clamped to [0, Duration]
public bool Loop { get; set; }
public float Duration { get; }                     // frameCount / fps  (A74-D8)
public float GlobalFrame { get; }                  // frameStart + clamp(Time * fps, 0, frameCount - 1)
public int LocalFrameIndex { get; }                // (int)clamp(Time * fps, 0, frameCount - 1)
public bool Advance(float deltaSeconds);           // loop: wrap with floor; else clamp and return false at the end
public void StepFrames(int frameDelta);            // Time += frameDelta / fps, clamped (wraps when Loop)
public void JumpToStart(); public void JumpToEnd();
```

**`VatPreviewMaterial.cs`** (new, `public sealed class VatPreviewMaterial : IDisposable`):

```csharp
public const string VatGraphPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitVatCrowdUnlit.shadergraph";
public static bool TryCreate(VatTextureSetAsset textureSet, Texture mainTexture, out VatPreviewMaterial preview, out string failureMessage);
public Material Material { get; }
public void SetFrame(float globalFrameA, float globalFrameB, float blend);   // _VatFrameA, _VatFrameB, _VatBlend
public void Dispose();                                                       // DestroyImmediate the material
```

`TryCreate` refuses, with a sentence each: a null set; `flavor != VatFlavor.BoneMatrix` ("a
vertex-flavour set has no runtime mesh and the package ships no vertex-fetch preview shader");
null `boneTexture`; null `runtimeMesh`; a shader that failed to load. On success: material with
`HideFlags.HideAndDontSave`, `_VatBoneTex` = `boneTexture`, `_VatTexelParams` =
`(textureWidth, boneTexture.height, rowsPerFrame, boneCount)`, `_MainTex` = `mainTexture ?? Texture2D.whiteTexture`,
`_BaseColor` = white, frames 0/0/0.

**`VatPreviewElement.cs`** (new, `public sealed class VatPreviewElement : VisualElement, ITransportTarget, IDisposable`):

- Owns `PreviewRenderUtility renderUtility` (constants per A74-D10, created lazily),
  `PreviewOrbitCameraRig cameraRig`, `PreviewCameraNavigation cameraNavigation` attached to its
  `Image viewportImage`, `VatPreviewMaterial material`, `VatPreviewPlayback playback`, the clip
  dropdown, the frame readout, the status label, a `TransportCoreElement`, and the ghost (below).
- `public void Show(VatTextureSetAsset textureSet, ClipSetAsset clipSetForNames, SkinnedMeshRenderer sourceRenderer, IReadOnlyList<VatBakeClip> bakeClipsOrNull)`:
  disposes the previous material, builds the dropdown from `textureSet.clipRanges` (label = the
  `ClipAsset` in `clipSetForNames.clips` whose `Id.Value == range.clipId`, or `clip 0x…`, plus
  ` · target 0x…` when `targetId != 0`), selects range 0, `playback.SetRange`,
  `cameraRig.SetFrameTarget(range.bounds)`, `cameraRig.ResetView()`, `VatPreviewMaterial.TryCreate`
  (main texture = `sourceRenderer?.sharedMaterial?.mainTexture`), status = the failure message or
  `bones 12 · frames 61 · 256×192`. Keeps `sourceRenderer` and the bake clips for the ghost.
- Ticks on `EditorApplication.update` between `AttachToPanelEvent` and `DetachFromPanelEvent`;
  each tick: `if (isPlaying && !playback.Advance(dt)) SetPlaying(false)`; `cameraNavigation.StepFly(dt)`;
  `material.SetFrame(playback.GlobalFrame, playback.GlobalFrame, 0f)`; render; refresh the readout;
  `transportCore.RefreshState()` when the playing flag changed.
- Render: `renderUtility.BeginPreview(new Rect(0, 0, w, h), GUIStyle.none)`; `cameraRig.ApplyTo(renderUtility.camera)`;
  `renderUtility.DrawMesh(textureSet.runtimeMesh, Matrix4x4.identity, material.Material, 0)`;
  ghost draw when on; `renderUtility.camera.Render()`; `viewportImage.image = renderUtility.EndPreview()`;
  `MarkDirtyRepaint()`. Skip when `contentRect` is NaN or under 1 px (the Actor panel's guard,
  `ActorEditorPanel.cs:583-588`).
- `ITransportTarget`: `Capabilities = StepBack | StepForward | Stop | JumpToEnd | Loop`;
  `IsLooping` ↔ `playback.Loop` (default true); `Stop` = pause + return to the time Play was pressed
  (A72-D4); `Step(n)` = `playback.StepFrames(n)`; `JumpToStart`/`JumpToEnd` forward.
- Dropdown change → `playback.SetRange`, `SetFrameTarget(range.bounds)`, `ResetView`.
- **Ghost** (A74-D11), `ghostToggle` in the rail (`d_SkinnedMeshRenderer Icon`, fallback "Ghost",
  off by default, disabled with a tooltip when there is no `sourceRenderer`): on enable,
  `ghostRoot = Object.Instantiate(sourceRenderer.transform.root.gameObject)`, `hideFlags = HideAndDontSave`,
  `renderUtility.AddSingleGO(ghostRoot)`, find the copy's `SkinnedMeshRenderer` by the same child
  path as the source, replace its materials with one translucent unlit material (URP Unlit, colour
  `ToolkitPalette.Accent` at alpha 0.35 — read it from `ToolkitPalette`, do not hard-code),
  `BoneTrackPoser ghostPoser` bound to `ghostRoot.transform`. Per tick while on: find the
  `VatBakeClip` whose `clipId`/`targetId` match the selected range; if it has an `animationClip`,
  `animationClip.SampleAnimation(ghostRoot, playback.Time)`; if it has `boneTracks`,
  `ghostPoser.ApplyTracks(boneTracks, playback.Time / playback.Duration)`. The ghost is drawn by
  the camera as a scene object, so nothing is queued for it. On disable or `Dispose`:
  `DestroyImmediate(ghostRoot)`. Ghost is unavailable (toggle disabled, tooltip says why) when the
  preview came from the **Preview Set** field without a renderer or bake clips in this session.

**`VatBakePanel` wiring.** `public ITransportTarget TransportTarget { get { return preview; } }`.
After `SaveResult` returns `setPath`: `AssetDatabase.LoadAssetAtPath<VatTextureSetAsset>(setPath)`
→ `previewSetField.SetValueWithoutNotify(set)` → `preview.Show(set, clipSet, renderer, bakeClips)`.
`Preview Set` field change → `preview.Show(newSet, clipSetField.value as ClipSetAsset, skinnedRendererField.value as SkinnedMeshRenderer, null)`.
`Create Sample Tentacle` → `VatSampleTentacleUtility.CreateSampleAssets(ResolveOutputFolder(...), out clipSet, out rig, out renderer)`
then fill the three fields with `SetValueWithoutNotify` and log one line naming the folder.

**`VatBakeWindow`.** `minSize = (960, 560)`; `rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath))`
with `public const string StyleSheetPath = "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uss"`
declared on `ClipEditorWindow` next to `LayoutAssetPath` (`ClipEditorWindow.cs:33`);
`rootVisualElement.AddToClassList("clip-editor__root")`; one root `KeyDownEvent` handler with the
transport map (Space → `TogglePlay`, Home/End → jumps, ←/→ → `Step(±1)`, Shift → `±10`) forwarded
to `panel.TransportTarget`. Register it once in `CreateGUI` — `CreateGUI` re-runs after a domain
reload and the root keeps its callbacks (the "one root KeyDown registration, ever" trap).

**`ClipEditorWindow.ResolveActiveTransportTarget`** (`:1622-1633`): add
`case ClipEditorTab.VatBake: return vatBakePanel != null ? vatBakePanel.TransportTarget : null;`.

**`VatSampleTentacleUtility.cs`** (new, `Editor/ClipUtilities/`, `public static class`):
`public static bool CreateSampleAssets(string assetFolder, out ClipSetAsset clipSet, out RigAsset rig, out SkinnedMeshRenderer renderer, out string failureMessage)`.
Calls `VatTentacleRigBuilder.CreateTentacle("VatSampleTentacle", out AnimationClip waveClip)`;
saves `waveClip` as `<folder>/VatSampleTentacleWave.anim`; creates a `ClipAsset` named
`VatSampleWave` with `duration = waveClip.length`, `frameRate = VatTentacleRigBuilder.BakeSampleRate`,
`vatSource = new VatClipSource { sourceClip = waveClip, sampleFps = BakeSampleRate, loopSafe = true }`;
a `ClipSetAsset` `VatSampleTentacleClips` with that clip in `clips`; a `RigAsset`
`VatSampleTentacleRig` with `EnsureStableIds()`; saves all three with `AssetDatabase.CreateAsset`
(replace if present, the way `VatBakePanel.CreateOrReplaceAsset` at `VatBakePanel.cs:485-494` does) and `SaveAssets`. Take the clip field values from
`Assets/_Scripts/Editor/ContentAuthoring/MaleCitizenContentAuthoring.cs:170-185` (the
game's own recipe — read for the shape, never reference it from the package). Undo-record the
scene object with `Undo.RegisterCreatedObjectUndo`.

## 5. Tasks

Wave 1 (`[parallel-safe]` with each other): T1, T2, T5, T6, T9. Wave 2: T3. Wave 3: T4, T7
(`[parallel-safe]` with each other). Wave 4: T8. Then T10, T11, the checkpoint.

### T0 — Baseline (orchestrator)
Gate per HANDOFF §3; record EditMode / PlayMode discovered totals in §7. Confirm the working tree
has nothing staged you did not make (the tree carried four modified `.cs`/`.uss` files and one
stray `.meta` on 2026-09-08 — leave them alone, stage your own paths only).

### T1 — `IPreviewCameraRig` + `PreviewCameraNavigation` [parallel-safe]
Files: **new** `Editor/ClipEditor/Preview/IPreviewCameraRig.cs`, **new**
`Editor/ClipEditor/Preview/PreviewCameraNavigation.cs`, **new**
`Tests/EditMode/PreviewCameraNavigationTests.cs`. Read `ClipEditorWindow.CameraNavigation.cs` in
full and `ClipEditorWindow.cs:2340-2365, 2847-2870, 2912-2935, 3439-3447` (the callers and the
wheel). Do **not** edit the window.
- Build §4.1's two files to the signatures given. Fly keys: W/S = ±forward, A/D = ∓/±right,
  E/Q = ±up, normalised, `fast` from Shift — the same table as `StepCameraFly`.
- Tests, against a stub `IPreviewCameraRig` that records calls:
  - `ResolveExclusiveGesture_MapsButtonsLikeTheSceneView`: `(2,false)→Pan`, `(1,false)→Look`,
    `(1,true)→Dolly`, `(0,false)→None`, `(0,true)→None` (Alt+left is the pick-cycle modifier —
    say so in one line).
  - `StepFly_MovesOnlyWhileLooking_AndForgetsKeysWhenTheGestureEnds`: begin `(1,false,false)`;
    `TryHandleKeyDown(KeyDownEvent.GetPooled('w', KeyCode.W, EventModifiers.None))` returns true;
    `StepFly(0.1f)` → stub saw one `Fly` with `localDirection.z > 0`; `EndGesture()`;
    `StepFly(0.1f)` → no further `Fly`. (Revert-to-fail: comment out `heldFlyKeys.Clear()` in
    `EndGesture`.)

### T2 — `PreviewOrbitCameraRig` [parallel-safe]
Files: **new** `Editor/ClipEditor/Preview/PreviewOrbitCameraRig.cs`, **new**
`Tests/EditMode/PreviewOrbitCameraRigTests.cs`. Read `ClipPreviewController.cs:24-60, 195-225, 1255-1420`
and the vault note named in §3.2. Implements `IPreviewCameraRig` (T1's interface — code against
§4.1's text; the orchestrator gates the wave together).
- Tests: `LookAround_HoldsTheCameraPositionStill` (position before/after within 1e-4 while the
  focus moved) and `Frame_BacksOffFarEnoughToHoldTheBounds` (a 2 m bounds → distance ≥
  extents.magnitude / tan(FOV/2), and ≥ `MinimumOrbitDistance`). (Revert-to-fail for the first:
  drop the position capture and the second `orbitFocus` line.)

### T3 — Controller implements the rig; the window becomes an adapter
Files: `ClipPreviewController.cs` (`:195-225` add the interface and `CapturePose`/`RestorePose`
next to `OrbitYaw`; `RestorePose` also clears `framePending`), `ClipEditorWindow.CameraNavigation.cs`
(rewrite: one `private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();`
whose `Rig` is set right after `previewController = new ClipPreviewController()` at `ClipEditorWindow.cs:751`, and the
existing method names forwarding: `TryBeginCameraGesture(e)` → `cameraNavigation.TryBeginExclusiveGesture(e)`,
`ContinueCameraGesture(delta)` → `ContinueGesture(delta, previewImage.contentRect.height)`,
`EndCameraGesture` → `EndGesture`, `TryHandleFlyKeyDown` → `TryHandleKeyDown` **minus** the `F`
case (the window handles `F` itself at `:2657`; pass `F` through by checking
`keyEvent.keyCode == KeyCode.F` before delegating), `OnViewportKeyUp` → `HandleKeyUp`,
`StepCameraFly` → `StepFly`, `ResetViewportCamera` → `cameraNavigation.ResetView(); Repaint();`,
`IsCameraFlying` → `cameraNavigation.IsFlying`), and `ClipEditorWindow.cs:2919`
(`activeCameraGesture != CameraGesture.None` → `cameraNavigation.ActiveGesture != PreviewCameraNavigation.Gesture.None`)
plus `:3441-3446` (`OnPreviewWheel` → `cameraNavigation.HandleWheel(wheelEvent)`). Delete the
window's private `CameraGesture` enum and the three fields it owned. Grep `activeCameraGesture|heldFlyKeys|isFlyingFast|CameraGesture\.`
across `Editor/ClipEditor/` afterwards — zero hits outside the new class.
- Test (`Tests/EditMode/ClipPreviewControllerPoseTests.cs`, new, disposes the controller):
  `CapturePose_ThenRestorePose_PutsFocusAndDistanceBack`: `Pan(new Vector2(40, 0), 400)`,
  `Zoom(3f)`, `Orbit(new Vector2(30, 10))`, capture; `ResetView()`; `RestorePose(in captured)`;
  `CapturePose()` equals the captured pose field for field (1e-4). (Revert-to-fail: make
  `RestorePose` write yaw/pitch only — today's borrow behaviour.)

### T4 — Actor Editor viewport camera and rail
Files: `ActorEditorPanel.cs` (lines in §3.5), `Shared/ToolkitIcons.cs` (add `SetToggleIcon`,
lifted from the Toggle overload at `ClipEditorWindow.cs:2752-2768`; leave the window's copy in place),
`Tests/EditMode/ActorEditorPanelTests.cs` (append). Build §4.2 exactly.
- Test `ViewportRail_OffersCameraAndPreviewTogglesButNoGizmoModes`: a new panel has elements
  named `actor-reset-camera-button`, `actor-billboard-preview-toggle`,
  `actor-ragdoll-preview-toggle` and none named `gizmo-move-toggle` / `gizmo-rotate-toggle` /
  `gizmo-scale-toggle`. Fails on the old panel (no rail).

### T5 — `VatPreviewPlayback` [parallel-safe]
Files: **new** `Editor/VatBaking/VatPreviewPlayback.cs`, **new**
`Tests/EditMode/VatPreviewPlaybackTests.cs`. Read `VatMaterialSystem.cs:150-200` and
`VatTextureSetAsset.cs:220-240`.
- Tests with a range `frameStart 100, frameCount 10, fps 30`:
  `GlobalFrame_MirrorsTheRuntimeFormula` (t 0 → 100; t 0.1 → 103 ± 1e-4; t 10 → 109);
  `Advance_LoopsBackToTheStart_ButStopsAtTheEndWhenNotLooping` (Loop on: advance past `Duration`
  wraps below `Duration` and returns true; Loop off: returns false and `Time == Duration`).

### T6 — `VatPreviewMaterial` [parallel-safe]
Files: **new** `Editor/VatBaking/VatPreviewMaterial.cs`, **new**
`Tests/EditMode/VatPreviewMaterialTests.cs`. Read `VatTextureSetAsset.cs:15-72`,
`VatMeshPreparer.cs`, `Documentation~/shader-contract.md:240-260`, `ShaderConformanceTests.cs:85-100`
(how the graph is loaded in a test).
- Tests: `TryCreate_BindsTexelParamsFromTheSet` (a `CreateInstance<VatTextureSetAsset>` with an
  8×6 `Texture2D`, `textureWidth 8`, `rowsPerFrame 3`, `boneCount 2`, any non-null `Mesh` →
  `Material.GetVector("_VatTexelParams") == (8, 6, 3, 2)` and `GetTexture("_VatBoneTex")` is the
  texture); `TryCreate_RefusesAVertexFlavourSet_AndNamesTheMissingShader` (message contains
  "vertex"). Destroy every object created in `TearDown`.

### T7 — `VatPreviewElement` [parallel-safe with T4]
Files: **new** `Editor/VatBaking/VatPreviewElement.cs`. Read §4.3 in full, `ActorEditorPanel.cs:286-330, 557-600`
(transport row and render guard to mirror), `ClipPreviewController.cs:1914-1935, 1462-1526`
(render utility setup and the `BeginPreview`/`Render`/`EndPreview` sequence), `TransportCoreElement.cs:89-135`,
`ToolkitPalette.cs` (the `Accent` field name), `BoneTrackPoser.cs:14-60`. Build everything in
§4.3's `VatPreviewElement` block **including** the ghost. No fixture: this is wiring over T2/T5/T6,
verified by T10's drive (HANDOFF §2 — UI wiring gets zero tests).

### T8 — Hosts: `VatBakePanel`, `VatBakeWindow`, the Clip Editor's key routing
Files: `VatBakePanel.cs` (layout at `:36-130`, `Bake` tail at `:237-238`, new fields), `VatBakeWindow.cs`,
`ClipEditorWindow.cs:33` (`StyleSheetPath` const) and `:1622-1633` (`VatBake` case). Build §4.3's
"`VatBakePanel` wiring", "`VatBakeWindow`" and "`ResolveActiveTransportTarget`" blocks. Rewrite the
inline-styling comment per A74-D9. No fixture.

### T9 — `VatSampleTentacleUtility` [parallel-safe]
Files: **new** `Editor/ClipUtilities/VatSampleTentacleUtility.cs`. Read `VatTentacleRigBuilder.cs:14-40, 180-189`,
`ClipAsset.cs:25-95, 380-430`, `ClipSetAsset.cs` (the `clips` list), `RigAsset.cs:50-70`, `VatBakePanel.cs:485-494`, and the
game recipe named in §4.3 for the create-or-replace shape. No fixture (asset-writing tests are
brittle; T10 drives it for real and deletes the output).

### T10 — Gate, drive, capture, docs (orchestrator)
1. Full suites (HANDOFF §3 steps 3–4); discovered totals must not drop below T0's.
2. Drive the standalone window over `mcp__UnityMCP__execute_code` (CodeDom C# 6, no `using`
   lines — see `reference_editor_capture_dpi_scale`): `VatBakeWindow.ShowWindow()`; call
   `VatSampleTentacleUtility.CreateSampleAssets("Assets/A74Scratch", …)`; reach the panel's private
   `Bake` by reflection; **in a second call** read the preview `Image.image != null`, the readout
   text, then `Step(5)` and read the readout again — the frame must have advanced. Third call:
   `cameraNavigation`'s rig `CapturePose()` before/after a `Pan` — the focus moved.
3. Open the Clip Editor, switch to the Actor Editor tab with `MaleCitizen.profile.asset`, and in
   two calls confirm the rail elements exist and `Pan` moves the controller's focus.
4. Capture both viewports to `Library/A74Captures/actor-editor.png` and `vat-bake.png`
   (`GrabPixels`, scale by `pixelsPerPoint`).
5. Delete `Assets/A74Scratch` and the scene tentacle; `git status` must show only your files.
6. `CHANGELOG.md`, `package.json` version, HANDOFF §4 paragraph, and the vault note
   `AnimationToolkit.md`: extend "The viewport camera is an orbit rig…" with one line saying the
   state machine now lives in `PreviewCameraNavigation` and three viewports share it.

### T11 — ⏸ owner checkpoint
End the session with this message, verbatim in spirit:

> **Actor Editor:** open `Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset`. In the
> preview: drag to orbit, middle-drag to pan, right-drag to look and hold W/A/S/D + Q/E to fly
> (Shift faster), Alt + right-drag to dolly, wheel to zoom, F to frame, double-click or the top
> rail button to reset. Toggle Billboard and Ragdoll on the rail; play a Death animation and watch
> the Ragdoll toggle light on its own. Switch to the Clip Editor tab and back — the Clip Editor's
> camera must be exactly where you left it.
> **VAT Bake:** `Window ▸ DOTS Animation Toolkit ▸ VAT Bake`. Press **Sample Tentacle**, then
> **Bake VAT Textures**. The tentacle should wave in the preview; Space pauses, ← → step, the Clip
> dropdown lists "VatSampleWave". Turn on **Ghost**: the translucent source should sit exactly on
> the baked mesh through the whole loop. Then try it on a real skinned mesh of yours.
> Judge: does the preview convince you the bake is right, and is the ghost the right way to show a
> mismatch? The layout is yours to change.

## 6. Out of scope (recorded, with why)

- A vertex-flavour preview shader (A74-D6) — no `ToolkitVatVertexFetch` graph exists to reuse.
- Folding `ClipPreviewController`'s orbit maths onto `PreviewOrbitCameraRig` (A74-D2).
- Renaming `clip-editor__overlay-*` to `toolkit-*` (A74-D4).
- Any change to how the runtime binds `_VatTexelParams` (A74-D7) — a documentation gap for
  customers, to be raised in the shader-contract doc separately.

## 7. Build log

**2026-09-08, orchestrating session.** T0 baseline: EditMode 767 discovered (1 pre-existing
Conformance_A asmdef-drift failure, not this amendment's), PlayMode 283 passed. All four waves
(T1/T2/T5/T6/T9 parallel; T3 solo; T4/T7 parallel; T8 solo) landed via Sonnet subagents with no
Unity MCP access of their own, gated and committed by the orchestrator after each wave. Final:
EditMode 777 (+10 new fixtures, same one pre-existing failure), PlayMode 283 (unchanged).

Two real bugs T10's drive-and-capture pass caught that no fixture did (neither wave's subagent had
compiler or Editor access to catch these themselves):
- **Conformance_D** (packaging conformance, not a fixture named in any task): T8's
  `CreateSampleTentacle` fallback hardcoded `"Assets/VatSamples"` when the Output Folder field was
  empty — a host asset folder path baked into package source, which the file's own
  `outputFolderField` tooltip already said not to do. Fixed to fall back to the assigned clip set's
  folder, or refuse and ask for an Output Folder, matching the pattern the rest of the panel already
  follows.
- **Invisible rail controls**, found only by capturing `vat-bake.png` and eyeballing it (the vault's
  "visual-first" lesson earning its keep again): `VatPreviewElement`'s Reset Camera/Ghost rail
  collapsed to ~10×2px. My own T7 brief never told the subagent to add
  `clip-editor__overlay-tool-button` to the button/toggle controls themselves — only their icon
  children got a class — unlike the (correctly specified) Actor Editor rail from T4. Fixed and
  re-captured to confirm.

Smaller drift, all self-corrected by the subagents without escalation:
- T1 flagged that the spec's literal "revert-to-fail: comment out `heldFlyKeys.Clear()`" doesn't
  actually fail `StepFly_MovesOnlyWhileLooking...` (the `!IsFlying` guard alone already blocks the
  second `Fly` call once `ActiveGesture` resets to `None`); it used the `ActiveGesture = Gesture.None`
  line instead and reported the substitution.
- T6 confirmed `VatFlavor.BoneMatrix`/`.VertexPosition` (Runtime/Components/AnimationToolkitEnums.cs)
  matched the spec's assumed names exactly — no drift.
- T7 caught that its own brief's `VatPreviewElement()` constructor snippet built `cameraRig` but
  never wired `cameraNavigation.Rig = cameraRig;` before `AttachTo` — added it; T10 confirmed the
  wiring live (`rigIsWired=True`) before trusting it.
- T9 confirmed `ClipAsset`/`ClipSetAsset`/`RigAsset` all construct via plain
  `ScriptableObject.CreateInstance<T>()` with no required fields beyond what the brief already set,
  and that `VatBakeClip` lives in `VatTextureBaker.cs` as a public struct (T7's brief had described
  it as if privately shaped by `VatBakePanel` — same fields, harmless mis-description, no `using`
  needed since same namespace).

T10 drove both hosts for real over `mcp__UnityMCP__execute_code` (reflection into private fields —
no test-only hooks were added to production code for this): standalone `VatBakeWindow` baked a
freshly-generated sample tentacle, the preview rendered (`imageIsSet=True`), the status line read
correctly once the frame-count/clip-range-count mislabel above was fixed, `Step(5)` moved
`GlobalFrame` 0→5, and `PreviewOrbitCameraRig.Pan` moved the captured pose's focus
(`focusDelta=0.0828`, later `0.4239` after a second-window re-drive). The Actor Editor tab, focused
via `ClipEditorWindow.FocusWithActorEditorTab` on `MaleCitizen.profile.asset`, showed all three rail
elements present and `ClipPreviewController.Pan` moving the shared controller's focus. Two domain
reloads (one from the Conformance_D fix, one from the rail-sizing fix) each discarded the driven
window's in-memory panel state, requiring a full re-drive from the surviving on-disk assets/scene
object rather than the original in-memory references — expected and unremarkable, noted here only
because a future session redoing this kind of live-drive verification should expect the same.

Captures: `Library/A74Captures/actor-editor.png`, `vat-bake.png` (not committed; `Library/` is
generated and gitignored). `Assets/A74Scratch` and the scene tentacle were deleted after capture;
`git status` showed only files this session touched plus the two pre-existing untouched items named
in T0 (left alone as instructed).

No open questions. A74-D6's vertex-fetch preview shader remains explicitly out of scope (§6), as
does folding `ClipPreviewController`'s orbit maths onto `PreviewOrbitCameraRig` (A74-D2) — both
still stand as recorded follow-ups, not gaps found during the build.
