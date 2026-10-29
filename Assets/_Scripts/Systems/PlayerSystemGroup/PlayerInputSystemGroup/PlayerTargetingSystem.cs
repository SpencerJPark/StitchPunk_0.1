using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Finds the nearest targetable entity within range of the player each frame.
/// Enables Target on the player when found, disables it when nothing is in range.
/// </summary>
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
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
        ComponentLookup<PlayerInteractableReach> reachLookup = SystemAPI.GetComponentLookup<PlayerInteractableReach>(true);

        foreach ((RefRO<LocalTransform> transform, RefRW<Target> target, EnabledRefRW<Target> targetEnabled) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRW<Target>, EnabledRefRW<Target>>()
                .WithAll<Player>()
                .WithPresent<Target>())
        {
            float3 playerPos = transform.ValueRO.Position;
            float rangeSq = TARGET_RANGE * TARGET_RANGE;

            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;

            foreach ((RefRO<LocalTransform> enemyTransform, Entity enemyEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<PlayerInteractable>()
                    .WithEntityAccess())
            {
                float distSq;
                if (reachLookup.TryGetComponent(enemyEntity, out PlayerInteractableReach reach))
                {
                    // Range to the capsule's surface, so a 10 m caravan is reachable from anywhere along its side.
                    float3 segmentStart = enemyTransform.ValueRO.TransformPoint(reach.localSegmentStart);
                    float3 segmentEnd = enemyTransform.ValueRO.TransformPoint(reach.localSegmentEnd);
                    float distanceToSurface = math.max(0f,
                        PlanarDistanceToSegment(playerPos, segmentStart, segmentEnd) - reach.radius);
                    distSq = distanceToSurface * distanceToSurface;
                }
                else
                {
                    float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
                    toEnemy.y = 0f;
                    distSq = math.lengthsq(toEnemy);
                }

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

    private static float PlanarDistanceToSegment(float3 point, float3 segmentStart, float3 segmentEnd)
    {
        float2 segment = segmentEnd.xz - segmentStart.xz;
        float segmentLengthSquared = math.lengthsq(segment);
        float alongSegment = segmentLengthSquared > 0.0001f
            ? math.saturate(math.dot(point.xz - segmentStart.xz, segment) / segmentLengthSquared)
            : 0f;
        return math.distance(point.xz, segmentStart.xz + segment * alongSegment);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
