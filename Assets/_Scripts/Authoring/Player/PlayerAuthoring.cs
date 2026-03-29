using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Tooltip("Drag the empty hand socket child (EquiptSocketAuthoring) here.")]
    public GameObject handSocket;

    public class Baker : Baker<PlayerAuthoring> {

        public override void Bake(PlayerAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player());
            
            AddComponent(entity, new PlayerActionMap{ activeActionMap = ActionMaps.Player});
            
            AddComponent(entity, new MovePlayerInput());
            SetComponentEnabled<MovePlayerInput>(entity, false);
            
            AddComponent(entity, new LookPlayerInput());
            SetComponentEnabled<LookPlayerInput>(entity, false);
            
            AddComponent(entity, new CursorPlayerInput());
            SetComponentEnabled<CursorPlayerInput>(entity, false);
            
            AddComponent(entity, new ZoomPlayerInput());
            SetComponentEnabled<ZoomPlayerInput>(entity, false);
            
            AddComponent(entity, new OnAttackPlayerInput());
            SetComponentEnabled<OnAttackPlayerInput>(entity, false);
            
            AddComponent(entity, new OnInteractPlayerInput());
            SetComponentEnabled<OnInteractPlayerInput>(entity, false);
            
            AddComponent(entity, new OnRollPlayerInput());
            SetComponentEnabled<OnRollPlayerInput>(entity, false);
            
            AddComponent(entity, new OnSneakPlayerInput());
            SetComponentEnabled<OnSneakPlayerInput>(entity, false);
            
            AddComponent(entity, new OnItemSlotPlayerInput());
            SetComponentEnabled<OnItemSlotPlayerInput>(entity, false);
            AddComponent(entity, new PlayerItemSlots());

            AddComponent(entity, new OnDropPlayerInput());
            SetComponentEnabled<OnDropPlayerInput>(entity, false);

            AddComponent(entity, new AimPlayerInput());
            SetComponentEnabled<AimPlayerInput>(entity, false);

            Entity socketEntity = authoring.handSocket != null
                ? GetEntity(authoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquipt { socketEntity = socketEntity });
        }
    }
}

