using Unity.Entities;
using UnityEngine;

public class ItemAuthoring : MonoBehaviour
{
    public ItemType itemType;

    [Tooltip("Drag the child GameObject that has ItemAttachPointAuthoring here.")]
    public GameObject gripPoint;

    [Tooltip("How fast the item travels when thrown (units/sec).")]
    public float throwSpeed = 10f;

    [Tooltip("Initial upward velocity when thrown (controls arc height).")]
    public float throwArc = 4f;

    [Tooltip("Damage dealt to a health entity when the thrown item hits it.")]
    public int throwDamage = 10;

    [Tooltip("Scales ragdoll violence when this item kills on impact. 1 = baseline (sword). 2+ = heavy/explosive.")]
    public float throwRagdollForce = 1f;

    public class Baker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Item { itemType = authoring.itemType });
            AddComponent(entity, new EquiptBy());
            AddComponent(entity, new AttachedTo());
            AddComponent(entity, new EquipRequest());
            SetComponentEnabled<EquipRequest>(entity, false);

            AddComponent(entity, new AttachRequest());
            SetComponentEnabled<AttachRequest>(entity, false);

            AddComponent(entity, new ThrownItem { throwSpeed = authoring.throwSpeed, throwArc = authoring.throwArc, throwDamage = authoring.throwDamage, ragdollForce = authoring.throwRagdollForce });
            SetComponentEnabled<ThrownItem>(entity, false);

            if (authoring.gripPoint != null)
            {
                Entity gripEntity = GetEntity(authoring.gripPoint, TransformUsageFlags.Dynamic);
                AddComponent(entity, new ItemGripPoint { entity = gripEntity });
            }
        }
    }
}
