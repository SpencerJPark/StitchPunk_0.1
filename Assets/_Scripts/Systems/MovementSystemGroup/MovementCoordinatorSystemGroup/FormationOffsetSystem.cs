using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Computes per-member formation offsets based on each Horde's FormationType and
// writes them to HordeMembership.formationOffset. Also updates FlowFieldFollower.targetPosition
// so each member steers toward its personal formation slot rather than the shared horde target.
//
// Runs before PathfindingCoordinatorSystem so offsets are fresh when the follower system runs.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateBefore(typeof(PathfindingCoordinatorSystem))]
public partial struct FormationOffsetSystem : ISystem
{
    private ComponentLookup<HordeMembership>   membershipLookup;
    private ComponentLookup<FlowFieldFollower> followerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        membershipLookup = state.GetComponentLookup<HordeMembership>(false);
        followerLookup   = state.GetComponentLookup<FlowFieldFollower>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        membershipLookup.Update(ref state);
        followerLookup.Update(ref state);

        foreach (var (horde, memberBuffer) in
            SystemAPI.Query<RefRO<Horde>, DynamicBuffer<HordeMemberBuffer>>())
        {
            int count = memberBuffer.Length;
            if (count == 0) continue;

            FormationType formation = horde.ValueRO.formationType;
            float3        target    = horde.ValueRO.targetPosition;

            for (int i = 0; i < count; i++)
            {
                Entity member = memberBuffer[i].memberEntity;
                if (!membershipLookup.HasComponent(member)) continue;

                float2 offset = ComputeOffset(formation, i, count);

                HordeMembership membership = membershipLookup[member];
                membership.formationOffset = offset;
                membershipLookup[member] = membership;

                // Shift the FlowFieldFollower's target to the member's personal slot.
                // Line-of-sight and arrival detection in FlowFieldFollowerSystem will
                // steer the unit toward its slot position rather than the shared center.
                if (followerLookup.HasComponent(member)
                    && followerLookup.IsComponentEnabled(member))
                {
                    FlowFieldFollower follower = followerLookup[member];
                    follower.targetPosition = target + new float3(offset.x, 0f, offset.y);
                    followerLookup[member] = follower;
                }
            }
        }
    }

    // Returns a 2D (XZ) offset for member index i out of n total members.
    private static float2 ComputeOffset(FormationType formation, int i, int n)
    {
        switch (formation)
        {
            case FormationType.Line:
            {
                // Spread evenly along X, centered on 0.
                float spacing = 1.2f;
                float totalWidth = (n - 1) * spacing;
                float x = i * spacing - totalWidth * 0.5f;
                return new float2(x, 0f);
            }

            case FormationType.Square:
            {
                int cols = (int)math.ceil(math.sqrt(n));
                int col  = i % cols;
                int row  = i / cols;
                float spacing = 1.2f;
                int totalCols = cols;
                int totalRows = (int)math.ceil((float)n / cols);
                float x = col * spacing - (totalCols - 1) * spacing * 0.5f;
                float z = row * spacing - (totalRows - 1) * spacing * 0.5f;
                return new float2(x, z);
            }

            case FormationType.Circle:
            {
                float radius = 0.8f * math.sqrt(n);
                float angle  = i * (2f * math.PI / n);
                return new float2(math.cos(angle) * radius, math.sin(angle) * radius);
            }

            default: // Blob
                return float2.zero;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
