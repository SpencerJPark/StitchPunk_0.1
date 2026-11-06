#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public sealed class UnitInspectorReader
{
    private const int TrailCapacity = 40;
    private const float TrailSampleIntervalSeconds = 0.25f;
    private const int MaxOptionRows = 12;

    private readonly Vector3[] trailRing = new Vector3[TrailCapacity];
    private int trailCount;
    private int trailNextWriteIndex;
    private float lastTrailSampleSeconds = float.NegativeInfinity;
    private Entity trailEntity = Entity.Null;

    private readonly List<UnitInspectorOptionRow> optionRowPool = new List<UnitInspectorOptionRow>();
    private Entity lastOptionsUnit = Entity.Null;
    private int lastOptionCount;
    private float lastOptionsRealtimeSeconds;
    private readonly List<UnitInspectorMotivationRow> motivationRowPool = new List<UnitInspectorMotivationRow>();
    private readonly List<UnitInspectorPersonalityRow> personalityRowPool = new List<UnitInspectorPersonalityRow>();
    private readonly List<UnitInspectorHistoryRow> historyRowPool = new List<UnitInspectorHistoryRow>();

    public void Fill(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot, float realtimeSeconds)
    {
        snapshot.Clear();
        if (unit == Entity.Null || !entityManager.Exists(unit) || !entityManager.HasComponent<LocalTransform>(unit))
        {
            snapshot.hasUnit = false;
            return;
        }

        snapshot.hasUnit = true;
        float3 unitPosition = entityManager.GetComponentData<LocalTransform>(unit).Position;
        snapshot.position = unitPosition;

        FillIdentity(entityManager, unit, snapshot);
        FillStateMachine(entityManager, unit, snapshot);
        FillOptions(entityManager, unit, snapshot, realtimeSeconds);
        FillMotivationsAndPersonality(entityManager, unit, snapshot);
        FillNavigation(entityManager, unit, snapshot, unitPosition);
        FillHistory(entityManager, unit, snapshot);
        UpdateTrail(unit, snapshot, unitPosition, realtimeSeconds);
    }

    private static void FillIdentity(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot)
    {
        if (entityManager.HasComponent<UnitData>(unit))
        {
            snapshot.unitType = entityManager.GetComponentData<UnitData>(unit).unitType;
        }
        snapshot.unitLabel = snapshot.unitType + " (entity " + unit.Index + ":" + unit.Version + ")";

        if (entityManager.HasComponent<Faction>(unit))
        {
            snapshot.faction = entityManager.GetComponentData<Faction>(unit).factionType;
        }
        if (entityManager.HasComponent<Health>(unit))
        {
            Health health = entityManager.GetComponentData<Health>(unit);
            snapshot.health = health.healthAmount;
            snapshot.healthMax = health.healthAmountMax;
        }
        snapshot.isDead = entityManager.HasComponent<Dead>(unit) && entityManager.IsComponentEnabled<Dead>(unit);
        if (entityManager.HasComponent<Awareness>(unit))
        {
            snapshot.awarenessRange = entityManager.GetComponentData<Awareness>(unit).range;
        }
    }

    private static void FillStateMachine(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot)
    {
        if (!entityManager.HasComponent<StateMachine>(unit))
        {
            return;
        }
        StateMachine stateMachine = entityManager.GetComponentData<StateMachine>(unit);
        snapshot.action = stateMachine.action;
        snapshot.behavior = stateMachine.activeBehavior;
        snapshot.phase = stateMachine.currentPhase;
        snapshot.commandIndex = stateMachine.CurrentCommandIndex;
        snapshot.activePriority = stateMachine.activePriority;

        if (stateMachine.targetEntity != Entity.Null)
        {
            snapshot.hasTargetEntity = true;
            snapshot.targetLabel = BuildEntityLabel(entityManager, stateMachine.targetEntity);
            if (entityManager.Exists(stateMachine.targetEntity) && entityManager.HasComponent<LocalTransform>(stateMachine.targetEntity))
            {
                snapshot.targetEntityPosition = entityManager.GetComponentData<LocalTransform>(stateMachine.targetEntity).Position;
            }
        }
        if (stateMachine.hasTargetPosition)
        {
            snapshot.hasTargetPosition = true;
            snapshot.targetPosition = stateMachine.targetPosition;
            if (snapshot.targetLabel == null)
            {
                snapshot.targetLabel = BuildPositionLabel(stateMachine.targetPosition);
            }
        }
    }

    private void FillOptions(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot, float realtimeSeconds)
    {
        if (!entityManager.HasBuffer<UtilityActions>(unit))
        {
            return;
        }
        DynamicBuffer<UtilityActions> rows = entityManager.GetBuffer<UtilityActions>(unit, true);
        int rowCount = rows.Length;
        if (lastOptionsUnit != unit)
        {
            lastOptionsUnit = unit;
            lastOptionCount = 0;
        }
        // Options are scored only when the unit thinks (about one frame in six), so between thinks keep showing the last table.
        if (rowCount == 0)
        {
            for (int cachedIndex = 0; cachedIndex < lastOptionCount; cachedIndex++)
            {
                snapshot.options.Add(optionRowPool[cachedIndex]);
            }
            snapshot.optionsAgeSeconds = lastOptionCount > 0 ? realtimeSeconds - lastOptionsRealtimeSeconds : 0f;
            return;
        }
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            UtilityActions source = rows[rowIndex];
            UnitInspectorOptionRow row = GetPooledRow(optionRowPool, rowIndex);
            row.action = source.actionType;
            row.priority = source.priority;
            row.totalUtility = source.totalUtility;
            row.isPlayerOrdered = source.isPlayerOrdered;
            row.isWinner = false;
            row.targetLabel = source.targetEntity != Entity.Null
                ? BuildEntityLabel(entityManager, source.targetEntity)
                : source.hasTargetPosition ? BuildPositionLabel(source.targetPosition) : null;
        }

        int selectedCount = math.min(rowCount, MaxOptionRows);
        bool winnerAssigned = false;
        for (int selectedIndex = 0; selectedIndex < selectedCount; selectedIndex++)
        {
            int bestIndex = selectedIndex;
            for (int candidateIndex = selectedIndex + 1; candidateIndex < rowCount; candidateIndex++)
            {
                if (optionRowPool[candidateIndex].totalUtility > optionRowPool[bestIndex].totalUtility)
                {
                    bestIndex = candidateIndex;
                }
            }
            UnitInspectorOptionRow best = optionRowPool[bestIndex];
            optionRowPool[bestIndex] = optionRowPool[selectedIndex];
            optionRowPool[selectedIndex] = best;

            // Rows sort by utility descending, so the first row of the active action is its highest.
            if (!winnerAssigned && best.action == snapshot.action)
            {
                best.isWinner = true;
                winnerAssigned = true;
            }
            snapshot.options.Add(best);
        }
        lastOptionCount = selectedCount;
        lastOptionsRealtimeSeconds = realtimeSeconds;
    }

    private void FillMotivationsAndPersonality(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot)
    {
        if (entityManager.HasBuffer<Motivation>(unit))
        {
            DynamicBuffer<Motivation> motivations = entityManager.GetBuffer<Motivation>(unit, true);
            for (int index = 0; index < motivations.Length; index++)
            {
                UnitInspectorMotivationRow row = GetPooledRow(motivationRowPool, index);
                row.need = motivations[index].needType;
                row.value = motivations[index].value;
                row.decayRate = motivations[index].decayRate;
                snapshot.motivations.Add(row);
            }
        }
        if (entityManager.HasBuffer<PersonalityAttributes>(unit))
        {
            DynamicBuffer<PersonalityAttributes> traits = entityManager.GetBuffer<PersonalityAttributes>(unit, true);
            for (int index = 0; index < traits.Length; index++)
            {
                UnitInspectorPersonalityRow row = GetPooledRow(personalityRowPool, index);
                row.personality = traits[index].personality;
                row.value = traits[index].value;
                snapshot.personality.Add(row);
            }
        }
    }

    private static void FillNavigation(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot, float3 unitPosition)
    {
        snapshot.navState = UnitInspectorNavState.Idle;
        bool followerEnabled = false;
        bool hasAgent = entityManager.HasComponent<PathfindingAgent>(unit);
        float3 agentTarget = default;
        if (hasAgent)
        {
            PathfindingAgent agent = entityManager.GetComponentData<PathfindingAgent>(unit);
            agentTarget = agent.targetPosition;
            snapshot.stoppingDistance = agent.stoppingDistance;
            snapshot.navModeLabel = NavModeLabel(agent.currentMode);
        }

        bool aStarEnabled = entityManager.HasComponent<AStarFollower>(unit) && entityManager.IsComponentEnabled<AStarFollower>(unit);
        bool flowEnabled = entityManager.HasComponent<FlowFieldFollower>(unit) && entityManager.IsComponentEnabled<FlowFieldFollower>(unit);
        bool dStarEnabled = entityManager.HasComponent<DStarLiteFollower>(unit) && entityManager.IsComponentEnabled<DStarLiteFollower>(unit);
        followerEnabled = aStarEnabled || flowEnabled || dStarEnabled;

        snapshot.pathPoints.Add(unitPosition);
        if (aStarEnabled && entityManager.HasBuffer<AStarPathWaypoint>(unit))
        {
            DynamicBuffer<AStarPathWaypoint> waypoints = entityManager.GetBuffer<AStarPathWaypoint>(unit, true);
            for (int index = 0; index < waypoints.Length; index++)
            {
                snapshot.pathPoints.Add(waypoints[index].position);
            }
            int followerIndex = entityManager.GetComponentData<AStarFollower>(unit).currentWaypointIndex;
            snapshot.currentWaypointIndex = math.clamp(followerIndex + 1, 0, snapshot.pathPoints.Count - 1);
        }
        else if ((flowEnabled || dStarEnabled) && hasAgent)
        {
            snapshot.pathPoints.Add(agentTarget);
            snapshot.currentWaypointIndex = 1;
        }

        if (snapshot.pathPoints.Count > 1)
        {
            snapshot.distanceToGoal = Vector3.Distance(unitPosition, snapshot.pathPoints[snapshot.pathPoints.Count - 1]);
        }
        else if (snapshot.hasTargetEntity)
        {
            snapshot.distanceToGoal = Vector3.Distance(unitPosition, snapshot.targetEntityPosition);
        }
        else if (snapshot.hasTargetPosition)
        {
            snapshot.distanceToGoal = Vector3.Distance(unitPosition, snapshot.targetPosition);
        }

        bool isAbstract = entityManager.HasComponent<AbstractSimulation>(unit) && entityManager.IsComponentEnabled<AbstractSimulation>(unit);
        if (isAbstract)
        {
            snapshot.navState = UnitInspectorNavState.Abstract;
            if (entityManager.HasBuffer<AbstractRouteWaypoint>(unit))
            {
                DynamicBuffer<AbstractRouteWaypoint> route = entityManager.GetBuffer<AbstractRouteWaypoint>(unit, true);
                if (route.Length > 0)
                {
                    snapshot.hasAbstractRouteEnd = true;
                    snapshot.abstractRouteEnd = route[route.Length - 1].position;
                }
            }
        }
        else if (entityManager.HasComponent<MovementStuck>(unit) && entityManager.IsComponentEnabled<MovementStuck>(unit))
        {
            snapshot.navState = UnitInspectorNavState.Stuck;
        }
        else if (entityManager.HasComponent<PathRequest>(unit) && entityManager.IsComponentEnabled<PathRequest>(unit))
        {
            snapshot.navState = UnitInspectorNavState.WaitingForPath;
        }
        else if (followerEnabled)
        {
            snapshot.navState = UnitInspectorNavState.Walking;
        }
    }

    private void FillHistory(EntityManager entityManager, Entity unit, UnitInspectorSnapshot snapshot)
    {
        if (!entityManager.HasBuffer<DecisionHistoryEntry>(unit))
        {
            return;
        }
        DynamicBuffer<DecisionHistoryEntry> entries = entityManager.GetBuffer<DecisionHistoryEntry>(unit, true);
        double nowSeconds = entityManager.World.Time.ElapsedTime;
        for (int newestFirstIndex = 0; newestFirstIndex < entries.Length; newestFirstIndex++)
        {
            DecisionHistoryEntry entry = entries[entries.Length - 1 - newestFirstIndex];
            UnitInspectorHistoryRow row = GetPooledRow(historyRowPool, newestFirstIndex);
            row.secondsAgo = (float)(nowSeconds - entry.worldSeconds);
            row.fromAction = entry.fromAction;
            row.toAction = entry.toAction;
            row.toBehavior = entry.toBehavior;
            row.priority = entry.priority;
            row.winningUtility = entry.winningUtility;
            row.wasPlayerOrdered = entry.wasPlayerOrdered;
            row.cause = entry.cause;
            snapshot.history.Add(row);
        }
    }

    private void UpdateTrail(Entity unit, UnitInspectorSnapshot snapshot, float3 unitPosition, float realtimeSeconds)
    {
        if (unit != trailEntity)
        {
            trailEntity = unit;
            trailCount = 0;
            trailNextWriteIndex = 0;
            lastTrailSampleSeconds = float.NegativeInfinity;
        }
        if (realtimeSeconds - lastTrailSampleSeconds >= TrailSampleIntervalSeconds)
        {
            lastTrailSampleSeconds = realtimeSeconds;
            trailRing[trailNextWriteIndex] = unitPosition;
            trailNextWriteIndex = (trailNextWriteIndex + 1) % TrailCapacity;
            trailCount = math.min(trailCount + 1, TrailCapacity);
        }
        int oldestIndex = (trailNextWriteIndex - trailCount + TrailCapacity) % TrailCapacity;
        for (int offset = 0; offset < trailCount; offset++)
        {
            snapshot.trailPoints.Add(trailRing[(oldestIndex + offset) % TrailCapacity]);
        }
    }

    private static TRow GetPooledRow<TRow>(List<TRow> pool, int index) where TRow : class, new()
    {
        while (pool.Count <= index)
        {
            pool.Add(new TRow());
        }
        return pool[index];
    }

    private static string BuildEntityLabel(EntityManager entityManager, Entity target)
    {
        if (entityManager.Exists(target) && entityManager.HasComponent<UnitData>(target))
        {
            return entityManager.GetComponentData<UnitData>(target).unitType.ToString();
        }
        return "entity " + target.Index + ":" + target.Version;
    }

    private static string BuildPositionLabel(float3 position)
    {
        return "pos (" + position.x.ToString("F1") + ", " + position.z.ToString("F1") + ")";
    }

    private static string NavModeLabel(PathfindingMode mode)
    {
        switch (mode)
        {
            case PathfindingMode.AStar: return "A*";
            case PathfindingMode.FlowField: return "FlowField";
            case PathfindingMode.DStarLite: return "D* Lite";
            default: return "Stop";
        }
    }
}
#endif
