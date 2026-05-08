using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Handles both drop (LB) and throw (RT held + Attack).
/// Unparents the equipped item, restores its world position, and for throws
/// applies a forward + arc velocity so ThrownItemSystem can move it.
/// </summary>
[UpdateInGroup(typeof(ItemEquiptSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct PlayerUnequipSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Player>();
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

        UnitEquipt unitEquipt = state.EntityManager.GetComponentData<UnitEquipt>(playerEntity);
        Entity itemEntity     = unitEquipt.equiptItemEntity;
        Entity socketEntity   = unitEquipt.socketEntity;

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
        state.EntityManager.SetComponentData(playerEntity, new UnitEquipt
        {
            equiptItemEntity = Entity.Null,
            socketEntity     = socketEntity
        });
        state.EntityManager.SetComponentData(itemEntity, new EquiptBy   { owner  = Entity.Null });
        state.EntityManager.SetComponentData(itemEntity, new AttachedTo { socket = Entity.Null });

        if (socketEntity != Entity.Null &&
            state.EntityManager.HasComponent<EquiptSocket>(socketEntity))
        {
            state.EntityManager.SetComponentData(socketEntity, new EquiptSocket { attachedItem = Entity.Null });
        }

        // ── Throw ─────────────────────────────────────────────────────────────
        if (isThrow)
        {
            state.EntityManager.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, false);

            LocalTransform playerTransform = state.EntityManager.GetComponentData<LocalTransform>(playerEntity);
            float3 forward = playerTransform.Forward();

            // ThrownItem owns X/Z only — Gravity owns Y
            ThrownItemRequest thrownItemRequest = state.EntityManager.GetComponentData<ThrownItemRequest>(itemEntity);
            state.EntityManager.SetComponentData(itemEntity, new ThrownItemRequest
            {
                velocity    = new float3(forward.x, 0f, forward.z) * thrownItemRequest.throwSpeed,
                throwSpeed  = thrownItemRequest.throwSpeed,
                throwArc    = thrownItemRequest.throwArc,
                throwDamage = thrownItemRequest.throwDamage,
                thrower     = playerEntity,
                throwOrigin = worldPos
            });
            state.EntityManager.SetComponentEnabled<ThrownItemRequest>(itemEntity, true);

            // Kick the arc through Gravity so UnitGravitySystem handles the full Y trajectory
            if (state.EntityManager.HasComponent<Gravity>(itemEntity))
            {
                Gravity gravity = state.EntityManager.GetComponentData<Gravity>(itemEntity);
                gravity.verticalVelocity = thrownItemRequest.throwArc;
                gravity.isGrounded       = false;
                state.EntityManager.SetComponentData(itemEntity, gravity);
            }
        }
    }

    public void OnDestroy(ref SystemState state) { }
}
