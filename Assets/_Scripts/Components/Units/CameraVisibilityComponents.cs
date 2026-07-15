using Unity.Entities;

// Camera-visibility gate (PRESENTATION ONLY). Enabled = the entity is on screen (or within the
// hysteresis padding of it); disabled = safely off screen. Flipped by CameraVisibilitySystem
// (GameManagerSystemGroup) from the CameraView singleton — on character rig roots directly, and
// propagated to every BodyPart child so part-level presentation jobs can chunk-filter on it.
//
// ⚠ HARD RULE: only presentation-side systems (animation sampling/apply, image-index push,
// billboard) may gate on this tag. Simulation systems (AI, movement, combat, health) must NEVER
// filter on it — the world keeps running off screen, or behavior becomes camera-dependent.
//
// Baked ENABLED by CharacterRigAuthoring (roots), BodyPartAuthoring (parts), and
// ImageIndexAuthoring (standalone image-index quads — never flipped, they belong to no rig, the
// tag just keeps them matching the gated presentation queries).
public struct CameraVisible : IComponentData, IEnableableComponent { }
