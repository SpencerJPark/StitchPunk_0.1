using Unity.Entities;
using UnityEngine;

// Place on an empty child GameObject of the hand bone.
// Marks this entity as the socket point that items snap to when equipped.
public class EquiptSocketAuthoring : MonoBehaviour
{
    public class Baker : Baker<EquiptSocketAuthoring>
    {
        public override void Bake(EquiptSocketAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EquiptSocket());
        }
    }
}
