# Ragdolls

## What a ragdoll is in this toolkit

A ragdoll is a **box per bone, falling under its own physics**, authored on the same rig hierarchy
everything else in this package hangs off. You add a **Ragdoll** component to a node in the Clip
Editor, size its box in the viewport, and that node becomes a rigid body welded to its nearest
ragdolled ancestor.

There is no separate ragdoll rig, no second hierarchy to keep in sync, and no joint objects to place.
The hierarchy you already have *is* the articulation.

> **A ragdoll is rig-scoped, like a socket.** Adding one is a change to the rig asset, not to the clip
> you happen to be looking at, so every clip in the set sees it. The Clip Editor badges the component
> to say so, and records the undo on the rig.

---

## The default is 2D, and that is the interesting part

A flat cutout character that billboards to face the camera has a problem no ordinary ragdoll solves:
**which way is down?** World-space down is wrong the moment the camera orbits — the character would
fall sideways out of its own plane and edge-on into invisibility.

So the default mode, `Planar2D`, simulates **inside the character's own plane of existence**:

- **Translation** is constrained to the billboard plane. A body never moves toward or away from the
  viewer.
- **Rotation** is constrained to the plane's normal. The other two rotational axes are frozen.
- **Gravity** is world gravity expressed *in the billboard frame*, which means a billboarded
  character falls **down the screen** — and keeps falling down the screen as the camera orbits,
  because the frame rotates with it.

This is not an approximation of the billboard; it reads the exact frame `BillboardResolveSystem`
resolved this frame, through `BillboardQuery`. The ragdoll and the renderer cannot disagree about
which way the character is facing, because there is only one answer and both read it.

A rig that declares no billboard root falls back to the world XY plane, which is the right answer for
a flat character that never turns.

### `Spatial3D`

The mode exists for rigs that are not flat: the same solver with the plane constraint removed,
quaternion orientations, and swing/twist limits instead of a hinge range.

> **3D mode is not finished.** The solver and the data model support it and both limit pairs are
> stored, but the editor surface has not been completed and one question — which local axis "twist" is
> measured about — is still open. Treat `Spatial3D` as unfinished rather than as a supported mode.

The mode is **per rig**, not per body. Half an articulation constrained to a plane and half of it free
is not a configuration; it is a bug. The Clip Editor shows the field on every Ragdoll component,
badged rig-scope, so you can change it from wherever you happen to be looking.

---

## Authoring a body

### 1. Select a node and add the component

Any node the hierarchy shows will do — an authored guiding part, a bare grouping transform, or an
imported skinned-mesh bone. All three are legal, and the box is sized from the node's own geometry
where it has some.

A node may carry **one** ragdoll body. Two boxes on one node is rejected by validation, because the
node has one transform and two bodies would both claim it.

### 2. Place the box

Select the component and the viewport shows its wireframe with grab handles: six faces to resize, a
centre to move, and a ring to rotate. The handles are live whenever a Ragdoll component is selected —
placing a box is a rig edit, but it is not a *hierarchy* edit, so it does not need Rig Edit mode, the
same call socket placement makes.

The box is stored in the node's local space, so it travels with the animated pose rather than sitting
in a fixed place in the world.

### 3. Set mass and limits

| Field | What it does |
|---|---|
| `mass` | Authored. **Inertia is derived from it and the box** — a box has a closed form, and asking anyone to type an inertia tensor is asking for a wrong one. |
| `linearDamping`, `angularDamping` | **−1 means "inherit the rig default."** A negative sentinel rather than a companion checkbox, matching this package's existing conventions. |
| `restitution`, `friction` | Contact response for this body. |
| Hinge min/max | `Planar2D` only. The signed angle range this body may swing through, measured **from its rest pose relative to its parent** — so a rig authored with a bent elbow keeps that elbow as its zero. |
| Swing / twist | `Spatial3D` only. Both pairs are always stored, so switching modes to look and switching back never destroys tuning. |

### The joint is the hierarchy

A body's parent is its **nearest ragdolled ancestor** — not its immediate transform parent. You can
skip past nodes that carry no body, so a hand can hang off a shoulder with an unragdolled elbow
between them, and the chain still works.

The root of the articulation is simply the body with no ragdolled ancestor above it. Nothing declares
it; storing a flag would be a second statement that could disagree with the hierarchy.

---

## Self-collision

Each body carries a **group** (one of eight) and a **mask** of the groups it collides with.

**Parent and child never collide, whatever the masks say.** Two boxes sharing a joint overlap by
construction, and letting them push each other apart is a ragdoll that explodes on its first frame.
This is the single most common way a hand-built ragdoll fails, so it is handled in the solver rather
than left to whoever authors the masks.

**Both bodies must admit each other** before a pair collides. Two independent masks can disagree — A
admits B's group while B excludes A's — and the conservative reading is the safe one, because the
permissive reading makes a body collide with something it explicitly excluded, which looks exactly
like a mask that was never applied.

`collidesWithWorld` turns off world contact for a body that should pass through geometry, such as a
cape tip.

---

## Previewing a drop

The Clip Editor's **Ragdoll** toolbar toggle drops the previewed rig where you can see it.

| Action | What happens |
|---|---|
| Toggle on | The current pose is captured, bodies are built, and the simulation starts. **The playhead freezes** — a ragdoll has no timeline. |
| Toggle off | The captured pose is restored **exactly**, and the timeline comes back. |
| Scrubbing while on | Turns the toggle off first. |
| Rig has no bodies | The toggle refuses to engage and the status line says why. |

The rig falls onto a ground plane at y = 0, plus any test props you add in
**Project Settings ▸ Ragdoll Preview Scenery**. Props live there rather than on the rig asset, because
a shipped rig must not carry somebody's test box.

**The preview runs the same solver the game does** — literally the same functions, not a
re-implementation — and a parity test exists to keep it that way. A preview that drifts from the
runtime is worse than no preview.

> **One honest caveat.** The preview derives each body's rest-relative orientation from *whatever pose
> is on screen* when you toggle on, rather than from the rig's authored rest pose the way the bake
> does. Toggling on mid-animation can therefore show a first-frame correction into the joint limits
> that the runtime would not produce. Toggle on from a neutral pose if you are judging limits closely.

---

## Driving it from a game

The whole public control surface is one enableable component.

```csharp
// Start the drop.
entityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);

// Put it back exactly where it was.
entityManager.SetComponentEnabled<RagdollActor>(actorEntity, false);
```

Enabling captures the pose; disabling restores it. "Before" means before *this drop*, not the rig's
rest pose — a character knocked over mid-swing and revived comes back to that swing.

To throw it, write an optional `RagdollLaunch` before enabling:

```csharp
entityManager.SetComponentData(actorEntity, new RagdollLaunch
{
    worldImpulse = killDirection * killForce,
    worldPoint   = hitPositionWorld,
    worldTorque  = spinAxis * spinStrength,
});
entityManager.SetComponentEnabled<RagdollLaunch>(actorEntity, true);
entityManager.SetComponentEnabled<RagdollActor>(actorEntity, true);
```

The launch is consumed and disabled on the frame it is applied.

Global tuning lives on the `RagdollConfig` singleton — `worldGravity`, the sleep thresholds,
`maxSubstepsPerFrame`, and `fallbackGroundHeight`. It is created with defaults if you never author
one.

### Sleeping

Once every body is quiet for `sleepDelaySeconds`, the ragdoll sleeps and the dynamics stop. It keeps
writing its settled pose every frame regardless, because the animation system rewrites every part's
transform unconditionally and would otherwise overwrite it. A sleeping ragdoll costs a transform copy,
not a simulation.

---

## World collision

| You have | What the ragdoll hits |
|---|---|
| Unity Physics installed | Real geometry, box-cast against the `CollisionWorld` |
| Unity Physics absent | A single horizontal plane at `RagdollConfig.fallbackGroundHeight` |

**The package itself does not depend on Unity Physics.** The physics probe lives in a separate
assembly that Unity excludes entirely when the package is not present, so installing this toolkit
never drags a physics dependency into your project. The solver names no physics type at all — which is
also what lets it run in the editor preview, where there is no physics world.

The shipped probe casts **along gravity**. A body falling onto a floor or a ledge is caught; a wall it
is drifting sideways into is not.

---

## Limitations

1. **A VAT/skinned actor does not ragdoll at run time.** Its skeleton exists only as texels in a
   texture — there is no bone entity to move. It authors and previews fully, which is where you judge
   the boxes and limits, but at run time it keeps playing its baked clip. Cutout and transform-track
   parts ragdoll completely.
2. **Bone-addressed bodies never resolve at run time**, and this is structural rather than a bug to be
   fixed later: the bone GameObjects such an address names live only on the source armature the VAT
   bake samples. The bake reports this at info level, not as an error, precisely so it is not confused
   with a genuinely broken address.
3. **Self-collision is box-vs-box only.** No capsules, no hulls.
4. **Contact response is linear.** A box does not spin up from landing on its corner.
5. **A ragdoll has no timeline.** It cannot be keyed, scrubbed, or baked into a clip.
6. **3D mode is unfinished** (see above).

---

## Common failures and what they actually mean

| Symptom | Cause |
|---|---|
| The ragdoll poses for one frame, then snaps back to the animation | Something stopped re-writing the pose after the animation system's unconditional transform write. The ragdoll must win *every* frame, including while asleep. |
| Boxes fly apart violently on the first frame | Two overlapping boxes are colliding that should not be. Parent/child pairs are excluded automatically, so this means two bodies overlap that are *not* directly related — widen the exclusion with the group masks. |
| The body bounces forever, higher each time | Energy injection: a contact whose penetration is applied more than once per solve. If you have written your own contact provider, re-derive penetration against current positions rather than reusing a scalar measured before the solve. |
| A skinned character previews a drop but never ragdolls in game | Working as documented — limitation 1. |
| A body listed on the rig never simulates at all | Its address resolves to nothing. Check the Console at bake: a broken rig-target or path address logs an error; a bone address logs at info and is expected. |
| The character falls sideways out of its own plane | The rig declares no billboard root, so the ragdoll fell back to the world XY plane. |
| An attached weapon lags a frame behind the hand | Socket resolution is running before the ragdoll writes. |
| The whole rig sinks through the floor | No contact provider is filling contacts — Unity Physics absent *and* `fallbackGroundHeight` below the character. |
| Joint limits look wrong in the preview but fine in game | You toggled on mid-animation; the preview derives rest-relative orientation from the on-screen pose. Toggle on from a neutral pose. |
