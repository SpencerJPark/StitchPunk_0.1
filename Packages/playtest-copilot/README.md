# Playtest Copilot

An AI playtest mode for the Unity Editor. Press **AI Play** instead of Play, talk through what you
want changed, pause and circle things on screen, and the session becomes a folder of timestamped
notes with clips, resolved object references, and a markdown file you can paste into any coding
agent.

This is the capture half, built first on purpose: it is useful on its own, and the parts that come
after it are worthless without it. A bad note produces a bad spec no matter how good the model
reading it is.

Spec, decision records and roadmap: `Assets/_Vault/playtest-copilot/`.

## What works today

- **AI Play button** in the main toolbar, beside Play. Normal Play is untouched and records
  nothing — that separation is a hard rule, not a setting.
- **Voice capture** in push-to-talk or always-on mode, rebindable key, with a warning when the key
  clashes with one of the project's own Input System bindings, and an on-screen record button for
  when it does.
- **Pause and draw**: freeze the game, circle or arrow what you mean, type a short note, resume.
  Frame, drawing and combined image are all saved.
- **Reference resolution**: each note carries the objects it was about, scored across six signals
  (what you circled, what was under the cursor, what was selected in the Editor, what you named
  out loud, what was near the camera, what you last interacted with) with a confidence per result.
- **State capture** at 4 Hz: camera and player transforms, nearby objects with their hierarchy
  paths and prefab sources, and any field you tag with `[PlaytestTrack]`.
- **A session folder** of plain files, readable without this tool installed, plus `index.md` to
  review a session with Unity closed and `session-for-agent.md` to hand to a coding agent.

Not built yet: transcription, LLM spec drafting, task splitting and agent dispatch. Notes carry
your typed text and the resolved references; the spoken audio is saved to `audio.wav` for the
transcription step that comes next.

## Install

The package is embedded at `Packages/playtest-copilot/`. Unity 6.4 or later.

## Use it

1. Press **AI Play** in the toolbar.
2. Hold the record key (backtick by default) and say what is wrong.
3. Press **F2** to freeze and circle something, type a note, press Enter.
4. Stop play. The session folder is written and its path is logged to the console.

Settings are under **Project Settings → Playtest Copilot**.

## Tagging your own values

```csharp
public class PlayerController : MonoBehaviour
{
    [PlaytestTrack] public float JumpHeight = 2.5f;
    [PlaytestTrack("gravity")] public float GravityScale = 1.0f;
}
```

Tagged members are read into every state snapshot and land in each note, so "this jump feels
floaty" arrives at an agent with the numbers attached.

## The session folder

```
Assets/PlaytestSessions/2026-09-28_1808_Level1/
  session.json           Unity version, scene, git commit, settings
  audio.wav              the full microphone recording
  state.jsonl            one JSON object per state sample
  index.md               the readable session summary
  session-for-agent.md   paste this into a coding agent
  notes/note_014/
    note.json            transcript, references, state at that moment
    frame.png            the frozen frame
    annotation.png       the drawing alone, transparent
    annotated.png        the two combined
    references.md        resolved objects with paths and confidences
```

Everything a person or an agent needs is in those files. If something is only visible inside the
Editor window, it is a bug.

Sessions live under `Assets/` so they appear in the Project window and can be opened, moved and
deleted without leaving Unity — `audio.wav` even imports as an AudioClip you can play from the
Inspector. They are gitignored. Find them with **Tools > Playtest Copilot > Open Last Session
Folder**, or point the root somewhere else in Project Settings.

## Building on it

`Documentation~/BUILD_CONTRACTS.md` is the surface map: the shared types, the capture bus and what
each file owns. The bus is the seam — runtime recorders raise, Editor writers subscribe — and
nothing should cross it any other way.

With no Editor open, `bash Tools~/compile-gate.sh` compiles all three assemblies against the
Unity install's own Roslyn. It must print `GATE PASS`. It sees C# errors only, so it is a floor,
not a replacement for opening the Editor.
