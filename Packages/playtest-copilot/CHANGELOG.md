# Changelog

All notable changes to Playtest Copilot are recorded here. This project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-29

The capture MVP: Milestone 1's recording half, end to end and fully local. No network call of any
kind, and no LLM.

### Added

- **AI Play toolbar button** next to Play, using the Unity 6.4 main toolbar element API, with the
  play-plus icon in light and pro skins at both densities. Normal Play stays a plain play session
  that records nothing. The capture flag lives in `SessionState` so it survives the domain reload
  that entering play mode causes.
- **Voice capture** in push-to-talk and always-on (voice activity detection) modes, at 16 kHz mono
  into a ring buffer written to `audio.wav`. Markers shorter than a quarter second are discarded
  as key bounce.
- **Rebindable record key** defaulting to backtick, with a live warning in Project Settings when it
  collides with a binding in one of the project's `.inputactions` assets, and an on-screen record
  button for developers whose key clashes or who have no keyboard hand free.
- **Pause-and-draw overlay**: circle, pen, arrow and eraser over a frozen game view, with a typed
  note field. Saves the frame, the drawing on transparency, and the two combined.
- **Reference resolution** across the spec's six signals, with a base confidence per signal, a
  bonus when signals agree, and a 0.98 cap. Each note carries its candidates and what resolved
  them.
- **GameObject state backend** sampling at 4 Hz: camera and player transforms, velocity, nearby
  objects with hierarchy paths and prefab sources, and every `[PlaytestTrack]` member found on
  them. The reflection member list is cached per type.
- **`[PlaytestTrack]`** attribute for opting a field or property into every state snapshot.
- **Session folder** in the documented layout, written as it happens: `session.json`,
  `state.jsonl` through a single long-lived writer, and a `notes/<id>/` folder per note.
- **`index.md`**, the readable session summary with images and resolved references, and
  **`session-for-agent.md`**, the single markdown file to paste into a coding agent, which marks
  low-confidence references as uncertain rather than presenting a guess as fact.
- **Project Settings page** at *Project Settings → Playtest Copilot* for voice, capture, state
  backends and the session folder location.
- **`Tools~/compile-gate.sh`**, an offline compile gate that drives the Roslyn inside the Unity
  install so the package can be built and checked with no Editor open.

### Known gaps

- Transcription is not wired up: `audio.wav` is recorded but `transcript.md` is not written, so a
  note carries its typed text and references but not its spoken words.
- No video recording and therefore no `clip.mp4` per note. The frozen frame and the combined
  annotation are the visual evidence for now.
- The Entities state backend is stubbed off in settings and arrives in Milestone 1b.
- Nothing in the package has run inside a live Editor yet. It passes the offline compile gate;
  the play-mode pass is owed.
