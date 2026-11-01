using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

// Instantiates a unit's StartingWeapon and equips it through the normal pickup path (EquipBy + AttachedTo + PickupRequest
// + AttachItemRequest), exactly as RequestPickup does, so ItemEquipSystemGroup links it next frame (H5-D10).
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateBefore(typeof(SpawnInitCleanupSystem))]
public partial struct StartingWeaponSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StartingWeapon>();
    }

    public void OnUpdate(ref SystemState state)
    {
        NativeList<Entity> unitsToArm = new NativeList<Entity>(8, Allocator.Temp);

        foreach ((RefRO<StartingWeapon> _, EnabledRefRW<StartingWeapon> startingWeaponEnabled, Entity unitEntity) in
            SystemAPI.Query<RefRO<StartingWeapon>, EnabledRefRW<StartingWeapon>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            bool reusedFromPool = SystemAPI.HasComponent<NewlySpawned>(unitEntity)
                && SystemAPI.IsComponentEnabled<NewlySpawned>(unitEntity);
            if (!startingWeaponEnabled.ValueRO && !reusedFromPool) continue;

            startingWeaponEnabled.ValueRW = false;
            unitsToArm.Add(unitEntity);
        }

        EntityManager entityManager = state.EntityManager;
        for (int unitIndex = 0; unitIndex < unitsToArm.Length; unitIndex++)
        {
            Entity unitEntity = unitsToArm[unitIndex];
            Entity weaponPrefab = entityManager.GetComponentData<StartingWeapon>(unitEntity).weaponPrefab;
            if (weaponPrefab == Entity.Null || !entityManager.HasComponent<UnitEquip>(unitEntity)) continue;

            UnitEquip unitEquip = entityManager.GetComponentData<UnitEquip>(unitEntity);
            if (unitEquip.equipItemEntity != Entity.Null && entityManager.Exists(unitEquip.equipItemEntity)) continue;

            Entity weaponEntity = entityManager.Instantiate(weaponPrefab);
            if (entityManager.HasComponent<LocalTransform>(weaponEntity) && entityManager.HasComponent<LocalTransform>(unitEntity))
            {
                LocalTransform weaponTransform = entityManager.GetComponentData<LocalTransform>(weaponEntity);
                weaponTransform.Position = entityManager.GetComponentData<LocalTransform>(unitEntity).Position;
                entityManager.SetComponentData(weaponEntity, weaponTransform);
            }

            if (entityManager.HasComponent<EquipBy>(weaponEntity))
                entityManager.SetComponentData(weaponEntity, new EquipBy { owner = unitEntity });
            if (entityManager.HasComponent<AttachedTo>(weaponEntity))
                entityManager.SetComponentData(weaponEntity, new AttachedTo { socket = unitEquip.socketEntity });
            if (entityManager.HasComponent<PickupRequest>(weaponEntity))
                entityManager.SetComponentEnabled<PickupRequest>(weaponEntity, true);
            if (entityManager.HasComponent<AttachItemRequest>(weaponEntity))
                entityManager.SetComponentEnabled<AttachItemRequest>(weaponEntity, true);
        }

        unitsToArm.Dispose();
    }
}
