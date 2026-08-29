using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

public class UnitSelectionManager : RegulatorSingleton<UnitSelectionManager>, IUpdateObserver
{
    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private EntityQuery physicsQuery;
    private Entity playerEntity;
    private bool playerEntityResolved;

    public bool IsSelecting { get; private set; }

    private Vector2 selectionStartScreenPosition;

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery   = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        physicsQuery  = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    #endregion

    #region Update

    public void ObservedUpdate()
    {
        if (!TryResolvePlayerEntity()) return;

        if (entityManager.IsComponentEnabled<OnSelectPlayerInput>(playerEntity))
        {
            OnSelectPlayerInput selectInput = entityManager.GetComponentData<OnSelectPlayerInput>(playerEntity);
            entityManager.SetComponentEnabled<OnSelectPlayerInput>(playerEntity, false);

            if (!selectInput.isReleased)
            {
                selectionStartScreenPosition = GetCursorScreenPosition();
                IsSelecting = true;
                DeselectAll();
            }
            else
            {
                HandleSelectionComplete();
                IsSelecting = false;
            }
        }

        if (IsSelecting)
        {
            Rect rect = GetSelectionAreaRect();
            entityManager.SetComponentData(playerEntity, new SelectionBoxData
            {
                PositionX = rect.x,
                PositionY = rect.y,
                Width     = rect.width,
                Height    = rect.height,
            });
            entityManager.SetComponentEnabled<SelectionBoxData>(playerEntity, true);
        }
        else
        {
            entityManager.SetComponentEnabled<SelectionBoxData>(playerEntity, false);
        }

        if (entityManager.IsComponentEnabled<OnCommandPlayerInput>(playerEntity))
        {
            HandleCommand();
            entityManager.SetComponentEnabled<OnCommandPlayerInput>(playerEntity, false);
        }

        if (entityManager.IsComponentEnabled<OnCycleGroupInput>(playerEntity))
        {
            OnCycleGroupInput cycleInput = entityManager.GetComponentData<OnCycleGroupInput>(playerEntity);
            HandleCycleGroup(cycleInput.delta);
            entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, false);
        }

        if (Keyboard.current[Key.Tab].wasPressedThisFrame)
            CycleFormationForSelectedHordes();
    }

    #endregion

    #region Selection

    private void HandleSelectionComplete()
    {
        Vector2 endPos = GetCursorScreenPosition();
        Rect rect = BuildSelectionRect(selectionStartScreenPosition, endPos);

        if (rect.width + rect.height > 40f)
            SelectInRect(rect);
        else
            SelectAtCursor();

        // If in group assignment mode, assign selected minions to the active group horde.
        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);
        if (groupData.assignmentGroupIndex > 0)
            AssignSelectedToGroup(groupData.assignmentGroupIndex);
    }

    private void SelectAtCursor()
    {
        if (physicsQuery.IsEmpty) return;
        PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(GetCursorScreenPosition());

        RaycastInput rayInput = new RaycastInput
        {
            Start  = ray.origin,
            End    = ray.GetPoint(200f),
            Filter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = 1u << ConstGameData.UNITS_LAYER,
                GroupIndex   = 0,
            }
        };

        if (!physics.CollisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit)) return;
        if (!entityManager.HasComponent<Minion>(hit.Entity))        return;
        if (!entityManager.IsComponentEnabled<Minion>(hit.Entity))  return;
        if (!entityManager.HasComponent<Selected>(hit.Entity))      return;

        SetSelected(hit.Entity, true);
    }

    private void SelectInRect(Rect rect)
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion, LocalTransform>()
            .WithPresent<Selected>()
            .Build(entityManager);

        NativeArray<Entity>         entities   = query.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Vector2 screenPos = Camera.main.WorldToScreenPoint(transforms[i].Position);
            if (rect.Contains(screenPos))
                SetSelected(entities[i], true);
        }

        entities.Dispose();
        transforms.Dispose();
    }

    private void DeselectAll()
    {
        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        NativeArray<Entity>   entities      = query.ToEntityArray(Allocator.Temp);
        NativeArray<Selected> selectedArray = query.ToComponentDataArray<Selected>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            Selected s     = selectedArray[i];
            s.onDeselected = true;
            entityManager.SetComponentData(entities[i], s);
            entityManager.SetComponentEnabled<Selected>(entities[i], false);
        }

        entities.Dispose();
        selectedArray.Dispose();
    }

    private void SetSelected(Entity entity, bool selected)
    {
        entityManager.SetComponentEnabled<Selected>(entity, selected);
        Selected s   = entityManager.GetComponentData<Selected>(entity);
        s.onSelected = selected;
        entityManager.SetComponentData(entity, s);
    }

    // Called by UI to draw the drag box.
    public Rect GetSelectionAreaRect()
    {
        return BuildSelectionRect(selectionStartScreenPosition, GetCursorScreenPosition());
    }

    #endregion

    #region Group Control

    private void HandleCycleGroup(int delta)
    {
        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);
        int total = groupData.unlockedGroupCount + 1; // +1 for index 0 = no group
        int next  = ((groupData.assignmentGroupIndex + delta) % total + total) % total;
        groupData.assignmentGroupIndex = next;
        entityManager.SetComponentData(playerEntity, groupData);
    }

    private void CycleFormationForSelectedHordes()
    {
        EntityQuery selectedQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, Minion, HordeMembership>()
            .Build(entityManager);

        NativeArray<HordeMembership> memberships = selectedQuery.ToComponentDataArray<HordeMembership>(Allocator.Temp);
        NativeHashSet<Entity> visited = new NativeHashSet<Entity>(4, Allocator.Temp);

        for (int i = 0; i < memberships.Length; i++)
        {
            Entity hordeEntity = memberships[i].hordeEntity;
            if (hordeEntity == Entity.Null || !visited.Add(hordeEntity)) continue;
            if (!entityManager.HasComponent<Horde>(hordeEntity)) continue;

            Horde horde = entityManager.GetComponentData<Horde>(hordeEntity);
            horde.formationType = (FormationType)(((byte)horde.formationType + 1) % 4);
            horde.needsPathUpdate = true;
            entityManager.SetComponentData(hordeEntity, horde);
        }

        memberships.Dispose();
        visited.Dispose();
    }

    private void AssignSelectedToGroup(int groupIndex)
    {
        DynamicBuffer<PlayerHordeSlot> slots = entityManager.GetBuffer<PlayerHordeSlot>(playerEntity);
        int slotIndex = groupIndex - 1;
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        Entity targetHorde = slots[slotIndex].hordeEntity;

        EntityQuery selectedQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, Minion>()
            .Build(entityManager);

        NativeArray<Entity> selected = selectedQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < selected.Length; i++)
        {
            Entity body = selected[i];
            if (entityManager.IsComponentEnabled<HordeMembership>(body))
                HordeUtils.LeaveHorde(entityManager, body);
            HordeUtils.JoinHorde(entityManager, body, targetHorde, 0);
        }
        selected.Dispose();
    }

    #endregion

    #region Command

    private void HandleCommand()
    {
        // Only issue commands if at least one minion is selected. Commands are stamped on each
        // selected minion (baked by UnitBakingUtil.AddPlayerControlled) and consumed one-shot by
        // MinionActionSelectionSystem.
        EntityQuery selectedMinions = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, Minion>()
            .Build(entityManager);
        if (selectedMinions.IsEmpty) return;

        NativeArray<Entity> minions = selectedMinions.ToEntityArray(Allocator.Temp);

        // F key → follow player regardless of cursor position.
        if (Keyboard.current[Key.F].isPressed)
        {
            for (int i = 0; i < minions.Length; i++)
            {
                if (!entityManager.HasComponent<OnMinionFollowCommand>(minions[i])) continue;
                entityManager.SetComponentEnabled<OnMinionFollowCommand>(minions[i], true);
                entityManager.SetComponentEnabled<PlayerUnitBrain>(minions[i], true);
            }
            minions.Dispose();
            return;
        }

        if (physicsQuery.IsEmpty)
        {
            minions.Dispose();
            return;
        }
        PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(GetCursorScreenPosition());

        RaycastInput rayInput = new RaycastInput
        {
            Start  = ray.origin,
            End    = ray.GetPoint(200f),
            Filter = new CollisionFilter
            {
                BelongsTo    = ~0u,
                CollidesWith = 1u << ConstGameData.UNITS_LAYER,
                GroupIndex   = 0,
            }
        };

        if (physics.CollisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
        {
            Entity target    = hit.Entity;
            bool   isHostile = entityManager.HasComponent<Dead>(target)
                            && !entityManager.IsComponentEnabled<Dead>(target)
                            && !entityManager.HasComponent<PlayerImmune>(target)
                            && (!entityManager.HasComponent<Minion>(target)
                                || !entityManager.IsComponentEnabled<Minion>(target));

            if (isHostile)
            {
                // Right-click hostile → attack command (minion retains control even if hit).
                for (int i = 0; i < minions.Length; i++)
                {
                    if (!entityManager.HasComponent<OnMinionAttackCommand>(minions[i])) continue;
                    entityManager.SetComponentData(minions[i], new OnMinionAttackCommand { targetEntity = target });
                    entityManager.SetComponentEnabled<OnMinionAttackCommand>(minions[i], true);
                    entityManager.SetComponentEnabled<PlayerUnitBrain>(minions[i], true);
                }
                minions.Dispose();
                return;
            }

            // Right-click interactable entity (non-hostile, non-minion) → interact command.
            if (entityManager.HasComponent<Interaction>(target))
            {
                for (int i = 0; i < minions.Length; i++)
                {
                    if (!entityManager.HasComponent<OnMinionInteractCommand>(minions[i])) continue;
                    entityManager.SetComponentData(minions[i], new OnMinionInteractCommand { targetEntity = target });
                    entityManager.SetComponentEnabled<OnMinionInteractCommand>(minions[i], true);
                    entityManager.SetComponentEnabled<PlayerUnitBrain>(minions[i], true);
                }
                minions.Dispose();
                return;
            }
        }

        Vector3 worldPos = CursorToWorldPosition(GetCursorScreenPosition());

        // Shift + right-click on ground → defend position.
        if (Keyboard.current[Key.LeftShift].isPressed || Keyboard.current[Key.RightShift].isPressed)
        {
            for (int i = 0; i < minions.Length; i++)
            {
                if (!entityManager.HasComponent<OnMinionDefendCommand>(minions[i])) continue;
                entityManager.SetComponentData(minions[i], new OnMinionDefendCommand
                {
                    position = worldPos,
                    radius   = 5f,
                });
                entityManager.SetComponentEnabled<OnMinionDefendCommand>(minions[i], true);
                entityManager.SetComponentEnabled<PlayerUnitBrain>(minions[i], true);
            }
            minions.Dispose();
            return;
        }

        // Default: move to ground position under the cursor.
        for (int i = 0; i < minions.Length; i++)
        {
            if (!entityManager.HasComponent<OnMinionMoveCommand>(minions[i])) continue;
            entityManager.SetComponentData(minions[i], new OnMinionMoveCommand { destination = worldPos });
            entityManager.SetComponentEnabled<OnMinionMoveCommand>(minions[i], true);
            entityManager.SetComponentEnabled<PlayerUnitBrain>(minions[i], true);
        }
        minions.Dispose();
    }

    #endregion

    #region Helpers

    private bool TryResolvePlayerEntity()
    {
        if (playerEntityResolved) return true;
        if (playerQuery.IsEmpty) return false;
        playerEntity = playerQuery.GetSingletonEntity();
        playerEntityResolved = true;
        return true;
    }

    private Vector2 GetCursorScreenPosition()
    {
        float2 pos = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
        return new Vector2(pos.x, pos.y);
    }

    private static Rect BuildSelectionRect(Vector2 start, Vector2 end)
    {
        return new Rect(
            Mathf.Min(start.x, end.x),
            Mathf.Min(start.y, end.y),
            Mathf.Abs(end.x - start.x),
            Mathf.Abs(end.y - start.y));
    }

    private static Vector3 CursorToWorldPosition(Vector2 screenPos)
    {
        if (Camera.main == null) return Vector3.zero;
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(screenPos);
        UnityEngine.Plane groundPlane = new UnityEngine.Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out float distance))
            return ray.GetPoint(distance);
        return Vector3.zero;
    }

    #endregion
}
