# Sound System — Design Spec

> **Status:** 🔨 built — all code landed (data layer, one-shot + loop paths, voice selection, WorldMood/music, animation markers, PlaySound behavior command, settings). Editor assets + play-test pending → see [`../../Spencer/verify-sound-system.md`](../../Spencer/verify-sound-system.md).
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "soundsystemgroup"
---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../../Memories/Code/Skills.md)):
- `dots-blob-library` — `SoundLibrarySO → SoundLibraryBlob` pipeline (§4)
- `dots-system-scaffold` — `SoundCollectionSystem`, `MusicStateSystem`, `SoundLibraryBakingSystem` (§5)
- `dots-authoring-baker` — `AmbientSoundAuthoring`, `SoundLibraryAuthoring` (§11)
- `dots-unit-ai` *(optional)* — only for the `PlaySound` `BehaviorCommand` addition (§8)

---

## 1. Purpose & v1 scope

A DOTS-friendly audio system that **mixes many concurrent sounds** for a horde game, entered the same way the log system is — gameplay spawns sound entities each frame, and the system reads them and plays them.

**v1 handles three categories:**

- **Gameplay SFX** — one-shot effects (footsteps, attacks, impacts, pickups), tied to animations *and* to behaviors.
- **Ambient world loops** — persistent looping emitters (machine hum, fire crackle, wind).
- **Music** — layered/adaptive: Explore / Combat / Tension stems + a separate Menu track.

**Out of v1:** dialogue/VO routing (the existing dialogue system keeps playing its own audio; a `Dialogue` bus hook is reserved but not wired). A `UI` sounds bus is reserved for later.

---

## 2. Architecture — "ECS decides, MonoBehaviour plays"

DOTS has no native audio, so playback is a thin managed bridge. The split is deliberately **backend-agnostic** so the playback layer can later be swapped for FMOD/Wwise without touching any gameplay system.

- **ECS side — `SoundSystemGroup` (runs late):** Burst jobs gather every requested sound this frame, compute distance to the listener, score by **priority + distance**, apply a **per-type instance cap + dedup window**, and select the winning ≤N voices into a singleton output list. This is the "mix" step — *all the math, no managed objects*.
- **Managed side — `AudioManager : PersistentSingleton<AudioManager>`:** pools real `AudioSource`s routed through an `AudioMixer`, owns the `SoundType → AudioClip[]` registry (clips are managed `UnityEngine.Object`s and **cannot** live in a Blob/Burst), and each `LateUpdate` reads the ECS output list to assign/steal voices, set clip/position/volume/pitch/loop, and play/stop. Mirrors the existing manager-bridge pattern (`DOTSEventsManager`, `CameraManager`).

**Why it runs late:** one-shots are spawned during gameplay (combat, animation, item systems). The collection/cull pass must run *after* all of them — so `SoundSystemGroup` lives in `LateSimulationSystemGroup` (after `SpawnSystemGroup`, before `DespawnSystemGroup`), and `AudioManager.LateUpdate` runs after all ECS sim.
**← DECISION:** confirm late-group placement vs. a sim group right after `AnimationSystemGroup`.

```
gameplay systems ──spawn PlaySound entities / toggle LoopingSound──┐
                                                                   ▼
LateSimulationSystemGroup ▸ SoundSystemGroup
   ListenerPosition (singleton, written by AudioManager last frame)
   ├─ SoundCollectionSystem / VoiceSelectionSystem  → ResolvedVoices (singleton list)
   └─ MusicStateSystem                              → MusicState (singleton)
                                                                   ▼
AudioManager.LateUpdate ──reads ResolvedVoices + MusicState──▶ AudioSource pool ▶ AudioMixer ▶ speakers
   (also writes ListenerPosition from the active camera/AudioListener for next frame)
```

---

## 3. Two emission patterns (the entry points)

### 3a. One-shot = spawn a signal entity (the log pattern)
Any system emits a sound by spawning (via ECB) a **one-frame entity** carrying a `PlaySound` `IComponentData`. `SoundCollectionSystem` reads them all, feeds voice selection, then `DestroyEntity(query)` — identical lifecycle to `LogMessage`.

```csharp
public struct PlaySound : IComponentData
{
    public SoundType type;
    public Entity    source;        // emitter entity (Null if positional)
    public float3    position;      // captured world pos (used when !followSource)
    public bool      followSource;  // true → AudioSource tracks source each frame
    public float     volumeMul;     // 1 = use SoundSO default
    public float     pitchMul;      // 1 = use SoundSO default
    public SoundBus  busOverride;   // default → SoundSO.bus
}
```
**Follow-entity OR fixed position:** a walking minion's footstep sets `followSource = true, source = unit`; a thrown rock's impact sets `followSource = false, position = hitPoint`.

### 3b. Looping = persistent component on the emitter entity
A world emitter (machine, fire, ongoing voice) carries `LoopingSound` (`IEnableableComponent`). The manager maps `entity → voice`. When the component is **disabled/removed or the entity is destroyed**, the manager frees the voice and stops the sound — **lifecycle follows the entity automatically, no explicit stop request needed.**

```csharp
public struct LoopingSound : IComponentData, IEnableableComponent
{
    public SoundType type;
    public float     volumeMul;
    public float     pitchMul;
}
```

---

## 4. Data model (Blob-library pipeline)

Follows the project's standard SO→Blob library pattern (use the `dots-blob-library` skill).

- **`SoundType`** enum (`_Scripts/Data/Enums/`) — one entry per distinct sound.
- **`SoundBus`** enum — `Music, SFX, Ambient` (`UI, Dialogue` reserved).
- **`SoundSO`** — per-sound authoring:
  ```csharp
  SoundType   type;
  SoundBus    bus;
  AudioClip[] clipVariations;   // random pick per play → anti-repetition
  Vector2     volumeRange;      // randomized per play
  Vector2     pitchRange;       // randomized per play
  bool        loop;
  byte        priority;         // higher = harder to steal
  float       minDistance;      // 3D rolloff near
  float       maxDistance;      // 3D rolloff far — widen for god-mode-audible sounds
  int         maxConcurrent;    // cap on simultaneous instances of this type
  float       dedupWindow;      // seconds; same-type emits inside this window collapse
  ```
- **`SoundLibrarySO` → `SoundLibraryBlob`** (Burst-readable params, **enum-indexed**, *no clips*) baked by `SoundLibraryBakingSystem` in `PostBakingSystemGroup`. `SoundLibrary` + `SoundLibraryReference` components expose it to jobs.
- **Clip registry:** `AudioManager` holds a parallel managed `AudioClip[][]` indexed by `SoundType` (authored from the same `SoundLibrarySO`), since clips can't be baked. Voice selection passes `SoundType` + `variationIndex`; the manager resolves the actual clip.

> Anti-repetition is built in: clip variations + pitch/volume randomization stop 200 footsteps sounding like a machine gun; `maxConcurrent` + `dedupWindow` collapse same-frame duplicates.

---

## 5. Systems (`SoundSystemGroup`)

### `SoundCollectionSystem` / `VoiceSelectionSystem`
- Gathers all `PlaySound` entities + all enabled `LoopingSound` entities.
- Reads the listener position from a `ListenerPosition` singleton (written by `AudioManager` from the active `AudioListener`/camera each frame).
- Scores each candidate: `priority`, distance vs `maxDistance`, loop-vs-one-shot continuity (don't drop an already-playing loop for a new one-shot of equal score).
- Applies per-type `maxConcurrent` cap + `dedupWindow`.
- Writes the top ≤N into a `ResolvedVoices` singleton `NativeList<ResolvedVoice>`:
  ```csharp
  struct ResolvedVoice {
      SoundType type; int variationIndex;
      float3 pos; Entity source; bool followSource;
      float volume; float pitch; bool loop;
      int stableVoiceKey;   // stable id so the manager can diff frame-to-frame
  }
  ```
- Then `DestroyEntity` the one-shot `PlaySound` query (log pattern). Loops stay (they're persistent components).
- **← DECISION:** voice count **N (default 32)**.

### `MusicStateSystem`
- Writes a `MusicState` singleton: `enum {Explore, Combat, Tension}` + per-layer target weights.
- **Combat** = any in-combat unit within camera range. **Tension** = nearby ally/player health below a threshold.
- Menu track is driven externally (game-state / narrative event), not by this system.
- **← DECISION:** exact combat/tension detection source — reuse `AttackRequest` / `ThreatEntry` for combat? what health threshold for tension?

---

## 6. MonoBehaviour `AudioManager`

- Pools N `AudioSource`s, each assignable to a mixer bus; `spatialBlend = 1` (3D) for SFX/Ambient, `0` (2D) for Music.
- Each `LateUpdate`:
  1. Write the `ListenerPosition` singleton from the camera/active `AudioListener`.
  2. Read `ResolvedVoices`; diff against currently-playing voices keyed by `stableVoiceKey` → keep continuing loops, start new voices, steal/stop dropped ones.
  3. For `followSource` voices, update `AudioSource.transform.position` from the entity each frame.
  4. Apply music layer weights from `MusicState` with crossfades.
- Owns the `entity → AudioSource` map for loops; on a missing entity / disabled component, fade out + free the voice.
- **Spatialization** = Unity 3D rolloff; the god-mode flyover is handled by per-`SoundSO` `maxDistance` (loud/important sounds get a wide range so distant minions stay audible). The `AudioListener` stays on the active camera.
- **← DECISION:** confirm the `AudioListener` rides the camera in *both* control modes (on-character and god-mode).

---

## 7. Clip-baked SFX (animation-locked sounds)

Extend `AnimationClipSO` with an optional list of sound markers:
```csharp
[Serializable] public class SoundMarker { public SoundType type; [Range(0,1)] public float normalizedTime; }
public List<SoundMarker> soundMarkers = new();
```
The animation execution path emits a `PlaySound` (`followSource = animating unit`) when playback crosses a marker's `normalizedTime`. Footsteps/swings are authored **once per clip**, no behavior edits.
**← DECISION:** confirm marker shape + which animation system fires markers (the clip-playback/`AnimationExecutionSystemGroup` path).

---

## 8. `PlaySound` behavior command (behavior-level cues)

Add `PlaySound` to the `BehaviorCommand` blob enum (alongside `PlayAnimation`). `BehaviorExecutionSystem` handles it as **fire-and-advance** (non-blocking): emits a `PlaySound` entity for the active unit. Lets a behavior bark a cue (e.g. a yell when Flee starts). The `SoundType` packs into the existing command int/enum params.

---

## 9. Mixer & settings

- **`AudioMixer` asset:** `Master → {Music, SFX, Ambient}`, each with an **exposed volume param**. (`UI`, `Dialogue` groups reserved for later.)
- **Extend `GameSettings`** (`_Scripts/Components/Save/GameDataComponents.cs`, currently only `animationFrameRate`):
  ```csharp
  public float masterVolume;
  public float musicVolume;
  public float sfxVolume;
  public float ambientVolume;
  ```
  `AudioManager` applies them to the mixer on settings change and on load → volumes **persist through the existing save system**. Add the four floats to the save DTO.
- **← DECISION:** default volume levels (e.g. master 1.0, others 0.8?).

---

## 10. Voice budget summary

32-voice pool · priority + distance voice-stealing · per-`SoundType` `maxConcurrent` cap · `dedupWindow` merges same-frame duplicates · loops virtualize (stop) when out of `maxDistance` and resume when back in range.

---

## 11. Proposed file manifest

**New:**
- `_Scripts/Data/Enums/SoundType.cs`, `SoundBus.cs`
- `_Scripts/Data/SOs/SoundSO.cs`, `SoundLibrarySO.cs`
- `_Scripts/Data/Blobs/SoundLibraryBlob.cs` (+ `SoundLibrary`, `SoundLibraryReference` components)
- `_Scripts/Components/Audio/SoundComponents.cs` — `PlaySound`, `LoopingSound`, `ListenerPosition`, `ResolvedVoices`, `MusicState`
- `_Scripts/Systems/SoundSystemGroup/` — `SoundCollectionSystem.cs` (a.k.a. `VoiceSelectionSystem`), `MusicStateSystem.cs`, `SoundLibraryBakingSystem.cs`
- `_Scripts/MonoBehaviours/Managers/AudioManager.cs`
- `_Scripts/Authoring/AmbientSoundAuthoring.cs`, `SoundLibraryAuthoring.cs`
- Assets: an `AudioMixer` (Master/Music/SFX/Ambient) + a `_SoundLibrary` SO

**Edited:**
- `_Scripts/Systems/SystemGroups.cs` — add `SoundSystemGroup` in `LateSimulationSystemGroup`
- `_Scripts/Data/SOs/AnimationClipSO.cs` — add `soundMarkers`
- `_Scripts/Components/Save/GameDataComponents.cs` + save DTO — volume floats
- `BehaviorCommand` enum + `BehaviorExecutionSystem` — the `PlaySound` command

---

## 12. Suggested build phases (for when this spec is handed back)

1. **Data layer** — enums + `SoundSO`/`SoundLibrarySO` + blob library + bake (`dots-blob-library`).
2. **One-shot path** — components + `AudioManager` pool + mixer + `PlaySound` end-to-end (one test SFX).
3. **Loops** — `LoopingSound` + `AmbientSoundAuthoring` + entity→voice lifecycle.
4. **Voice selection** — priority/distance/cap/dedup job at horde scale; profile.
5. **Authoring hooks** — clip-baked animation markers + `PlaySound` behavior command.
6. **Music** — layered stems + `MusicStateSystem` + crossfades.
7. **Settings** — `GameSettings` volumes + save persistence + settings UI hook.

---

## 13. Verification (per phase, when built)

- Play mode in `DOTSTestScene`; trigger a test one-shot (spawn a `PlaySound` from a debug key) → hear it, confirm the entity is destroyed same frame (Entities inspector).
- Place an `AmbientSoundAuthoring` emitter → loop plays; destroy the entity → loop stops.
- Spawn a horde, fire many footsteps → voice count stays ≤ pool, no machine-gun artifact (dedup working), closest/highest-priority win.
- Toggle camera to god-mode → distant minions still audible per `maxDistance`.
- Force `MusicState` Explore→Combat→Tension → layers crossfade.
- Change a volume slider → mixer responds; save + reload → volume persists.

---

## Open decisions (collected — resolved at build)

- [x] §2 — placement: **`SoundSystemGroup` in `LateSimulationSystemGroup`** (`UpdateAfter SpawnInitSystemGroup`, `UpdateBefore DespawnSystemGroup`).
- [x] §5 — voice pool **N = 32**.
- [x] §5 — detection **redesigned**: a generic **`WorldMood` singleton** (`Explore/Tension/Combat`) set idempotently via `WorldMoodUtil` from **camera-visible** state — Combat = `AttackRequest` in view, Tension = non-empty `ThreatEntry` in view. `MusicStateSystem` maps `WorldMood → MusicState` weights. (No health threshold — replaced by camera visibility per Spencer.)
- [x] §6 — `AudioListener` rides the active camera; `AudioManager` publishes the camera ground point + `cameraViewRadius`.
- [x] §7 — `SoundMarker { SoundType type; float normalizedTime }`; `AnimationSoundMarkerSystem` fires on crossings after `AnimationTimeSystem`.
- [x] §9 — defaults: master 1.0, music 0.7, sfx 0.9, ambient 0.7 (in `GameSettings`, persisted).

**Build note:** `WorldMood` (`Components/World/WorldStateComponents.cs`) + `WorldMoodUtil` were added as a generic world-state layer (reusable by NPC behaviours later), feeding `MusicState`. Everything is code-complete and inert until the Editor assets in the Spencer verify file exist (`AudioMixer`, clips, populated `_SoundLibrary`, `AudioManager` in scene, music stems).
