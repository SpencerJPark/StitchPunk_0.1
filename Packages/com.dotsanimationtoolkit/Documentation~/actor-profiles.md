# Actor profiles

An `ActorProfileAsset` is what an actor **has**: the rig it's bound against, the clip sets its
registry is built from, how many directions it turns through, and a set of named, layered
animations. Games play by name — `PlaybackApi.PlayAnimation(walkKey)` — never by raw clip id or
layer index. One `ActorAuthoring.profile` field replaces the rig/clip-set/starting-layer fields
that used to live directly on the actor.

```
ActorProfileAsset
  rig              — the RigAsset every animation here is bound against
  clipSets         — what the registry is built from
  turnDirections   — how many directions this actor turns through (content, not rig)
  layers           — ordered playback layers, [0] = Base, [^1] = Override
```

---

## Layers, and the fixed bookends

A profile's `layers` list is playback-priority order, exactly like the old rig layers were —
higher layers composite over lower ones. What changed is where they live: layers are the actor's
content now, not the rig's, so two actors sharing a rig can carry a different number of them.

Every profile always has at least two layers, and the first and last are fixed:

- **`layers[0]` is always named `Base`.**
- **`layers[^1]` is always named `Override`.**

`ActorProfileAsset.EnsureBookends()` creates both the moment a profile is created and refuses to
let either be removed or reordered — the inspector can't do it, and neither can a script that
edits the list directly, because the next `OnValidate` puts them back. Anything you add lives
between them. The reason they're structural rather than a convention: `BehaviorExecution` and the
Actor Editor (A71) both key "the top layer" and "the base layer" off list position, so a profile
can never exist in a state where either question has no answer.

`MaxLayerCount` is 8, bookends included.

## One entry, one animation name

Every `ActorAnimationDefinition` names an **animation key** — an id minted once in the project's
Animation Names registry and never reused. You never type or see the raw `uint`: every picker in
the toolkit that shows an animation is a `VocabularyPicker` over that registry, exactly like the
target-tag and event-name pickers. Generate a C# `AnimNames` class from **Project Settings ▸ DOTS
Animation Toolkit ▸ Animation Names** (the third vocabulary page, alongside Target Tags and Event
Names) so game code writes `PlaybackApi.PlayAnimation(AnimNames.Walk)` instead of a magic number.

A key is unique **across the whole profile**, not per layer — `PlayAnimation(key)` has to resolve
to exactly one entry without a layer argument, so `ActorProfileValidation` rejects (P3) a key that
appears on two layers of the same profile.

Each entry also carries how it plays: `loop` (`UseClipDefault` defers to the clip's own authored
default), `speed`, `blendIn` (`NaN` = the clip's default), and an optional ragdoll trigger (below).

## A plain clip, or a direction

An entry is one of two shapes:

- **Non-directional** — names one `ClipAsset` outright. What plays never depends on facing.
- **Directional** — names a `DirectionSlots` instead: up to five clips, one per east-side facing.

```
DirectionSlots
  southEast   — front three-quarter, facing the camera toward the right
  northEast   — back three-quarter, facing away toward the right
  south       — head-on, facing the camera
  north       — head-away, facing away from the camera
  east        — true profile, facing right
```

Only the east side is ever authored. The west side (`southWest`, `northWest`, `west`) is served by
mirroring the corresponding east slot — the same mirror `PartFacing` already applies for a
part tagged **Faces Direction** — so filling five slots covers eight directions with five clips,
not eight.

**Coverage is derived from which slots are filled, never declared.** Filling only `southEast`
gives a Two-coverage set (it never turns, mirror only); adding `northEast` promotes it to Four;
adding `south` and `north` promotes it to Six; adding `east` completes Eight. `targetDirections` on
the entry is authoring intent only — a note to yourself about where you're headed — the actual
coverage the runtime resolves against is always read back from the fill pattern
(`TryGetEffectiveDirections`). A set of filled slots that doesn't form one of these five patterns
is a validation error (P4).

At play, the profile resolves the actor's `ActorFacing.facing` against `turnDirections` (the
actor's own granularity) and then against the entry's own effective coverage — an entry with fewer
authored directions than the actor turns through folds onto whatever it actually has, so a
Two-coverage walk on a Six-turning actor still plays something for every facing instead of nothing
for the facings it was never given.

`DirectionSetAsset` (used by cutscene clip blocks) is now a thin wrapper around the same
`DirectionSlots` class — `directionSetAsset.slots.southEast`, not a field on the asset directly.

## Starting animations

`ActorLayerDefinition.startingAnimationKey` seeds that layer's `PlaybackLayer` at bake, resolved
through the same facing-fold path `PlaybackApi.PlayAnimation` uses at runtime (facing defaults to
`SouthEast`). `0` means "nothing" — the layer bakes stopped. `defaultActive` with no starting key
is a warning (P7): the layer is marked active but has nothing seeded to be active with.

A key can seed whichever layer its entry actually lives on, which is not necessarily the layer
that named it as its starter — `ActorBaker` warns (also P7) when the two disagree.

## `ActorAuthoring.profile`

`ActorAuthoring` now carries one required field, `profile`, plus presentation-only fields
(`sampleOverride`, `addDistanceLod`, `billboardMode`, `frozenYawDegrees`) that have nothing to do
with what the actor plays. `RigTargetAuthoring.rig == null` inherits `profile.rig`. An actor with
no profile, or a profile with no rig or no clip sets, fails the bake with a console error naming
the GameObject.

## The `ActorFacing` contract

```csharp
public struct ActorFacing : IComponentData
{
    public Direction facing;         // host-written
    public Direction appliedFacing;  // the package's last re-pick
}
```

**The package never derives `facing`.** What "forward" means — movement direction, a look target,
a cutscene angle — is entirely the host's call; the package only reads it. Write `facing` from
your own movement/AI system whenever it changes.

When `facing != appliedFacing`, `ActorFacingRepickSystem` walks every layer whose active clip came
from a directional entry and swaps its clip **in place** — same `time`, same `loop`, no crossfade,
the same hard-cut re-pick `CutsceneTimelineSystem` already does for a cutscene direction variant —
then writes `appliedFacing = facing`. An entry whose coverage folds every facing onto one slot (a
Two-coverage entry on a Six-turning actor) never swaps, because the fold lands on the same clip
either way.

`PartFacing` is untouched by any of this. It stays exactly what it always was — the host-owned
`viewOffset`/`mirrorX` a part mirrors by — and this amendment does not fold it into `ActorFacing`.

## Playing by name

```csharp
using DotsAnimationToolkit;

// Somewhere with a DynamicBuffer<AnimationCommand>, an
// EnabledRefRW<AnimationCommandPending>, and (to read back state) a
// DynamicBuffer<PlaybackLayer> for this actor:

PlaybackApi.PlayAnimation(ref commands, commandPendingEnabled, AnimNames.Walk);

// ... later, when the walk should end:
PlaybackApi.StopAnimation(ref commands, commandPendingEnabled, AnimNames.Walk);

bool isWalking = PlaybackApi.IsAnimationPlaying(playbackLayers, AnimNames.Walk);
```

- **`PlayAnimation`** resolves the key against the actor's current `ActorFacing` and starts it on
  whichever layer the entry lives on — you never name a layer. `speed`/`loop`/`blendDuration`
  default to `NaN`/`UseClipDefault`/`NaN`, meaning "use the entry's own authored value."
- **`StopAnimation`** stops the entry's layer **only if that layer is still playing this exact
  key** — calling it for an animation that already moved on, or never started, is a no-op rather
  than stopping whatever happens to be on that layer now.
- **`IsAnimationPlaying`** is true only while some layer's active clip was started by this key and
  that layer is currently `Active`.

An unresolved key (renamed away, deleted from the registry, or never baked) is reported through the
same resolve-failure event a bad raw `Play` clip id uses — no layer is touched.

## Ragdoll triggers

An entry can start or stop the actor's ragdoll:

```
ragdollTrigger      — None / Start / Stop
ragdollAtEventKey   — 0 = at play, immediately; else an event marker key on the clip
```

- **At play** (`ragdollAtEventKey == 0`): the trigger fires the same frame the animation starts
  playing.
- **At an event**: the trigger fires only on the frame the named marker actually emits — so a death
  animation can play its first few frames of reaction before the body goes limp.

Either way, the trigger is honoured **only where the rig has ragdoll bodies** (`RagdollActor` is
present on the baked entity). A profile is allowed to name a trigger a rig cannot honour — nothing
is logged at runtime for it, because that would spam every frame an unragdolled actor plays such an
entry; the Actor Editor's validation badge (P6) is where that mismatch is reported to the author.
`Start` enables `RagdollActor`; `Stop` disables it. The trigger never adds `RagdollActor` to an
actor that doesn't already have it — ragdoll bodies are rig content, and a profile only says *when*.

## Validation (P1–P7)

| Rule | Severity | Checks |
|---|---|---|
| P1 | Error | `2 ≤ layers.Count ≤ 8`; `layers[0]` is named `Base`, `layers[^1]` is named `Override` |
| P2 | Error | Every `animationKey` is non-zero and exists in the Animation Names registry |
| P3 | Error | No `animationKey` appears on more than one layer of the same profile |
| P4 | Error | A non-directional entry names a clip; a directional entry's filled slots form one of the five valid coverage patterns |
| P5 | Warning | A named clip isn't in any of the profile's `clipSets` — it would resolve to nothing at play |
| P6 | Warning | `ragdollTrigger != None` but the rig declares no ragdoll bodies |
| P7 | Warning | `startingAnimationKey` names an entry on a different layer, or `defaultActive` is set with no starting key |

P2 (registry membership) is judged at bake and in the Actor Editor badge, not by
`ActorProfileBuilder` itself — building a blob has no access to the editor-only vocabulary
provider that the registry check needs.

## Authoring in the Actor Editor

Double-click an `ActorProfileAsset` (or open the Clip Editor and pick the **Actor Editor** tab,
alongside New Rig · Clip Editor · VAT Bake · Cutscene Editor) to author and test one live. Unlike
the Clip Editor, which previews one clip, this tab previews the whole profile: every layer
composited, triggered the way the game triggers them, turning through the profile's directions,
dropping and restoring ragdoll on the entries that say so.

**Header.** A profile field and a validation badge (P1–P7 plus clip/rig binding). The three panes
below it — **Layers**, **Preview**, **Actor Inspector** — each carry a title, the same way the Clip
Editor's do.

**Transport.** Under the preview, in the toolkit's shared icon style: ⏮ resets every layer to its
starter or inactive, ragdoll off, direction to south-east; ▶/⏸ runs the composer; ■ pauses and
resets; ▶ (step) advances one thirtieth of a second while paused. Space, ← → and Home reach it
whenever this tab is showing. Beside the buttons, a direction slider with a readout in the form
"137° → SouthEast, mirrored".

**Layers column.** Each layer is its own box. `Base` and `Override` are fixed bookends — no delete,
no reorder — with **+ Layer** in the pane header inserting between them; other layers move with the
▲/▼ buttons in their box header. The box header also carries an **eye**, which is the layer's
`defaultActive` (whether the baked actor starts with the layer on — the preview re-seeds when you
change it, so the layer visibly stops or starts), a live dot while the composer has it active, and
the starter-animation button. **+** at the foot of a box opens the animation-name picker (typing a
new name mints it in the registry). Each animation row carries ▶/■ to trigger
`PlayAnimation`/`StopAnimation` on the live composer, a live dot while playing, and its own per-row
scrub field.

**Inspector column.** Blocks for whichever of profile / layer / animation is selected. An
animation block has a direction-dimension toggle that swaps a plain clip field for the slot queue
(south-east through east) with the same derived coverage readout as the coverage rules above, plus
loop/speed/blend-in (each with a "use clip default" option) and, for a ragdoll trigger, a
Start/Stop choice and an at-event picker from the Event Names registry.

**Preview.** Every layer is advanced by the runtime's own step function (`PlaybackTimeMath`,
shared with `PlaybackTimeSystem`, so the preview and the game can never drift out of step), then
composited into one pose; west-side facings are the mirrored east slot, exactly as in the game.

**Ragdoll mix.** An entry's `Start` trigger drops the previewed body into the ragdoll solver while
every other layer keeps animating — a death-face sprite layer keeps stepping on a non-body part
while the body lies limp, the mix the ragdoll-triggers section above describes. `Stop` restores the
captured pose exactly, then the entry plays. A rig with no ragdoll bodies refuses and says so in
the status line (the P6 case) rather than silently doing nothing.

## What changed from rig layers

> **Breaking.** If you have an existing project on an earlier version of this package:
>
> - `RigAsset.layers` / `LayerDefinition` / `RigAsset.MaxLayerCount` are gone. Layers are
>   authored on an `ActorProfileAsset` now.
> - `ActorAuthoring.rig`, `.clipSets`, and `.startingLayers` are gone, replaced by one
>   `ActorAuthoring.profile` field. Every actor needs an `ActorProfileAsset` naming its rig and
>   clip sets.
> - `DirectionSetAsset`'s five clip fields and its coverage methods moved onto a nested
>   `DirectionSetAsset.slots` (a `DirectionSlots`). Update `directionSet.southEast` to
>   `directionSet.slots.southEast`, and likewise for the other four slots and `GetSlot`/`SetSlot`/
>   `TryGetEffectiveDirections`.
> - `ClipRegistryBlob.layerCount` is gone — a registry is per (rig, clip sets); layer count is
>   per profile now, so two profiles can share one registry with different layer counts.
>
> There is no migration path. A pre-existing rig's layers and an actor's rig/clip-set/starting-layer
> fields need to be re-authored by hand onto a new `ActorProfileAsset`.
