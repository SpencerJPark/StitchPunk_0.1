# Amendment A70 — Actor Profile: layers, named animations, per-animation direction, ragdoll triggers

**Status:** ✅ spec written 2026-09-07, nothing built. Owner-requested (product calls in
`Assets/_Vault/Tasks/NewPlans/ActorEditor_Roadmap.md` §2 — settled, do not re-ask). Package version
after this lands: **0.16.0** (breaking: `RigAsset.layers` removed, `ActorAuthoring` re-shaped,
`DirectionSetAsset` re-shaped).
**Scope:** `Packages/com.dotsanimationtoolkit/` — `Authoring/`, `Runtime/`, `Editor/Inspectors/`,
`Editor/ClipUtilities/`, `Tests/`, `Samples~/`, `Documentation~/`. **No Actor Editor UI** — that is
A71. **No game code** — that is G5.
**Execution protocol:** `Cutscene_Roadmap.md` §4 with the `A70-Tn:` prefix. HANDOFF §2 comment and
naming rules apply to every new file (`Conformance_F/G/H` enforce them).

---

## 1. Why this exists

The package has clips, sets, rigs and a command buffer that plays a **clip id on a layer index**.
Everything above that — which animation an actor *has*, which layer it belongs on, how many
directions it turns through, which clip serves which facing — lives in the host game as
`UnitSO` tables and a `DirectionSetBlob` fold. The owner wants that knowledge in the package, on
one asset per actor, so the same asset can be authored, mixed and tested in an editor tab (A71)
and then drive the game unchanged (G5). Four owner calls shape it (roadmap §2): layers on the
profile not the rig; the game plays by **name**; Base and Override are fixed bookends; ragdoll is a
flag on an animation.

## 2. Read first (in this order, and only these)

1. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6.
2. `Assets/_Vault/Tasks/NewPlans/ActorEditor_Roadmap.md` — §2 product calls and §5 vocabulary.
3. `Authoring/Assets/RigAsset.cs` (`LayerDefinition`, `EnsureStableIds`, `IStableIdMintReporter`),
   `Authoring/Assets/DirectionSetAsset.cs`, `Authoring/Assets/ClipSetAsset.cs`,
   `Authoring/Assets/TargetTagRegistry.cs` + `IVocabularyRegistry.cs`,
   `Authoring/Baking/ActorAuthoring.cs` + `ActorBaker.cs` (`AddPlaybackLayers`, `SeedStartingLayers`,
   `AddRagdollBodies`), `Authoring/Build/ClipRegistryBuilder.cs` (line ~368 `layerCount`, the hash
   at ~1033), `Authoring/Validation/ClipValidation.cs` lines 300–330 (the layer rules).
4. `Runtime/Components/AnimationCommand.cs`, `PlaybackLayer.cs`, `AnimEventOutput.cs`,
   `AnimationToolkitEnums.cs` (`CommandKind`, `Direction`, `AnimationDirections`),
   `Runtime/Api/PlaybackApi.cs`, `Runtime/Systems/CommandApplySystem.cs`,
   `EventEmissionSystem.cs` (the two `new AnimEventOutput` sites), `Runtime/Sampling/FacingResolver.cs`,
   `Runtime/Components/RagdollComponents.cs` (`RagdollActor`), `Runtime/Api/CutsceneApi.cs`
   (`CreatePlayRequest(..., byte layerIndex = 0, ...)`), `Runtime/Systems/CutsceneTimelineSystem.cs`
   line ~87 (`play.layerIndex`).
5. `Editor/ClipUtilities/VocabularyRegistryProvider.cs`, `Editor/Inspectors/VocabularySettingsProvider.cs`,
   `VocabularyConstantsSection.cs`, `Editor/ClipUtilities/ConstantsGenerator.cs`,
   `Editor/Inspectors/ActorAuthoringEditor.cs`, `Editor/Inspectors/RigAssetEditor.cs` (the `Layers`
   `PropertyField` at line ~97).
6. The game's fold, which moves into the package: `Assets/_Scripts/Data/Structs/DirectionSetBlob.cs`
   (`ResolveSlot`), `Assets/_Scripts/Utils/DirectionSetBakeUtil.cs`,
   `Assets/_Scripts/Tests/DirectionSetBlobFoldTests.cs`.
7. Tests you will touch: `Tests/EditMode/AuthoringTestAssets.cs` (rig-with-layers helpers),
   `DataContractTests.cs` (actor archetype inventory), `DirectionSetCoverageTests.cs`,
   `ClipValidationTests.cs`, `DiskRoundTripTests.cs`; `Tests/PlayMode/ActorBakeFixture.cs`,
   `PlaybackTestActor.cs`, `CommandApplySystemTests.cs`, `PlaybackApiTests.cs`,
   `EventEmissionSystemTests.cs`, `CutsceneTimelineSystemTests.cs`, `SystemGroupStructureTests.cs`.
8. `Samples~/QuickStartActor/Editor/QuickStartActorBuilder.cs` and
   `Samples~/CompositeActor/Editor/CompositeActorBuilder.cs` (both set `rig.layers` and
   `ActorAuthoring.clipSets`) — `Samples~` is not compiled; check it through a temp assembly
   (`Gotchas.md`).

## 3. Design

### 3.1 Authoring — `ActorProfileAsset`

`Authoring/Assets/ActorProfileAsset.cs`, `CreateAssetMenu("DOTS Animation Toolkit/Actor Profile")`,
`IStableIdMintReporter` like the other assets.

```
ActorProfileAsset : ScriptableObject
  ulong stableId
  RigAsset rig                                  // the rig every animation here is bound against
  List<ClipSetAsset> clipSets                   // what the registry is built from (moves off ActorAuthoring)
  AnimationDirections turnDirections = Six      // the actor's turn granularity (was UnitSO.animationDirections)
  List<ActorLayerDefinition> layers             // [0] = Base, [Count-1] = Override, always present

ActorLayerDefinition
  string displayName                            // cosmetic; identity is list position
  bool defaultActive
  uint startingAnimationKey                     // 0 = none; seeds this layer at bake (replaces StartingLayerState)
  List<ActorAnimationDefinition> animations

ActorAnimationDefinition
  uint animationKey                             // AnimationNameRegistry id; unique across the whole profile (rule P3)
  bool hasDirections
  ClipAsset clip                                // when !hasDirections
  DirectionSlots directionSlots                 // when hasDirections
  LoopMode loop = UseClipDefault
  float speed = 1
  float blendIn = NaN                           // NaN = clip default
  RagdollTrigger ragdollTrigger = None          // None / Start / Stop
  uint ragdollAtEventKey                        // 0 = at play; else the marker key that fires the trigger

DirectionSlots  ([Serializable] class, Authoring/Assets/DirectionSlots.cs)
  ClipAsset southEast, northEast, south, north, east
  AnimationDirections targetDirections = Six
  GetSlot / SetSlot / TryGetEffectiveDirections / GetRequiredSlots / GetMembers   (moved verbatim from DirectionSetAsset)
```

`DirectionSetAsset` keeps existing for cutscene clip blocks (`CutsceneSlot.directionSet`,
`CutsceneClipBlockBlob.directionVariants`) but becomes a thin holder: `public DirectionSlots slots`,
with its five old fields and methods removed. Its one `.asset` in the game
(`MaleCitizenWalkDirections`) is re-authored by hand in G5 — no migration code (directive).

**Bookends are structural.** `ActorProfileAsset.OnValidate`/`EnsureBookends()` guarantees
`layers.Count >= 2`, `layers[0].displayName == "Base"` and `layers[^1].displayName == "Override"`
exist and cannot be removed or reordered by the inspector; user layers live between them.
`MaxLayerCount = 8` moves here from `RigAsset`.

### 3.2 The vocabulary — `AnimationNameRegistry`

Third `IVocabularyRegistry`, `Authoring/Assets/AnimationNameRegistry.cs`, shaped exactly like
`TargetTagRegistry` (entries, `generatedConstantsPath`). Provided by
`VocabularyRegistryProvider.AnimationNames` from
`ProjectSettings/DotsAnimationToolkitAnimationNameRegistry.asset`; `Persist` overload +
`PersistVocabulary` branch; a third page **Project/DOTS Animation Toolkit/Animation Names** in
`VocabularySettingsProvider`; `VocabularyConstantsSection` generates a class named `AnimNames`.
Every editor surface that shows an animation uses `VocabularyPicker` — a name is typed once, in the
registry (HANDOFF §5). No raw ids anywhere.

### 3.3 Runtime — blob, components, commands

`Runtime/Blobs/ActorProfileBlob.cs`:

```
ActorProfileBlob { int schemaVersion (=1); AnimationDirections turnDirections; byte layerCount;
                   BlobArray<ActorAnimationBlob> animations /* sorted by animationKey */ }
ActorAnimationBlob { uint animationKey; byte layerIndex; bool hasDirections; ClipId clip;
                     DirectionSlotsBlob slots; LoopMode loop; float speed; float blendIn;
                     RagdollTrigger ragdollTrigger; uint ragdollAtEventKey }
DirectionSlotsBlob { ClipId southEast, northEast, south, north, east; AnimationDirections effectiveDirections;
                     ClipId ResolveSlot(Direction eastSideFacing) /* the game's double fold, moved here */ }
```

Built by `Authoring/Build/ActorProfileBuilder.cs` (`Build(ActorProfileAsset, out validation)`),
deterministic, hashed like the registry. `ActorBaker` adds `ActorProfile { Value }` next to
`ClipRegistry`, sizes the `PlaybackLayer` buffer from `profile.layers.Count` (the registry's
`layerCount` field and its hash term are **deleted** — a registry is per (rig, sets), a layer count
is per profile), and seeds layers from `startingAnimationKey` through the same resolve path the
runtime uses (facing = `ActorFacing` default `SouthEast`).

New components, all on the actor root, all rows in `DataContractTests`:

- `ActorProfile : IComponentData { BlobAssetReference<ActorProfileBlob> Value }`.
- `ActorFacing : IComponentData { Direction facing; Direction appliedFacing }` — `facing` is
  host-written (the toolkit never derives it: what "forward" means is the host's), `appliedFacing`
  is the toolkit's last re-pick, so a change is detectable without a second component. Baked to
  `SouthEast`. `PartFacing` stays exactly as it is — host-owned `viewOffset`/`mirrorX`; this
  amendment does not touch it (`PartFacingWiringTests` unchanged).
- `PlaybackLayer.animationKey` (0 = the layer was driven by a raw `Play`), written by
  `CommandApplySystem` on every Play/PlayAnimation/Stop, and `AnimEventOutput.animationKey` copied
  from the emitting layer (both `new AnimEventOutput` sites in `EventEmissionSystem`).
- `CommandKind.PlayAnimation = 5`, `CommandKind.StopAnimation = 6`; `AnimationCommand.animationKey`.

`PlaybackApi` gains:

```
PlayAnimation(ref commands, pendingEnabled, uint animationKey, float speed = NaN, LoopMode loop = UseClipDefault, float blendDuration = NaN)
StopAnimation(ref commands, pendingEnabled, uint animationKey, float blendDuration = NaN)
bool IsAnimationPlaying(in DynamicBuffer<PlaybackLayer> layers, uint animationKey)        // any layer whose animationKey matches and is Active
```

`Runtime/Api/ActorProfileApi.cs`: `bool TryResolve(ref ActorProfileBlob, uint animationKey,
Direction facing, out byte layerIndex, out ClipId clip, out int animationIndex)` — binary search on
key, then `FacingResolver.ResolveClipFacing(facing, blob.turnDirections, out clipFacing, out _)` and
`slots.ResolveSlot(clipFacing)` for a directional entry. NaN speed / `UseClipDefault` loop / NaN
blend on the command mean "the profile entry's value".

`CommandApplySystem.ApplyAnimationCommandsJob` takes `in ActorProfile` (WithAll — every actor bakes
one now) and `in ActorFacing`; `PlayAnimation` resolves then falls into the existing `ApplyPlay`
path with `layer.animationKey = key`; `StopAnimation` stops the entry's layer only if that layer's
`animationKey` matches (a Stop for an animation that is not playing is a no-op, not a layer stop).
An unknown key logs through the existing unknown-clip reporter with the key formatted `X`.

### 3.4 Facing re-pick — `ActorFacingRepickSystem`

`AnimationToolkitLogicSystemGroup`, after `CommandApplySystem`, before `PlaybackTimeSystem`. When
`facing != appliedFacing`: for every layer whose `animationKey != 0` and whose entry
`hasDirections`, resolve the new slot; if it differs from `layer.clip`, swap the clip **in place**
(same `time`, same `loop`, no crossfade — the A65 direction-variant re-pick precedent in
`CutsceneTimelineSystem`) and write `appliedFacing = facing`. A Two-coverage entry on a Six-turning
actor never swaps (its fold lands on the same slot); mirroring stays the host's `PartFacing` job.

### 3.5 Ragdoll trigger — `ActorRagdollTriggerSystem`

`AnimationToolkitLogicSystemGroup`, after `EventEmissionSystem`. Two sources, one rule:

- **At play** (`ragdollAtEventKey == 0`): `CommandApplySystem` records the trigger on the actor in a
  new `ActorRagdollRequest : IComponentData, IEnableableComponent { RagdollTrigger trigger }`
  (baked disabled) — the apply job is `ScheduleParallel` and `RagdollActor` is opt-in, so it must
  not take an `EnabledRefRW<RagdollActor>` (Phase D G2: an absent component excludes every actor).
- **At event**: the trigger system scans `AnimEventOutput` (gated `AnimEventsPending`) for an event
  whose `animationKey` names an entry with `ragdollAtEventKey == eventKey`.

The trigger system then, only where `RagdollActor` is present (lookup + `HasComponent`), sets it
enabled for `Start` and disabled for `Stop`, and clears the request. An actor with no ragdoll
bodies logs nothing — a profile is allowed to name a trigger a rig cannot honour; the Actor
Editor's validation badge says so instead (rule P6, a warning).

### 3.6 Cutscenes on the top layer

`CutsceneApi.TopLayer = byte.MaxValue`; `CreatePlayRequest(..., byte layerIndex = CutsceneApi.TopLayer, ...)`.
`CutsceneTimelineSystem` resolves it per bound actor as `playbackLayers.Length - 1` wherever it
reads `play.layerIndex`. A literal index still works. `Gotchas.md`'s "the cutscene layer must exist
on the rig" trap closes as a consequence — the top layer always exists.

### 3.7 What leaves

`RigAsset.layers`, `LayerDefinition`, `RigAsset.MaxLayerCount`, the rig inspector's `Layers` field,
the `1..8 layers` rule in `ClipValidation` (line ~317; its number is reused for P1 below),
`ClipRegistryBlob.layerCount`, `ActorAuthoring.rig` / `.clipSets` / `.startingLayers` /
`StartingLayerState` (replaced by `ActorAuthoring.profile`; `RigTargetAuthoring.rig == null` now
inherits `profile.rig`), `ActorAuthoringEditor`'s starting-layer rows, and `DirectionSetAsset`'s five
fields. `CutsceneSlot.rig`/`.clipSets` stay (a slot may stage an actor with no profile), and the
A71 cast panel will offer "fill from profile".

### 3.8 Validation — rules P1–P7 (`Authoring/Validation/ActorProfileValidation.cs`)

| Rule | Severity | Check |
|---|---|---|
| P1 | error | 2 ≤ `layers.Count` ≤ 8; `[0]` is Base and `[^1]` is Override |
| P2 | error | every `animationKey` is non-zero and in the registry (mirror of T3 for tags) |
| P3 | error | no `animationKey` appears twice in one profile (across layers) |
| P4 | error | a non-directional entry names a clip; a directional entry's `DirectionSlots` has a valid fill pattern (`TryGetEffectiveDirections`) |
| P5 | warning | a named clip is not in any of `clipSets` (it would resolve to nothing at play) |
| P6 | warning | `ragdollTrigger != None` on a rig with no `ragdollBodies` |
| P7 | warning | `startingAnimationKey` names an entry on a different layer, or `defaultActive` with no starter |

`ValidateBind(rig, clipSets, …)` is unchanged; the profile validator runs beside it in the Actor
Editor badge (A71) and at bake (`ActorBaker` refuses on any error, same as today's rig errors).

## 4. Decisions (recorded; revert notes inline)

- **A70-D1 `DirectionSlots` is a serializable class shared by the profile entry and
  `DirectionSetAsset`**, rather than the profile referencing `DirectionSetAsset`s. One animation =
  one entry, no asset per action. Revert: make the entry hold a `DirectionSetAsset` reference.
- **A70-D2 Name uniqueness is per profile, not per layer.** `PlayAnimation(key)` must be
  unambiguous without a layer argument. Revert: add a layer argument and relax P3.
- **A70-D3 The registry loses `layerCount`; the profile owns it.** One profile per actor, one
  registry per (rig, sets); a registry shared by two profiles with different layer counts is
  legal. Schema version of the registry bumps (the golden hash in `ClipRegistryDeterminismTests`
  changes with it — update it deliberately, in the same commit, and say so).
- **A70-D4 `ActorAuthoring` = `profile` + presentation fields.** `rig`/`clipSets`/`startingLayers`
  go; a prefab's `ActorAuthoring.profile` is the runtime truth. Revert: keep the three fields as
  overrides that win over the profile — not built, because two sources of truth is the bug G0 §7
  documented for `UnitSO.rig`.
- **A70-D5 The ragdoll trigger honours only `RagdollActor` presence, never adds it.** Bodies are
  rig content (RG); the profile only says *when*.
- **A70-D6 `ActorFacing` is host-written and never derived.** `PartFacing` untouched. Folding
  mirror into the package is a later amendment if the host wants to drop `UnitFacingSystem`'s
  part loop.

## 5. Tasks

After each: compile gate → the task's fixtures → tick → commit `A70-Tn: <what>`. Full suites once
at T10. **[parallel-safe]** tasks may go to a subagent that never touches MCP.

- [ ] **T1 — `DirectionSlots` extraction.** New class; `DirectionSetAsset` becomes `{ slots }`;
  `DirectionSetCoverageTests` re-targeted at `DirectionSlots`; cutscene readers of
  `directionSet.GetSlot` → `directionSet.slots.GetSlot` (`CutsceneBlobBuilder`, `CutsceneTimelineSystem`,
  the Cutscene editor panel, `DirectionSetsPanel`/`DirectionSetClipQueueView`). *Gate:*
  `DirectionSetCoverageTests`, `CutsceneTimelineSystemTests`.
- [ ] **T2 — `AnimationNameRegistry` + provider + settings page + `AnimNames` generation.**
  [parallel-safe with T3] Mirror `TargetTagRegistry` end to end. *Fixture:*
  `VocabularyRegistryPersistenceTests` gains the third registry's round trip;
  `TargetTagRegistryTests`' duplicate-guard test duplicated for names (one test).
- [ ] **T3 — `ActorProfileAsset`, `ActorLayerDefinition`, `ActorAnimationDefinition`, bookends,
  `ActorProfileValidation` P1–P7.** [parallel-safe with T2] *Fixtures:*
  `ActorProfileValidationTests` — one test per rule that fails for its own reason only (the
  `RagdollValidationTests` pattern); `DiskRoundTripTests` gains a saved-and-reloaded profile
  (HANDOFF §9 lesson 4 — a profile with a `NaN` `blendIn` must survive serialization).
- [ ] **T4 — Blob + builder + `ActorProfileApi.TryResolve`.** Port `DirectionSetBlob.ResolveSlot`
  and `DirectionSetBlobFoldTests` (the game's) into the package as `DirectionSlotsBlob` +
  `ActorProfileFoldTests`. *Fixtures:* those, plus `ActorProfileBuilderTests` (sorted keys,
  binary-search resolve, determinism of the hash).
- [ ] **T5 — Layers move.** Delete `RigAsset.layers`/`LayerDefinition`/`MaxLayerCount`, the rig
  inspector field, the `ClipValidation` layer rule, `ClipRegistryBlob.layerCount` (+ hash term,
  schema bump, golden hash). `ActorAuthoring` → `profile`; `ActorBaker` sizes and seeds layers from
  the profile, adds `ActorProfile` + `ActorFacing` + `ActorRagdollRequest`; `RigTargetBaker`
  inherits `profile.rig`. Update `AuthoringTestAssets`, `ActorBakeFixture`, `PlaybackTestActor`,
  `DataContractTests`, both samples. *Gate:* every fixture in §2 item 7 compiles and stays green;
  `ActorBakingAcceptanceTests` gains "a profile with three layers bakes a three-element
  `PlaybackLayer` buffer seeded from `startingAnimationKey`".
- [ ] **T6 — Commands.** `CommandKind.PlayAnimation/StopAnimation`, `AnimationCommand.animationKey`,
  `PlaybackLayer.animationKey`, `PlaybackApi.PlayAnimation/StopAnimation/IsAnimationPlaying`,
  `CommandApplySystem` resolve path. *Fixtures:* `CommandApplySystemTests` — PlayAnimation on a
  directional entry at facing `NorthWest` lands the `northEast` slot's clip on the entry's layer
  with the layer's `animationKey` set; `StopAnimation` for a key not on its layer leaves the layer
  playing (fails if implemented as a plain layer stop); `PlaybackApiTests` — `IsAnimationPlaying`
  true only while Active.
- [ ] **T7 — `ActorFacingRepickSystem`.** *Fixture:* `ActorFacingRepickTests` — flip `facing`
  from `SouthEast` to `NorthEast` on a Four-coverage entry: the layer's clip swaps and `time` is
  preserved; on a Two-coverage entry nothing swaps. `SystemGroupStructureTests` gains the edge.
- [ ] **T8 — `ActorRagdollTriggerSystem` + `AnimEventOutput.animationKey`.** *Fixture:*
  `ActorRagdollTriggerTests` — an entry with `Start` at play enables `RagdollActor` the same frame;
  an entry with `Start` at event key K enables it only on the frame K is emitted; `Stop` disables;
  an actor without `RagdollActor` is untouched and logs nothing. `EventEmissionSystemTests` gains
  the `animationKey` copy.
- [ ] **T9 — `CutsceneApi.TopLayer`.** *Fixture:* `CutsceneTimelineSystemTests` — a request at
  `TopLayer` drives `PlaybackLayer[Length-1]` on an actor with three layers.
- [ ] **T10 — Docs, CHANGELOG, version, full suites.** New `Documentation~/actor-profiles.md`
  (what a profile is, bookends, direction per animation, `PlayAnimation`, ragdoll triggers,
  `ActorFacing` contract); `index.md` entry; `getting-started.md` and `cutout-characters.md`
  re-read for `startingLayers`/`rig.layers` mentions; `cutscene-api.md` for `TopLayer`;
  `sharing-clips.md` unaffected. CHANGELOG `## [0.16.0]` with a **Breaking** block; `package.json`
  and the `PackagingConformanceTests` version assertion together. `Samples~` compile-checked through
  a temp assembly. Full suites: EditMode, PlayMode, plus `StitchPunk.Tests`/`.PlayMode` **expected
  red on the game side** — `UnitSO`/`ActorAuthoring` field removals break the game until G5; record
  the exact failing game fixtures in §7 and stop. Update HANDOFF §4 (one paragraph) and §1's
  "read first" to name this amendment.

## 6. Owner checkpoint

None inside A70 — nothing here is visible. A71 carries the checkpoint.

## 7. Build log

- *(drift, decisions taken, the registry golden hash before/after, the list of game fixtures left
  red for G5.)*
