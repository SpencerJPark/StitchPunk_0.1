using Unity.Entities;
using UnityEngine;

public class UnitActionAuthoring : MonoBehaviour
{
    public class Baker : Baker<UnitActionAuthoring>
    {
        public override void Bake(UnitActionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitAction());
        }
    }
}


