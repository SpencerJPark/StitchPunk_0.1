using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour
{
    [Tooltip("Drag the empty hand socket child (EquiptSocketAuthoring) here.")]
    public GameObject handSocket;

    [Tooltip("Child GameObject that acts as the aim arrow visual (shown while aiming).")]
    public GameObject aimIndicator;

    [Header("Debug — Starting Equipment")]
    [SearchableEnum] public ItemType debugSlot1 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot2 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot3 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot4 = ItemType.None;

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

            AddComponent(entity, new OnEquipmentSlotPlayerInput());
            SetComponentEnabled<OnEquipmentSlotPlayerInput>(entity, false);
            AddComponent(entity, new PlayerEquipmentSlots
            {
                itemSlot1 = authoring.debugSlot1,
                itemSlot2 = authoring.debugSlot2,
                itemSlot3 = authoring.debugSlot3,
                itemSlot4 = authoring.debugSlot4,
            });

            AddComponent(entity, new OnDropPlayerInput());
            SetComponentEnabled<OnDropPlayerInput>(entity, false);

            AddComponent(entity, new AimPlayerInput());
            SetComponentEnabled<AimPlayerInput>(entity, false);

            AddComponent(entity, new AimDirection { direction = new Unity.Mathematics.float3(0f, 0f, 1f) });

            Entity indicatorEntity = authoring.aimIndicator != null
                ? GetEntity(authoring.aimIndicator, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new AimIndicatorRef { visualEntity = indicatorEntity });

            AddComponent(entity, new OnMinionMoveCommand());
            SetComponentEnabled<OnMinionMoveCommand>(entity, false);

            AddComponent(entity, new OnMinionInteractCommand());
            SetComponentEnabled<OnMinionInteractCommand>(entity, false);

            AddComponent(entity, new SelectionBoxData());
            SetComponentEnabled<SelectionBoxData>(entity, false);

            AddComponent(entity, new CursorScreenPosition());

            Entity socketEntity = authoring.handSocket != null
                ? GetEntity(authoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquipt { socketEntity = socketEntity });
        }
    }
}

