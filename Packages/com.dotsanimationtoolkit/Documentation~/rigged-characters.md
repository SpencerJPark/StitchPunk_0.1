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

- **The pose is frozen at bake time.** No IK, no procedural bone override, no runtime retargeting. What you baked is what plays.
- **Ragdoll is the sharpest case of that, and the rule has two halves worth separating.** You *can* put ragdoll bodies on a rigged character's bones, place their boxes, tune their limits, and watch the whole thing fall in the Clip Editor's preview — the preview poses a real GameObject skeleton, so all of that works exactly as it does for a cutout rig. What you cannot do is simulate it **at run time**: a VAT character's skeleton exists only as texels in a texture, so there is no bone entity for a solver to move, and the actor keeps playing its baked clip instead. This is structural rather than unfinished. If you need a rigged character to physically fall in game, drive the fall with transform-track parts, which are real entities. See [`ragdoll.md`](ragdoll.md).
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

Add `ActorAuthoring` to a prefab, assign the clip set, and add a child per target with `RigTargetAuthoring`. Assign the baked runtime mesh and a material using `ToolkitVatCrowdUnlit` (or your own shader — see [`shader-contract.md`](shader-contract.md)).

**Put the prefab in a SubScene.** Baking is what turns authoring assets into entities; a prefab in a plain scene will not animate.

### 7. Play it

Send an `AnimationCommand` naming the clip id. `ClipSetAsset`'s inspector has **Generate Clip Id Constants**, which writes a C# file of `public const ulong` values so game code references clips by name instead of by magic number.

---

---

## Authoring it

Everything about the window itself — selection, keying, the dopesheet, sockets
and the route into prefab mode — is in [`clip-editor.md`](clip-editor.md). Two
things there are worth calling out for a rigged character in particular:

- **The rig hierarchy is the bone picker.** Assign your rigged prefab to the
  toolbar's **Bone Source** field, select a bone in the tree, and the inspector
  offers **Add Bone Track** for exactly that bone. No typing, so the "name
  resolved to nothing and the bake froze it at rest" failure cannot happen.
- **Bone sockets need a bake; rig-target sockets do not.** A bone exists at run
  time only as texels in a texture, so a socket following one has its motion
  captured by the VAT bake. Until that has happened it resolves to the actor's
  origin, and the socket inspector says so.

---


> **Preview limitation, stated plainly:** the Clip Editor poses the real GameObject skeleton with your Bone Source prefab's actual `SkinnedMeshRenderer` attached, so the deformed mesh *is* what you see while scrubbing bone tracks — it is not left at rest. What the preview does **not** exercise is the run-time technique itself: it never samples a baked VAT texture through the actor's shader, so it cannot catch a bake-only defect (a stale bake, a texture-format mismatch, a rig that changed after the last bake). Confirm those by baking and looking at the actor in Play mode, not by trusting the preview alone.

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
