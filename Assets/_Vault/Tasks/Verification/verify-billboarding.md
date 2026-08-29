---
title: Verify Hierarchical Billboarding (DOTS Animation Toolkit, amendment A44)
status: active
created: 2026-08-17
area: code
---

## Goal

Confirm A44's hierarchical billboarding works end to end in the Editor: a rig can declare billboard
roots, descendants inherit them, a nested root overrides its ancestor, and the Clip Editor shows all
of it live. **Everything automatable is already green** (431 EditMode + 212 PlayMode) — this file is
only the things a test cannot see.

**Where the code is:** `Packages/com.dotsanimationtoolkit/`, package version `0.10.0`.
Spec and build notes are **Amendment A44** at the end of
[`Docs/AnimationToolkit/Phase_B_Architecture.md`](../../../../Docs/AnimationToolkit/Phase_B_Architecture.md).
User-facing doc is `Documentation~/billboarding.md`.

**The host game is untouched.** `Assets/_Scripts/.../BillboardSystem.cs`, `Billboard` and
`BillboardAuthoring` all still run exactly as before. Nothing in Stitch Punk's look should have
changed. If it has, that is a bug and not the plan.

---

## ⚠ Check 1 — the facing sign (do this first, it gates everything else)

**A44 corrected the billboard facing sign, and it flips existing content.** The package shipped
`-cameraForward`; the host game uses `+cameraForward`. A44 adopted the host's, on the grounds that
Unity's `PrimitiveType.Quad` carries its visible normal on **−Z**, so a quad whose local +Z points at
the camera is presenting its back.

Every test in the suite proves the three code paths agree with **each other**. None can prove they
agree with a *mesh*, because the package cannot see one. Hence this.

### Run it

1. Open Unity.
2. **Tools ▸ DOTS Animation Toolkit ▸ Build Billboard Sign Check Scene**
3. Watch the **Game view**. The camera orbits on its own — no Play mode needed.

The camera moves and the quads billboard to it. If the camera sits still, that is a bug in
`ToolkitOrbitCamera`, not a billboard failure — tell me rather than reading the result.

Three quads, all wearing the same asymmetric glyph — a white **F**, a **green** bar down its left
edge, a **red** bar down its right:

| Quad | What it is |
|---|---|
| **A_Reference_NoBillboard** (left) | No billboarding at all. Ground truth. |
| **B_ShaderPath_Full** (middle) | Per-vertex billboard, `ToolkitBillboard.hlsl`. |
| **C_CpuPath_Full** (right) | `BillboardMath.TryResolve`, the same function the runtime job calls. |

### Read it

`ToolkitSpriteUnlit` declares no `Cull`, so it culls back faces. **A wrong sign makes a quad vanish,
not merely look odd** — you don't have to judge a subtle rotation, only notice whether something is
there.

- [ ] **PASS** — B and C stay visible from every angle, and the F reads the same way on both as it
      does on A in the first frame (green bar on the *left*).
- [ ] **FAIL, middle gone** → the shader sign is inverted. One line in `ToolkitBillboardFacing`.
- [ ] **FAIL, right gone** → the CPU sign is inverted. One line in `BillboardMath.TryResolveFacing`.
- [ ] **FAIL, F mirrored** (red bar on the left) → sign inverted *and* something is rendering
      double-sided.

The **left** quad turning away and going edge-on as the camera orbits is **correct** — that is what
"not billboarding" looks like, and it is there to prove the other two really are billboarding rather
than the camera not really moving.

### One question I'd like answered from memory

**Did you ever run `AnimationToolkitBillboardDemo.unity` and actually see the quads?**
(That scene and its builder were deleted on 2026-08-29; `BillboardDemoBuilder` regenerated it
from nothing, so recovering it means git, not a menu item.)

That scene was built during C5 with the **old** sign. If quads were visible there, the old sign was
rendering front faces and my reasoning above is wrong somewhere — which would be the single most
useful thing you could tell me. If you never ran it, or don't remember, that's fine and the sign
check above settles it either way.

---

## Setup — the gotcha that will waste your time otherwise

- [ ] **Something must write `AnimationToolkitCameraData` every frame, or nothing billboards at
      all.** The package never reads a `Camera`. `ToolkitCameraBinder` did it for the demo
      scenes and was deleted with them on 2026-08-29, so no writer exists now; the real game will
      need its own writer when the toolkit is eventually adopted. Symptom of forgetting: everything
      holds its animated pose and `BillboardResolveSystem` never runs.
- [ ] **Re-bake.** `ActorBaker` and `RigTargetBaker` both changed. Re-open the subscene or re-enter
      Play mode.
- [ ] Clip blob **schema is now 8** and the golden hash was re-recorded. Any subscene baked before
      today is stale and must re-bake — it will, automatically, but a stale one would silently hold
      old data.

---

## Editor checks (Clip Editor)

Open **Window ▸ DOTS Animation Toolkit ▸ Clip Editor** with a clip set loaded.

### Marking roots

- [ ] Right-click a hierarchy row → **Billboard ▸ Make Billboard Root**. The row gains a `◈` glyph
      and a blue tint.
- [ ] Rows *under* it gain a faint `·` and a dimmer tint.
- [ ] **Hover an inheriting row** — the tooltip should name the root it belongs to
      ("Billboards with «Torso»").
- [ ] Hover the root itself — tooltip names its mode.
- [ ] Right-click the root again → **Billboard ▸ Clear Billboard Root**. All markers disappear.
- [ ] Ctrl+Z undoes it. (The rig asset is `Undo.RecordObject`'d; the *prefab* is not touched at all.)
- [ ] Mark a **bare grouping transform** (something with no `RigTargetAuthoring`) — this is the case
      path addressing exists for. Then rename that object and confirm the bake reports the broken
      address rather than silently not billboarding.

### The override — the case the whole feature exists for

- [ ] Make the actor root a billboard root, and *also* make a node deep under it (a hand, or a pivot
      under a hand) its own root.
- [ ] In the viewport, orbit. **Both should face you, and the inner one must not spin at double
      rate.** Double rotation is the specific failure the depth-ordering machinery prevents; if you
      see it, the buffer order or the parent-cancellation is wrong.

### Viewport

- [ ] Billboarding shows live and follows the orbit.
- [ ] The **Billboard** toolbar toggle turns it off, and the rig then holds its authored pose so you
      can inspect it from any angle.
- [ ] Scrub the timeline with billboarding on — the pose animates *underneath* the billboard.

### Modes, snapping, clamping

On the rig asset, under **Billboarding**, per root:

- [ ] `Screen Aligned` (default) — every root takes the same rotation; two actors far apart on screen
      look identically rotated.
- [ ] `Full` — each root faces the camera *point*; two actors far apart visibly differ.
- [ ] `Upright` — never leans, however high the camera goes.
- [ ] `Axis Constrained` with a non-vertical axis — turns about that axis. (A zero axis is validation
      rule V23 and should be reported, not silently treated as upright.)
- [ ] **Snap**, 8 steps — the rig clicks between 8 facings instead of tracking smoothly. Try the
      phase offset; the whole wheel should rotate.
- [ ] **Clamp**, e.g. 90° arc — the rig turns only within that arc of its rest pose.
- [ ] **Snap + clamp together** — at the arc boundary the result may sit *off* a snap step. That is
      intended: the clamp is a constraint, the snap is a look.

### Rig asset inspector

- [ ] The **Billboarding** section lists roots and edits their fields.
- [ ] Adding a root here (rather than via the hierarchy) still gets a stable id — it should, via
      `OnValidate` → `EnsureStableIds`. Worth one check, because a root with id 0 is one no clip
      track can address.

---

## Runtime checks

- [ ] Enter Play mode with a billboarded actor. It faces the camera; moving the camera turns it.
- [ ] An actor whose rig declares **no** roots gets no `BillboardRootElement` buffer at all
      (check in the Entities Hierarchy window). Billboarding is opt-in and must cost nothing.
- [ ] The `ActorAuthoring` **Billboard Mode** checkbox still works — it bakes one root on the actor
      itself. If the rig *also* declares a root for the actor root, the rig wins and there is only
      one root, not two.
- [ ] Off-screen actors skip billboarding (`AnimVisible` gate) and resume correctly on re-entry.

---

## Already covered by tests — don't spend time here

- Nearest-ancestor resolution, override precedence, depth ordering (`BillboardRootResolverTests`).
- All six modes, snapping, clamping, blending, offset ordering, degenerate cameras
  (`BillboardMathTests`, 35 tests).
- Bake output: buffer contents, ordering, degrees→radians, arc halving, sentinel conversion,
  membership, the A41 checkbox, unresolved-address reporting (`BillboardBakingTests`).
- Clip track bake + sampling + V24/V03/V04 validation (`BillboardTrackTests`).
- Rig validation V21/V22/V23 (`BillboardRigAuthoringTests`).
- Preview ↔ runtime plumbing parity (`BillboardPreviewParityTests`).

---

## Known gaps (already recorded in A44, not bugs)

1. **Billboard tracks have no Clip Editor timeline row.** They bake, sample and resolve correctly and
   are authorable through the clip asset's own inspector — but the dopesheet does not draw them yet.
   This is the one line of A44 that is specified and not built.
2. **The shader path has no hierarchy and no arbitrary axis.** `_BillboardParams` has no channel wide
   enough, so `ToolkitBillboard.hlsl` treats `AxisConstrained` as `Upright`. Hierarchical
   billboarding is CPU-only by design (A41: rotating each quad about its own pivot fans a layered
   cutout character apart).
3. **The host game is not migrated.** Deliberate — that is a separate, verifiable cutover.

---

## Next work, in order

1. Whatever the sign check above turns up.
2. Billboard track timeline rows in the Clip Editor (gap 1).
3. **Billboard-space ragdoll physics** — the reason A44 was built first. The spec is
   [`Assets/_Vault/Tasks/Claude/AnimationRagdoll.md`](../Claude/AnimationRagdoll.md). It consumes
   `BillboardQuery.TryGetFrame` as its gravity reference and must not recompute facing.

---

## Handoff prompt for tomorrow

> Copy everything below into a fresh chat.

```
We're continuing work on the DOTS Animation Toolkit
(Packages/com.dotsanimationtoolkit, currently v0.10.0).

Yesterday we built Amendment A44: hierarchical, authorable billboarding. Any rig
node can be a billboard root, descendants inherit the nearest one, and a nested
root overrides its ancestor so a character can billboard as a whole while a held
item billboards independently. Phases D1-D6 all landed; 431 EditMode and 212
PlayMode tests are green.

Read these first, in this order:
1. Assets/_Vault/Tasks/Verification/verify-billboarding.md  (the test checklist
   I was working through, and the known gaps)
2. Amendment A44 at the end of Docs/AnimationToolkit/Phase_B_Architecture.md
   (the spec plus the D2 and D3-D6 build notes explaining what changed and why)
3. Packages/com.dotsanimationtoolkit/Documentation~/billboarding.md
   (the user-facing account)

Three things you need to know without having to derive them:

- The billboard facing sign was corrected to match the host game
  (+cameraForward, correct for Unity's PrimitiveType.Quad whose visible normal
  is on -Z). No test can settle whether that's right for real art -- it needs
  eyes. That's the first item on the verification checklist.
- All of an actor's billboard state is ONE buffer on the actor root, ordered by
  hierarchy depth, and that ordering is load-bearing: the resolve system reads
  rest orientations through live LocalTransform walks, so an ancestor root has
  already written its result when a nested one reads through it. That's the
  whole mechanism preventing double rotation.
- The host game is deliberately NOT migrated. Assets/_Scripts/.../BillboardSystem.cs
  still runs. Don't touch it unless I ask.

Known gap, already recorded: billboard tracks (angle offset / blend weight /
enable) bake, sample and resolve, but have no Clip Editor timeline row yet.

Here's what I found testing:
[paste your results, or say "haven't tested yet"]

What I want to work on today: [pick one]
  (a) fix whatever the testing turned up
  (b) billboard track timeline rows in the Clip Editor
  (c) start the billboard-space ragdoll -- spec is
      Assets/_Vault/Tasks/Claude/AnimationRagdoll.md, and it consumes
      BillboardQuery.TryGetFrame rather than recomputing facing

Project conventions still apply: no `var`, explicit types, no single-letter
names, .Schedule()/.ScheduleParallel() never .Run(). Compile gate is
mcp__UnityMCP__refresh_unity then read_console, and always check the DISCOVERED
test count, not just pass/fail.
```
