using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Attach this to the selection ring child quad on a unit prefab.
// Bakes SelectionColor onto the entity so SelectedVisualSystem can drive
// the _SelectionColor shader property at runtime without any cross-entity lookup.
public class SelectionVisualAuthoring : MonoBehaviour
{
    public class Baker : Baker<SelectionVisualAuthoring>
    {
        public override void Bake(SelectionVisualAuthoring authoring)
        {
            Entity visualEntity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(visualEntity, new SelectionColor
            {
                Value = new float4(1f, 1f, 1f, 1f),
            });
        }
    }
}
