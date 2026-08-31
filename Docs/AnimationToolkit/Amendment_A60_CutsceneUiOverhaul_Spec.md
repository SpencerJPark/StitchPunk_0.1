# Amendment A60 — Cutscene Editor UI Overhaul

Raised 2026-08-30, owner verdict on the A58-era tab: *"currently this looks pretty bad ui wise …
this is unusable"* — with a directive to research what makes a cutscene editor good and deliver a
professional product. Built together with A59-T1 (the in-tab scene viewport) in one pass, because
the missing viewport **was** the biggest UI defect.

## 1. Method — how this pass avoided the last three misses

1. **Looked first.** The window was captured *from inside the editor* before any edit
   (`GUIView.GrabPixels` via reflection — occlusion-proof, unlike `ReadScreenPixel`, which
   returned whatever app covered those screen pixels). The capture confirmed the owner's report:
   a 240px ruler floating in void, unstyled text lanes, dead gray where the scene should be.
2. **Researched the convention.** Unity Timeline and Unreal Sequencer share one shape: outliner
   track headers left on a darker ground, full-width labeled time ruler, saturated rounded clip
   blocks, icon transport, the viewport as the star. That shape was adopted wholesale rather than
   invented. (Sources: Epic's Sequencer Editor reference, Unity Learn's Timeline introduction,
   Unity's Sequences timeline docs.)
3. **Verified by capture again after building** — the same GrabPixels loop, plus a programmatic
   smoke of session-restore, scrub, RT content, and camera-leak count.

## 2. What changed

**A59-T1 landed — the scene renders in the tab.** `CutsceneViewportElement` (new): hidden
`HideAndDontSave` camera + RenderTexture, rendered through URP's `SingleCameraRequest`
(`RenderPipeline.SubmitRenderRequest`; legacy `Camera.Render` fallback for non-URP), orbit/pan/
wheel navigation, `F`/Frame to frame the cast, and a **Shot** toggle that locks the view to the
camera lane via `SampleCameraWithCuts` — dragging in Shot mode adopts the rendered pose into the
free rig and unlocks, so the view never jumps. Overlay states live *in the viewport*: no cutscene,
no remembered scene ("Remember Current Scene"), wrong scene ("Open Scene"). The tab layout is now
cast | viewport | inspector over the timeline (nested `TwoPaneSplitView`s). "Preview Shot" became
opt-in "Drive Scene View" — the tab shows the shot regardless, so yanking the author's Scene view
camera on every scrub stopped being the default. The Editor asmdef gained
`Unity.RenderPipelines.Universal.Runtime` (no new dependency — package.json already requires URP).

**Timeline made legible** (USS section in `ClipEditorWindow.uss` + `CreateRow` classes):
- Lanes and ruler always reach the visible edge (`max(content, viewport width)`) — the floating
  ruler strip was the worst single defect.
- Outliner headers: darker header column with a right border, group rows (slot names, Camera,
  Events, Holds) bold on a raised ground, per-kind accent strips (actor blue, prop green, camera
  teal, events orange, holds yellow), indented child labels.
- **Dead rows deleted**: a part track is one row (label = header, keys beside it); Camera/Events/
  Holds group labels sit on their own first lane instead of an empty spacer row. Clicking a part
  row's header selects the track (`PartTrackKey` with item −1 routes to the track inspector).
- Clip blocks: rounded, saturated blue (loop = green + "⟳" in the label), yellow selection border.
  Keys are diamonds (`rotate: 45deg` on the marker class). Selection is a row modifier class,
  never an inline color.
- Scrubbing no longer rebuilds the timeline per pointer-move — the playhead repaints itself.

**Transport made real**: icon buttons (first/play–pause/stop/last, `d_Animation.*`/`d_PlayButton`
family — `EditorGUIUtility.IconContent` is UI-Toolkit-legal; Conformance_E bans only
OnGUI/GUILayout/Handles), a boxed `0.00 / 12.00 s` readout updated per scrub with no relayout,
Toggle `text` instead of the ~150px inspector label column, Key button carries the record icon.

**Recompile survival**: the open cutscene now rides `SessionState` (`RestoreSessionCutscene`) —
the tab previously came back empty from every domain reload, presenting as a dead tool.

## 3. Verified (2026-08-30, live editor)

Compile gate clean at each of four checkpoints. Post-reload smoke, by reflection against the real
window: session restore returned `NewCutscene` unaided; `SetPlayhead(0.5)` ran the scrub path;
the viewport RT measured 4058/4096 non-black center pixels (the scene is really there); exactly
one hidden viewport camera existed after reloads (the leak sweep uses
`Resources.FindObjectsOfTypeAll` — `GameObject.Find` cannot see `HideAndDontSave` objects).
Full suites owed once, at the commit point, per HANDOFF §3 cadence.

## 4. Still owed (the A59/A60 backlog, in order)

1. **Owner's eyes** — this amendment exists because green gates kept hiding visual defects.
2. A59-T2: click-select in the viewport (ray-pick the cast, sync selection).
3. A59-T3: in-viewport translate/rotate gizmo + Key.
4. Cast panel compaction (rows are still field+three-buttons tall) and inspector styling.
5. Frozen (non-scrolling) track header column — G2's recorded cut, now more visible since the
   lanes pan under full-width headers.
6. Right/alt navigation parity with the Clip Editor viewport (look-around, fly), zoom-to-playhead.
