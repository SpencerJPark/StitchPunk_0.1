using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(AddInnateActionsSystem))]
public partial struct WaypointQuerySystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Waypoint> waypointLookup;
    private BufferLookup<WaypointAction> waypointActionsLookup;
    private BufferLookup<WaypointOccupant> occupantLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpatialHashSingleton>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        waypointLookup = state.GetComponentLookup<Waypoint>(true);
        waypointActionsLookup = state.GetBufferLookup<WaypointAction>(true);
        occupantLookup = state.GetBufferLookup<WaypointOccupant>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        waypointLookup.Update(ref state);
        waypointActionsLookup.Update(ref state);
        occupantLookup.Update(ref state);

        var spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();

        state.Dependency = new NPCQueryWaypointsJob
        {
            cellSize = SpatialHashSystem.CELL_SIZE,
            waypointCells = spatialHash.waypointCells,
            transformLookup = transformLookup,
            waypointLookup = waypointLookup,
            waypointActionsLookup = waypointActionsLookup,
            occupantLookup = occupantLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct NPCQueryWaypointsJob : IJobEntity
{
    public float cellSize;

    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Waypoint> waypointLookup;
    [ReadOnly] public BufferLookup<WaypointAction> waypointActionsLookup;
    [ReadOnly] public BufferLookup<WaypointOccupant> occupantLookup;

    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        in Needs needs,
        in BrainLink brainLink,
        in ActionLock actionLock,
        Entity entity)
    {
        // Only query when we need to make a decision
        // This is the KEY optimization - don't query every frame
        bool needsDecision = actionLock.lockedAction == ActionType.None || 
                             actionLock.isComplete ||
                             actionLock.decisionTimer <= 0.05f;

        if (!needsDecision)
            return;

        // Get body position
        if (!transformLookup.TryGetComponent(brainLink.body, out LocalTransform bodyTransform))
            return;

        float3 pos = bodyTransform.Position;
        int2 centerCell = GetCell(pos);

        // Query surrounding cells (3x3 grid = up to 30m range with 10m cells)
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                int2 cell = new int2(centerCell.x + dx, centerCell.y + dz);

                if (!waypointCells.TryGetFirstValue(cell, out Entity waypointEntity, out var iterator))
                    continue;

                do
                {
                    ProcessWaypoint(ref options, waypointEntity, pos, needs, entity);
                }
                while (waypointCells.TryGetNextValue(out waypointEntity, ref iterator));
            }
        }
    }

    private void ProcessWaypoint(
        ref DynamicBuffer<ActionOption> options,
        Entity waypointEntity,
        float3 npcPos,
        Needs needs,
        Entity brainEntity)
    {
        if (!waypointLookup.TryGetComponent(waypointEntity, out Waypoint waypoint))
            return;

        if (!transformLookup.TryGetComponent(waypointEntity, out LocalTransform waypointTransform))
            return;

        float3 waypointPos = waypointTransform.Position;
        float distance = math.distance(npcPos, waypointPos);

        if (distance > waypoint.broadcastRadius)
            return;

        // Check occupancy
        if (occupantLookup.TryGetBuffer(waypointEntity, out DynamicBuffer<WaypointOccupant> occupants))
        {
            if (occupants.Length >= waypoint.maxOccupants)
                return;

            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i].brain == brainEntity)
                    return;
            }
        }

        if (!waypointActionsLookup.TryGetBuffer(waypointEntity, out DynamicBuffer<WaypointAction> actions))
            return;

        float3 approachPos = waypoint.approachPoint != Entity.Null &&
            transformLookup.TryGetComponent(waypoint.approachPoint, out LocalTransform approachTransform)
            ? approachTransform.Position
            : waypointPos;

        for (int i = 0; i < actions.Length; i++)
        {
            WaypointAction action = actions[i];
            float score = ScoreAction(action.needModifiers, needs, distance);

            if (score > 0.01f)
            {
                options.Add(new ActionOption
                {
                    waypoint = waypointEntity,
                    actionType = action.actionType,
                    animation = action.animation,
                    duration = action.duration,
                    needModifiers = action.needModifiers,
                    position = approachPos,
                    interactionRange = waypoint.interactionRange,
                    score = score
                });
            }
        }
    }

    private float ScoreAction(NeedModifiers mods, Needs needs, float distance)
    {
        float score = 0f;

        if (mods.hunger > 0) score += (1f - needs.hunger) * mods.hunger * 10f;
        if (mods.energy > 0) score += (1f - needs.energy) * mods.energy * 10f;
        if (mods.entertainment > 0) score += (1f - needs.entertainment) * mods.entertainment * 8f;
        if (mods.social > 0) score += (1f - needs.social) * mods.social * 6f;
        if (mods.comfort > 0) score += (1f - needs.comfort) * mods.comfort * 5f;
        if (mods.bladder > 0) score += (1f - needs.bladder) * mods.bladder * 12f;
        if (mods.safety > 0) score += (1f - needs.safety) * mods.safety * 15f;
        if (mods.movement > 0) score += (1f - needs.movement) * mods.movement * 4f;

        float distancePenalty = 1f / (1f + distance * 0.05f);
        score *= distancePenalty;

        return score;
    }

    private int2 GetCell(float3 pos)
    {
        return new int2(
            (int)math.floor(pos.x / cellSize),
            (int)math.floor(pos.z / cellSize)
        );
    }
}