# Preview Viewports — the Actor Editor gets the Clip Editor's camera, the VAT Baker gets a viewport

> **Status:** 📋 specced 2026-09-08, not built.
> **Spec:** [`Amendment_A74_PreviewViewports_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A74_PreviewViewports_Spec.md).
> **Session prompt:** [`Amendment_A74_PreviewViewports_Prompt.md`](../../../../Docs/AnimationToolkit/Amendment_A74_PreviewViewports_Prompt.md).
> **Executor:** one Editor-connected orchestrator running the gate; Sonnet subagents edit files in
> four waves (five run at once in wave 1) and never touch MCP. Waves and parallel-safety are the
> spec's §5.

## What it delivers

- **One camera for three viewports.** The gesture state machine in
  `ClipEditorWindow.CameraNavigation.cs` becomes `PreviewCameraNavigation`, driving any
  `IPreviewCameraRig`. `ClipPreviewController` is one rig (unchanged maths); a new
  `PreviewOrbitCameraRig` is the other, for viewports that have no controller.
- **Actor Editor:** orbit / pan / look + fly / dolly / zoom / `F` / double-click reset, and the Clip
  Editor's rail **minus Move/Rotate/Scale** — Reset Camera, Billboard, Ragdoll. Tab switches now
  restore the Clip Editor's whole camera pose, not just yaw and pitch.
- **VAT Bake:** a live viewport beside the form renders the baked `runtimeMesh` through the
  shipped `ToolkitVatCrowdUnlit` graph, driven by the set just written — transport, clip picker,
  frame readout, the same camera, and a translucent **source ghost** overlaid so a bake that drifts
  from its source shows as a double image. A **Sample Tentacle** button makes the project's first
  VAT-bound content so the owner can see it in a minute.

## Owner calls recorded in the spec (§2) — do not re-ask

No gizmo modes in the Actor Editor; the Ragdoll toggle is a manual override the composer also
writes; bone flavour only (no vertex-fetch shader ships); the ghost is a preview-scene copy, never
the live scene object; the VAT preview owns its own `PreviewRenderUtility`; the standalone
`VatBakeWindow` joins the shared stylesheet.

## Open after the build

⏸ **T11 owner checkpoint** — the spec's §5 T11 message says exactly what to open and press.
