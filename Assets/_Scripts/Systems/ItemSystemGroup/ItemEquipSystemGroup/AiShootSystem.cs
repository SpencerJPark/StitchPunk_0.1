using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Fires a held gun (SmartHumanAI H5-D6): spawns the item's projectile prefab at the hand, flying at the point WindUp locked.
// Instantiated on the main thread so ProjectileFlightSystem (ThrownItemSystemGroup, later this frame) moves it at once.
[UpdateInGroup(typeof(ItemEquipSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct AiShootSystem : ISystem
{
    public const float PROJECTILE_LIFETIME_RANGE_MULTIPLIER = 1.5f;
    public const float FALLBACK_PROJECTILE_SPEED = 25f;

    private struct PendingShot
    {
        public Entity shooter;
        public Entity target;
        public float3 aimPoint;
        public bool   hasAimPoint;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        NativeList<PendingShot> pendingShots = new NativeList<PendingShot>(8, Allocator.Temp);

        foreach ((RefRO<AiShootRequest> shootRequest,
                  EnabledRefRW<AiShootRequest> aiShootRequestEnabled,
                  Entity unitEntity) in
            SystemAPI.Query<RefRO<AiShootRequest>, EnabledRefRW<AiShootRequest>>().WithEntityAccess())
        {
            aiShootRequestEnabled.ValueRW = false;
            pendingShots.Add(new PendingShot
            {
                shooter     = unitEntity,
                target      = shootRequest.ValueRO.target,
                aimPoint    = shootRequest.ValueRO.aimPoint,
                hasAimPoint = shootRequest.ValueRO.hasAimPoint,
            });
        }

        if (pendingShots.Length == 0)
        {
            pendingShots.Dispose();
            return;
        }

        EntityManager entityManager = state.EntityManager;
        BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.GetSingleton<ItemLibrary>().library;
        double elapsedSeconds = SystemAPI.Time.ElapsedTime;

        // Copied out first: Instantiate is a structural change and would invalidate a live buffer handle.
        Entity itemLibraryEntity = SystemAPI.GetSingletonEntity<ItemLibrary>();
        NativeArray<ItemProjectilePrefab> projectilePrefabs = entityManager.HasBuffer<ItemProjectilePrefab>(itemLibraryEntity)
            ? entityManager.GetBuffer<ItemProjectilePrefab>(itemLibraryEntity, true).ToNativeArray(Allocator.Temp)
            : new NativeArray<ItemProjectilePrefab>(0, Allocator.Temp);

        for (int shotIndex = 0; shotIndex < pendingShots.Length; shotIndex++)
        {
            PendingShot pendingShot = pendingShots[shotIndex];
            Entity unitEntity = pendingShot.shooter;
            if (!entityManager.HasComponent<UnitEquip>(unitEntity)) continue;

            UnitEquip unitEquip = entityManager.GetComponentData<UnitEquip>(unitEntity);
            Entity gunEntity = unitEquip.equipItemEntity;
            if (gunEntity == Entity.Null || !entityManager.Exists(gunEntity) || !entityManager.HasComponent<Item>(gunEntity)) continue;

            int itemIndex = (int)entityManager.GetComponentData<Item>(gunEntity).itemType;
            if (itemIndex < 0 || itemIndex >= itemLibrary.Value.items.Length || itemIndex >= projectilePrefabs.Length) continue;

            ref ItemBlob gunBlob = ref itemLibrary.Value.items[itemIndex];
            Entity projectilePrefab = projectilePrefabs[itemIndex].prefab;
            if (gunBlob.rangedKind != RangedKind.Shoot || projectilePrefab == Entity.Null) continue;

            LocalTransform unitTransform = entityManager.GetComponentData<LocalTransform>(unitEntity);
            float3 muzzlePosition = unitTransform.Position + new float3(0f, 1f, 0f);
            Entity socketEntity = unitEquip.socketEntity;
            if (socketEntity != Entity.Null && entityManager.HasComponent<LocalToWorld>(socketEntity))
                muzzlePosition = entityManager.GetComponentData<LocalToWorld>(socketEntity).Position;

            float3 aimPosition = muzzlePosition + unitTransform.Forward();
            if (pendingShot.hasAimPoint)
            {
                aimPosition = pendingShot.aimPoint;
            }
            else if (pendingShot.target != Entity.Null && entityManager.Exists(pendingShot.target)
                     && entityManager.HasComponent<LocalTransform>(pendingShot.target))
            {
                aimPosition = entityManager.GetComponentData<LocalTransform>(pendingShot.target).Position;
            }

            float2 planarDirection = math.normalizesafe((aimPosition - muzzlePosition).xz, unitTransform.Forward().xz);
            float3 flightDirection = new float3(planarDirection.x, 0f, planarDirection.y);
            float  projectileSpeed = gunBlob.projectileSpeed > 0f ? gunBlob.projectileSpeed : FALLBACK_PROJECTILE_SPEED;

            Entity projectileEntity = entityManager.Instantiate(projectilePrefab);
            entityManager.SetComponentData(projectileEntity,
                LocalTransform.FromPositionRotation(muzzlePosition, quaternion.LookRotationSafe(flightDirection, math.up())));

            Projectile projectile = entityManager.GetComponentData<Projectile>(projectileEntity);
            projectile.velocity     = flightDirection * projectileSpeed;
            projectile.shooter      = unitEntity;
            projectile.origin       = muzzlePosition;
            projectile.damageSource = gunBlob.weaponAttack;
            entityManager.SetComponentData(projectileEntity, projectile);

            if (entityManager.HasComponent<Lifetime>(projectileEntity))
            {
                float maximumRange = math.max(gunBlob.aiRangedMaxRange, 1f);
                entityManager.SetComponentData(projectileEntity, new Lifetime
                {
                    secondsRemaining = PROJECTILE_LIFETIME_RANGE_MULTIPLIER * maximumRange / projectileSpeed,
                });
                entityManager.SetComponentEnabled<Lifetime>(projectileEntity, true);
            }

            if (entityManager.HasComponent<TacticsCooldown>(unitEntity))
            {
                TacticsCooldown tacticsCooldown = entityManager.GetComponentData<TacticsCooldown>(unitEntity);
                tacticsCooldown.rangedReadyAtSeconds = elapsedSeconds + gunBlob.rangedCooldownSeconds;
                entityManager.SetComponentData(unitEntity, tacticsCooldown);
            }
        }

        projectilePrefabs.Dispose();
        pendingShots.Dispose();
    }
}
