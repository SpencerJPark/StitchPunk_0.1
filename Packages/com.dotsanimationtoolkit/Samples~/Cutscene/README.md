# Cutscene

A minimal host for a staged cutscene: press a key, it plays; press another, it releases a named
holding event; when it ends, the host cleans up after itself.

## Run it

1. Author a cutscene in the Cutscene Editor tab and stage it in a scene via `CutsceneStageAuthoring`
   (see `Documentation~/cutscenes.md`). Note the asset's `StableId` — the Cutscene Editor's cast panel
   shows it.
2. Add `CutsceneSampleHost` to any GameObject in the scene (a manager object is fine — it does not
   need to be one of the cutscene's own actors). Set `cutsceneKey` to the `StableId` from step 1.
3. If the cutscene authors a holding event, set `holdIdToRelease` to that event's exact name; the
   default is `"Dialogue"`.
4. Enter Play mode, press **P** to start the cutscene, **R** to release the hold once the cutscene is
   paused on it.

## What it demonstrates

- **Finding and starting a staged cutscene** — `CutsceneApi.TryFindStage` + `CreatePlayRequestFromStage`
  is the whole recipe; the stage already carries every scene binding the Cutscene Editor's cast panel
  set up.
- **Driving the camera** — `CutsceneCameraPose` is a world singleton the toolkit writes and never
  reads back; this host copies it onto `Camera.main` every frame it is `isDriven`, and does nothing the
  rest of the time so a completed or camera-less cutscene never fights your own gameplay camera.
- **A pathfinding stand-in** — every bound slot with an outstanding `CutsceneMoveToMark` order is
  walked straight toward it, frame by frame. The toolkit only ever judges arrival (or times out and
  places the entity); moving it is entirely the host's job. Swap this for real pathfinding in a real
  game — the straight-line walk here exists only so the sample compiles and does something visible
  without depending on a movement system this package does not ship.
- **Releasing a named hold** — `CutsceneApi.TryGetCurrentHoldId` answers "what is the clock waiting
  for", and a matching `CutsceneHoldRelease` write is the only way to unstick it. A release for the
  wrong id is silently ignored, not an error, so a UI can fire it speculatively.
- **Draining events and cleaning up** — the request's `AnimEventOutput` buffer is cleared once
  `AnimEventsPending` is set (a real game would dispatch each one to sound/dialogue/whatever it names
  instead of discarding it), and the request entity is destroyed the frame `CutscenePlaybackState.isComplete`
  goes true. Nothing here holds a reference to a destroyed entity afterward.

## What it deliberately does not do

No dialogue system, no real movement/pathfinding, no facing integration (`CutsceneFacing`), no attach/
detach handling beyond what the toolkit already does on its own. Each of those is a real host
integration a real game needs — see the **Host integration checklist** in
`Documentation~/cutscene-api.md` for the full list and what each one is for.
