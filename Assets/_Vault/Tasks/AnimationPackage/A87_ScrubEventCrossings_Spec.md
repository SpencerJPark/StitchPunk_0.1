# Amendment A87 — Scrub crossings and sound on scrub

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.34.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Lifts** HANDOFF §5's "not on the queue — do not start it" on the owner's 2026-09-10 instruction
> to spec it. Sound *mixing* remains out of the package (roadmap §2).
> **Predecessors:** A83 (the timeline pane owns the playhead), A86 (one inspector for the preview
> clip row).
> **Executor:** one orchestrator; `worker` subagents in **one wave of five**, each ≤ 2 files. **T0
> contains a platform probe the orchestrator must run before the wave** (D3).

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A87** on the DOTS Animation Toolkit package (head `0.33.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A87_ScrubEventCrossings_Spec.md`. Read it, the roadmap
§3 protocol, then only what §3 here names. **Run T0's probe before spawning anything** — D3's
outcome changes T4's brief. T1 yours; one wave (T2–T6); one gate; T7–T9 yours. Stop at T10.

---

## 1. Goal

The Clip Editor's scrub path poses through `ClipPreviewController.SamplePose(clipId,
normalizedTime)` and never runs `EventEmissionSystem` — events are play-time ECS. So an animator
placing a footstep hears nothing and sees only a pin. After this amendment: a pure
`ScrubEventCrossingResolver` compares playhead-before with playhead-after and reports the markers
crossed (wrap-aware for looping clips); the timeline flashes the crossed pin; and, when the
registry entry for that key names an editor-only preview `AudioClip`, the editor plays it. Playing
and scrubbing both fire it; stepping by frame fires it once per crossing.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A87-D1 — Crossing is a pure function over normalized time.** `(previous, current, loop,
  markers) → crossed indices`. Forward scrub crosses markers in `(previous, current]`; backward
  scrub crosses `[current, previous)` — a marker is heard when dragged over in either direction. A
  wrap (looping clip, `current < previous` while playing forward) crosses `(previous, 1]` then
  `[0, current]`. A jump larger than half the clip while **not** playing is treated as a seek, not a
  scrub: nothing fires. ⚠ (The alternative, firing every marker skipped, is the audio-editor
  convention; the owner chooses at the checkpoint.)
- **A87-D2 — The preview clip lives on the registry entry**: `AudioClip previewClip` on
  `AnimEventKeyEntry`. The registry is already "authoring only — never baked, never read at
  runtime" (its own summary), and it lives under `ProjectSettings/`, so a player build never sees
  the reference. A marker has no per-marker sound; per-key is the granularity.
- **A87-D3 — Editor audio playback is probed, not assumed.** Three candidates, in order:
  (1) `UnityEditor.AudioUtil.PlayPreviewClip` via reflection (what the Inspector's clip preview
  uses); (2) an `AudioSource` on a hidden `HideFlags.HideAndDontSave` GameObject with
  `AudioSource.PlayOneShot` — works in edit mode only when the Editor's "Mute Audio" is off and an
  `AudioListener` exists; (3) none, in which case the amendment ships the crossing flash and the
  data field and the sound waits. T0 runs (1) then (2) over `execute_code` against a real clip and
  records which produced sound. T4 is briefed with the winner.
- **A87-D4 — One `EditorEventPreviewPlayer`** (plain noun, allowlist) owns whichever mechanism won,
  exposes `Play(AudioClip clip, float volume)`, and rate-limits the same clip to once per 40 ms so
  a fast drag does not machine-gun.
- **A87-D5 — The flash is the pin's colour at full alpha for 120 ms**, falling back to the lane
  colour — no new colour token.
- **A87-D6 — Cutscenes get the same** through the cutscene timeline's playhead, since A86 made the
  lanes share their drawing. If the cutscene playhead path is not reachable in two files, log it
  and leave it for a follow-up.

---

## 3. Read first

- `Runtime/Sampling/EventWrapMath.cs` in full — the runtime's wrap rule; the editor resolver must
  agree with it (same marker crossed in the same wrap).
- `Runtime/Systems/EventEmissionSystem.cs` lines 39–130 — what "crossed" means at run time.
- `Editor/ClipEditor/Panes/TimelinePane.cs` — grep `PlayheadNormalized` / `SetPlayhead` (pre-A83:
  grep `SamplePose(` in `ClipEditorWindow.cs`).
- `Editor/ClipEditor/TrackLaneElement.cs` — grep `EventLaneStyle` (post-A86) for the pin draw call.
- `Authoring/Assets/AnimEventKeyRegistry.cs` lines 150–175.
- `Editor/Inspectors/AnimEventKeyRegistryEditor.cs` — the Payload foldout A85 added.

---

## 4. Design

### 4.1 `Editor/ClipEditor/Preview/ScrubEventCrossingResolver.cs` (T2)

```csharp
public static class ScrubEventCrossingResolver
{
    public static void Resolve(float previousNormalized, float currentNormalized, bool isPlaying,
        LoopMode loop, IReadOnlyList<EventMarker> markers, List<int> crossedIndices);
}
```

`Resolver` suffix (pure id/time → index). Sorted-insensitive: markers are scanned in list order
and results are appended in crossing order (time order, wrap segment first).

### 4.2 `AnimEventKeyEntry` addition (T1)

`[Tooltip("Editor-only: played when a marker with this key is crossed while scrubbing or previewing.")] public AudioClip previewClip;`

### 4.3 `Editor/ClipEditor/Preview/EditorEventPreviewPlayer.cs` (T4)

Per D3/D4. Disposed by the pane that owns it.

### 4.4 Timeline hook (T3)

Where the pane writes the playhead: keep the previous value, call the resolver, for each crossed
index call `lane.FlashPin(index)` and, if `registry.FindPreviewClip(marker.eventKey)` is non-null,
`player.Play(clip, 1f)`. Never from a value-changed callback that rebuilds (vault rule); the flash
is a scheduled style change on the existing pin element.

---

## 5. Tasks

- [ ] **T0 — Baseline + probe (orchestrator).** Gate; totals. **Probe D3** with `execute_code`
  (CodeDom C# 6: fully-qualified names, no `using`): reflect
  `UnityEditor.AudioUtil.PlayPreviewClip(AudioClip, int, bool)` and call it with any project
  `AudioClip`; ask the owner-less way — check `AudioSettings` and `EditorUtility.audioMasterMute` —
  then try (2). Record which produced sound in §7; brief T4 with it.
- [ ] **T1 — Entry field (orchestrator).** §4.2; gate; commit `A87-T1`.
- [ ] **T2 — Resolver + fixture [parallel-safe]** — Files: new `ScrubEventCrossingResolver.cs`,
  new `Tests/EditMode/ScrubEventCrossingResolverTests.cs`. Fixtures:
  `ForwardWrap_CrossesTailThenHead` (markers at 0.9 and 0.1; previous 0.85, current 0.15, playing,
  Loop → `[idxOf0.9, idxOf0.1]` in that order) and `BackwardScrub_CrossesMarkerBetween` (marker
  0.5; previous 0.6, current 0.4, not playing → one). Revert-to-fail: drop the wrap branch; drop the
  backward branch.
- [ ] **T3 — Timeline hook + pin flash [parallel-safe]** — Files: `TimelinePane.cs` (playhead
  write range only), `TrackLaneElement.cs` (add `FlashPin(int flatIndex)`; D5).
- [ ] **T4 — `EditorEventPreviewPlayer` [parallel-safe]** — Files: new file. Brief carries the
  D3 winner verbatim.
- [ ] **T5 — Registry inspector: preview clip row [parallel-safe]** — Files:
  `AnimEventKeyRegistryEditor.cs` (the Payload foldout gains an `ObjectField<AudioClip>`),
  `AnimEventKeyRegistry.cs` (add `FindPreviewClip(uint)` beside `FindName`).
- [ ] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Hearing events in the editor" subsection, stating D1 and that this is preview-only, not a sound
  system), `CHANGELOG.md` `## [0.34.0]`.
- **Gate the wave.** `ScrubEventCrossingResolverTests`. Commit `A87-T2..T6`.
- [ ] **T7 — Orchestrator edits.** Wire the player's construction/disposal into the pane; D6 for
  the cutscene playhead if reachable; `Conformance_G` allowlist (`EditorEventPreviewPlayer`);
  `package.json`; vault note section "Sound on scrub (A87)" recording the D3 outcome.
- [ ] **T8 — Drive.** Full suites. Assign a clip to one key; open a clip with that marker; drag the
  playhead across it — hear it, see the flash; step frames across it — once; play a looping clip —
  once per loop. Record in §7 which of these you could verify (sound needs the owner's ears —
  report "flash verified, sound not verifiable from here" if so).
- [ ] **T9 — Close.** HANDOFF §4 (and strike the §5 "do not start it" line, citing this
  amendment), roadmap checkbox.
- [ ] **T10 — ⏸ owner checkpoint.** Message: "Give Footstep a preview clip in Project Settings ▸
  Event Keys. Scrub across a footstep marker in any clip. Two questions: ⚠ D1 — when you click far
  along the ruler, should every skipped marker fire (audio-editor style) or none (current)? And is
  120 ms of flash visible enough?"

---

## 6. Deliberately out of scope

- Any runtime sound: no `AudioSource` component, no mixer, no routing. Hosts consume
  `AnimEventOutput` (A93's routing table is the data path).
- Per-marker clips, volume curves, pitch variation.
- Window (mask) events: only the pulse crossing fires; a window's open span has no sound.

## 7. Build log

_(empty — must contain the D3 probe result before any wave is spawned)_
