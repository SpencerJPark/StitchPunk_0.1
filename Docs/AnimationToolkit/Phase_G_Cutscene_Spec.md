# Phase G — Cutscene Editor

Owner Q&A recorded 2026-08-29. §2–§4 are the owner's product calls, verbatim in intent — flag a
written amendment before changing any of them. §7 decisions are recorded under the standing
delegation directive (architecture calls are the session's to make and record); flag before G1
lands if any is wrong.

## 1. What this is

The `tab-cutscene-editor` placeholder becomes a real editor: a timeline that stages **multiple
actors in a real scene** — clip blocks and keyframes on the same lanes — plus a camera and an
event lane, baked to a blob and played back in-game by an ECS player. Authoring in the editor,
playback at runtime; **runtime is the real goal, the editor exists to author it.** Design the data
for baking from day one.

Toolkit feature, fully generic: no Stitch Punk types anywhere in the package. The game consumes it
the way a customer would.

Envelope: no designed-in caps on length or actor count, but the editor UX is optimized for short
vignettes (seconds to ~30s, 1–5 actors).

## 2. Product calls — the timeline (owner, 2026-08-29)

- **One group per actor, with sub-tracks:** a clip lane (which clip is playing), a root-motion
  lane, a facing lane, and per-part key lanes whose keys layer *over* the playing clip (Override
  composition, like `ApplyHeldTargetPose`).
- **Clip blocks:** a block names a clip, a duration, and loop on/off ("walking clip on loop").
  **Overlap = crossfade** — dragging two blocks to overlap makes the overlap the blend window,
  cross-fading with the existing layer-blend math. Blocks that merely touch are a hard cut.
- **Root keys move the actor; clips play in place.** The root lane keys position/rotation through
  the scene; the walk cycle never advances the actor itself. (Foot-slide on a speed mismatch is
  the authoring feedback loop, not a system's job to prevent — a later nicety may warn or
  auto-scale clip rate to travel speed.)
- **Facing is automatic with override keys.** Default: derive facing from root travel direction
  through the actor's `DirectionSetAsset` + `FacingResolver` — the runtime path, exactly as the
  Direction Sets pane already does. A facing lane holds explicit override keys ("face the camera
  during this line").
- **Camera lane, smooth by default.** The owner's words: the game uses Cinemachine, and the
  cutscene camera should feel "like one camera just moving around the scene" — continuous keyed
  movement in and out, with **optional hard-cut markers** for the exceptions. The toolkit lane is
  a plain keyed camera (position/rotation/FOV); feeding it into Cinemachine is the host's job (§4).
- **Event lane** using the existing `AnimEventKeyRegistry` vocabulary — host reads the emissions
  for sound, dialogue, quest triggers, same contract as clip events.
- **Hold points are first-class, designed in from the start.** A hold marker pauses the clock when
  the playhead reaches it — looping clips keep cycling, the camera holds its shot — until the host
  releases it (dialogue advanced, button pressed). Cutscene length is therefore elastic; the data
  model must never assume a fixed end time.
- **Prop slots:** a slot may be a plain transform target with no rig and no clip lane — just
  position/rotation/scale key lanes. Doors, crates, lights.

## 3. Product calls — scene and binding (owner, 2026-08-29)

- **Named actor slots.** The cutscene asset defines abstract slots ("Bertha", "Minion A"), each
  pinning a `RigAsset` + `ClipSetAsset` list (+ optional `DirectionSetAsset`). The same cutscene
  can be recast.
- **Authored in the real Scene view.** The tab shows **timeline + inspector only**; Unity's own
  Scene view is the viewport. Scrubbing the playhead poses the bound actors (and previews the
  camera) there. No embedded 3D pane, no second `PreviewRenderUtility` client.
- **The cutscene remembers its scene.** The asset stores a scene reference; opening the cutscene
  offers to open that scene (prompting to save). Slot→GameObject bindings are stored per-scene and
  reconnect. Wrong scene open = clear warning, and timing edits still work.
- **Keys are authored with Scene-view gizmos** at the playhead — move the actor or a rig part,
  press Key (or auto-key), same interaction family as Rig Edit and Unity Timeline recording.
  Camera keys also from "align to Scene view camera".

## 4. Product calls — runtime (owner, 2026-08-29)

- **Actors stay where the cutscene left them.** The player writes final root transforms and
  releases; gameplay resumes from there. **Skip jumps to the final frame first**, so world state
  is identical skipped or watched.
- **Controls: skip, pause/resume, speed scale**, plus hold-point release. All host-driven.
- The host resolves slots to entities and applies the camera pose (Cinemachine, in Stitch Punk's
  case). The toolkit never touches `Camera.main` and spawns nothing.

## 5. Data model

New asset `CutsceneAsset` (`Authoring/Assets/`, like `DirectionSetAsset`):

- `sceneGuid` + `scenePath` (display) — the remembered scene.
- `slots`: list of `CutsceneSlot` — `name`, `slotId` (stable hash), `kind` (Actor | Prop);
  actors add `rig`, `clipSets`, `directionSet`.
- Per-slot lanes: clip blocks (`clipId`, `start`, `duration`, `loop` — overlap with the previous
  block is the blend window), root keys, facing override keys, per-part transform keys
  (addressed by target **tag**, per the Phase E/F rules — a slot recast to a different rig keeps
  its keys wherever tags line up, T2-lenient).
- Camera lane: pose+FOV keys, cut markers. Event lane: `(time, eventKey)` markers, each with
  `fireOnSkip` (default on). Hold markers: `(time, holdId)`.
- **Editor-only scene bindings ride in the asset as strings**: per scene GUID, a
  `slotId → GlobalObjectId.ToString()` map. Writing a string needs no `UnityEditor` reference, so
  `Authoring/` stays clean (Conformance_C); only editor code parses it.

Bake: `CutsceneBlob` via a builder beside `ClipRegistryBuilder` — clip blocks resolve `clipId`
against each slot's `(rig, clipSets)` bind key, so a cutscene rides the same registry blobs the
actors already use. The timeline bakes as **segments split at hold points**; the runtime clock is
`(segmentIndex, timeInSegment)`, which is what keeps "elastic length" from infecting every time
lookup.

## 6. Runtime player

New components + systems in the toolkit runtime assembly:

- Host creates a request entity: `CutscenePlay` (blob ref, speed) + a `CutsceneActorBinding`
  buffer of `(slotId, Entity)` the host fills — explicit casting, no discovery magic. Control via
  `CutsceneControl` (pause, speed, skip flag) and `CutsceneHoldRelease`.
- The player drives bound actors **through the existing playback machinery** — clip blocks become
  layer plays (crossfade = the seam's overlap), cutscene keys compose as an Override layer, root
  keys write the entity transform. No second animation pipeline; if the player needs something
  `ClipSampler`/`PlaybackLayer` can't express, that's an amendment, not a workaround.
- Camera output: the player writes a `CutsceneCameraPose` singleton (position, rotation, FOV,
  `isCut`); the host applies it. Events emit through the same output shape clip events use.
- End/skip: write final transforms, remove cutscene layers, fire remaining `fireOnSkip` events,
  release the actors.

## 7. Decisions (recorded per the delegation directive; flag before G1 lands if any is wrong)

- **G-D1** Scene-view scrub preview is **non-destructive**: entering preview captures every bound
  GameObject's transform state, and leaving it (tab switch, scene save, close, playhead off)
  restores it exactly. Timeline-style preview mode, never dirty-the-scene-and-undo.
- **G-D2** Editor code lives in `Editor/ClipEditor/Cutscene/`; the timeline is a new block-based
  lane control, *not* a retrofit of the Clip Editor's key-lane stack (blocks, groups and elastic
  holds are a different animal than key rows — sharing USS tokens yes, sharing `RebuildTimeline`
  no).
- **G-D3** Facing overrides key a direction *angle* (the Direction Sets pane's 0–360° model), and
  the resolver path is shared with that pane — one `FacingResolver` call site family.
- **G-D4** `fireOnSkip` defaults on; per-event opt-out. A skipped cutscene must leave the same
  world state as a watched one unless an event explicitly says otherwise.
- **G-D5** Decided at G1: `CutsceneHoldMarker.holdId` is a plain `string`, not an
  `IVocabularyRegistry` vocabulary. A tag or event key needs the registry machinery because it is
  resolved against a shared, dense-indexed vocabulary at bake and preview time; a hold id is never
  resolved against anything — a host compares it for equality exactly once, against a
  `CutsceneHoldRelease` it wrote itself. The dropdown-only-selection/duplicate-guard/codegen
  machinery exists to keep a *shared* vocabulary from drifting, and there is no shared resolution
  step here for it to protect.
- **G-D6** Slot resolution at runtime is the host's job via the binding buffer (§6). The toolkit
  ships no component that marks "this entity is Bertha".
- **G-D7** Decided at G4: a camera cut marker splits the lane into independent interpolation
  windows rather than describing a blend shape of its own. Sampling at time *t* only considers
  keys inside `[lastCutAtOrBefore(t), nextCutAfter(t))`; a window with no key of its own holds the
  last key before it opened. This is what "one camera just moving around the scene, with optional
  hard-cut markers for the exceptions" (§2) reduces to without inventing a second blend concept
  beside crossfade — the same `isCut` flag the runtime `CutsceneCameraPose` singleton exposes (§6)
  is true exactly on the marker's own frame.

## 8. Out of scope

- Sound while scrubbing (standing HANDOFF directive: not on the queue).
- Branching, interactivity beyond hold points, multiple concurrent cutscenes on one actor.
- Cinemachine, dialogue, quest systems — host side, reached only through events and the camera
  pose singleton.
- Clip root-motion curves (the root lane is the movement authority).
- An embedded scene-rendering viewport.

## 9. The queue

Build order; each step gates (compile + touched fixtures) and commits before the next.

1. **G1 — data model.** `CutsceneAsset`, slots, lanes, holds, serialization. Save/reload proof
   against a real asset (HANDOFF §3 rule). No UI.
2. **G2 — the tab.** Slot list + inspector, scene remember/open/warn flow, per-scene bindings,
   block timeline (add/move/resize/overlap), lanes and groups. Replaces the placeholder in
   `ShowCutsceneTab`.
3. **G3 — Scene-view preview + keying.** Non-destructive scrub posing bound actors (G-D1),
   gizmo keying with auto-key, facing resolution in preview.
4. **G4 — camera lane.** Keys, cut markers, align-to-Scene-view, scrub preview of the shot.
5. **G5 — bake.** `CutsceneBlob`, segment split at holds, validation (unresolved clip/tag rules
   inherit Phase F's lenient-vs-error split).
6. **G6 — runtime player.** Request/binding/control components, playback through existing
   machinery, camera singleton, events, skip/pause/speed/hold-release. PlayMode tests on the
   player's contract (end-state parity between skip and play-through is *the* test).
7. **G7 — prop slots + docs** (`Documentation~/cutscenes.md`, CHANGELOG).

## 10. Risks

- **G3 is the hard one.** Posing live scene GameObjects from an EditorWindow, non-destructively,
  with prefab instances and undo in play — this is where Unity Timeline spends most of its
  complexity. Budget accordingly; G2 must not assume G3's shape.
- The Clip Editor tab machinery gates `UpdatePreview` on `activeTab == ClipEditor`; the cutscene
  tab must keep that true (it renders nothing into the shared `ClipPreviewController`).
- Elastic time (holds) touches every "time → what's playing" lookup. The segment model (§5) is
  the containment; any code doing raw `cutsceneTime * frameRate` math is a bug.
- The window dies on domain reload (see the AnimationToolkit memory note): every piece of
  cutscene tab state needs the same `[SerializeField]` session treatment as `sessionClipSet`.
