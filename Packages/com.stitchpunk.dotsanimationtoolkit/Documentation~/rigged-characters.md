# Rigged characters and bone VAT

## Two routes to a rigged character's motion

You can **author bone animation directly in the Clip Editor**, or **import it from Blender**. Both bake to the same VAT texture, and a single clip can use both — some bones authored, others imported.

| Route | Author with | Best for |
|---|---|---|
| **Bone tracks** (`BoneTrack`) | The Clip Editor timeline | Motion that has to line up with flipbook, socket or event rows on the same character. Hit frames, reactions, anything hybrid |
| **Imported clip** (`vatSource` / `vatTracks`) | Blender, Maya, Unity's animation window | Full walk cycles and anything authored from nothing. Blender is the better tool for that and always will be |

**Why bone tracks exist at all:** the value is composition. A hit frame whose event marker, arm swing, cape VAT and weapon socket all sit on one timeline and scrub together is the thing this toolkit is for. Doing that across two applications is the friction it removes.

**What bone tracks are not:** a rigging suite. There is no weight painting, no IK solver, no constraint graph. You are keyframing an existing skeleton — one imported and rigged elsewhere.

> **A note on key types.** `TransformKey` carries `float3 position, float3 rotation, float3 scale` — three axes throughout, because nothing animated here is assumed to be flat. Rotation is Euler degrees in ZXY order, the numbers you type and drag. Bones use a separate `BoneKey` whose rotation is a full quaternion, because nobody types those: they arrive from a bake or a solver, and a quaternion has no gimbal order to agree on.

---

## Are bone VATs DOTS-friendly?

**Yes — it is the most DOTS-native animation path in this package.** That is precisely why it exists.

A VAT-driven character has:

- **No `Animator` component.** Nothing per-entity to update on the main thread.
- **No GameObject bone hierarchy.** The skeleton exists only as texel rows in a texture at runtime; the mesh renders through an ordinary `MeshRenderer` with no `SkinnedMeshRenderer` anywhere.
- **No CPU skinning.** The vertex shader reads the pose out of the texture and skins on the GPU.
- **Three floats of per-instance data**, and nothing else:

| Property | Written by | Meaning |
|---|---|---|
| `_VatFrameA` | `VatMaterialSystem` | fractional global frame of the current clip |
| `_VatFrameB` | `VatMaterialSystem` | frame of the clip being crossfaded *from* |
| `_VatBlend` | `VatMaterialSystem` | 0→1 weight between them |

Because those are `[MaterialProperty]` components inside the DOTS instancing block, **every instance shares one material**, which is what lets the Batch Renderer Group draw a crowd of them in very few draw calls. Two characters on different frames of different clips still batch together.

The per-frame CPU cost is a Burst job writing three floats per visible part. That is the whole animation cost.

### What you give up

VAT trades flexibility for that. Be deliberate about it:

- **The pose is frozen at bake time.** No IK, no procedural bone override, no runtime retargeting, no ragdoll blending. What you baked is what plays.
- **Blending is a two-frame lerp.** You can crossfade clip A into clip B (`_VatFrameA`/`_VatFrameB`/`_VatBlend`). You cannot run an additive layer on top of a VAT part — `VatDriven.layerIndex` binds a part to exactly *one* playback layer.
- **Memory scales with frames × bones.** Bone flavour writes a 3×4 matrix as **3 texture rows per frame**, at RGBAHalf (8 bytes/texel) unless you enable full precision. Texture width is the next power of two above your bone count, capped at 1024, wrapping to more rows beyond that.

  A 40-bone rig → width 64 → 64 × 3 × 8 = **1.5 KB per frame**. Ten seconds at 30fps ≈ 450 KB. That is cheap. A 200-bone rig at 60fps for a minute is not.
- **Half precision has a size cliff.** For rigs much larger than a couple of metres, quantisation shows up as stepping. Turn on **Full Precision (RGBAFloat)** and pay double memory.

### When to use which technique

| Use | For |
|---|---|
| **Bone VAT** | Rigged 3D characters, crowds, anything where you want hundreds on screen and don't need runtime pose control |
| **Vertex VAT** | Cloth, blendshapes, anything a skeleton can't express. Reproduces *any* deformation, but memory scales with vertex count rather than bone count — much larger |
| **Transform tracks** | Anything keyed part-by-part in this toolkit — 2.5D paper-doll characters, and 3D props and vehicles, since rotation and scale carry all three axes |
| **Flipbook sprite tracks** | Frame-by-frame art, per-part texture-array slices |

These compose. A single actor can have VAT parts and flipbook parts at the same time — a VAT torso with a flipbook head is a supported setup, because VAT and sprite parts resolve per *part*, not per clip.

---

## The rigged-character workflow, end to end

### 1. Author the animation in Blender

Rig and animate as you normally would. Nothing about this step is toolkit-specific.

Two things to keep in mind for later:

- **Bone names are a contract.** Sockets bind to bones *by name*, because an imported hierarchy is not something this package can assign ids inside. Renaming a bone after you've set up sockets breaks the binding — the bake reports the unresolved name rather than silently baking a socket pinned to the origin, but it is still a rename you have to follow through.
- **Keep the bone count honest.** It sets your texture width and therefore your memory.

### 2. Import to Unity

Standard FBX import. You want a `SkinnedMeshRenderer` in a scene — the baker poses the real hierarchy to sample it, so the rig must be *instantiated*, not just an asset on disk.

### 3. Create the rig asset

**Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Rig Asset**

Add one target per VAT sub-mesh you want independently controlled. For a single-mesh character that is one target. Targets carry stable ids — never rename-derived — so renaming a target later never re-points a track.

If you want attachment points (a weapon in a hand, an effect at a fingertip), add **sockets** here too, with `mode = Bone` and the bone name from step 1. The `RigAsset` inspector gives you a dropdown of bone names once you assign a source prefab, which is the way to avoid typos.

### 4. Create the clip set and clips

**Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Clip Set Asset**, then a `ClipAsset` per animation.

Here is where a rigged character's clips differ from a cutout character's: you do **not** author tracks. Instead you point the clip at its source animation:

- **One source for the whole clip:** set `vatSource.sourceClip` to the imported `AnimationClip`.
- **Different sources per part:** add `vatTracks` rows, each naming a target and its own source clip. This is how a torso and a cape can come from different Blender actions in one clip.

Set `duration`, `defaultLoop`, and `loopSafe` (which appends a duplicate of frame 0 so the shader's two-frame lerp never reads across the loop seam into the next clip's rows).

### 5. Bake

**Window ▸ DOTS Animation Toolkit ▸ VAT Bake**

Assign the clip set and the scene's `SkinnedMeshRenderer`, choose **Bone Matrix** flavour, set your sample rate, and bake.

It produces the textures, a `VatTextureSetAsset` holding the per-clip frame ranges, **a runtime mesh** with bone influences packed into `UV1`, and — if your rig declares bone sockets — their baked motion.

> **The runtime mesh matters.** A plain `MeshRenderer` does not bind `BLENDINDICES`/`BLENDWEIGHT`, so the bone influences travel in `UV1` as `(idx0, idx1, w0, w1)`. Use the mesh the bake produced, not your original. If you skip it, nothing errors — the mesh renders as a **motionless clump**, because every vertex reads bone 0 at weight 0.

Watch the Console: unresolved socket bone names are reported here as warnings.

### 6. Set up the actor

Add `ActorAuthoring` to a prefab, assign the clip set, and add a child per target with `RigTargetAuthoring`. Assign the baked runtime mesh and a material using `ToolkitVatCrowdUnlit` (or your own shader — see [`shader-contract.md`](../../../Docs/AnimationToolkit/shader-contract.md)).

**Put the prefab in a SubScene.** Baking is what turns authoring assets into entities; a prefab in a plain scene will not animate.

### 7. Play it

Send an `AnimationCommand` naming the clip id. `ClipSetAsset`'s inspector has **Generate Clip Id Constants**, which writes a C# file of `public const ulong` values so game code references clips by name instead of by magic number.

---

## What else the Clip Editor gives you on a rigged character

Beyond authoring bone tracks:

- **The rig hierarchy is the bone picker.** Assign your rigged prefab to the toolbar's **Rig** field and its transforms appear as a tree in the left column. Select a bone there and the inspector offers **Add Bone Track** for exactly that bone — no typing, so the "name resolved to nothing and the bake froze it at rest" failure cannot happen. Bones the selected clip already animates are shown in bold. With no rig assigned you can still type a name, which is the only option a cutout set has.
- **Click the model to select it.** Clicking in the viewport picks the object under the cursor and selects it in the tree; selecting in the tree outlines it in the viewport. It picks the *child* under the cursor, not the prefab root, so a hand or a prop is one click away. Selection follows the pose as you scrub.
  - **Alt- or shift-click cycles** through everything stacked under the cursor, nearest first. Repeat it to walk backwards through overlapping parts; an ordinary click returns to the nearest.
  - **Bones are clickable even where there is no geometry.** Every joint of a `SkinnedMeshRenderer` gets an octahedral handle, linked to its parent so the skeleton reads as a skeleton, and the handle is the click target. Joints are offered *ahead* of the mesh around them — a bone sits inside the thing it deforms, so ordering strictly by depth would make it impossible to click. The mesh underneath is still reachable by cycling.
  - Dragging orbits, as before. A click only selects if the pointer did not travel, so an orbit never changes the selection.
- **Keying.** Select a part and its transform is always on screen, updating as you scrub. The field border says which of three things you are looking at: a stored key, a sampled in-between value, or a change you have made but not keyed. **Auto Key** writes edits straight into a key at the playhead; with it off, press **Key** to keep one.
- **Gizmos.** W/E/R give move, rotate and scale handles on the selected part in the viewport. They write the same values the numeric fields do — one code path — and commit a key on release when Auto Key is on. Rotate is a single Z ring and scale is XY only, because that is exactly what a cutout part's data has.
- **The dopesheet.** Expand a track to see its channels as separate rows. Drag keys horizontally to retime; drag across empty space to box-select; a key dragged past a neighbour reorders rather than stopping. Selecting a key exposes its easing, and **Bézier** gives draggable tangent handles plotted through the same function the runtime evaluates.
- **Focus.** Selecting a part filters the timeline to that part's tracks — the way to read a busy clip. The status line names what is shown and how many rows are hidden; deselect to bring them back. Event rows always stay visible, because they belong to the clip rather than to any one part.
- **Several parts at once.** Ctrl- or shift-click in the hierarchy to select more. The timeline shows all of their tracks together, and the inspector gives each part its own labelled block — its own live transform, its own flipbook indices — so there is never a question of whose numbers are whose. One block is marked **(active)**: the one the viewport gizmo and outline are on, which can only be in one place.
- **Flipbook indices step on their keys.** A frame index does not interpolate: the key at or before the playhead is what you see, and it holds until the next key's own time. Scrub through a key and the number changes exactly there, not halfway to the next one.
- **Parts start where the prefab puts them.** Assign the prefab in the toolbar's **Rig** field and each rig target picks up the position, rotation and scale of the prefab transform with the same name, measured relative to the prefab root. That is the part's rest pose, and a clip animates *from* it — position and rotation add to it, scale multiplies it — which is exactly how the runtime composes, so what you preview is what plays. A target with no matching transform in the prefab falls back to the origin at unit scale.
- **The space is centred on 0,0,0, and squares are one unit.** Two grids: a backdrop in the XY plane to measure a flat rig against, and a floor in the XZ plane so a 3D prop or vehicle has something to stand on. Each square is one world unit — the height of Unity's default cube — so on a character running about two units tall a square reads directly as half its height. The origin is drawn as three short axis stubs, X red, Y green, Z blue. The rig is spawned there, so a character authored standing on the floor reads as standing on the floor.
- **The camera frames the rig, not the origin.** It opens aimed at the middle of the rig and far enough back to hold all of it, sized from the rig's own bounds. Placement and framing are separate: the rig sits at 0,0,0, but aiming there would look at the ground between a character's feet. Framing happens once when the rig or the loaded prefab changes — after that the camera is yours. Double-click the viewport to reframe.
- **Retiming.** Change `duration` and every key moves with it — times are normalized, so a re-time never moves a key relative to the clip.
- **Events.** Place `EventMarker`s on the timeline — footfalls, hit frames, VFX triggers. These surface at runtime in the actor's `AnimEventOutput` buffer.
- **Layer and blend defaults.** `defaultBlendIn`/`defaultBlendOut` set how the clip crossfades in and out.
- **Socket offsets.** The preview draws rig-target socket markers so you can tune an offset visually instead of guessing and entering Play mode.
- **Validation.** The toolbar badge runs the same `ClipValidation` the bake runs, so an error you see here is the error a bake would throw, with the same rule code.
- **Scrubbing the composite.** The preview poses through `ClipSampler` — the runtime's own functions — so what you scrub is what plays.

---

## Editing the rig itself

The Clip Editor animates a rig. It does not restructure one — parenting, adding parts and moving meshes happen in Unity's prefab mode, which already handles them correctly. What the window provides is a short path there and an honest account of what changed when you come back.

### Getting into prefab mode

- **Edit Prefab** in the hierarchy header opens the loaded prefab.
- **Right-click any row** for *Open Prefab Here* (opens with that object selected and framed), *Ping in Project*, and *Select in Scene*.
- **Double-click a row** does the same as *Open Prefab Here*.

It is a mode switch, not a window arrangement. The Clip Editor docks beside the Scene view, so entering prefab mode brings the Scene view and Hierarchy forward and the Clip Editor steps behind on its own; leaving prefab mode brings it back with the playhead and selection where you left them. You never have to move a window.

> If your Clip Editor is currently floating, the first **Edit Prefab** docks it for you, carrying its clip set, playhead and selection across. That happens once.

### Coming back

Saving or closing the stage reloads the preview, rebuilds the tree, and puts the playhead and selection back where they were — selection by name, since the tree's ids are indices into a hierarchy your edit just changed.

Then it tells you what no longer binds. **It will not silently drop a track.**

> **What a restructure can and cannot break.** Transform and sprite tracks bind to a rig target's **stable id**, minted once and never derived from a name. Rename a part, reparent it, move it across the hierarchy — none of that touches what those tracks point at. That is what the ids are for.
>
> Three bindings *are* name-based and do break:
>
> | Binding | What breaking costs |
> |---|---|
> | `BoneTrack.boneName` | The track stays authored but bakes nothing |
> | A socket with `mode = Bone` | The attachment bakes at the origin |
> | A rig target's `displayName` | Tracks still play; the preview has no rest pose for that part |

The reconciliation panel lists each one with a dropdown of names that exist, a **Remap** button, and **Delete** for the two track-like kinds. Deleting is confirmed and tells you how many keys go with it. A rig target cannot be deleted here — it carries the id every track binds to, so renaming is the fix and deleting is a rig-asset decision. **Dismiss** hides the panel without changing anything; the next save reports the same findings again.

### Rig Edit mode

If you want to nudge the base setup without leaving the window, toggle **Rig Edit** in the toolbar. It is a mode, not a modifier, and it is impossible to be in it by accident: the toggle is tinted, the viewport gets an orange border, and a banner across the top says what a drag will do. **Auto Key** greys out, and keying is refused outright rather than merely discouraged.

In Rig Edit:

- **Gizmo drags write the prefab's base pose** on release. No keyframes are created.
- **Drag a hierarchy row onto another** to reparent it in the prefab. World position is preserved — you are changing what drives the part, not where it sits.

Both go through Unity's prefab APIs. With a prefab stage open for that asset, edits land in the stage: undoable, visible, saved when you save the stage. With no stage open, the asset is written immediately via `LoadPrefabContents`/`SaveAsPrefabAsset`, which **cannot be undone** — there is no open instance for the undo system to restore. Open the prefab first if you want an undo stack.

---

> **Preview limitation, stated plainly:** the preview renders untextured quads driven by transform tracks. It does **not** play VAT. A bone socket has no marker there, because the bone it follows exists only inside a texture and the preview has nothing to follow. For a rigged character, the preview shows you timing and events, not the mesh.

---

## Common failures and what they actually mean

| Symptom | Cause |
|---|---|
| Mesh renders as a motionless clump | Using the original mesh instead of the baked runtime mesh — no bone influences in `UV1` |
| Mesh renders as noise | Texture layout mismatch — `rowsPerFrame` or width disagrees between bake and shader |
| Every instance shows the same frame | A VAT property declared outside the DOTS instancing block, so it is a uniform rather than per-instance |
| Attachment sits at the actor's origin | Socket bone name matched nothing at bake — check the Console warning from the bake |
| Attachment is one frame behind | You are reading `LocalToWorld` somewhere instead of composing from the actor matrix |
| Visible stepping on a large rig | Half precision quantisation — enable Full Precision |
| Nothing animates at all | The prefab is not in a SubScene, or the clip set failed validation and baked no registry |
