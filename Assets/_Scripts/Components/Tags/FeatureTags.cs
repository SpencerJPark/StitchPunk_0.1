using Unity.Entities;

// Per-feature plug tags — baked by FeatureConfigAuthoring (Authoring/Tags/). A top-level
// feature group gates on its tag via RequireForUpdate<XFeature>() in SystemGroups.cs; a scene
// that does not bake the tag runs without that feature entirely (no code change, no #if).
// Presence-based on purpose (NOT IEnableableComponent): plugging a feature is a scene-authoring
// decision, not a runtime toggle.
//
// NOTE: the group-side RequireForUpdate calls are NOT wired yet — wiring them before every
// playable subscene contains a FeatureConfigAuthoring would silently disable the feature.
// See _Vault/Tasks/Plans/ FeatureToggle plan for the rollout steps.

public struct CombatFeature : IComponentData { }

public struct BuildingsFeature : IComponentData { }

public struct SoundFeature : IComponentData { }

public struct SaveFeature : IComponentData { }
