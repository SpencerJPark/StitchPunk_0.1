using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(ItemEquipSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct AiThrowItemSystem : ISystem
{
    private struct PendingThrow
    {
        public Entity thrower;
        public Entity target;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        NativeList<PendingThrow> pendingThrows = new NativeList<PendingThrow>(8, Allocator.Temp);

        foreach ((RefRO<AiThrowRequest> throwRequest,
                  EnabledRefRW<AiThrowRequest> aiThrowRequestEnabled,
                  Entity unitEntity) in
            SystemAPI.Query<RefRO<AiThrowRequest>, EnabledRefRW<AiThrowRequest>>().WithEntityAccess())
        {
            aiThrowRequestEnabled.ValueRW = false;
            pendingThrows.Add(new PendingThrow { thrower = unitEntity, target = throwRequest.ValueRO.target });
        }

        if (pendingThrows.Length == 0)
            return;

        EntityManager entityManager = state.EntityManager;
        BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.GetSingleton<ItemLibrary>().library;
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int throwIndex = 0; throwIndex < pendingThrows.Length; throwIndex++)
        {
            PendingThrow pendingThrow = pendingThrows[throwIndex];
            Entity unitEntity = pendingThrow.thrower;

            UnitEquip unitEquip = entityManager.GetComponentData<UnitEquip>(unitEntity);
            Entity itemEntity = unitEquip.equipItemEntity;
            Entity socketEntity = unitEquip.socketEntity;
            if (itemEntity == Entity.Null || !entityManager.Exists(itemEntity))
                continue;

            LocalTransform unitTransform = entityManager.GetComponentData<LocalTransform>(unitEntity);

            float3 worldPos = unitTransform.Position + new float3(0f, 1f, 0f);
            if (socketEntity != Entity.Null && entityManager.HasComponent<LocalToWorld>(socketEntity))
                worldPos = entityManager.GetComponentData<LocalToWorld>(socketEntity).Position;

            entityManager.SetComponentData(itemEntity, LocalTransform.FromPosition(worldPos));
            ecb.RemoveComponent<Parent>(itemEntity);
            ecb.SetComponentEnabled<PlayerInteractable>(itemEntity, true);

            // Keep socketEntity so the unit can pick up again later
            entityManager.SetComponentData(unitEntity, new UnitEquip
            {
                equipItemEntity = Entity.Null,
                socketEntity = socketEntity
            });
            entityManager.SetComponentData(itemEntity, new EquipBy { owner = Entity.Null });
            entityManager.SetComponentData(itemEntity, new AttachedTo { socket = Entity.Null });
            if (socketEntity != Entity.Null && entityManager.HasComponent<EquipSocket>(socketEntity))
                entityManager.SetComponentData(socketEntity, new EquipSocket { attachedItem = Entity.Null });

            float3 aimDirection = unitTransform.Forward();
            Entity targetEntity = pendingThrow.target;
            if (targetEntity != Entity.Null && entityManager.Exists(targetEntity)
                && entityManager.HasComponent<LocalTransform>(targetEntity))
            {
                aimDirection = entityManager.GetComponentData<LocalTransform>(targetEntity).Position - worldPos;
            }
            float2 planarDirection = math.normalizesafe(aimDirection.xz, unitTransform.Forward().xz);

            Item itemComponent = entityManager.GetComponentData<Item>(itemEntity);
            ref ItemBlob itemBlob = ref itemLibrary.Value.items[(int)itemComponent.itemType];

            // ThrownItem owns X/Z only; Gravity owns Y
            entityManager.SetComponentData(itemEntity, new ThrownItemRequest
            {
                velocity = new float3(planarDirection.x, 0f, planarDirection.y) * itemBlob.throwSpeed,
                thrower = unitEntity,
                throwOrigin = worldPos
            });
            ecb.SetComponentEnabled<ThrownItemRequest>(itemEntity, true);

            if (entityManager.HasComponent<Gravity>(itemEntity))
            {
                Gravity gravity = entityManager.GetComponentData<Gravity>(itemEntity);
                gravity.verticalVelocity = itemBlob.throwArc;
                gravity.isGrounded = false;
                entityManager.SetComponentData(itemEntity, gravity);
            }
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
        pendingThrows.Dispose();
    }
}
