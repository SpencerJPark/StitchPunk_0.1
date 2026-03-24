using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Finds the nearest targetable entity within range of the player each frame.
/// Enables Target on the player when found, disables it when nothing is in range.
/// Runs before OutlineSystem reads Target to drive the outline highlight.
/// </summary>
[UpdateInGroup(typeof(GameManagerSystemGroup))]
[BurstCompile]
public partial struct PlayerTargetingSystem : ISystem
{
    private const float TARGET_RANGE = 5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, target, targetEnabled) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRW<Target>, EnabledRefRW<Target>>()
                .WithAll<Player>()
                .WithPresent<Target>())
        {
            float3 playerPos = transform.ValueRO.Position;
            float rangeSq = TARGET_RANGE * TARGET_RANGE;

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;

            foreach (var (enemyTransform, enemyEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<Health, Alive>()
                    .WithNone<Player, PlayerImmune>()
                    .WithEntityAccess())
            {
                float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
                toEnemy.y = 0f;
                float distSq = math.lengthsq(toEnemy);

                if (distSq > rangeSq) continue;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = enemyEntity;
                }
            }

            if (bestTarget != Entity.Null)
            {
                target.ValueRW = new Target { entity = bestTarget };
                targetEnabled.ValueRW = true;
            }
            else
            {
                targetEnabled.ValueRW = false;
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
