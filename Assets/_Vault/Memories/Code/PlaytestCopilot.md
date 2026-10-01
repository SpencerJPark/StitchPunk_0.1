# Playtest Copilot

`Packages/playtest-copilot/` — the fourth embedded package. An AI playtest mode for the Editor:
press AI Play instead of Play, talk and circle things, and the session becomes a folder of notes
with resolved object references and a markdown file to hand a coding agent.

Product spec and decision records: `Assets/_Vault/playtest-copilot/`. Surface map for anyone
editing the package: `Packages/playtest-copilot/Documentation~/BUILD_CONTRACTS.md`.

Built 2026-09-29 as 0.1.0 on branch `playtest-copilot`, in a worktree, by fourteen parallel
Sonnet workers against a pinned contract sheet. Capture MVP only — no transcription, no LLM, no
task dispatch.

## The shape

Two assemblies with one seam between them.

- `PlaytestCopilot.Runtime` holds everything that must run inside play mode: the recorders, the
  IMGUI overlays, the reference resolver, `[PlaytestTrack]`.
- `PlaytestCopilot.Editor` holds the toolbar button, the settings, and every file writer.
- They only ever meet at `PlaytestCaptureBus`: runtime raises, Editor subscribes. Nothing crosses
  any other way, which is what lets the recorders work with no Editor and the writers be tested
  with no play mode.
- `PlaytestSessionBootstrap` is the only file that knows both halves. It builds the descriptor,
  opens the folder, spawns `PlaytestRecorderHost`, and tears all three down.

## Traps

**Bus subscription order decides whether a note has any content.** The Editor's folder writer
subscribes at domain load; the recorder host is created later, at play start. A marker raised
straight to `MarkerCompleted` reaches the writer before anything has filled its references or
state, and `note.json` is written empty — with no error anywhere. That is why the bus has a
separate `MarkerEnriching` event that `RaiseMarkerCompleted` fires first. If you add another
producer of markers, raise through the bus, never call subscribers directly.

**The annotation arrives before its marker.** You draw, then keep talking, so the drawing is
captured while the utterance is still open. `PlaytestSessionFolderWriter` handles both orders
explicitly, stashing circled regions against a marker id that has not appeared yet. Any new
consumer of `AnnotationCaptured` has to do the same.

**IMGUI's origin is top left; every screen region is bottom left.** The overlay does the flip when
it builds a `PlaytestStroke`. Get it wrong and nothing looks broken — the resolver just raycasts
through the mirrored part of the screen and every reference comes out wrong.

**`ScreenCapture.CaptureScreenshotAsTexture()` outside end-of-frame returns black or torn.** The
overlay grabs the frame from a coroutine after `WaitForEndOfFrame`.

**The frame clock is `realtimeSinceStartupAsDouble`, never `Time.time`.** Pause-and-draw zeroes
`timeScale`; a marker must still be able to close and the streams must stay in sync.

## Compile gate with no Editor

`bash Packages/playtest-copilot/Tools~/compile-gate.sh` compiles all three assemblies with the
Roslyn inside the Unity install. Must print `GATE PASS`. Three things it took to get working, all
of which will bite again if the script is rewritten:

- `Managed/UnityEngine/` already contains `UnityEditor.dll`. Referencing the copy one level up as
  well makes every forwarded Editor type ambiguous (CS0433).
- A worktree has no `Library`, so nunit and the TestRunner are borrowed from the main checkout,
  found via `git worktree list`.
- Unity's nunit is built against `mscorlib` while everything else resolves through `netstandard`,
  so the Tests pass needs the `NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll` facade or every
  `[Test]` attribute fails with CS0012.

It is a floor, not a substitute: CS errors only, never an import setting, a bake, or anything that
needs the Editor running.

## Verified platform facts

**The main toolbar factory must return a concrete `MainToolbarElement`, not a `VisualElement`.**
Returning a `VisualElement` compiles cleanly and then fails at load with
`Methods with MainToolbarElement attribute must return MainToolbarElementData` — a type that does
not exist; the real base is `UnityEditor.Toolbars.MainToolbarElement`. The only symptom is a
toolbar with no button on it, so a clean compile proves nothing here. The concrete subclasses are
`MainToolbarButton`, `MainToolbarToggle`, `MainToolbarDropdown`, `MainToolbarLabel` and
`MainToolbarSlider`, each taking a `MainToolbarContent` (which has ctors for text, image, tooltip
and combinations). `MainToolbarToggle` has **no settable `value`** — only a private `m_Value` — so
the on state has to live outside it and seed the constructor on each domain reload.

The attribute itself is `[MainToolbarElement("path", defaultDockPosition = ..., defaultDockIndex =
...)]` on a **static** method. Those are property initialisers with `=`, not named constructor
arguments with `:` — the constructor takes the path and nothing else. `MainToolbarDockPosition` is
`Left | Right | Middle`, and **Middle is the zone holding Play/Pause/Step** — Left is the far-left
tools zone. A dock default applies only the first time an element registers, so changing it later
will not move a button that is already placed. `MainToolbar.Refresh(path)` rebuilds one element
without an Editor restart.

Because the package's minimum is Unity 6.4 there is no reflection-based toolbar injection to
write, despite what the spec says.

**The AssetDatabase addresses a package by its package name, not its folder name.** This package
lives in `Packages/playtest-copilot/` but loads from `Packages/com.playtestcopilot/...`, and the
folder path silently returns null. Do not hardcode either: ask
`PackageManager.PackageInfo.FindForAssembly(typeof(T).Assembly).assetPath`, which is what
`PlaytestIconLoader` does.

## What is not done

Nothing here has run inside a live Editor. No transcription (`audio.wav` is recorded,
`transcript.md` is not written), no video and so no `clip.mp4`, no Entities state backend, no LLM
spec drafting, no task splitting or agent dispatch.
