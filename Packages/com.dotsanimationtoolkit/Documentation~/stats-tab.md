# The Stats tab

**Window ▸ DOTS Animation Toolkit ▸ Clip Editor** — the Stats tab.

Reads the running game world four times a second and shows what the toolkit
is doing right now: how many actors it is driving, how the LOD system is
bucketing them, how many events fired, how much VAT texture memory is live,
and how much time each toolkit system group is spending per frame. Use it to
sanity-check a scene's actor count, catch a runaway LOD bucket, or grab a
snapshot for a bug report.

---

## When it reads

The tab only reads in Play mode, from `World.DefaultGameObjectInjectionWorld`
— the world the toolkit runs in for a normal game. In Edit mode every number
is a dash and the footer tells you to enter Play mode.

It polls four times a second from the editor's own update, never on every
editor frame. Each poll only reads existing data: nothing in the runtime
changes and no counters are added to any system to make this work.

## Actors box

- **Actors** — entities carrying a `PlaybackLayer` buffer.
- **Layers** — every actor's `PlaybackLayer` length, summed.
- **Ragdolling** — actors whose `RagdollActor` is enabled.
- **In cutscene** — entities carrying `CutscenePlay`.
- **LOD 0–3** — actors bucketed by `AnimLod.level`: 0 is full rate, 1 is half
  rate, 2 is quarter rate with snapped blends, and 3 is a frozen pose. The
  bucketing works from a copy of every actor's `AnimLod`, which costs about
  0.005 ms for 1,000 actors.

Above 100,000 actors only the first 1,024 are bucketed and the histogram is
marked **sampled** — the shares it shows are an estimate, not an exact count.

## Events / frame box

A sparkline holds the last 60 polls, which is 15 seconds of history. "Now" is
the latest poll and "peak" is the largest value seen across those 60 polls.

- **Events this frame** — the `AnimEventOutput` entries summed over every
  actor whose `AnimEventsPending` is enabled. This is last frame's pulse of
  events as seen at poll time, so a burst of events that fires and clears
  between two polls is never seen.
- **Pending actors** — actors with `AnimEventsPending` enabled.
- **Actors with windows open** — actors whose `AnimEventMask` is enabled.
  This counts actors, not the windows themselves.

## VAT box

- **Parts bound** — entities carrying a `VatPartTextureBinding`.
- **Textures** — the distinct position/bone and normal textures those parts
  reference.
- **Texture memory** — `Profiler.GetRuntimeMemorySizeLong` summed over those
  distinct textures. In the Editor this can include a CPU-readable copy of
  each texture, so the number reads higher than it would in a player build.

## Timing (ms) box

Shows the toolkit's top-level system group and its four child groups —
Binding, Logic, Presentation, and Ragdoll — each averaged over the last 8
frames. Ragdoll runs inside Presentation, so its time is part of
Presentation's total, not added on top of it. The toolkit group's own time
includes all of its children.

Timings come from `ProfilerRecorder`s attached to the systems' own profiler
markers, so no Profiler window needs to be open for the numbers to appear.
If the recorders cannot attach to a marker, that cell reads "unavailable"
instead of a number.

## Snapshot

The Snapshot button copies a Markdown table of every row above to the
clipboard, along with the package version, the current time, and the world
name — ready to paste into a bug report or a devlog. It does not write a
file.
