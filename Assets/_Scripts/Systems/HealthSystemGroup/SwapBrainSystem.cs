using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

// Processes SwapBrainRequest on body entities. Runs after ReviveSystem so the unit
// is already alive/undead before the new brain is linked.
//
// Flow:
//   1. PlayerReviverSystem enables SwapBrainRequest on the body
//   2. ReviveSystem flips Alive/Undead/Dead
//   3. This system destroys the old brain (if known via BrainLink), instantiates
//      the zombie brain prefab, cross-links body <-> new brain, enables Minion.
//
// BrainLink is NOT required in the query — scene-placed citizens may lack it if
// BrainLinkAuthoring is absent. In that case the old brain is left to clean itself
// up (it will have no valid BodyLink) and only the new brain + link are written.
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(ReviveSystem))]
public partial struct SwapBrainSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
        DynamicBuffer<UnitPrefabEntry> prefabs = SystemAPI.GetBuffer<UnitPrefabEntry>(libraryEntity);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (unitData, transform, bodyEntity) in
            SystemAPI.Query<
                RefRO<UnitData>,
                RefRO<LocalTransform>>()
            .WithAll<SwapBrainRequest>()
            .WithEntityAccess())
        {
            UnitType zombieType = GetZombieTypeFor(unitData.ValueRO.unitType);
            Entity zombieBrainPrefab = GetBrainPrefabForType(prefabs, zombieType);
            if (zombieBrainPrefab == Entity.Null) continue;

            // Destroy the old brain if the body has a BrainLink to it.
            if (SystemAPI.HasComponent<BrainLink>(bodyEntity))
            {
                Entity oldBrain = SystemAPI.GetComponent<BrainLink>(bodyEntity).brain;
                if (oldBrain != Entity.Null)
                    ecb.DestroyEntity(oldBrain);
            }

            // Instantiate and position the new zombie brain.
            Entity newBrain = ecb.Instantiate(zombieBrainPrefab);
            ecb.SetComponent(newBrain, LocalTransform.FromPosition(transform.ValueRO.Position));

            // Fix up BodyLink on the new brain to point to this body.
            ecb.SetComponent(newBrain, new BodyLink { body = bodyEntity });

            // Update or add BrainLink on the body to point to the new brain.
            if (SystemAPI.HasComponent<BrainLink>(bodyEntity))
                ecb.SetComponent(bodyEntity, new BrainLink { brain = newBrain });
            else
                ecb.AddComponent(bodyEntity, new BrainLink { brain = newBrain });

            // Tag the new brain for pool management.
            ecb.AddComponent(newBrain, new PoolOwner { unitType = zombieType });

            // Zombies start idle — activated by player orders or MinionAutoCounterSystem.
            ecb.SetComponentEnabled<NeedsAction>(newBrain, false);

            // Mark the body as a player minion.
            ecb.SetComponentEnabled<Minion>(bodyEntity, true);

            // Clear movement state so the body idles in place rather than
            // continuing toward the citizen's last interaction destination.
            ecb.SetComponentEnabled<PathRequest>(bodyEntity, false);
            ecb.SetComponentEnabled<DStarLiteFollower>(bodyEntity, false);
            ecb.SetComponentEnabled<FlowFieldFollower>(bodyEntity, false);
            ecb.SetComponent(bodyEntity, new Movement
            {
                moveSpeed   = SystemAPI.GetComponent<Movement>(bodyEntity).moveSpeed,
                rotationSpeed = SystemAPI.GetComponent<Movement>(bodyEntity).rotationSpeed,
                targetPosition = transform.ValueRO.Position,
                isMoving    = false,
            });

            // Disable the request so it doesn't fire again next frame.
            ecb.SetComponentEnabled<SwapBrainRequest>(bodyEntity, false);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private static UnitType GetZombieTypeFor(UnitType citizenType)
    {
        return citizenType == UnitType.FemaleCitizen ? UnitType.FemaleZombie : UnitType.MaleZombie;
    }

    private static Entity GetBrainPrefabForType(DynamicBuffer<UnitPrefabEntry> prefabs, UnitType type)
    {
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].unitType == type) return prefabs[i].brainPrefab;
        return Entity.Null;
    }
}
