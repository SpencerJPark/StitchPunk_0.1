using Unity.Entities;
using UnityEngine;

public class BillboardAuthoring : MonoBehaviour {

    public GameObject parentGameObject;

    public class Baker : Baker<BillboardAuthoring> {


        public override void Bake(BillboardAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Billboard
            {
                parentEntity = GetEntity(authoring.parentGameObject, TransformUsageFlags.Dynamic),
            });
        }
    }
}




public struct Billboard : IComponentData
{
    public Entity parentEntity;
}