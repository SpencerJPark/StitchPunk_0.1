using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public GameObject body;
    public float awarenessRange;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            BrainBakeHelper.AddRequirements(this, entity, authoring.body, authoring.awarenessRange);
            BrainBakeHelper.AddHumanMotivations(this, entity);
            BrainBakeHelper.AddRandomMotivations(this, entity, (uint)entity.Index);
        }
    }
}