using Unity.Entities;
using UnityEngine;

public class BrainLinkAuthoring : MonoBehaviour
{
    public GameObject body;

    public class Baker : Baker<BrainLinkAuthoring>
    {
        public override void Bake(BrainLinkAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity bodyEntity = authoring.body != null 
                ? GetEntity(authoring.body, TransformUsageFlags.Dynamic) 
                : Entity.Null;

            AddComponent(entity, new BrainLink { body = bodyEntity });
            AddComponent<IsBrain>(entity);
        }
    }
}

public struct BrainLink : IComponentData
{
    public Entity body;
}

public struct IsBrain : IComponentData { }