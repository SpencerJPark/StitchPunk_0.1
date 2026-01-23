using Unity.Entities;
using UnityEngine;

public class GuardBrainAuthoring : MonoBehaviour
{
    public class Baker : Baker<GuardBrainAuthoring>
    {
        public override void Bake(GuardBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<GuardBrain>(entity);
            AddComponent<CanPatrol>(entity);
            AddComponent<CanAttack>(entity);
            AddComponent<CanEat>(entity);
            AddComponent<CanSleep>(entity);
        }
    }
}

public struct GuardBrain : IComponentData { }