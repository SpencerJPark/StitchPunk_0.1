using Unity.Entities;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CitizenBrain>(entity);
            AddComponent<CanAttack>(entity);
            AddComponent<CanEat>(entity);
            AddComponent<CanSleep>(entity);
        }
    }
}

public struct CitizenBrain : IComponentData { }