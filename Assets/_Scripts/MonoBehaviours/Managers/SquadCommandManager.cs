using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Owns squad switching, number-key assignment and the radial's quick commands (CampLoop Phase 3).
public class SquadCommandManager : MonoBehaviour, IUpdateObserver
{
    public static SquadCommandManager Instance { get; private set; }

    private const float DefaultOnFootCommandRadius = 15f;
    private const float HoldDefendRadius = 5f;
    private const float GatherSearchRadius = 20f;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private EntityQuery squadMinionQuery;
    private EntityQuery selectedMinionQuery;

    #region Unity Lifecycle

    private void Awake()
    {
        Instance = this;
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        squadMinionQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion, SquadMember, LocalTransform>()
            .WithPresent<Selected>()
            .Build(entityManager);
        selectedMinionQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, Minion>()
            .Build(entityManager);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    #endregion

    #region Player State

    private bool TryGetPlayer(out Entity playerEntity)
    {
        playerEntity = Entity.Null;
        if (playerQuery.IsEmpty) return false;
        playerEntity = playerQuery.GetSingletonEntity();
        return true;
    }

    public int ActiveSquadIndex
    {
        get
        {
            if (!TryGetPlayer(out Entity playerEntity)) return 0;
            if (!entityManager.HasComponent<PlayerMinionGroupsData>(playerEntity)) return 0;
            return entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity).assignmentGroupIndex;
        }
    }

    public int UnlockedSquadCount
    {
        get
        {
            if (!TryGetPlayer(out Entity playerEntity)) return 0;
            if (!entityManager.HasComponent<PlayerMinionGroupsData>(playerEntity)) return 0;
            return entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity).unlockedGroupCount;
        }
    }

    public bool IsCommanderMounted
    {
        get
        {
            if (!TryGetPlayer(out Entity playerEntity)) return false;
            return entityManager.HasComponent<WorkstationMounted>(playerEntity)
                && entityManager.IsComponentEnabled<WorkstationMounted>(playerEntity);
        }
    }

    public float OnFootCommandRadius
    {
        get
        {
            if (!TryGetPlayer(out Entity playerEntity)) return DefaultOnFootCommandRadius;
            if (!entityManager.HasComponent<PlayerSquadSettings>(playerEntity)) return DefaultOnFootCommandRadius;
            return entityManager.GetComponentData<PlayerSquadSettings>(playerEntity).onFootCommandRadius;
        }
    }

    public Vector3 CommanderPosition
    {
        get
        {
            if (!TryGetPlayer(out Entity playerEntity)) return Vector3.zero;
            return entityManager.GetComponentData<LocalTransform>(playerEntity).Position;
        }
    }

    private void SetActiveSquad(Entity playerEntity, int squadIndex)
    {
        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);
        groupData.assignmentGroupIndex = squadIndex;
        entityManager.SetComponentData(playerEntity, groupData);
    }

    #endregion

    #region Input

    public void ObservedUpdate()
    {
        if (!TryGetPlayer(out Entity playerEntity)) return;

        if (entityManager.IsComponentEnabled<OnQuickCommandInput>(playerEntity))
        {
            int squadNumber = entityManager.GetComponentData<OnQuickCommandInput>(playerEntity).commandIndex;
            entityManager.SetComponentEnabled<OnQuickCommandInput>(playerEntity, false);
            HandleNumberKey(squadNumber);
        }

        if (entityManager.IsComponentEnabled<OnCycleGroupInput>(playerEntity))
        {
            int delta = entityManager.GetComponentData<OnCycleGroupInput>(playerEntity).delta;
            entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, false);
            HandleCycle(delta);
        }
    }

    private void HandleNumberKey(int squadNumber)
    {
        int unlocked = UnlockedSquadCount;
        bool controlHeld = Keyboard.current != null
            && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
        bool hasSelection = !selectedMinionQuery.IsEmpty;

        SquadKeyAction action = SquadRules.ResolveNumberKey(squadNumber, unlocked, hasSelection, controlHeld);
        if (action == SquadKeyAction.SwitchToSquad) SwitchToSquad(squadNumber);
        else if (action == SquadKeyAction.AssignSelectionToSquad) AssignSelectionToSquad(squadNumber);
        else Debug.Log("[Squad] Squad " + squadNumber + " is locked");
    }

    private void HandleCycle(int delta)
    {
        int unlocked = UnlockedSquadCount;
        int active = ActiveSquadIndex;
        if (delta > 0)
        {
            SwitchToSquad(SquadRules.NextSquadIndex(active, unlocked));
        }
        else if (delta < 0 && unlocked >= 1)
        {
            int previous = active <= 1 || active > unlocked ? unlocked : active - 1;
            SwitchToSquad(previous);
        }
    }

    #endregion

    #region Squad Actions

    public void SwitchToSquad(int squadIndex)
    {
        // Squad 0 means "unassigned"; switching to it would select every loose minion.
        if (squadIndex < 1) return;
        if (!TryGetPlayer(out Entity playerEntity)) return;
        SetActiveSquad(playerEntity, squadIndex);

        NativeArray<Entity> minions = squadMinionQuery.ToEntityArray(Allocator.Temp);
        NativeArray<SquadMember> members = squadMinionQuery.ToComponentDataArray<SquadMember>(Allocator.Temp);
        for (int minionIndex = 0; minionIndex < minions.Length; minionIndex++)
        {
            if (!entityManager.HasComponent<Selected>(minions[minionIndex])) continue;
            entityManager.SetComponentEnabled<Selected>(minions[minionIndex], false);
        }
        for (int minionIndex = 0; minionIndex < minions.Length; minionIndex++)
        {
            if (members[minionIndex].squadIndex != squadIndex) continue;
            if (!entityManager.HasComponent<Selected>(minions[minionIndex])) continue;
            entityManager.SetComponentEnabled<Selected>(minions[minionIndex], true);
        }
        minions.Dispose();
        members.Dispose();
    }

    public void AssignSelectionToSquad(int squadIndex)
    {
        if (!TryGetPlayer(out Entity playerEntity)) return;

        NativeArray<Entity> selected = selectedMinionQuery.ToEntityArray(Allocator.Temp);
        for (int selectedIndex = 0; selectedIndex < selected.Length; selectedIndex++)
        {
            if (!entityManager.HasComponent<SquadMember>(selected[selectedIndex])) continue;
            entityManager.SetComponentData(selected[selectedIndex], new SquadMember { squadIndex = squadIndex });
        }
        selected.Dispose();

        SetActiveSquad(playerEntity, squadIndex);
    }

    public void IssueQuickCommand(QuickCommandType command)
    {
        if (command == QuickCommandType.None) return;
        if (!TryGetPlayer(out Entity playerEntity)) return;

        int activeSquad = ActiveSquadIndex;
        if (activeSquad == 0)
        {
            Debug.Log("[Squad] No active squad to command");
            return;
        }

        Entity targetEntity = Entity.Null;
        if (command == QuickCommandType.AttackTarget)
        {
            bool hasTarget = entityManager.HasComponent<CombatTarget>(playerEntity)
                && entityManager.IsComponentEnabled<CombatTarget>(playerEntity);
            if (hasTarget)
            {
                targetEntity = entityManager.GetComponentData<CombatTarget>(playerEntity).entity;
                if (!entityManager.Exists(targetEntity)) targetEntity = Entity.Null;
            }
            if (targetEntity == Entity.Null)
            {
                Debug.Log("[Squad] No target in range");
                return;
            }
        }

        if (command == QuickCommandType.Gather)
        {
            targetEntity = FindNearestGatherTarget(playerEntity);
            if (targetEntity == Entity.Null)
            {
                Debug.Log("No resource within reach.");
                return;
            }
            if (!PlayerHasDeployedWorkstation(entityManager, playerEntity))
            {
                Debug.Log("Deploy your workstation first \u2014 gatherers need somewhere to deliver.");
                return;
            }
        }

        bool commanderMounted = IsCommanderMounted;
        float3 commanderPosition = CommanderPosition;
        float onFootRadius = OnFootCommandRadius;

        NativeArray<Entity> minions = squadMinionQuery.ToEntityArray(Allocator.Temp);
        NativeArray<SquadMember> members = squadMinionQuery.ToComponentDataArray<SquadMember>(Allocator.Temp);
        NativeArray<LocalTransform> transforms = squadMinionQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        int commandedCount = 0;
        for (int minionIndex = 0; minionIndex < minions.Length; minionIndex++)
        {
            if (members[minionIndex].squadIndex != activeSquad) continue;
            float3 memberPosition = transforms[minionIndex].Position;
            if (!SquadRules.HearsCommand(commanderMounted, commanderPosition, memberPosition, onFootRadius)) continue;

            ApplyCommand(minions[minionIndex], command, memberPosition, targetEntity, commandedCount);
            commandedCount++;
        }

        minions.Dispose();
        members.Dispose();
        transforms.Dispose();

        if (commandedCount == 0)
        {
            Debug.Log("[Squad] No members of squad " + activeSquad + " in range");
            return;
        }
        Debug.Log("[Squad] " + command + " -> " + commandedCount + " members of squad " + activeSquad);
    }

    private void ApplyCommand(Entity member, QuickCommandType command, float3 memberPosition, Entity targetEntity, int memberOrdinal)
    {
        switch (command)
        {
            case QuickCommandType.Follow:
                if (!entityManager.HasComponent<MinionStandingOrder>(member)) return;
                EndGather(entityManager, member);
                // Slot 0 stays the player's own spot, so members start at slot 1.
                entityManager.SetComponentData(member, new MinionStandingOrder
                {
                    type = StandingOrderType.FollowCommander,
                    formationSlotIndex = memberOrdinal + 1,
                    engageRadius = 8f,
                    lastIssuedDestination = new float3(float.MaxValue),
                    lastIssuedTime = -1000,
                });
                break;
            case QuickCommandType.Hold:
                if (!entityManager.HasComponent<MinionStandingOrder>(member)) return;
                EndGather(entityManager, member);
                entityManager.SetComponentData(member, new MinionStandingOrder
                {
                    type = StandingOrderType.HoldPosition,
                    anchorPosition = memberPosition,
                    engageRadius = HoldDefendRadius,
                    lastIssuedDestination = memberPosition,
                    lastIssuedTime = -1000,
                });
                break;
            case QuickCommandType.AttackTarget:
                if (!entityManager.HasComponent<OnMinionAttackCommand>(member)) return;
                entityManager.SetComponentData(member, new OnMinionAttackCommand { targetEntity = targetEntity });
                entityManager.SetComponentEnabled<OnMinionAttackCommand>(member, true);
                break;
            case QuickCommandType.Return:
                if (!entityManager.HasComponent<OnMinionReturnCommand>(member)) return;
                entityManager.SetComponentEnabled<OnMinionReturnCommand>(member, true);
                break;
            case QuickCommandType.Gather:
                if (!TryGetPlayer(out Entity gatherPlayer)) return;
                if (!StartGather(entityManager, member, gatherPlayer, targetEntity)) return;
                break;
            default:
                return;
        }

        if (entityManager.HasComponent<PlayerUnitBrain>(member))
            entityManager.SetComponentEnabled<PlayerUnitBrain>(member, true);
    }

    #endregion

    #region Gather

    public static bool IsGatherTarget(EntityManager entityManager, Entity target)
    {
        if (!entityManager.Exists(target)) return false;
        if (entityManager.HasComponent<ResourceNode>(target))
            return entityManager.GetComponentData<ResourceNode>(target).remaining > 0;
        return IsHostileCorpse(entityManager, target);
    }

    private static bool IsHostileCorpse(EntityManager entityManager, Entity target)
    {
        if (!entityManager.HasComponent<Dead>(target) || !entityManager.IsComponentEnabled<Dead>(target)) return false;
        if (!entityManager.HasComponent<UnitData>(target) || !entityManager.HasComponent<Faction>(target)) return false;
        // Already being hauled away (or pooled): matches the gather system's IsHaulableCorpse.
        if (entityManager.HasComponent<Despawn>(target) && entityManager.IsComponentEnabled<Despawn>(target)) return false;
        return entityManager.GetComponentData<Faction>(target).factionType != FactionType.Player;
    }

    public static void EndGather(EntityManager entityManager, Entity minion)
    {
        if (!entityManager.HasComponent<MinionGatherOrder>(minion)) return;
        MinionGatherOrder gatherOrder = entityManager.GetComponentData<MinionGatherOrder>(minion);
        gatherOrder.phase = GatherPhase.None;
        gatherOrder.carriedAmount = 0;
        entityManager.SetComponentData(minion, gatherOrder);
    }

    public static bool PlayerHasDeployedWorkstation(EntityManager entityManager, Entity playerEntity)
    {
        if (!entityManager.HasComponent<PlayerWorkstationLink>(playerEntity)) return false;
        Entity workstation = entityManager.GetComponentData<PlayerWorkstationLink>(playerEntity).workstation;
        return workstation != Entity.Null && entityManager.Exists(workstation);
    }

    // Callers check PlayerHasDeployedWorkstation first so the warning logs once, not per minion.
    public static bool StartGather(EntityManager entityManager, Entity minion, Entity playerEntity, Entity target)
    {
        if (!entityManager.HasComponent<MinionGatherOrder>(minion)) return false;
        if (!entityManager.HasComponent<PlayerWorkstationLink>(playerEntity)) return false;
        Entity station = entityManager.GetComponentData<PlayerWorkstationLink>(playerEntity).workstation;
        if (station == Entity.Null) return false;

        ResourceType resourceType = entityManager.HasComponent<ResourceNode>(target)
            ? entityManager.GetComponentData<ResourceNode>(target).resourceType
            : ResourceType.Corpse;

        entityManager.SetComponentData(minion, new MinionGatherOrder
        {
            phase = GatherPhase.ToNode,
            node = target,
            resourceType = resourceType,
            station = station,
            harvestTimer = 0f,
            carriedAmount = 0,
            lastIssuedDestination = new float3(float.MaxValue),
            lastIssuedTime = -1000,
        });
        if (entityManager.HasComponent<MinionStandingOrder>(minion))
        {
            MinionStandingOrder standingOrder = entityManager.GetComponentData<MinionStandingOrder>(minion);
            standingOrder.type = StandingOrderType.None;
            entityManager.SetComponentData(minion, standingOrder);
        }
        if (entityManager.HasComponent<PlayerUnitBrain>(minion))
            entityManager.SetComponentEnabled<PlayerUnitBrain>(minion, true);
        return true;
    }

    private Entity FindNearestGatherTarget(Entity playerEntity)
    {
        float3 referencePoint = CommanderPosition;
        if (IsCommanderMounted && Camera.main != null
            && entityManager.HasComponent<CursorScreenPosition>(playerEntity))
        {
            float2 cursor = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
            UnityEngine.Ray cursorRay = Camera.main.ScreenPointToRay(new Vector3(cursor.x, cursor.y, 0f));
            UnityEngine.Plane commanderPlane = new UnityEngine.Plane(Vector3.up, new Vector3(0f, referencePoint.y, 0f));
            if (commanderPlane.Raycast(cursorRay, out float planeDistance))
                referencePoint = cursorRay.GetPoint(planeDistance);
        }

        Entity nearest = Entity.Null;
        float nearestDistanceSquared = GatherSearchRadius * GatherSearchRadius;

        EntityQuery nodeQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<ResourceNode, LocalTransform>().Build(entityManager);
        NativeArray<Entity> nodes = nodeQuery.ToEntityArray(Allocator.Temp);
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            if (!IsGatherTarget(entityManager, nodes[nodeIndex])) continue;
            float distanceSquared = math.distancesq(referencePoint, entityManager.GetComponentData<LocalTransform>(nodes[nodeIndex]).Position);
            if (distanceSquared > nearestDistanceSquared) continue;
            nearestDistanceSquared = distanceSquared;
            nearest = nodes[nodeIndex];
        }
        nodes.Dispose();

        EntityQuery corpseQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Dead, UnitData, Faction, LocalTransform>().Build(entityManager);
        NativeArray<Entity> corpses = corpseQuery.ToEntityArray(Allocator.Temp);
        for (int corpseIndex = 0; corpseIndex < corpses.Length; corpseIndex++)
        {
            if (!IsGatherTarget(entityManager, corpses[corpseIndex])) continue;
            float distanceSquared = math.distancesq(referencePoint, entityManager.GetComponentData<LocalTransform>(corpses[corpseIndex]).Position);
            if (distanceSquared > nearestDistanceSquared) continue;
            nearestDistanceSquared = distanceSquared;
            nearest = corpses[corpseIndex];
        }
        corpses.Dispose();
        return nearest;
    }

    #endregion
}
