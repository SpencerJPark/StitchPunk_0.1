using Unity.Entities;
using UnityEngine;

public class NeedsAuthoring : MonoBehaviour
{
    [Range(0f, 1f)] public float hunger = 0.5f;
    [Range(0f, 1f)] public float energy = 0.5f;
    [Range(0f, 1f)] public float entertainment = 0.5f;
    [Range(0f, 1f)] public float social = 0.5f;

    public class Baker : Baker<NeedsAuthoring>
    {
        public override void Bake(NeedsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Needs
            {
                hunger = authoring.hunger,
                energy = authoring.energy,
                entertainment = authoring.entertainment,
                social = authoring.social
            });
        }
    }
}

public struct Needs : IComponentData
{
    public float hunger;
    public float energy;
    public float entertainment;
    public float social;
}