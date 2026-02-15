using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public float awarenessRange;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<CitizenBrain>(entity);
            AddComponent(entity, new Awareness
            {
                range = authoring.awarenessRange
            });
            
            BrainBakeHelper.AddRequirements(this, entity);
            BrainBakeHelper.AddHumanMotivations(this, entity);
            BrainBakeHelper.AddRandomMotivations(this, entity, (uint)entity.Index);
        }
    }
}


public struct CitizenBrain : IComponentData { }

public struct Awareness : IComponentData
{
    public float range;
}