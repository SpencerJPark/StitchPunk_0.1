using Unity.Entities;
using UnityEngine;

// Place on an empty child GameObject of the hand bone.
// Marks this entity as the socket point that items snap to when equipped.
public class EquipSocketAuthoring : MonoBehaviour
{
    public class Baker : Baker<EquipSocketAuthoring>
    {
        public override void Bake(EquipSocketAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EquipSocket());
        }
    }
}
