using Unity.Entities;
using UnityEngine;

public class PlayerControllerAuthoring : MonoBehaviour
{
    [Tooltip("Drag the empty hand socket child (EquipSocketAuthoring) here.")]
    public GameObject handSocket;

    [Tooltip("Child GameObject that acts as the aim arrow visual (shown while aiming).")]
    public GameObject aimIndicator;

    [Header("Combat")]
    [SearchableEnum] public DamageSource defaultAttack = DamageSource.Slash;

    [Header("Debug — Starting Equipment")]
    [SearchableEnum] public ItemType debugSlot1 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot2 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot3 = ItemType.None;
    [SearchableEnum] public ItemType debugSlot4 = ItemType.None;

    [Header("Minion Groups")]
    [Tooltip("Number of group slots available at start (1–4). Upgrade system will write this at runtime.")]
    [Range(1, 4)] public int startingGroupCount = 1;

    [Tooltip("One scene-object marker per group slot (index 0 = group 1). Shown at the order destination, hidden on arrival or overwrite.")]
    public GameObject[] groupOrderMarkers = new GameObject[4];

    public class Baker : Baker<PlayerControllerAuthoring> {

        public override void Bake(PlayerControllerAuthoring controllerAuthoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player());

            AddComponent(entity, new PlayerActionMap{ activeActionMap = ActionMaps.Player});
            
            AddComponent(entity, new Faction
            {
                factionType = FactionType.Player,
            });

            // Reviver targeting: PlayerTargetingSystem writes the nearest PlayerInteractable
            // (e.g. a revivable corpse) here; PlayerReviverSystem reads it. Both query
            // WithPresent<Target>, so the component must exist on the player — baked disabled
            // (no target until one is in range). The player is not a UnitAuthoring entity, so
            // this is the only place Target gets added to the player.

            AddComponent(entity, new MovePlayerInput());
            SetComponentEnabled<MovePlayerInput>(entity, false);

            AddComponent(entity, new LookPlayerInput());
            SetComponentEnabled<LookPlayerInput>(entity, false);

            AddComponent(entity, new CursorPlayerInput());
            SetComponentEnabled<CursorPlayerInput>(entity, false);

            AddComponent(entity, new ZoomPlayerInput());
            SetComponentEnabled<ZoomPlayerInput>(entity, false);

            AddComponent(entity, new PlayerSelectedAttack { damageSource = controllerAuthoring.defaultAttack });

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
                itemSlot1 = controllerAuthoring.debugSlot1,
                itemSlot2 = controllerAuthoring.debugSlot2,
                itemSlot3 = controllerAuthoring.debugSlot3,
                itemSlot4 = controllerAuthoring.debugSlot4,
            });

            AddComponent(entity, new OnDropPlayerInput());
            SetComponentEnabled<OnDropPlayerInput>(entity, false);

            AddComponent(entity, new AimPlayerInput());
            SetComponentEnabled<AimPlayerInput>(entity, false);

            AddComponent(entity, new AimDirection { direction = new Unity.Mathematics.float3(0f, 0f, 1f) });

            Entity indicatorEntity = controllerAuthoring.aimIndicator != null
                ? GetEntity(controllerAuthoring.aimIndicator, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new AimIndicatorRef { visualEntity = indicatorEntity });

            AddComponent(entity, new OnMinionMoveCommand());
            SetComponentEnabled<OnMinionMoveCommand>(entity, false);

            AddComponent(entity, new OnMinionInteractCommand());
            SetComponentEnabled<OnMinionInteractCommand>(entity, false);

            AddComponent(entity, new OnMinionAttackCommand());
            SetComponentEnabled<OnMinionAttackCommand>(entity, false);

            AddComponent(entity, new OnMinionDefendCommand());
            SetComponentEnabled<OnMinionDefendCommand>(entity, false);

            AddComponent(entity, new OnMinionFollowCommand());
            SetComponentEnabled<OnMinionFollowCommand>(entity, false);

            // Minion group input components
            AddComponent(entity, new OnCycleGroupInput());
            SetComponentEnabled<OnCycleGroupInput>(entity, false);

            AddComponent(entity, new OnQuickCommandInput());
            SetComponentEnabled<OnQuickCommandInput>(entity, false);

            AddComponent(entity, new OnSelectPlayerInput());
            SetComponentEnabled<OnSelectPlayerInput>(entity, false);

            AddComponent(entity, new OnCommandPlayerInput());
            SetComponentEnabled<OnCommandPlayerInput>(entity, false);

            AddComponent(entity, new OnSnapCameraBackInput());
            SetComponentEnabled<OnSnapCameraBackInput>(entity, false);

            // Minion group data — one group unlocked by default
            AddComponent(entity, new PlayerMinionGroupsData
            {
                unlockedGroupCount = controllerAuthoring.startingGroupCount,
                assignmentGroupIndex = 0
            });

            // Create 4 persistent horde entities (one per group slot) and store refs in buffer
            DynamicBuffer<PlayerHordeSlot> hordeSlots = AddBuffer<PlayerHordeSlot>(entity);
            for (int slotIndex = 0; slotIndex < 4; slotIndex++)
            {
                Entity hordeEntity = CreateAdditionalEntity(TransformUsageFlags.None);

                Entity markerEntity = Entity.Null;
                if (controllerAuthoring.groupOrderMarkers != null
                    && slotIndex < controllerAuthoring.groupOrderMarkers.Length
                    && controllerAuthoring.groupOrderMarkers[slotIndex] != null)
                {
                    markerEntity = GetEntity(controllerAuthoring.groupOrderMarkers[slotIndex], TransformUsageFlags.Dynamic);
                }

                AddComponent(hordeEntity, new Horde
                {
                    hordeId        = slotIndex + 1,
                    targetPosition = new Unity.Mathematics.float3(0f, 0f, 0f),
                    targetEntity   = Entity.Null,
                    flowFieldIndex = -1,
                    memberCount    = 0,
                    isActive       = true,
                    needsPathUpdate = false,
                    behaviorFlags  = 0,
                    markerEntity   = markerEntity,
                });
                AddBuffer<HordeMemberBuffer>(hordeEntity);
                hordeSlots.Add(new PlayerHordeSlot { hordeEntity = hordeEntity });
            }

            AddComponent(entity, new SelectionBoxData());
            SetComponentEnabled<SelectionBoxData>(entity, false);

            AddComponent(entity, new CursorScreenPosition());

            Entity socketEntity = controllerAuthoring.handSocket != null
                ? GetEntity(controllerAuthoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquip { socketEntity = socketEntity });
        }
    }
}

