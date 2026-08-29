using DotsMovementToolkit;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Handles both drop (LB) and throw (RT held + Attack).
/// Unparents the equipped item, restores its world position, and for throws
/// applies a forward + arc velocity so ThrownItemSystem can move it.
/// </summary>
[UpdateInGroup(typeof(ItemEquipSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct PlayerUnequipSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<ItemLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        bool isDrop    = state.EntityManager.IsComponentEnabled<OnDropPlayerInput>(playerEntity);
        bool isAiming  = state.EntityManager.IsComponentEnabled<AimPlayerInput>(playerEntity);
        bool isAttack  = state.EntityManager.IsComponentEnabled<OnAttackPlayerInput>(playerEntity);
        bool isThrow   = isAiming && isAttack;

        if (!isDrop && !isThrow) return;

        if (isDrop)  state.EntityManager.SetComponentEnabled<OnDropPlayerInput>(playerEntity, false);

        UnitEquip unitEquip = state.EntityManager.GetComponentData<UnitEquip>(playerEntity);
        Entity itemEntity     = unitEquip.equipItemEntity;
        Entity socketEntity   = unitEquip.socketEntity;

        if (itemEntity == Entity.Null)
        {
            // Nothing equipped — still consume inputs so other systems don't misfire
            if (isThrow) state.EntityManager.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, false);
            return;
        }

        // ── Unequip ──────────────────────────────────────────────────────────
        // Capture socket world position before removing the parent relationship
        // so the item stays in place rather than snapping to world origin.
        float3 worldPos = float3.zero;
        if (socketEntity != Entity.Null &&
            SystemAPI.HasComponent<LocalToWorld>(socketEntity))
        {
            worldPos = SystemAPI.GetComponent<LocalToWorld>(socketEntity).Position;
        }

        state.EntityManager.SetComponentData(itemEntity, LocalTransform.FromPosition(worldPos));

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        ecb.RemoveComponent<Parent>(itemEntity);
        ecb.SetComponentEnabled<PlayerInteractable>(itemEntity, true);
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        // Clear equip data (preserve socketEntity on player for future pickups)
        state.EntityManager.SetComponentData(playerEntity, new UnitEquip
        {
            equipItemEntity = Entity.Null,
            socketEntity     = socketEntity
        });
        state.EntityManager.SetComponentData(itemEntity, new EquipBy   { owner  = Entity.Null });
        state.EntityManager.SetComponentData(itemEntity, new AttachedTo { socket = Entity.Null });

        if (socketEntity != Entity.Null &&
            state.EntityManager.HasComponent<EquipSocket>(socketEntity))
        {
            state.EntityManager.SetComponentData(socketEntity, new EquipSocket { attachedItem = Entity.Null });
        }

        // ── Throw ─────────────────────────────────────────────────────────────
        if (isThrow)
        {
            state.EntityManager.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, false);

            LocalTransform playerTransform = state.EntityManager.GetComponentData<LocalTransform>(playerEntity);
            float3 forward = playerTransform.Forward();

            BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.GetSingleton<ItemLibrary>().library;
            Item itemComp = state.EntityManager.GetComponentData<Item>(itemEntity);
            ref ItemBlob itemBlob = ref itemLibrary.Value.items[(int)itemComp.itemType];

            // ThrownItem owns X/Z only — Gravity owns Y
            state.EntityManager.SetComponentData(itemEntity, new ThrownItemRequest
            {
                velocity    = new float3(forward.x, 0f, forward.z) * itemBlob.throwSpeed,
                thrower     = playerEntity,
                throwOrigin = worldPos
            });
            state.EntityManager.SetComponentEnabled<ThrownItemRequest>(itemEntity, true);

            // Kick the arc through Gravity so UnitGravitySystem handles the full Y trajectory
            if (state.EntityManager.HasComponent<Gravity>(itemEntity))
            {
                Gravity gravity = state.EntityManager.GetComponentData<Gravity>(itemEntity);
                gravity.verticalVelocity = itemBlob.throwArc;
                gravity.isGrounded       = false;
                state.EntityManager.SetComponentData(itemEntity, gravity);
            }
        }
    }

    public void OnDestroy(ref SystemState state) { }
}
