using Unity.Entities;
using UnityEngine;

public class NeedsAuthoring : MonoBehaviour
{
    [Range(0f, 1f)] public float hunger = 0.2f;
    [Range(0f, 1f)] public float energy = 0.1f;
    [Range(0f, 1f)] public float comfort = 0.1f;
    [Range(0f, 1f)] public float entertainment = 0.6f;
    [Range(0f, 1f)] public float bladder = 0f;
    [Range(0f, 1f)] public float social = 0.5f;
    [Range(0f, 1f)] public float safety = 1f;

    public class Baker : Baker<NeedsAuthoring>
    {
        public override void Bake(NeedsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Needs>(entity, new Needs
            {
                hunger = authoring.hunger,
                energy = authoring.energy,
                comfort = authoring.comfort,
                entertainment = authoring.entertainment,
                bladder = authoring.bladder,
                social = authoring.social,
                safety = authoring.safety,
            });
        }
    }
}

public struct Needs : IComponentData
{
    // time based
    public float hunger;
    public float energy;
    public float comfort;
    public float entertainment;
    public float bladder;
    public float social;
    
    // event based
    public float safety;
}