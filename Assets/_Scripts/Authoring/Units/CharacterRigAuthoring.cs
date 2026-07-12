using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

// Root of a character rig. Replaces AnimatorAuthoring + Ragdoll2DAuthoring (root config) +
// DesignAuthoring in a single component on the body root:
//   • starting animation layers → AnimationLayer buffer + SetAnimation buffer + AnimationRequest,
//   • ragdoll root config → Ragdoll2DConfig + Ragdoll2DLaunch (joint parts flag themselves via BodyPartAuthoring),
//   • design state → RandomizeDesign + PersistedDesign + CharacterPalette + ChangeDesignRequest,
//   • an empty BodyPart buffer that CharacterRigBakingSystem / BodyPartInitSystem fill from descendants.
public class CharacterRigAuthoring : MonoBehaviour
{
    [Header("Starting Animations")]
    public List<StartingLayer> startingLayers = new()
    {
        new StartingLayer { layer = AnimationLayerType.Base, animation = AnimationType.Idle, speed = 1f, looping = true },
    };

    [System.Serializable]
    public class StartingLayer
    {
        public AnimationLayerType layer = AnimationLayerType.Base;
        public AnimationType animation = AnimationType.Idle;
        public float speed = 1f;
        public bool looping = true;
    }

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

    [Header("Ragdoll")]
    [Tooltip("Bake the fake-ragdoll root config. Uncheck for characters that never ragdoll on death.")]
    public bool enableRagdoll = true;

    [Tooltip("The direct visual child that holds the whole character (this is what tilts on Z).")]
    public GameObject visualChild;

    [Tooltip("Angular speed at which the body tips over (deg/s). 180 = reaches 90° in ~0.5s.")]
    public float bodyFallSpeed = 180f;

    [FormerlySerializedAs("groundBuffer")]
    [Tooltip("Global ground buffer when falling FORWARD (fallSideSign >= 0): how far above root.Y to clamp.")]
    public float groundBufferForward = 0.15f;

    [Tooltip("Global ground buffer when falling BACKWARD (fallSideSign < 0). Falls back to forward if 0.")]
    public float groundBufferBackward = 0.15f;

    [Tooltip("Additive degrees on the base tilt when falling FORWARD.")]
    public float tiltOffsetForward;

    [Tooltip("Additive degrees on the base tilt when falling BACKWARD.")]
    public float tiltOffsetBackward;

    public class Baker : Baker<CharacterRigAuthoring>
    {
        public override void Bake(CharacterRigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // --- Animation ---
            DynamicBuffer<AnimationLayer> layers = AddBuffer<AnimationLayer>(entity);
            foreach (StartingLayer startingLayer in authoring.startingLayers)
            {
                layers.Add(new AnimationLayer
                {
                    layer     = startingLayer.layer,
                    animation = startingLayer.animation,
                    time      = 0f,
                    speed     = startingLayer.speed,
                    active    = true,
                    looping   = startingLayer.looping,
                });
            }
            SortLayers(ref layers);

            AddBuffer<SetAnimation>(entity);
            AddComponent<AnimationRequest>(entity);
            SetComponentEnabled<AnimationRequest>(entity, false);

            // --- Unified body-part registry (assembled by CharacterRigBakingSystem / BodyPartInitSystem) ---
            AddBuffer<BodyPart>(entity);
            AddComponent<CharacterRigConfig>(entity);

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

            // --- Ragdoll root config (joint parts stamp themselves via CharacterRigBakingSystem) ---
            if (authoring.enableRagdoll && authoring.visualChild != null)
            {
                Entity visualRootEntity = GetEntity(authoring.visualChild, TransformUsageFlags.Dynamic);

                AddComponent<Ragdoll2DLaunch>(entity);
                SetComponentEnabled<Ragdoll2DLaunch>(entity, false);

                float backward = authoring.groundBufferBackward > 0f
                    ? authoring.groundBufferBackward
                    : authoring.groundBufferForward;

                AddComponent(entity, new Ragdoll2DConfig
                {
                    visualRoot           = visualRootEntity,
                    groundBufferForward  = authoring.groundBufferForward,
                    groundBufferBackward = backward,
                    tiltOffsetForward    = authoring.tiltOffsetForward,
                    tiltOffsetBackward   = authoring.tiltOffsetBackward,
                    fallSpeed            = authoring.bodyFallSpeed,
                });
            }
        }

        private static void SortLayers(ref DynamicBuffer<AnimationLayer> layers)
        {
            for (int sortPass = 0; sortPass < layers.Length - 1; sortPass++)
            {
                for (int compareIndex = 0; compareIndex < layers.Length - 1 - sortPass; compareIndex++)
                {
                    if ((int)layers[compareIndex].layer > (int)layers[compareIndex + 1].layer)
                    {
                        AnimationLayer swapTemp = layers[compareIndex];
                        layers[compareIndex] = layers[compareIndex + 1];
                        layers[compareIndex + 1] = swapTemp;
                    }
                }
            }
        }
    }
}
