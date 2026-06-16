---
title: Verify Sound System (SFX / loops / voice selection / music / settings)
status: active
created: 2026-06-14
area: code
---

## Goal

Confirm the Sound System works end-to-end in `Assets/Scenes/TestArea/DOTSTestScene.unity`. All **code** is committed (data layer, one-shot + loop paths, `VoiceSelectionSystem`, `WorldMood`/`MusicStateSystem`, `AnimationSoundMarkerSystem`, `PlaySound` behavior command, persisted volume settings, `AudioManager`). The system is **inert until the Editor-only assets below exist** — none of which can be created outside Unity.

Spec: [`../Claude/Plans/Sound_System.md`](../Claude/Plans/Sound_System.md).

## Steps

### Compile + import (first)
- [ ] Re-enter the Unity Editor; confirm **no compile errors**.
- [ ] Confirm **no duplicate-GUID warnings** — all `.cs.meta` GUIDs were hand-generated outside Unity. If a collision is reported, delete that `.meta` and let Unity regenerate it, then re-commit.
- [ ] Systems window: `SoundSystemGroup` exists in `LateSimulationSystemGroup` (after SpawnInit, before Despawn) with `VoiceSelectionSystem`, `WorldMoodSystem`, `MusicStateSystem`; `AnimationSoundMarkerSystem` is in `AnimationExecutionSystemGroup` after `AnimationTimeSystem`.

### Editor assets (one-time setup — code can't create these)
- [ ] Create an `AudioMixer`: `Master → {Music, SFX, Ambient}`. Expose each group's volume as params named `MasterVolume`, `MusicVolume`, `SFXVolume`, `AmbientVolume` (or change the names on the AudioManager to match).
- [ ] Create `SoundSO` assets (Audio/Sound), import `AudioClip`s into `clipVariations`, set bus/priority/min-max distance/maxConcurrent. Add them to a `_SoundLibrary` (Audio/Sound Library).
- [ ] Add `SoundLibraryAuthoring` to a scene GO and assign `_SoundLibrary` (bakes the blob).
- [ ] Create the `AudioManager` GameObject (it's a `PersistentSingleton`): assign `mixer`, the three `AudioMixerGroup`s, `soundLibrary` (= `_SoundLibrary`), and the three music stems. Ensure an `AudioListener` sits on the active camera (both on-character and god-mode cameras).

### One-shot SFX
- [ ] Hook a debug key to spawn a `PlaySound` (via `SoundUtil.Play`/`PlayOn`) → the sound is audible, and the one-frame entity is gone next frame (Entities inspector).
- [ ] Confirm pitch/volume randomization + clip variation (no machine-gun on repeats).

### Ambient loops
- [ ] Add `AmbientSoundAuthoring` (e.g. MachineHum) to a scene object → loop plays positionally. Disable `LoopingSound` (or destroy the entity) → loop stops automatically.

### Voice selection at scale
- [ ] Spawn a horde firing footsteps (animation markers below) → concurrent voices stay ≤ 32 and within per-type `maxConcurrent`; closest/highest-priority win. Profile; jobify `VoiceSelectionSystem` if it shows up.
- [ ] God-mode flyover: distant minions stay audible per their `maxDistance`.

### Animation-locked SFX
- [ ] Add `SoundMarker`s (type + normalizedTime) to a walk/attack `AnimationClipSO` → footstep/swing fires as playback crosses each marker.

### Music + WorldMood
- [ ] Stage a camera-visible attack → `WorldMood` flips Explore→Tension→Combat (Entities inspector) and music stems crossfade. Move the camera away → returns to Explore.

### Settings + persistence
- [ ] Change a volume in `GameSettings` → mixer responds. Save + reload → the volume persists (auto-saved via IPersist; no DTO code).

## Notes

Code files (committed this round):
- `Assets/_Scripts/Data/Enums/SoundType.cs`, `SoundBus.cs`; `Data/SOs/SoundSO.cs`, `SoundLibrarySO.cs`; `Data/Structs/SoundBlobs.cs`.
- `Assets/_Scripts/Components/Audio/SoundComponents.cs`, `SoundLibraryComponents.cs`; `Components/World/WorldStateComponents.cs`.
- `Assets/_Scripts/Utils/SoundUtil.cs`, `WorldMoodUtil.cs`.
- `Assets/_Scripts/Systems/SoundSystemGroup/{VoiceSelectionSystem,WorldMoodSystem,MusicStateSystem}.cs`; `PostBakingSystemGroup/SoundLibraryBakingSystem.cs`; `AnimationSystemGroup/AnimationExecutionSystemGroup/AnimationSoundMarkerSystem.cs`.
- `Assets/_Scripts/MonoBehaviours/Managers/AudioManager.cs`; `Authoring/EntityLibraries/SoundLibraryAuthoring.cs`, `Authoring/AmbientSoundAuthoring.cs`.
- Edits: `SystemGroups.cs` (`SoundSystemGroup`), `AnimationClipSO.cs` + `AnimationBlobs.cs` + `AnimationLibraryBakingSystem.cs` (sound markers), `GameDataComponents.cs` + `GameDataAuthoring.cs` (volumes), `AiEnums.cs` + `BehaviorExecutionSystem.cs` (`PlaySound` command).

Gotchas to watch:
- `VoiceSelectionSystem` is **not** `[BurstCompile]` (it does a structural `DestroyEntity`); the others are. If burst errors appear on a singleton-creating system, that's the first place to look.
- The `ListenerPosition` is the **camera's ground point** (x,0,z); tune `cameraViewRadius` on the AudioManager so "in view" matches the zoom. Distance culling/scoring use it.
- One-shots are fire-and-forget (`PlayOneShot`); loops are kept alive by `stableVoiceKey` (= emitter `entity.Index`) and stopped when they leave the resolved set.
- `WorldMood` is a generic singleton — NPC behaviours can read it later. It only writes on change.
- `.meta` GUIDs hand-generated; if Unity reimports clips/mixer with new GUIDs that's expected (those are your assets).

When everything passes: move this file to `Assets/_Vault/Tasks/Done/` and flip the spec status to ✔️ done.
