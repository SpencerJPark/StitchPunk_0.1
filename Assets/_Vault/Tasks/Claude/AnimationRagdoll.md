Billboard-space ragdoll physics

Depends on the hierarchical billboard system. Ragdolls consume the resolved billboard frame as their simulation reference — do not recompute facing or camera-relative orientation independently.

Simulation frame

Each ragdoll resolves its billboard root by walking up the rig hierarchy, exactly as the animation system does, and queries that root's billboard frame. If a subtree has its own billboard root, it simulates in that frame.
Gravity is a vector defined in billboard-local space, not Physics.gravity. Bodies fall "down" relative to the billboard's current orientation, so the ragdoll reads correctly from the viewer's perspective at any camera angle.
Simulation happens in a child space beneath the billboard transform. When the billboard reorients — from camera movement, angle snapping, or a keyed billboard offset — the simulation space rotates with it rather than the bodies being flung in world space.
Where the rig's joint constraints are set to 2D, constrain bodies to translate and rotate within the billboard plane only, with no out-of-plane drift.
Handle billboard reorientation mid-simulation: transform existing velocities into the new frame rather than leaving them expressed in the old one, so a camera orbit doesn't inject spurious energy. If angle snapping is enabled, the discrete jumps must not read as impulses — damp or interpolate across the snap.

Solver

Disable global gravity on ragdoll rigidbodies (useGravity = false) and apply the billboard-local gravity vector manually in FixedUpdate.
For strictly planar rigs, evaluate a lightweight custom solver (verlet or sequential-impulse over the joint chain) against Unity's 3D solver constrained to a plane. Recommend one with reasoning before implementing — the custom path is likely cheaper and better-behaved, but confirm rather than assume.

Lifetime and freezing

Ragdolls simulate for a configurable duration, default 5 seconds, exposed on the rig asset for per-character tuning.
Early-freeze when total kinetic energy drops below a threshold, rather than burning frames on a settled body.
On expiry or early-freeze: set all bodies kinematic, zero velocities, stop simulating. The transition must be visually seamless — no snap, twitch, or pose change.
Bake the final pose, return the entity to the DOTS/VAT path, and release the GameObject ragdoll to the pool.
Frozen ragdolls follow a configurable cleanup policy: persist, fade after N seconds, or despawn immediately.
A frozen ragdoll still billboards. Its bodies are kinematic, but the billboard root keeps facing the camera, and the frozen pose rotates with it as a unit.

Budget
Pool ragdoll GameObjects rather than instantiating per event, and cap simultaneous active ragdolls with a configurable budget that recycles the oldest.

That last freeze point is easy to miss — a frozen ragdoll that stops billboarding will visibly break when the camera moves, so it's worth checking explicitly once it's running.
