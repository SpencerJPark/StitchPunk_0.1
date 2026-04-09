using Unity.Entities;
using UnityEngine;

// Bakes the undead-specific components onto a unit body entity.
// Add this alongside MinionAuthoring on zombie prefabs, but it can exist
// independently on any entity that can be revived without being player-controllable.
public class UndeadAuthoring : MonoBehaviour
{
    public bool startUndead;

    public class Baker : Baker<UndeadAuthoring>
    {
        public override void Bake(UndeadAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Undead>(entity);
            SetComponentEnabled<Undead>(entity, authoring.startUndead);

            AddComponent<Revive>(entity);
            SetComponentEnabled<Revive>(entity, false);

            AddComponent<SwapBrainRequest>(entity);
            SetComponentEnabled<SwapBrainRequest>(entity, false);
        }
    }
}
