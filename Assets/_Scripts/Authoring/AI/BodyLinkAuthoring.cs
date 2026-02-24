using Unity.Entities;
using UnityEngine;

public class BodyLinkAuthoring : MonoBehaviour
{
    public GameObject body;

    public class Baker : Baker<BodyLinkAuthoring>
    {
        public override void Bake(BodyLinkAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity bodyEntity = authoring.body != null 
                ? GetEntity(authoring.body, TransformUsageFlags.Dynamic) 
                : Entity.Null;

            AddComponent(entity, new BodyLink { body = bodyEntity });
            
        }
    }
}

