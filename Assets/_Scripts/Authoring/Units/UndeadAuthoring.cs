using Unity.Entities;
using UnityEngine;

public class UndeadAuthoring : MonoBehaviour
{
    public class Baker : Baker<UndeadAuthoring>
    {
        public override void Bake(UndeadAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Undead>(entity);
            SetComponentEnabled<Undead>(entity, true);
        }
    }
}
