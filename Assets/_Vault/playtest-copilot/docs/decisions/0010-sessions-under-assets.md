# 0010: Session folders live under `Assets/`

Status: accepted (2026-10-01)

## Decision
The default sessions root is `Assets/PlaytestSessions/`, not the project root. It stays gitignored,
and the location is still overridable in settings.

## Why
The owner's first real session landed at `<project root>/PlaytestSessions/` and the immediate
reaction was that it could not be seen or managed from inside the Editor. Unity's Project window
only shows `Assets/`, so a folder beside it is invisible: no selecting, no deleting, no previewing,
and no way to find a recording without leaving Unity.

Under `Assets/` the opposite is true, and better than expected — `audio.wav` imports as an
AudioClip that plays in the Inspector, and `index.md` as a TextAsset that reads there.

## Consequences
Unity imports every session file, which costs import time and a `.meta` per file. That is the price
of being manageable, and it is the right trade for a developer tool whose output the developer is
meant to browse.

Two traps came with it:
- The folder's own `.meta` sits *outside* the ignored folder, so `PlaytestSessions/` alone leaves
  `Assets/PlaytestSessions.meta` tracked. `.gitignore` names it explicitly.
- Files written during play mode do not appear until the asset database is told, so the session end
  calls `AssetDatabase.Refresh()`. Without it the files are on disk and invisible, which is exactly
  the problem this decision set out to fix.
