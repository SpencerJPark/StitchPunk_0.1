# Phase D — Ragdoll (Amendment A50): technical spec

**Opened:** 2026-08-22.
**Spec basis:** `Phase_B_Architecture.md` §3.1 (RigAsset), §5.1–5.2 (groups, components), §5.6 (transform technique), §7.3 (preview strategy), Amendment A44 (billboarding).
**Package version:** 0.10.0 → **0.11.0** on completion.

**Product directive.** A ragdoll is a new rig-scoped component in the Clip Editor. Adding it to a
node gives that node a box collider you place and size in the viewport. It works on both kinds of
node the editor already shows — authored guiding parts and imported skinned-mesh bones. It defaults
to a **2D ragdoll**: one that lives inside the character's own plane of existence, so a billboarded
flat character falls *via its billboard* rather than in world space. A **3D** mode exists for rigs
that are not flat. The toolbar's Ragdoll toggle starts the simulation; turning it off restores the
character exactly as it was.

---

## 0. Owner decisions taken at spec time

Recorded here because each one closed a genuine fork, and the reasoning is what a future reader will
want, not the outcome alone.

| # | Fork | Decision | Why |
|---|---|---|---|
| D1 | VAT/skinned rigs have **no bone entities at runtime** — the skeleton is texels (`rigged-characters.md`). Does v1 ragdoll them? | **Runtime simulates entity-backed nodes only** (transform-track parts and authored guiding nodes). Skinned bones get full authoring and full *editor-preview* simulation; at runtime a VAT actor keeps playing its baked clip. | The preview owns a real GameObject skeleton (`PreviewSkeletonMirror`), so authoring and judging a drop cost nothing extra. A runtime skinned ragdoll needs a per-entity bone-matrix write path and a shader-contract change, which would double the phase and put the crowd-batching story at risk for a feature most buyers reach for on cutout characters first. Stated as a limitation, never as a silent gap. |
| D2 | Unity Physics is in this project but is **not** a package dependency, and `Conformance_A` pins the asmdef reference lists exactly. | **Own solver, optional Unity Physics.** The solver ships in `Runtime/` and names no physics assembly. World contacts arrive through a buffer that a *probe provider* fills; Unity Physics is one provider, in a separate optional assembly. | The package stays dependency-free for buyers, the solver runs in the editor preview (where an ECS `PhysicsWorld` does not exist), and the plane constraint is native to the solver rather than fought against a 3D rigid-body engine. |
| D3 | How much joint authoring in the component UI? | **Collider + limits; the joint is implied by hierarchy.** A body's parent is its nearest ragdolled ancestor. No joint objects to place. | A ragdoll on a 2D character is a chain of hinges. Explicit anchors, axes and springs would be knobs nobody touches, and every one of them is a way to author a broken articulation. |
| D4 | What does the preview rig fall onto? | **Ground plane at y = 0 plus drop-in test props**, self-collision always on. Props are editor-only scenery, stored outside the rig asset. | Reproducible. Mirroring the open scene would make the preview depend on whatever the user happened to have open, and a shipped rig asset must never carry a test box. |

---

## 1. What already exists (do not rebuild)

The billboarding work laid this groundwork on purpose. **Read these before writing anything.**

| Existing | What it already gives the ragdoll |
|---|---|
| `BillboardRootElement.resolvedRotation` | The world-space billboard frame, documented as existing "for anything that needs it as a reference (the ragdoll's gravity frame, most of all)". **Always meaningful** — a failed resolve writes the node's unmodified world orientation, never a stale one. |
| `BillboardMember` | Which root a node inherits, resolved *at bake*. This is what makes the gravity frame an O(1) lookup per body per step instead of a parent-chain walk. |
| `BillboardQuery.TryGetFrame` / `ToBillboardSpace` | Burst-compatible, lookup-taking. `ToBillboardSpace(frame, worldGravity, out planarGravity)` is literally the ragdoll's gravity call. **Do not recompute facing.** |
| `ragdoll-preview-toggle` in `ClipEditorWindow.uxml` | The toolbar toggle exists, is covered by `ClipEditorLayoutTests`, and carries a tooltip that says outright nothing reads it. Phase D wires it and rewrites the tooltip. |
| `BillboardRootDefinition` + `BillboardNodeAddress` | The exact template for a rig-scoped, hierarchy-addressed row list on `RigAsset`. Its own doc comment predicts these rows: "the ragdoll work will add its rows beside these." |
| `ClipComponentKind.Billboard` | The exact template for a rig-scoped component kind: add-menu entry, scope badge, component-stack body, removal confirmation, rig-asset section. |
| Host game's `Ragdoll2DSystem` (`Assets/_Scripts/Systems/RagdollSystemGroup/`) | **Prior art, not a dependency.** Read it for the lessons in §9; the package may not name it (`Conformance_D`). |

---

## 2. Address generalisation (breaking, data-safe)

`BillboardNodeAddress` names a node two ways — a rig target by stable id, or a prefab transform by
path. The ragdoll needs a third: **a skinned bone by name**, because that is the only handle the VAT
bake has on a bone (`SocketDefinition.boneName` already works this way) and because a bone's path
below the prefab root changes when an artist reparents inside the armature.

Rather than a second near-identical address struct — which is exactly the "two statements that could
disagree" this codebase forbids — the existing one is generalised:

```
BillboardAddressKind  → RigNodeAddressKind   { RigTarget = 0, HierarchyPath = 1, Bone = 2 }
BillboardNodeAddress  → RigNodeAddress       { kind, targetId, hierarchyPath, boneName }
```

`BillboardRootDefinition.address` keeps its field name and its type slot; only the type's *name*
changes, and `Bone` is simply an address kind billboarding rejects at validation (V-R8).

**This does not break serialized assets.** Unity writes a plain `[Serializable]` struct inline as its
fields — the type name reaches the YAML only for `UnityEngine.Object` subclasses and
`[SerializeReference]` fields, and this is neither. Existing rigs load unchanged. It *is* a public
API break for anything compiling against 0.10.0, so it goes in the CHANGELOG under **Breaking** and
the version moves to 0.11.0.

---

## 3. Authoring data model

### 3.1 `RigAsset` additions

```
public List<RagdollBodyDefinition> ragdollBodies = new List<RagdollBodyDefinition>();
public RagdollRigSettings ragdollSettings = RagdollRigSettings.Default;
```

Beside `billboardRoots`. A rig with an empty list bakes **nothing** — no components, no buffer, no
archetype change. That is the billboard precedent, and it is what keeps the feature free for rigs
that never ask for it.

`EnsureStableIds` must cover the new list. It is guarded independently of the others, for the reason
already written into that method: an early return would make whichever list came last depend on the
ones before it.

### 3.2 `RagdollRigSettings` — rig-wide, not per body

| Field | Meaning |
|---|---|
| `RagdollSpace space` | `Planar2D` (**default**) or `Spatial3D`. |
| `float gravityScale` | Multiplies the config's gravity for this rig. A paper cut-out and a stone golem fall differently. |
| `float defaultLinearDamping`, `defaultAngularDamping` | Seeds for bodies that do not override. |
| `float jointStiffness`, `jointDamping` | Limit-constraint softness, rig-wide. Per-body softness is a knob D3 deliberately did not add. |
| `byte solverIterations` | Position-solve iterations per substep. Default 6. |
| `float substepHz` | Fixed solver rate. Default 120. |

**Why `space` is rig-wide.** A ragdoll is one articulated body; half of it constrained to a plane and
half of it free is not a mode, it is a bug. The Clip Editor shows the field in every Ragdoll
component body, badged rig-scope exactly as billboard settings are, so it is editable from wherever
the user is looking without pretending to be per-node.

### 3.3 `RagdollBodyDefinition`

```
displayName : string          // cosmetic only; identity is Id, never this
stableId    : uint            // → RagdollBodyId; minted by EnsureStableIds
address     : RigNodeAddress  // which node this body is welded to
```

**Box collider** — local to the addressed node, so it travels with the animated pose:

| Field | Notes |
|---|---|
| `float3 boxCenter` | Local offset from the node's origin. |
| `float3 boxSize` | Full extents. All three components must be > 0 (V-R4). |
| `float3 boxEulerAngles` | Local rotation, degrees, ZXY — matching `TransformKey`, so a typed angle means the same thing everywhere in this toolkit. |

**Physical:**

| Field | Notes |
|---|---|
| `float mass` | Must be > 0 (V-R7). The inertia tensor is derived from `mass` and `boxSize` at bake — a box has a closed form, and asking a user for an inertia tensor is asking for a wrong one. |
| `float linearDamping`, `angularDamping` | **−1 means "inherit the rig default"**. A negative sentinel rather than a companion bool, following `snapSteps < 2` and `sliceIndex == -1`. |
| `float restitution`, `friction` | Contact response for this body. |

**Joint limits** — against the body's implied parent (D3). The pair that applies depends on
`RagdollRigSettings.space`; **both are stored**, so switching modes to look and switching back does
not destroy tuning:

| Field | Space | Notes |
|---|---|---|
| `float limitMinDegrees`, `limitMaxDegrees` | Planar2D | Signed hinge range about the plane normal, relative to the **rest** relative orientation. `min ≤ max`, both within [−180, 180] (V-R5). |
| `float swingLimitDegrees` | Spatial3D | Cone half-angle, [0, 180]. |
| `float twistLimitDegrees` | Spatial3D | Half-range about the bone axis, [0, 180]. |

**Self-collision:**

| Field | Notes |
|---|---|
| `byte selfGroup` | Which of 8 self-collision groups this body belongs to. |
| `byte selfCollidesWith` | Bitmask of groups it collides with. Default: all. |
| `bool collidesWithWorld` | Default true. A body a designer wants passing through geometry — a cape tip — turns it off. |

**Parent–child pairs are always ignored**, automatically, regardless of masks. Two boxes sharing a
joint overlap by construction; letting them collide is a ragdoll that explodes on its first frame,
and it is the single most common way a hand-built ragdoll fails.

**Both bodies must admit each other before a pair collides** (D2's tie-break, which this section did
not specify). Two independent bitmasks can disagree — A admits B's group while B excludes A's — and
the conservative reading is the safe one: a disagreement means no collision. The permissive reading
would make a body collide with something it explicitly excluded, which is the harder failure to
diagnose because it looks like a mask that was never applied.

### 3.4 What is deliberately *not* on the definition

- **No `isRoot` flag.** A body whose ancestor chain contains no other body *is* the root. Storing it
  would be a second statement that could disagree with the hierarchy.
- **No parent reference.** Same reason. The chain is the parent (D3).
- **No enabled flag.** The toggle is a runtime component on the actor, not authoring data.

---

## 4. Validation rules

New rows in §3.5's authoritative list, implemented in `ClipValidation` and surfaced by both the rig
inspector and the Clip Editor's `ValidationBadgeElement`.

> **`V-R*` is a Phase-D-local label, not a real code.** These become ordinary sequential
> `ValidationCode.Vxx` entries in the existing enum in `Authoring/Validation/ValidationMessage.cs`.
> D0 already landed **V-R8 as `ValidationCode.V25`** (the next code after the pre-existing V24), so
> **D1 assigns V-R1…V-R7 to `V26`…`V32`** in that order. Do not invent a separate ragdoll numbering
> scheme — one enum, one sequence, and the rule bodies live in `ClipValidation` beside their
> neighbours.

| Rule | Check | Severity |
|---|---|---|
| **V-R1** | Every `ragdollBodies[i].address` resolves against the source prefab. | Error |
| **V-R2** | Body `stableId`s are unique within the rig, and none is 0. | Error |
| **V-R3** | No two bodies address the same node. | Error |
| **V-R4** | Every `boxSize` component > 0. | Error |
| **V-R5** | `limitMinDegrees ≤ limitMaxDegrees`, both in [−180, 180]; swing/twist in [0, 180]. | Error |
| **V-R6** | The body graph is a single tree — exactly one body has no ragdolled ancestor. | Warning |
| **V-R7** | `mass > 0`. | Error |
| **V-R8** | A `RigNodeAddressKind.Bone` address on a `billboardRoots` row. | Error |

V-R6 is a warning rather than an error on purpose: two disconnected articulations on one rig is odd
but simulable, and a rig mid-authoring passes through that state on the way to a finished one.

### 4.1 What authoring-time validation genuinely cannot check (D1 finding)

**V26 (address resolves) and V31 (single tree) are only partially checkable from the asset alone**,
because the rig asset carries no hierarchy — the hierarchy lives on the authoring prefab. This is the
same structural gap `V21` already documents for billboard roots, and D1 mirrored that precedent
rather than inventing a second answer:

- **V26** resolves `RigTarget` addresses only. `HierarchyPath` and `Bone` are deferred to the bake.
- **V31** uses string-prefix ancestry over `HierarchyPath`-kind bodies only. It deliberately does
  **not** consult `RigTargetDefinition.sourceNodePath`, which is documented as an unvalidated editor
  convenience rather than a source of truth.

**Consequence, and it is a real one:** a rig whose bodies are entirely `RigTarget`- or `Bone`-addressed
— very plausible for a skinned-bone-heavy rig — never trips V31 at authoring time at all.

**Decision: D3's baker must emit its own disconnected-ragdoll diagnostic.** It is the first thing in
the pipeline that holds the resolved hierarchy, and it is already walking that hierarchy to compute
`parentBodyIndex`, so counting how many bodies come back with −1 is free. More than one is the
disconnected case. It reports through `ActorBakeFailed` as a warning, never a silent drop — matching
how an unresolvable billboard address is already handled. Authoring-time V31 stays as the early,
partial signal; the bake is the authoritative one.

---

## 5. Runtime components

New file `Runtime/Components/RagdollComponents.cs`. Everything lives on the **actor root**, for the
same reason the billboard buffer does: a baker may write only on the entity it bakes, and a ragdoll
body can be a bare grouping transform with no authoring component of its own.

### 5.1 `RagdollActor : IComponentData, IEnableableComponent`

**The toggle.** Baked *disabled*. Enabled = the ragdoll drives the pose; disabled = the animation
does. This is the whole public control surface — a game enables it on death and disables it on
revive, and the Clip Editor's toolbar toggle does the same thing to the preview.

### 5.2 `RagdollBody : IBufferElementData` (actor root)

One element per authored body. **Ordered shallowest-first**, and that ordering is load-bearing for
exactly the reason `BillboardRootElement`'s is: a body's parent has already been integrated by the
time the child reads through it. Reorder this buffer and chains solve backwards.

| Field | Kind | Notes |
|---|---|---|
| `bodyId` | baked | The authored `RagdollBodyId`. Addressed by id, never by buffer position. |
| `node` | baked | The entity whose transform this body writes. Patched by `Instantiate` via `LinkedEntityGroup`, like `RigPartBinding.actorRoot`. |
| `parentBodyIndex` | baked | Index into this buffer; **−1 for the root**. Resolved at bake by walking the prefab hierarchy. |
| `parentAnchorOffset` | baked | **Added by D2 — this table originally omitted it, and the joint constraint cannot be written without it.** The fixed offset from the *parent's* centre of mass to the shared joint, in the parent's rest-local axes. The child side needs no counterpart: a joint authored by hierarchy places the child node *at* the joint, so the child's own origin is its anchor. D3's baker produces this from the same rest-hierarchy walk that already yields `restRelativeRotation`, so it is free to compute — but it is a real field, not a derivation. |
| `boxCenter`, `boxHalfExtents`, `boxRotation` | baked | Collider in node-local space. Half-extents, not full size — halving once at bake beats halving per body per substep. |
| `invMass`, `invInertia` | baked | Inverted at bake. A solver divides by mass on every constraint; a static body is `invMass == 0` and needs no branch. |
| `linearDamping`, `angularDamping`, `restitution`, `friction` | baked | Rig defaults already folded in — the −1 sentinel is resolved at bake, never at runtime. |
| `limitMin`, `limitMax`, `swingLimit`, `twistLimit` | baked | **Radians.** Degrees are what an author types; radians are what trigonometry consumes (the `BillboardSettings` precedent). |
| `restRelativeRotation` | baked | The child's orientation relative to its parent at rest. Limits are measured from this, so a rig authored with a bent elbow keeps that elbow as its zero. |
| `selfGroup`, `selfCollidesWith`, `flags` | baked | `flags` carries `CollidesWithWorld` and `IsRoot`. |
| `position`, `orientation` | state | World-space, integrated by the solver. |
| `linearVelocity`, `angularVelocity` | state | World-space. |

### 5.3 `RagdollRestPose : IBufferElementData` (actor root)

Parallel to `RagdollBody`, one element per body: the node's `LocalTransform` and
`PostTransformMatrix` **captured on the frame the ragdoll was switched on**. This is the entirety of
"turning it off resets the character to before" — restore these and the actor is byte-identical to
where it stood.

Captured rather than baked, because "before" means *before this drop*, not *at rest*. A character
knocked over mid-swing and revived returns to the swing.

### 5.4 `RagdollState : IComponentData` (actor root)

| Field | Notes |
|---|---|
| `frameRotation` | The billboard frame, captured at switch-on and **re-read every step**, so an orbiting camera carries the plane with it. |
| `planeNormal` | The frame's local +Z in world space. Cached; Planar2D projects against it constantly. |
| `substepAccumulator` | Fixed-timestep remainder. |
| `sleepTimer` | Seconds spent below the sleep thresholds. |
| `flags` | `Sleeping`, `CaptureNeeded`, `RestoreNeeded`. |

### 5.5 `RagdollLaunch : IComponentData, IEnableableComponent` (optional, actor root)

An impulse the host writes before enabling the ragdoll: `float3 worldImpulse`, `float3 worldPoint`,
`float3 worldTorque`. Consumed and disabled by the capture system.

Optional, and therefore read through a `ComponentLookup` with a `HasComponent` check — **never as a
job parameter**, which would enrol the job in an `All` query for an opt-in component and silently
exclude every actor that never asked for a launch. That is the `PartFacing` trap, already documented
in this package, already paid for once.

### 5.6 `RagdollWorldContact : IBufferElementData` (actor root)

The seam that keeps Unity Physics optional. A **provider** fills this buffer each frame; the solver
reads it and names no physics assembly.

```
int bodyIndex; float3 point; float3 normal; float distance; float restitution; float friction;
```

**`bodyIndex` was missing from this list and D2 had to add it.** Without it a multi-body actor's
solver cannot attribute a contact to a body — the buffer is per *actor*, not per body, so a contact
that does not say which body it struck is unusable. Every provider in §7.5 must populate it, and
D3's buffer element must carry it or the solver literally cannot consume the buffer.

### 5.7 `RagdollConfig : IComponentData` (singleton)

Global tuning, created with defaults by `ConfigBootstrapSystem` if absent. Flat floats, nothing
enum-indexed, so a plain component rather than a blob: `worldGravity` (default `(0, −9.81, 0)`),
`sleepLinearSpeed`, `sleepAngularSpeed`, `sleepDelaySeconds`, `maxSubstepsPerFrame`,
`fallbackGroundHeight`, `contactProbeRadius`.

---

## 6. The solver

`Runtime/Sampling/RagdollSolver.cs` — **pure static functions, Burst-compiled, no ECS types in the
math**, exactly as `ClipSampler`, `BillboardMath` and `EventWrapMath` are. This is what lets the
editor preview run the identical code against GameObject transforms, and what lets a parity test
prove it did.

### 6.0 Where the structs live — the D2/D3 seam

**The solver owns its own plain structs; the ECS buffer element wraps them.** This mirrors the
package's existing precedent exactly: `BillboardMath` takes a plain `BillboardSettings`, and
`BillboardRootElement : IBufferElementData` *contains* one rather than redeclaring its fields.

| File | Phase | Contents |
|---|---|---|
| `Runtime/Sampling/RagdollSolverTypes.cs` | **D2** | `RagdollBodyParams` (the baked, constant half of §5.2: box, inverse mass and inertia, damping, limits, `restRelativeRotation`, masks, flags) and `RagdollBodyState` (the integrated half: position, orientation, linear and angular velocity). Plain structs, no `IComponentData`, no `Entity`. |
| `Runtime/Components/RagdollComponents.cs` | **D3** | `RagdollBody : IBufferElementData` = `bodyId` + `node` (Entity) + `parentBodyIndex` + a `RagdollBodyParams` + a `RagdollBodyState`. Plus every other component in §5. |

Two consequences worth stating, because both are load-bearing:

- **D2 no longer depends on D3, and does not touch `RagdollComponents.cs`.** The solver is written
  and fully tested against plain structs before any ECS type exists. That is what makes it testable
  in EditMode with no World, which §10 requires.
- **The editor preview needs no parallel type.** `RagdollPreviewSimulation` builds
  `NativeArray<RagdollBodyParams>` + `NativeArray<RagdollBodyState>` straight from the rig asset and
  hands them to the same entry points the runtime job uses. A preview that redeclared the solver's
  inputs would drift the first time either side gained a field — the precise failure
  `SocketPreviewParityTests` exists to prevent.

The §5.2 table stays the authoritative field list; it is simply split across the two structs at the
`baked` / `state` boundary already marked in its **Kind** column.

> **Burst entry-point discipline.** A `[BurstCompile]` static is an external entry point: struct and
> vector parameters must be `in`/`ref`/`out`, never by value, and nothing may be *returned* by value
> (BC1064/BC1067). `BillboardQuery.ToBillboardSpace` takes an `out float3` for exactly this reason.
> Every public solver entry follows the same shape.

### 6.1 Method: extended position-based dynamics (XPBD)

Chosen over an impulse solver because it is unconditionally stable at large timesteps, converges
predictably with a fixed iteration count, and expresses a plane constraint as one more projection
rather than as a special case. Per substep:

1. **Predict.** Integrate gravity and damping into velocities; predict positions and orientations.
2. **Project constraints**, `solverIterations` times, **in this order**:
   - *Joint* — pin the child body's origin to the parent's anchor (`parentAnchorOffset`).
   - *Limit* — clamp the child's relative orientation into its authored range, measured from
     `restRelativeRotation`.
   - *Self-contact* — box-vs-box SAT, skipping parent↔child pairs and mask-excluded pairs.
   - *World contact* — resolve each `RagdollWorldContact` as a non-penetration constraint.
   - *Plane* (Planar2D only) — project position onto the frame plane and orientation onto the frame
     normal. **Last, not third.**
3. **Derive velocities** from the position delta; apply restitution and friction at contacts.

> **Why Plane moved to last.** It was specified third and D2 found that wrong. SAT-driven self and
> world contacts each introduce a small out-of-plane component, so a plane projection that runs
> *before* them leaves that component in the result and the planar invariant holds only on average.
> Running it after every contact makes the invariant exact on every single step, which is what
> `RagdollPlanarConstraintTests` asserts. The constraint *categories* are unchanged — only the
> intra-iteration order.

**Contact response is linear-only.** A contact applies no torque from its point offset, so a box
landing on a corner does not spin up from that contact alone. A deliberate scope cut, not an
oversight: §10 asks for SAT detection and correct exclusion, not a manifold-accurate angular
response, and no later phase depends on contact-induced spin. Revisit only if drops read as too
lifeless once D6 makes them visible.

**Restitution and friction are applied once**, after velocities are derived from the position solve,
against a small touch-slop re-detection rather than the constraint-time contact. Applying them
per-iteration would be overwritten by the position-delta velocity derivation that follows.

> **Every contact constraint must re-derive its penetration each iteration. D4 found this the hard
> way and it is the most dangerous mistake in this section.** A `RagdollWorldContact` arrives from a
> probe carrying a `distance` measured *once*, before the solve. Re-applying that same immutable
> scalar on each of the `solverIterations` passes pushes the body out by up to six times the intended
> amount per substep — twelve times per frame at two substeps — which *injects energy* rather than
> removing it. The observed symptom was a body that never settled: linear velocity climbing from
> −0.16 to +89 units/s over roughly 200 frames, bouncing higher each time, guaranteed never to
> converge. Self-contact was already correct because SAT re-runs against current positions; world
> contact must do the equivalent, recomputing live penetration as
> `contact.distance + dot(state.position − referencePosition, contact.normal)` against the position
> captured at the start of the step. **No D2 fixture caught this** — determinism, limits and the
> plane invariant all hold perfectly while energy is being pumped in. It took a runtime settle test
> to expose, which is the argument for D4's fixtures existing at all.
4. **Sleep check.** Below both speed thresholds for `sleepDelaySeconds`, set `Sleeping`.

### 6.2 Planar2D — what "the plane of existence" means, precisely

The frame comes from `BillboardQuery.TryGetFrame` on the root body's node. Its axes are the
billboard basis: local **+Y** is up within the plane, **+Z** points away from the viewer, **+X**
completes the pair.

- **Translation** is constrained to the frame's XY plane through the actor origin. A body never moves
  toward or away from the viewer. "The actor origin" named no field that carried it into the solver,
  so D2 added `RagdollSolverSettings.planeOrigin`; **D3 and D4 must supply the actor root's world
  position** or every body is projected onto a plane through the world origin instead.
- **Rotation** is constrained to the frame's Z axis. The other two rotational degrees of freedom are
  frozen. This is what "ignores the localized z direction for rotation" means concretely: z is the
  axis a body turns *about* and the axis it may not move *along*.
- **Gravity** is `ToBillboardSpace(frame, config.worldGravity, out g)` with `g.z` discarded, then
  scaled by `gravityScale`. A billboarded character therefore falls *down the screen*, and keeps
  falling down the screen as the camera orbits, because the frame rotates with it.
- **Contacts** are projected into the plane before they are solved. A world contact whose normal is
  nearly parallel to the plane normal contributes nothing and is skipped.

If `TryGetFrame` fails — the rig declares no billboard root, or the root was removed since bake — the
frame falls back to world identity and the ragdoll simulates in the world XY plane. Documented, not
silent: the fallback is reported once per actor.

### 6.3 Spatial3D

The same solver with the plane projection skipped, quaternion orientations throughout, and
swing/twist limits in place of the hinge range. Gravity is world gravity directly; no billboard frame
is read, so a 3D ragdoll on a rig with no billboard roots costs nothing extra.

### 6.4 Determinism

Fixed substep rate, fixed iteration count, no reliance on frame delta, and a stable pair-iteration
order (buffer index ascending, `i < j`). `RagdollSolverDeterminismTests` runs the same launch twice
and asserts bit-identical state, mirroring `ClipRegistryDeterminismTests`.

---

## 7. Systems

New group `AnimationToolkitRagdollSystemGroup`, inside the Presentation group.

```
AnimationToolkitPresentationSystemGroup
  AnimLodDistanceSystem            (OrderFirst)
  TransformSampleSystem
  TransformApplySystem
  BillboardResolveSystem           [UpdateAfter TransformApplySystem]
  ▸ AnimationToolkitRagdollSystemGroup      ← new
        [UpdateAfter  BillboardResolveSystem]
        [UpdateBefore SocketResolveSystem]
      RagdollCaptureSystem   (OrderFirst)
      RagdollProbeFallbackSystem
      RagdollSolveSystem
      RagdollApplySystem
      RagdollReleaseSystem   (OrderLast)
  SocketResolveSystem
  SpriteMaterialSystem / VatMaterialSystem / RenderBoundsUpdateSystem
```

**Two ordering edges, both load-bearing:**

- **After `BillboardResolveSystem`** — the gravity frame must be this frame's, not last frame's.
- **Before `SocketResolveSystem`** — a socket resolving before the ragdoll writes puts the sword in
  the hand one frame late, which `rigged-characters.md` already lists as a known failure symptom.
  `SocketResolveSystem`'s own `[UpdateAfter(TransformApplySystem)]` does not order it against a group
  that did not exist when it was written, so the edge goes on the ragdoll group.

### 7.1 `RagdollCaptureSystem`

Runs where `RagdollActor` is **enabled** and `RagdollState.CaptureNeeded` is set. Fills
`RagdollRestPose` from the live transforms, seeds every `RagdollBody`'s world position and
orientation from the current hierarchy, resolves and caches the billboard frame, zeroes velocities,
applies `RagdollLaunch` if present, and clears the flag.

Structural-free: both buffers are baked at their full length, so capture is a write, never an add.

### 7.2 `RagdollSolveSystem`

One `IJobEntity` over actor roots. **Each root exclusively owns its bodies' node entities**, so
transform writes go through `[NativeDisableParallelForRestriction]` lookups — disjoint per `Execute`,
which is precisely the pattern the host game's `RagdollDriveJob` uses and the only way this
parallelises. `.ScheduleParallel(state.Dependency)`; never `.Run()`.

Sleeping actors skip the dynamics entirely but **still fall through to `RagdollApplySystem`** — see
§9 G1.

### 7.3 `RagdollApplySystem`

Writes each body's solved world orientation and position back onto its node as `LocalTransform` (and
`PostTransformMatrix` where scale is involved), converting through the parent chain. Runs every frame
for every enabled ragdoll, awake or asleep.

### 7.4 `RagdollReleaseSystem`

Runs where `RagdollActor` is **disabled** and `RestoreNeeded` is set: copies `RagdollRestPose` back
onto the nodes, sets `CaptureNeeded` for the next switch-on, clears the flag. One frame, then it
matches nothing. `TransformSampleSystem` resumes owning the pose the very next frame with no
handshake, because it never stopped writing (§9 G1).

### 7.5 Probe providers

| Provider | Assembly | Fills `RagdollWorldContact` from |
|---|---|---|
| `RagdollProbeFallbackSystem` | `Runtime` | `RagdollConfig.fallbackGroundHeight` — one plane. Always present. |
| `RagdollPhysicsProbeSystem` | `Runtime.Physics` (optional) | `CollisionWorld` box-casts per body. Disables the fallback when present. |
| `RagdollPreviewProbe` | `Editor` | The preview's ground plane and drop-in props (D4). |

**The optional assembly.** `DotsAnimationToolkit.Runtime.Physics.asmdef`, referencing `Unity.Physics`
and `DotsAnimationToolkit.Runtime`, carrying both a `versionDefine`
(`com.unity.physics` ≥ `1.0.0` → `DOTS_ANIM_TOOLKIT_PHYSICS`) and
`"defineConstraints": ["DOTS_ANIM_TOOLKIT_PHYSICS"]`. Without the package the constraint fails, the
assembly is excluded from compilation, and its unresolvable reference is never evaluated.

> **Build-step verification item.** That an excluded assembly's unresolved reference produces no
> console error must be confirmed in the Editor at the *start* of phase D7 — before any code depends
> on it — with a clean-project import check. If it does error, the fallback shape is a source-level
> `#if DOTS_ANIM_TOOLKIT_PHYSICS` file inside `Runtime`, with the versionDefine on `Runtime`'s own
> asmdef and `Unity.Physics` added to its references. That costs the hard dependency, and must then
> go back to the owner as a re-decision on D2 rather than being taken quietly.

---

## 8. Editor

### 8.1 `ClipComponentKind.Ragdoll = 5`

Rig-scoped (`ClipComponentScope.Rig`), like `Socket` and `Billboard`. Registered in
`ClipComponentModel`'s `AllKinds` and `AddableKinds`, with:

- `AllowsMultiple` **false** — V-R3 forbids two bodies on one node.
- `RequiresRigTarget` **false** — a ragdoll body is legal on a bare grouping transform and on a
  skinned bone, which is the whole of "works for both bone kinds".

`Add` mints a `RagdollBodyDefinition` with a box sized from the node's renderer bounds where it has
one and a unit box where it does not, undo recorded on the **rig asset**. `Remove` confirms first,
matching `ConfirmRemoveBillboard` — a rig-scoped delete is seen by every clip in the set.

### 8.2 Component-stack body (`ClipEditorWindow.ComponentStack.cs`)

`AddRagdollFields` renders, in order: the rig-wide `space` dropdown (badged rig-scope), the box
fields, mass and damping, the limit pair for the active space, and the self-collision group/mask.
Every field writes through one `ApplyRagdollEdit` that records a single undo — the `ApplyBillboardEdit`
shape.

**UI Toolkit only.** `Conformance_E` bans IMGUI APIs in editor sources; there is no exception for a
gizmo panel.

### 8.3 Box handles in the viewport

New `Editor/ClipEditor/Preview/PreviewRagdollBoxHandles.cs`, drawn by `PreviewSceneGizmos` and routed
through the existing `GizmoDragRouting`:

- Wireframe box for every ragdoll body, the selected one highlighted.
- Six face handles resize — symmetric with a modifier held, one-sided without.
- A centre handle moves; a ring rotates about the plane normal in Planar2D, three rings in Spatial3D.
- Handles are live **whenever a Ragdoll component is selected**, not only in Rig Edit mode. Placing a
  box is a rig edit but not a *hierarchy* edit, which is the same call socket placement already makes.
- Handle sizing follows `ClipPreviewController.GizmoHandleLength`, so boxes scale with the camera like
  every other gizmo.

### 8.4 The Ragdoll toggle

Wired at last, in `BindToolbar` beside its billboard sibling.

| Transition | Behaviour |
|---|---|
| **Off → On** | Capture the current preview pose, build the preview body array from the rig's `ragdollBodies` resolved against the preview hierarchy, start the fixed-step simulation, **freeze the playhead**. |
| **On → Off** | Restore the captured pose, discard the sim, unfreeze. |
| **Scrubbing while on** | Turns the toggle off first. A ragdoll has no timeline; pretending it does would be a lie the transport cannot keep. |
| **No bodies on the rig** | The toggle refuses to engage, and the status line says why. |

New tooltip: *"Drop the previewed rig as an active ragdoll — its own physics, ground contact and
self-collision — to see whether a pose still reads on impact. Turning it off restores the pose
exactly."*

### 8.5 Preview simulation

`Editor/ClipEditor/Preview/RagdollPreviewSimulation.cs` builds a `NativeArray<RagdollBody>` from the
rig and drives it with **the same `RagdollSolver` functions the runtime calls**, then writes results
onto `PreviewRigMirror` quads and `PreviewSkeletonMirror` bones. Ticked from the window's existing
repaint loop through a fixed-timestep accumulator — editor delta time is jittery, and a variable step
would make the preview a different simulation from the game's.

**This is why D1's limitation is acceptable:** a skinned rig that cannot ragdoll at runtime still
ragdolls here, on real Transforms, so its boxes and limits can be authored and judged.

### 8.6 Preview scenery (D4)

A ground plane at y = 0, plus a list of test props (box / ramp, with position, size, rotation) held in
a `ScriptableSingleton` under `ProjectSettings/`. **Never on the rig asset** — a shipped rig must not
carry a test box, for the same reason `SocketDefinition.previewAttachment` sits inside
`#if UNITY_EDITOR`.

### 8.7 Rig asset inspector

A **Ragdoll** section in `RigAssetEditor`, beside Billboarding: the rig-wide settings, the body list
with its validation badges, and a "Fix addresses" affordance for rows whose node moved — the
`BindingReconciler` route sockets already use.

---

## 9. Gotchas the build must respect

| # | Trap | Why it bites |
|---|---|---|
| **G1** | `TransformApplySystem` stomps **every** visible part's `LocalTransform` unconditionally, every frame. | The ragdoll must re-write the pose *after* it, every frame, **including while asleep**. The host game's `Ragdoll2DSystem` carries this exact comment; it is the most likely source of "the ragdoll works for one frame and then snaps back". The tempting fix — `[WithDisabled(typeof(RagdollActor))]` on the sampler — is **wrong**: `RagdollActor` is opt-in, and a `WithDisabled` on an absent component excludes every actor that never asked for a ragdoll. That is the `PartFacing` trap, already documented, already paid for once. |
| **G2** | An `EnabledRefRW<T>` job parameter enrols `T` as an **All** (enabled-filtered) query component, silently matching nothing. | `RagdollCaptureSystem` and `RagdollReleaseSystem` both take enable-state on `RagdollActor`. Use `[WithPresent(...)]`, as `CommandApplySystem` does for `BoundsDirty`. |
| **G3** | Naming convention: an `EnabledRefRW`/`RO` parameter is named *component name* + `Enabled`. | `ragdollActorEnabled`, not `enabled`. |
| **G4** | `Conformance_A` asserts asmdef reference lists match architecture §1.3 **exactly**. | Adding `Runtime.Physics` means updating §1.3 and the test in the same commit, or the suite goes red on a correct change. |
| **G5** | `Supplementary_PackageManifest_MatchesSection11Identity` asserts the exact version string. | The 0.11.0 bump updates `package.json` *and* that assertion together. |
| **G6** | `DataContractTests.ActorRootComponents_MatchTheSection52Inventory` enumerates the actor archetype. | Every new actor-root component is a row there. |
| **G7** | A blob-struct layout change makes the *first* EditMode run fail spuriously in untouched fixtures. | Recompile and re-run before debugging anything. |
| **G8** | Burst log strings accept only `G/g/D/d/X/x` specifiers, reject `+` concatenation, and print enums as type names. | Applies to every diagnostic the solver emits. |
| **G9** | `Samples~` is excluded from Unity compilation and rots silently. | If a sample gains a ragdoll, compile-check it through a temp assembly. |
| **G10** | Absolute rules: never `var`, never single-letter names, never `.Run()` a job. | The solver's inner loops are exactly where a `float3 p` feels justified. It is not. |
| **G11** | A **mutable** `NativeArray<T>` crossing a `[BurstCompile]` static entry point must be `ref NativeArray<T>`. | Found by D2. `in` and a bare value parameter both fail (BC1063/BC1064) even though `NativeArray` is only a pointer handle. `ClipSampler`'s existing `in NativeArray<PlaybackLayer>` works solely because it is read-only, so copying that pattern for a write target fails the whole Runtime assembly. |
| **G12** | `invMass == 0` means **fully static** — position *and* orientation frozen. | D2's reading, since §5.2 has no separate "pin linear only" concept. A baker that wants a pinned-but-swinging body has no way to express it; that would need a new flag, not a zero mass. |

---

## 10. Test obligations

**EditMode** (no World):

| Fixture | Asserts |
|---|---|
| `RagdollSolverTests` | Joint pinning, limit clamping at both ends, damping decay, box-vs-box SAT, parent↔child pair skipping, mask exclusion. |
| `RagdollPlanarConstraintTests` | After any number of steps, every body's position lies in the frame plane and every orientation is a pure rotation about the frame normal — for a **rotated** frame, not just an axis-aligned one. |
| `RagdollSolverDeterminismTests` | Identical launches produce bit-identical state. |
| `RagdollValidationTests` | V-R1…V-R8, each rule failing for its own reason and no other. |
| `RagdollAddressTests` | All three `RigNodeAddressKind`s resolve; the `Bone` kind is rejected on a billboard root. |
| `RagdollPreviewParityTests` | The preview and the runtime produce the same state from the same input — the `SocketPreviewParityTests` / `BillboardPreviewParityTests` obligation, non-optional. |
| `ClipComponentModelTests` (extend) | The Ragdoll kind's scope, addability, multiplicity, add and remove. |
| `ClipEditorLayoutTests` (extend) | The toggle is bound and has a callback. |

**PlayMode** (World):

| Fixture | Asserts |
|---|---|
| `RagdollBakingTests` | Bodies bake shallowest-first; `parentBodyIndex` resolves; the root is −1; a rig with no bodies bakes **no** ragdoll components at all. |
| `RagdollToggleTests` | Enable → capture; disable → the pose is restored **exactly**, to float equality, `PostTransformMatrix` included. |
| `RagdollSystemOrderTests` | The group sits after `BillboardResolveSystem` and before `SocketResolveSystem`; a socket on a ragdolling node is not one frame late. |
| `RagdollSleepTests` | A settled ragdoll sleeps, and the settled pose survives `TransformApplySystem` stomping it (G1). |
| `SystemGroupStructureTests` (extend) | The new group's membership and edges. |

**Owner-verified in the Editor** — the documented handoff, which Claude cannot check alone: that a
billboarded character falls down the screen and keeps doing so as the camera orbits; that the boxes
are draggable and the drag feels right; that a drop reads as a fall rather than a collapse.

---

## 11. Build phases

Each phase ends compile-clean with its own tests green, gated through the Unity MCP
(`refresh_unity` → poll `editor_state.isCompiling` → `read_console` for `error CS`/`BC` →
`run_tests`, **checking the discovered count, not just pass/fail**). One subagent per phase; D2, D4
and D6 are the ones that need care.

| # | Phase | Deliverable | Depends on |
|---|---|---|---|
| **D0** | Address generalisation | `RigNodeAddress` / `RigNodeAddressKind` + `Bone` kind; every call site updated; V-R8; CHANGELOG **Breaking** entry. | — |
| **D1** | Authoring data model | `RagdollBodyDefinition`, `RagdollRigSettings`, `RagdollSpace`, `RagdollBodyId`, `RigAsset` fields, `EnsureStableIds` coverage, V-R1…V-R7, rig-inspector Ragdoll section. | D0 |
| **D2** | Solver core | `RagdollSolverTypes.cs` (§6.0) + `RagdollSolver` (XPBD, planar + spatial), pure static Burst functions; `RagdollSolverTests`, `RagdollPlanarConstraintTests`, `RagdollSolverDeterminismTests`. **No ECS, no editor, no authoring** — touches only `Runtime/Sampling/`, so it is the one phase that can run against an untouched tree. | — |
| **D3** | Runtime components + bake | `RagdollComponents.cs` wrapping D2's structs (§6.0), `ActorBaker` resolution into the buffer shallowest-first, inertia derivation, `RagdollBakingTests`, `DataContractTests` row. | D1, D2 |
| **D4** | Runtime systems | The group and its five systems, the fallback probe, `RagdollToggleTests`, `RagdollSystemOrderTests`, `RagdollSleepTests`. **G1 and G2 apply here.** | D2, D3 |
| **D5** | Clip Editor component | `ClipComponentKind.Ragdoll`, model registration, component-stack body, add/remove with rig-scoped undo, `ClipComponentModelTests`. | D1 |
| **D6** | Preview simulation + toggle | `RagdollPreviewSimulation`, `PreviewRagdollBoxHandles`, preview scenery singleton, toolbar wiring, `RagdollPreviewParityTests`. | D2, D5 |
| **D7** | Optional Unity Physics probe | The §7.5 verification item **first**, then the `Runtime.Physics` assembly, `RagdollPhysicsProbeSystem`, §1.3 + `Conformance_A` update. | D4 |
| **D8** | 3D mode polish | Swing/twist limits end to end, three-ring handles, `Spatial3D` fixtures. | D4, D6 |
| **D9** | Docs and close | `Documentation~/ragdoll.md`, index entry, `rigged-characters.md` limitation rewritten (it currently says "no ragdoll blending" flatly), `clip-editor.md` §Ragdoll rewritten, CHANGELOG, 0.11.0 bump + `Supplementary_PackageManifest` update, Amendment A50 appended to `Phase_B_Architecture.md`, vault memory note. | all |

---

## 12. Documented limitations (§12 rows — to ship in the CHANGELOG and the docs)

1. **A VAT/skinned actor does not ragdoll at run time.** Its skeleton exists only as texels; there is
   no bone entity to move. Authoring and editor preview are complete; the runtime keeps playing the
   baked clip. Cutout and transform-track parts ragdoll fully. (D1.)

   **D3 sharpened this into a structural fact, and it is worth stating exactly.** A
   `RigNodeAddressKind.Bone` address can *never* resolve at `ActorBaker` time — not "does not yet",
   but cannot, ever. The bone GameObjects such an address names live only on the separate source
   armature the VAT bake samples; the runtime prefab has no GameObject bone hierarchy at all
   (`rigged-characters.md`). So a bone-addressed ragdoll body is legal, authors cleanly, previews
   fully, and simply never gains a runtime node.

   **This is therefore not V26's error case, and must not be reported as one.** D3 distinguishes the
   two: a `RigTarget` or `HierarchyPath` address that matches nothing is a genuine authoring mistake
   and logs an error; a bone-only body is the documented limitation and logs at info level, never as
   a toolkit error or warning. Conflating them would train users to ignore a real error, which is
   the more expensive failure.
2. **Self-collision is box-vs-box only.** No capsules, no convex hulls. A box is what the authoring UI
   offers and what the solver understands, and the two agree by construction.
3. **World collision needs a provider.** Without Unity Physics the world is one horizontal plane at
   `fallbackGroundHeight`. With it, box-casts against the real `CollisionWorld`.
4. **A ragdoll has no timeline.** It cannot be keyed, scrubbed, or baked into a clip. Enabling it in
   the preview freezes the playhead.
5. **`space` is per rig, not per body.** Half a ragdoll on a plane is not a supported configuration.
6. **Contact response is linear-only** — no torque from a contact's point offset (§6.1). A box does
   not spin up from landing on its corner.

---

## 13. Open questions carried forward

| # | Question | Blocks | Current provisional answer |
|---|---|---|---|
| **Q1** | **Which local axis is "twist" measured about in Spatial3D?** §3.3 names a `twistLimitDegrees` but never says what it twists around, and the choice changes what twist *means* for every 3D rig — it is not a tuning value that can be adjusted later without re-authoring. | **D8** | D2 used the child's rest-local **+Y**, matching `BillboardMath`'s existing up-axis convention. Needs owner sign-off before D8 builds the 3D UI on top of it. |

Q1 does not block D0–D7: Planar2D never reads a twist axis, and 3D mode is not surfaced in the UI
until D8.
