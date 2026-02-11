using Unity.Entities;
using UnityEngine;

public class NeedsAuthoring : MonoBehaviour
{
    [Header("All needs: 1 = satisfied, 0 = urgent")]
    [Range(0f, 1f)] public float hunger = 0.8f;
    [Range(0f, 1f)] public float energy = 0.8f;
    [Range(0f, 1f)] public float entertainment = 0.6f;
    [Range(0f, 1f)] public float social = 0.5f;
    [Range(0f, 1f)] public float comfort = 0.8f;
    [Range(0f, 1f)] public float bladder = 0.9f;
    [Range(0f, 1f)] public float safety = 1f;
    [Range(0f, 1f)] public float movement = 0.5f;

    public class Baker : Baker<NeedsAuthoring>
    {
        public override void Bake(NeedsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Needs>(entity, new Needs
            {
                hunger = authoring.hunger,
                energy = authoring.energy,
                entertainment = authoring.entertainment,
                social = authoring.social,
                comfort = authoring.comfort,
                bladder = authoring.bladder,
                safety = authoring.safety,
                movement = authoring.movement
            });
        }
    }
}

public struct Needs : IComponentData
{
    // All needs: 1 = satisfied, 0 = urgent/critical
    public float hunger;        // 1 = full, 0 = starving
    public float energy;        // 1 = rested, 0 = exhausted
    public float entertainment; // 1 = entertained, 0 = bored
    public float social;        // 1 = social, 0 = lonely
    public float comfort;       // 1 = comfortable, 0 = uncomfortable
    public float bladder;       // 1 = empty, 0 = urgent
    public float safety;        // 1 = safe, 0 = terrified
    public float movement;      // 1 = satisfied, 0 = restless
}

