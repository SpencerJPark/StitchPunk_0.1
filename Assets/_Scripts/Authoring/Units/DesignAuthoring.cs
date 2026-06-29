using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place on the root body GameObject (sibling to the other unit authoring). Declares, per body
/// part, one or more valid [min,max] texture-array index ranges. The baker flattens these into the
/// DesignPart + DesignRange buffers (mirrors Ragdoll2DAuthoring's joint/zone flatten), bakes
/// RandomizeDesign enabled (DesignRandomizeSystem rolls a random in-range index per part on spawn),
/// an empty PersistedDesign (the saved result), and ChangeDesignRequest disabled (runtime re-skin).
/// </summary>
public class DesignAuthoring : MonoBehaviour
{
    [Tooltip("Pre-placed (subscene-baked) units never pass through the runtime spawner, so the design " +
             "pipeline never runs on them. Check this to enable NewlySpawned at bake so they roll + apply " +
             "a random design once on load. Leave unchecked for prefabs spawned at runtime (handled already).")]
    public bool reloadDesign;

    [Tooltip("Per body part: one or more valid [min,max] index ranges into the texture array. " +
             "On spawn a random index is rolled uniformly across the union of all ranges for that " +
             "part — keep zombie slices out of these ranges so randomization only picks human parts.")]
    public List<DesignPartEntry> parts = new();

    public class Baker : Baker<DesignAuthoring>
    {
        public override void Bake(DesignAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            DynamicBuffer<DesignPart>  partBuffer  = AddBuffer<DesignPart>(entity);
            DynamicBuffer<DesignRange> rangeBuffer = AddBuffer<DesignRange>(entity);
            int rangeIndex = 0;
            foreach (DesignPartEntry entry in authoring.parts)
            {
                int rangeStart = rangeIndex;
                if (entry.ranges != null)
                {
                    foreach (IndexRange range in entry.ranges)
                    {
                        rangeBuffer.Add(new DesignRange { min = range.min, max = range.max });
                        rangeIndex++;
                    }
                }
                partBuffer.Add(new DesignPart
                {
                    target     = entry.target,
                    rangeStart = rangeStart,
                    rangeCount = rangeIndex - rangeStart,
                });
            }

            // Roll a random design on first spawn (consumed + disabled by DesignRandomizeSystem).
            AddComponent<RandomizeDesign>(entity);
            SetComponentEnabled<RandomizeDesign>(entity, true);

            // Chosen indices — empty until rolled / restored. Round-tripped via the generic IPersist path.
            AddComponent(entity, new PersistedDesign());

            // Runtime re-skin hook — baked present + disabled, enabled by callers (e.g. zombie conversion).
            AddComponent<ChangeDesignRequest>(entity);
            SetComponentEnabled<ChangeDesignRequest>(entity, false);

            // Pre-placed units: flag for DesignReloadBakingSystem to enable NewlySpawned (runs the
            // spawn-init design pipeline once on load). Bake-only marker, stripped from the runtime world.
            if (authoring.reloadDesign)
                AddComponent<DesignReloadOnBake>(entity);
        }
    }
}

[Serializable]
public class DesignPartEntry
{
    [Tooltip("Which body part this entry customizes.")]
    public AnimationTarget target;

    [Tooltip("Valid [min,max] index ranges for this part (union of all ranges is the valid set).")]
    public List<IndexRange> ranges = new();
}

[Serializable]
public class IndexRange
{
    [Tooltip("Minimum (inclusive) valid texture-array index.")]
    public int min;

    [Tooltip("Maximum (inclusive) valid texture-array index.")]
    public int max;
}
