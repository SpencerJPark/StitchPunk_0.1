using Unity.Entities;
using UnityEngine;

// Scene-level feature plugboard. Drop ONE of these into a playable subscene (next to the
// GameSceneTag prefab) and untick a feature to unplug its whole system group in that scene —
// no code change, no recompile. The corresponding top-level group must gate on the tag via
// RequireForUpdate<XFeature>() in SystemGroups.cs for the toggle to take effect.
public class FeatureConfigAuthoring : MonoBehaviour
{
    [Header("Feature Plugs — untick to disable the feature's system group in this scene")]
    [Tooltip("CombatSystemGroup — attack execution, hazards, damage reactions")]
    public bool combatEnabled = true;

    [Tooltip("BuildingsSystemGroup — factory grid + station production loop")]
    public bool buildingsEnabled = true;

    [Tooltip("SoundSystemGroup — voice selection, world mood, music state")]
    public bool soundEnabled = true;

    [Tooltip("SaveSystemGroup — play time tracking, auto-save, save/load to disk")]
    public bool saveEnabled = true;

    public class Baker : Baker<FeatureConfigAuthoring>
    {
        public override void Bake(FeatureConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            if (authoring.combatEnabled)    AddComponent<CombatFeature>(entity);
            if (authoring.buildingsEnabled) AddComponent<BuildingsFeature>(entity);
            if (authoring.soundEnabled)     AddComponent<SoundFeature>(entity);
            if (authoring.saveEnabled)      AddComponent<SaveFeature>(entity);
        }
    }
}
