# Cutout characters and flipbook parts

The paper-doll route: a character assembled from separate quads — head, torso,
upper arm, lower arm, hand — each one a **rig target** the toolkit can move,
rotate, scale and re-frame independently. No skeleton, no skinning, no VAT bake.

This is the technique to reach for when the art is 2D and the motion is
part-by-part. For rigged 3D characters see
[`rigged-characters.md`](rigged-characters.md); for the editor itself see
[`clip-editor.md`](clip-editor.md).

> **"Cutout" does not mean "flat".** `TransformKey` carries `float3 position,
> float3 rotation, float3 scale` — three axes throughout. A 2.5D character
> usually leaves x and y rotation at zero, but nothing requires it, and the same
> tracks animate a 3D prop or vehicle perfectly well.

---

## What drives a part

Two track kinds bind to a rig target, and they compose rather than compete:

| Track | Drives | Interpolates? |
|---|---|---|
| **`TransformTrack`** | The part's position, rotation and scale | Yes — linear, eased, stepped or Bézier |
| **`SpriteTrack`** | Which slice of a texture array the part shows | **No.** A frame index is a discrete instruction |

A part can have both. An arm that swings *and* changes its drawn frame is one
target with a transform track and a sprite track, keyed on the same timeline.

**Transform values are offsets from rest, not absolutes.** Position and rotation
add to the part's rest pose; scale multiplies it. A key of "no offset" leaves the
part exactly where the prefab put it. That is what lets one clip drive several
characters whose proportions differ, and it is why the Clip Editor's preview
takes each part's rest pose from the prefab you assign in the **Bone Source**
field.

---

## The cutout workflow, end to end

### 1. Build the prefab

One child GameObject per animatable part, each with a quad and a material. Nest
them the way they move — hand under lower arm, lower arm under upper arm — so
that rotating the shoulder carries the whole limb.

**The names matter.** A rig target binds to its prefab transform by name, so
`Torso` in the rig finds the object called `Torso`. Rename one later and the Clip
Editor's reconciliation panel will tell you (see
[`clip-editor.md`](clip-editor.md)), but it is a rename you have to follow
through.

### 2. Create the rig asset

**Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Rig Asset**

Add one target per part. Targets carry **stable ids**, minted once and never
derived from the name — which is precisely why renaming a part or reparenting it
never re-points a track.

Also declare your **layers** here. Layer index *is* priority: higher layers
composite over lower ones, so reordering them is a content edit, not a rename. A
typical set is `Base` for locomotion and `Override` for an upper-body action.

### 3. Author clips

**Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Clip Set Asset**, then a
`ClipAsset` per animation, then open the **Clip Editor**.

Unlike a rigged character, a cutout clip's tracks are authored *here* rather than
imported. Select a part, scrub, move it, key it. The full window reference is
[`clip-editor.md`](clip-editor.md).

Set `duration`, `defaultLoop`, and the `defaultBlendIn`/`defaultBlendOut` that
decide how the clip crossfades.

### 4. Set up the actor

Add `ActorAuthoring` to the prefab root and assign the clip set. Add
`RigTargetAuthoring` to each part, pointing at its rig target.

**Put the prefab in a SubScene.** Baking is what turns authoring assets into
entities; a prefab in a plain scene will not animate.

### 5. Play it

Send an `AnimationCommand` naming the clip id. `ClipSetAsset`'s inspector has
**Generate Clip Id Constants**, which writes a C# file of `public const ulong`
values so game code references clips by name rather than by magic number.

---

## Flipbook parts

A sprite track keys an **integer index** into a texture array — frame-by-frame
art on a part that a transform track may also be moving.

**Indices step on their key.** The key at or before the playhead is what shows,
and it holds until the next key's own time. There is no interpolation and no
midpoint crossover: a frame index has no meaningful in-between value, so the
change lands exactly where you put it.

### Two independent bases, and why both exist

A stored index passes through two retargeting steps before it reaches the
material, and they answer different questions:

| Step | Set on | Answers |
|---|---|---|
| **`indexMode`** — `Absolute` or `RelativeToBase` | The key, against the track's `baseIndex` | "Where does this *track* live in the array?" |
| **`sliceSpace`** — `Absolute` or `RelativeToRest` | The track, against the entity's `restSliceIndex` | "Which *variant* is this character?" |

The first is authoring-time: a mouth track based at 0 and an eye track based at
32 can animate the same part without either knowing the other exists, and moving
`baseIndex` slides a whole track onto a different span of the array with every
relative key keeping its offset.

The second is runtime: a design or skin system writes `TargetRestPose.restSliceIndex`
per entity, and a `RelativeToBase` track then reads as an offset from *that*, so
one clip drives every variant of a character.

> **A relative key keeps its offset, not its resolved frame.** Toggling a key
> between `Absolute` and `RelativeToBase` in the Clip Editor is lossless, and the
> inspector shows both readings (`+5 → 37`) so there is no guessing which number
> you are looking at.

### Facing and mirroring

Direction selects *which clip plays*, not an offset applied to one. A rig seen
from the front does not move like the same rig from the side plus a nudge — the
motion is different data. So a directional character is a clip-set convention
(`Walk_N`, `Walk_E`, …) with the game choosing the `ClipId`.

What the package ships to support that is the `MirrorPair` table on the rig
(authored per rig — mirrors are never inferred from names) and the **Mirror Clip**
utility that uses it, so a left-facing clip is generated from its right-facing
twin rather than authored twice.

---

## Common failures and what they actually mean

| Symptom | Cause |
|---|---|
| Nothing animates at all | The prefab is not in a SubScene, or the clip set failed validation and baked no registry |
| A part sits at the origin at unit scale | Its rig target's name matches no transform in the prefab — the Clip Editor's reconciliation panel reports this |
| A part animates but the wrong one moves | Two rig targets share a name; ids are unique but the preview binds rest poses by name |
| A frame change lands early or late | Timing authored against a build older than 0.9.0, where flipbook indices switched at the segment midpoint rather than on the key |
| The whole character animates as one | Parts nested under one another inherit each other's motion — check the prefab hierarchy |
| A frame index shows nothing | `-1` is the "keep the current frame" sentinel for absolute keys, not an array index |
