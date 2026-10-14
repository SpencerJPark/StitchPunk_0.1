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
            default:
                return;
        }

        if (entityManager.HasComponent<PlayerUnitBrain>(member))
            entityManager.SetComponentEnabled<PlayerUnitBrain>(member, true);
    }

    #endregion
}
