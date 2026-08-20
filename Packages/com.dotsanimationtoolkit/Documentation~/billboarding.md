# Billboarding

Billboarding turns part of a rig to face the viewer. It is a property of the **rig**, not of an actor
component, so it travels with the rig and every actor instanced from it billboards the same way.

The thing that makes it more than a checkbox: **any node can be a billboard root, and everything
beneath it inherits that root** — unless a node declares a root of its own. That is what lets a
character billboard as a whole while the sword in its hand billboards independently.

---

## The model

A **billboard root** is a node the rig marks. It turns to face the viewer, and so does everything
under it, because everything under it is a transform child and rides along for free.

```
Actor            ← billboard root: the whole character faces the camera
 └ Torso              inherits
    └ HandR           inherits
       └ ItemPivot ← billboard root: overrides, turns on its own
          └ Sword     inherits ItemPivot, not Actor
```

A node with no billboarded ancestor is not billboarded and transforms normally. Billboarding is
opt-in throughout: a rig that declares no roots bakes nothing and costs nothing at runtime.

### Marking a node

The easiest way is the **Clip Editor's hierarchy**: right-click a row → **Billboard ▸ Make Billboard
Root**. The address is filled in from the row you clicked. Right-click a root again to clear it.

The hierarchy then shows what is going on:

| Row | Means |
|---|---|
| `◈ Torso`, tinted | A declared billboard root. Hover for its mode. |
| `· HandR`, faint | Inherits a root. **Hover names which one.** |
| plain | Not billboarded. |

You can also edit the list directly on the rig asset, under **Billboarding**. That is the better
place to *tune* a root; the hierarchy is the better place to *create* one.

### How a root names its node

Two kinds of address, because a rig has two kinds of node:

- **Rig target** — addressed by the target's stable id. Survives renaming and reparenting the part.
- **Hierarchy path** — addressed by path below the actor root. For grouping nodes that are nobody's
  animatable part (an `ItemPivot` that exists only to hang a sword off). Like a bone name, this
  **breaks if you rename the object**, and the bake reports it rather than silently not billboarding.

An empty path addresses the actor root itself — the whole-actor billboard.

---

## Modes

| Mode | Turns |
|---|---|
| **Screen Aligned** *(default)* | To the camera's forward. Every root takes the same rotation. The classic 2.5D look. |
| **Full** | To the camera's *position*, on every axis. Each root faces the camera point. |
| **Upright** | About world Y only. Faces the camera without ever leaning. |
| **Axis Constrained** | About an axis you specify. A windmill sail about its hub, a book cover about its spine. |
| **Frozen Yaw** | Holds an authored yaw while pitch still follows the camera. The corpse case. |
| **Off** | Nothing. |

`Upright` is exactly `Axis Constrained` with the axis `(0, 1, 0)`; it has its own entry because it is
the common case and reads better in an inspector.

### Snapping and clamping

Both are optional, and both are measured **from the node's rest orientation** — not from the world.
So a character animated to turn on the spot carries its snap wheel and its arc with it, which is what
eight-direction sprite facing actually means.

- **Snap** quantises the turn to N even steps. 8 and 16 are the usual sprite counts. The phase offset
  rotates the whole wheel so its steps can straddle the cardinal directions rather than land on them.
- **Clamp** limits the turn to an arc centred on the rest orientation. An arc of 0 pins the node.

**The clamp outranks the snap.** At the arc boundary the result can sit off-step: the clamp is a
constraint and the snap is a look, and a constraint a look can override is not a constraint.

---

## Evaluation order — what an animated rotation means

This is the part worth reading twice.

1. The clip pose is composited.
2. The pose is written to each part's transform.
3. **Billboarding is applied on top.**

So the animated pose is the billboard's **rest orientation**, and at full blend weight the billboard
**replaces** it. Keying rotation on a fully billboarded node changes nothing you can see.

That is not a bug to work around — it is what billboarding means. If you want a billboarded node's
rotation to be animatable, that is what the two keyable channels are for.

Position, scale and every other channel are untouched. A billboard is an orientation and only an
orientation.

---

## Animating it

A clip can key three channels per billboard root:

| Channel | Does |
|---|---|
| **Angle offset** | Turns the node off the resolved facing, about the billboard's own up axis. *Adds* to the root's authored rest offset. |
| **Blend weight** | 1 = fully billboarded, 0 = hands the node back to its animation, between = slerp. |
| **Enabled** | Whether the root billboards at all. |

The angle offset and the blend weight interpolate. **The enable flag does not** — it holds from its
key, like a flipbook index, because it is an instruction that fires at a moment rather than a value
being approximated between two moments.

If several active layers key the same root, the **highest layer wins**, since layer index is
priority.

---

## In the viewport

The Clip Editor's preview shows billboarding live, and the camera orbits, so you can confirm the rig
faces correctly from every angle. It runs the same code the game does — literally the same function,
not a reimplementation — so what you see is what ships.

The **Billboard** toggle in the toolbar turns the preview off. You will want it: a billboarded rig
faces you from every angle, which makes the pose you actually authored impossible to inspect.

---

## Reading the billboard frame at runtime

Each root publishes its resolved world-space orientation. Anything that needs the billboard's own
sense of "up" or "down" should read it rather than recomputing facing:

```csharp
if (BillboardQuery.TryGetFrame(member, rootElementLookup, out quaternion frame))
{
    BillboardQuery.ToBillboardSpace(frame, worldGravity, out float3 billboardGravity);
}
```

`BillboardMember` is on every node under a root and names its root, so this is one hop — not a walk
up the hierarchy. That matters when the caller is asking once per body per physics step.

The frame is always meaningful: when a billboard refuses to resolve (mode off, root disabled,
degenerate camera) it holds the node's unmodified world orientation rather than a stale value, so you
never have to ask whether what you just read is real.

---

## Limits worth knowing

- **The CPU path and the shader path are alternatives, never both.** `ToolkitBillboard.hlsl` rotates
  each quad about its own pivot, which fans a layered cutout character apart; the rig path rotates
  nodes and keeps the composition rigid. Two rotations are no rotation.
- **The shader path has no hierarchy and no arbitrary axis.** `_BillboardParams` has no channel wide
  enough for one, so it treats `Axis Constrained` as `Upright`. Hierarchical billboarding is CPU-only.
- **A host must write the camera.** The package never reads a `Camera` — it cannot know which of your
  cameras matters. Write `AnimationToolkitCameraData` each frame. Leave `forward` at zero and
  screen-aligned modes fall back to spherical, which is a different look rather than a broken one.
- **Facing never comes from the view matrix.** During shadow rendering the view matrix belongs to the
  light, and a billboard derived from it casts the shadow of a shape the camera never sees.
