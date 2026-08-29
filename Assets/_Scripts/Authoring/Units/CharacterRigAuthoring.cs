using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// Root of a character rig. Replaces AnimatorAuthoring + Ragdoll2DAuthoring (root config) +
// DesignAuthoring in a single component on the body root:
//   • design state → RandomizeDesign + PersistedDesign + CharacterPalette + ChangeDesignRequest,
//   • an empty BodyPart buffer that CharacterRigBakingSystem / BodyPartInitSystem fill from descendants.
// Animation and ragdoll baking (starting layers, AnimationCommand buffer, RagdollActor/RagdollLaunch)
// is the toolkit's own ActorAuthoring component, added directly to the rig root alongside this one —
// see the Animation Toolkit Migration spec. The toolkit adds RagdollActor/RagdollLaunch automatically
// once the rig declares any ragdoll body; there is nothing for this baker to add.
public class CharacterRigAuthoring : MonoBehaviour
{
    [Header("Design")]
    [Tooltip("Pre-placed (subscene-baked) units never pass through the runtime spawner, so the design " +
             "pipeline never runs on them. Check this to enable NewlySpawned at bake so they roll + apply " +
             "a random design once on load. Leave unchecked for prefabs spawned at runtime.")]
    public bool reloadDesign;

    [Tooltip("What a random spawn may roll, per shape-tag group: the character picks ONE tag per group " +
             "from these lists (e.g. Skin: Pale/Tan/Dark). Designs whose tag is listed nowhere (e.g. " +
             "\"Zombie\") are reachable only via ChangeDesignRequest. Authoring decides randomness — " +
             "the UnitPartSO assets stay purely descriptive.")]
    public List<RandomTagGroup> randomTags = new();

    [System.Serializable]
    public class RandomTagGroup
    {
        [Tooltip("Shape-tag group name, matching UnitPartSO.group (e.g. \"Skin\", \"Hair\").")]
        public string group = "";

        [Tooltip("The tags a random spawn may roll for this group.")]
        public List<string> tags = new();
    }

    public class Baker : Baker<CharacterRigAuthoring>
    {
        public override void Bake(CharacterRigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // --- Unified body-part registry (assembled by CharacterRigBakingSystem / BodyPartInitSystem) ---
            AddBuffer<BodyPart>(entity);
            AddComponent<CharacterRigConfig>(entity);

            // Camera-visibility gate (starts visible; CameraVisibilitySystem flips it from CameraView).
            AddComponent<CameraVisible>(entity);
            SetComponentEnabled<CameraVisible>(entity, true);

            // --- Design ---
            AddComponent<RandomizeDesign>(entity);
            SetComponentEnabled<RandomizeDesign>(entity, true);
            AddComponent(entity, new PersistedDesign());
            AddComponent(entity, new CharacterPalette());
            AddComponent<ChangeDesignRequest>(entity);
            SetComponentEnabled<ChangeDesignRequest>(entity, false);

            // Authoring-decided random roll pool: one entry per (group, tag) a spawn may roll.
            DynamicBuffer<RandomTagOption> randomTagOptions = AddBuffer<RandomTagOption>(entity);
            foreach (RandomTagGroup tagGroup in authoring.randomTags)
            {
                if (tagGroup == null || string.IsNullOrEmpty(tagGroup.group)) continue;

                foreach (string tagName in tagGroup.tags)
                {
                    if (string.IsNullOrEmpty(tagName)) continue;

                    RandomTagOption option = default;
                    option.group.CopyFromTruncated(tagGroup.group);
                    option.tag.CopyFromTruncated(tagName);
                    randomTagOptions.Add(option);
                }
            }

            if (authoring.reloadDesign)
                AddComponent<DesignReloadOnBake>(entity);
        }
    }
}
