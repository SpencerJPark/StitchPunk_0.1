using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    [Header("Action Lock Settings")]
    public BrainBakeHelper.ActionLockSettings actionLockSettings;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            BrainBakeHelper.AddRequirements(this, entity, authoring.actionLockSettings);
            
            BrainBakeHelper.AddHumanMotivations(this, entity);
            BrainBakeHelper.AddRandomMotivations(this, entity, (uint)entity.Index);
        }
    }
}

public struct CitizenBrain : IComponentData { }