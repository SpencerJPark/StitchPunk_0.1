# Amendment A87 — Scrub crossings and sound on scrub

> **Status:** ✅ built 2026-09-13 as `0.34.0` (Clip Editor; D6 cutscenes deferred, see §7). **T10 owner checkpoint open.**
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

- [x] **T0 — Baseline + probe (orchestrator).** Gate; totals. **Probe D3** with `execute_code`
  (CodeDom C# 6: fully-qualified names, no `using`): reflect
  `UnityEditor.AudioUtil.PlayPreviewClip(AudioClip, int, bool)` and call it with any project
  `AudioClip`; ask the owner-less way — check `AudioSettings` and `EditorUtility.audioMasterMute` —
  then try (2). Record which produced sound in §7; brief T4 with it.
- [x] **T1 — Entry field (orchestrator).** §4.2; gate; commit `A87-T1`.
- [x] **T2 — Resolver + fixture [parallel-safe]** — Files: new `ScrubEventCrossingResolver.cs`,
  new `Tests/EditMode/ScrubEventCrossingResolverTests.cs`. Fixtures:
  `ForwardWrap_CrossesTailThenHead` (markers at 0.9 and 0.1; previous 0.85, current 0.15, playing,
  Loop → `[idxOf0.9, idxOf0.1]` in that order) and `BackwardScrub_CrossesMarkerBetween` (marker
  0.5; previous 0.6, current 0.4, not playing → one). Revert-to-fail: drop the wrap branch; drop the
  backward branch.
- [x] **T3 — Timeline hook + pin flash [parallel-safe]** — Files: `TimelinePane.cs` (playhead
  write range only), `TrackLaneElement.cs` (add `FlashPin(int flatIndex)`; D5).
- [x] **T4 — `EditorEventPreviewPlayer` [parallel-safe]** — Files: new file. Brief carries the
  D3 winner verbatim.
- [x] **T5 — Registry inspector: preview clip row [parallel-safe]** — Files:
  `AnimEventKeyRegistryEditor.cs` (the Payload foldout gains an `ObjectField<AudioClip>`),
  `AnimEventKeyRegistry.cs` (add `FindPreviewClip(uint)` beside `FindName`).
- [x] **T6 — Docs + changelog [parallel-safe]** — Files: `Documentation~/animation-events.md`
  ("Hearing events in the editor" subsection, stating D1 and that this is preview-only, not a sound
  system), `CHANGELOG.md` `## [0.34.0]`.
- **Gate the wave.** `ScrubEventCrossingResolverTests`. Commit `A87-T2..T6`.
- [x] **T7 — Orchestrator edits.** Wire the player's construction/disposal into the pane; D6 for
  the cutscene playhead if reachable; `Conformance_G` allowlist (`EditorEventPreviewPlayer`);
  `package.json`; vault note section "Sound on scrub (A87)" recording the D3 outcome.
- [x] **T8 — Drive.** Full suites. Assign a clip to one key; open a clip with that marker; drag the
  playhead across it — hear it, see the flash; step frames across it — once; play a looping clip —
  once per loop. Record in §7 which of these you could verify (sound needs the owner's ears —
  report "flash verified, sound not verifiable from here" if so).
- [x] **T9 — Close.** HANDOFF §4 (and strike the §5 "do not start it" line, citing this
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

### T0 — 2026-09-13, head `e84a2aac`

- **Baseline gate:** refresh + compile clean, console has no errors. Suite totals taken from the
  A86 close at this head, not re-run: EditMode 829 (one pre-existing failure,
  `PackagingConformanceTests.Conformance_A` asmdef reference list), PlayMode 283.
- **D3 probe (against `Assets/Audio/Splat1.mp3`, 1.848 s, DecompressOnLoad):**
  `EditorUtility.audioMasterMute` false; output 48 kHz stereo.
  (1) `UnityEditor.AudioUtil` (internal type) exposes **public static**
  `PlayPreviewClip(AudioClip clip, int startSample = 0, bool loop = false)`,
  `IsPreviewClipPlaying()`, `StopAllPreviewClips()`. Invoked by reflection:
  `IsPreviewClipPlaying()` returned **true** straight after.
  (2) `EditorUtility.CreateGameObjectWithHideFlags(HideAndDontSave, AudioSource)` +
  `PlayOneShot`: `isPlaying` **true**, but only because the open scene has an `AudioListener`.
  **Winner: (1)**, by spec order and because it needs no listener in the open scene. Its limits,
  carried into T4: one preview voice (a second call replaces the first) and no volume control.
  Nobody has listened to either one yet. Both are "reported playing", and the owner's ears settle it.
- **Drift: the playhead write is not in `TimelinePane.cs`.** The pane's `SetPlayheadTime` is a
  delegate (`TimelinePane.cs:117`) bound to `ClipEditorWindow.SetPlayheadTime` (`:3310`, wired at
  `:644`). Every write reaches it: ruler scrub, key click, frame step, rebuild re-set, and the play
  tick (`OnEditorTick`, which wraps with `Floor` before calling it). **Call:** the pane gets
  `ReportPlayheadMoved(previous, current, isPlaying, isLoopEnabled)` (T3: resolver call, lane flash,
  player), and the window's setter calls it with the pre-clamp `playheadTime` (T7, one line). A
  rebuild re-sets the same value and fires nothing. The pane skips a report when the selected clip
  differs from the one it last saw, so switching clips never fires the new clip's markers.
- **Drift: `FlashPin(int flatIndex)` cannot take a flat index.** A lane holds only its own
  lane-local key times. **Call:** `TrackLaneElement.FlashPin(int keyIndex)` is lane-local, and the pane maps
  flat → (lane, local) through `EventLaneAddressing`.
- **Drift: D1's wrap test is forward-only, but the window plays backwards too** (negative speed
  wraps 0.02 → 0.98). **Call:** while playing with Loop, a raw delta larger than half the clip is
  a wrap taken the short way round the loop, in either direction. While not playing, the same delta
  is a seek and fires nothing (D1 as written). The spec's two fixtures are unchanged by this.
- **Drift: D4's `Play(AudioClip clip, float volume)`.** The winning mechanism has no volume.
  **Call:** `Play(AudioClip clip)`, because a parameter the implementation ignores is a lie. Every
  §4.4 caller passed `1f`.
- **⚠ D5 interpretation.** `ToolkitPalette.EventColors` are already full alpha, so "full alpha"
  shows nothing. **Call:** a flashing pin draws through `EventLaneStyle.DrawPin`'s explicit-outline
  overload with the pin's own colour as a 3 px outline, which makes it swell for 120 ms. Selection
  still wins the outline. The flash adds no new colour token. Added to the T10 question.
- **Drift: T10 message.** This project has no `Footstep` key (registry: Sound 16, Damage 17,
  Attack 18, Dialogue 19), and the page is **Project Settings ▸ DOTS Animation Toolkit ▸ Event
  Names**. T10's message is adapted to name `Sound`. No key is added.
- `Editor/ClipEditor/Preview/` exists, so §4's new files have their home.
- **D2 persistence check.** The project registry is not a YAML asset. `VocabularyRegistryProvider`
  writes it with `EditorJsonUtility.ToJson` and reads it with `FromJsonOverwrite`. An in-memory
  round-trip kept `previewClip` as `{fileID: 8300000, guid, type: 3}` and restored
  `Assets/Audio/Splat1.mp3`, so the reference survives the JSON path.

### Wave T2–T6 — spawned 2026-09-13 after `82811536`

T3 is briefed to add `TimelinePane.ReportPlayheadMoved` and a lane-local `FlashPin(int keyIndex)`.
T4 builds on AudioUtil. T5 adds `PropertyField(previewClip)` in the Payload foldout; a PropertyField
on an `AudioClip` reference renders an `ObjectField<AudioClip>`.

- All five workers finished at 53–78k tokens. Compile clean, no errors. T6's warnings are the
  owner's `NewClip 1` T6 bake skips, not A87.
- Review fixes by the orchestrator: removed three `<summary>` blocks T3 added (TimelinePane,
  TrackLaneElement) and T4's empty constructor. **Added an `isDraggingKeys` guard** in
  `ReportPlayheadMoved`: `UpdateKeyDrag` carries the playhead with the dragged pin, so the dragged
  marker sat at `current` on every move and would have re-fired its own flash and sound.
- T5's persistence answer covered only the `SerializedObject` binding. The project registry is JSON
  on disk, so T8 proves the write.
- **Fixtures:** `ScrubEventCrossingResolverTests` 2/2 pass. Revert-to-fail, as one mutation because
  each fixture exercises only its own branch: wrap branch disabled and backward branch emptied →
  both failed (`Missing: < 1, 0 >`, `Missing: < 0 >`). Restored byte-identical (sha256 `cd5f6565…`).

### T7 — orchestrator

- `ClipEditorWindow.SetPlayheadTime` keeps `previousPlayheadTime` and calls
  `timelinePane.ReportPlayheadMoved(previousPlayheadTime, playheadTime, isPlaying,
  isLoopEnabled)` after `SyncTransportPlayhead`. `isLoopEnabled` lives in `ClipEditorTransport.cs`.
  The player is created lazily by the pane and disposed in `TimelinePane.Dispose`, so the window
  needs no construction wiring.
- **Drift: no Conformance_G allowlist entry.** Its regex matches only `static class`, and
  `EditorEventPreviewPlayer` is a sealed instance class. An allowlist entry would be dead.
- **D6 deferred, logged per the spec.** Cutscene markers are `CutsceneEventMarker.time` in seconds.
  Their pins are one `VisualElement` per marker in `CutsceneMomentLaneElement`, not
  `TrackLaneElement` paint. The hook belongs in `CutsceneEditorPanel.SetPlayhead` (`:916`, also
  reached by the tick at `:740` and `OnPlayheadScrubbed`). That is three files (panel, moment lane,
  a seconds overload of the resolver), not two. Follow-up candidate.
- Version pins 0.34.0: `package.json`, and `PackagingConformanceTests` comment + Assert.
- Vault note: `AnimationToolkit.md` § "Sound on scrub (A87, 0.34.0)".

### T8 — drive (2026-09-13)

- **Full suites:** EditMode 831 (829 + 2; only the standing `Conformance_A` asmdef-list failure),
  PlayMode 283/283. Totals did not drop.
- **Crossings, one level down** (`execute_code`). The drive used a `TimelinePane` with a
  `ClipEditorSession`, an in-memory clip with one Sound (key 16) marker at 0.5, and an event
  `TrackLaneElement` in its `laneColumn`. Sound was given `Splat1` in memory only, restored in
  `finally`.
  - Scrub 0.4 → 0.6: pin flashed, player created, `IsPreviewClipPlaying` true.
  - Frame steps: 0.49 → 0.50 flashed and 0.50 → 0.51 did not, so a step fires once.
  - Paused seek 0.05 → 0.95: no flash.
  - Looping play ticks over two loops: 2 crossings. Reverse looping play through a wrap: 1.
  - During a key drag: none. First report after a clip switch: none; the next scrub fired.
  - `Dispose`: player gone, preview stopped.
- **Registry write proven on disk.** The registry editor builds one "Preview Clip" `PropertyField`
  per row, bound to `entries.Array.data[n].previewClip` (`PPtr<$AudioClip>`, so an AudioClip
  `ObjectField`). A write through `SerializedObject` + `VocabularyRegistryProvider.Persist` (what
  `OnSerializedObjectChanged` calls) put Splat1's guid inside the Sound entry on disk. A fresh
  `FromJsonOverwrite` of the file gave `FindPreviewClip(16)` = `Assets/Audio/Splat1.mp3` and
  `FindPreviewClip(17)` = null. The file was restored from backup, sha256 `b315ce62…` again, with
  no other files changed.
- **Not verified from here:** that the sound is audible (owner's ears), and the flash on screen.
  The owner's docked DOTS Animator window was not driven or captured, to avoid replacing their open
  clip session. The `TrackSerializedObjectValue` callback path needs a live panel, so the drive
  called the method it invokes instead.

### T9 — close

HANDOFF §4 paragraph added, and §5's "do not start it" struck, citing A87. Roadmap status line
updated. The A87 box stays unticked until T10 is answered, like A86's.
