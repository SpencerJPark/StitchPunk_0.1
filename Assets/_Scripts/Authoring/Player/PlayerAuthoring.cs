using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    public float rollTimeMax = 5f;
    public float rollSpeed = 5f;
    
    public class Baker : Baker<PlayerAuthoring> {

        public override void Bake(PlayerAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player());
            AddComponent(entity, new PlayerData
            {
                rollTimeMax = authoring.rollTimeMax,
                rollSpeed = authoring.rollSpeed,
            });
            AddComponent(entity, new PlayerAttackInput());
            SetComponentEnabled<PlayerAttackInput>(entity, false);
            AddComponent(entity, new PlayerInteractInput());
            SetComponentEnabled<PlayerInteractInput>(entity, false);
            AddComponent(entity, new PlayerRollInput());
            SetComponentEnabled<PlayerRollInput>(entity, false);
            AddComponent(entity, new PlayerSneakToggle());
            SetComponentEnabled<PlayerSneakToggle>(entity, false);
        }
    }
}

