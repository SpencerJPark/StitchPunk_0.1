using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

public class UnitSelectionManager : RegulatorSingleton<UnitSelectionManager>, IUpdateObserver
{
    [SerializeField] private PlayerInput playerInput;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private EntityQuery physicsQuery;
    private Entity playerEntity;
    private bool playerEntityResolved;

    public bool IsSelecting { get; private set; }

    private Vector2 selectionStartScreenPosition;
    private bool selectPressed;
    private bool selectReleased;
    private bool commandPressed;

    private const string SELECT_ACTION  = "Select";
    private const string COMMAND_ACTION = "Command";

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery   = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        physicsQuery  = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
    }

    private void OnEnable()
    {
        UpdateManager.RegisterObserver(this);
        if (playerInput?.actions == null) return;

        InputAction select  = playerInput.actions.FindAction(SELECT_ACTION);
        InputAction command = playerInput.actions.FindAction(COMMAND_ACTION);

        if (select != null)
        {
            select.started  += OnSelectStarted;
            select.canceled += OnSelectCanceled;
        }
        if (command != null)
            command.started += OnCommandStarted;
    }

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        if (playerInput == null || playerInput.actions == null) return;

        InputAction select  = playerInput.actions.FindAction(SELECT_ACTION);
        InputAction command = playerInput.actions.FindAction(COMMAND_ACTION);

        if (select != null)
        {
            select.started  -= OnSelectStarted;
            select.canceled -= OnSelectCanceled;
        }
        if (command != null)
            command.started -= OnCommandStarted;
    }

    #endregion

    #region Input Callbacks

    private void OnSelectStarted(InputAction.CallbackContext ctx)
    {
        if (!IsControlUnitsMode()) return;
        selectPressed = true;
    }

    private void OnSelectCanceled(InputAction.CallbackContext ctx)
    {
        if (!IsControlUnitsMode()) return;
        selectReleased = true;
    }

    private void OnCommandStarted(InputAction.CallbackContext ctx)
    {
        if (!IsControlUnitsMode()) return;
        commandPressed = true;
    }

    #endregion

    #region Update

    public void ObservedUpdate()
    {
        if (selectPressed)
        {
            selectionStartScreenPosition = GetPointerScreenPosition();
            IsSelecting = true;
            DeselectAll();
            selectPressed = false;
        }

        if (selectReleased)
        {
            HandleSelectionComplete();
            IsSelecting = false;
            selectReleased = false;
        }

        if (!TryResolvePlayerEntity()) return;

        if (IsSelecting)
        {
            Rect rect = GetSelectionAreaRect();
            entityManager.SetComponentData(playerEntity, new SelectionBoxData
            {
                PositionX = rect.x,
                PositionY  = rect.y,
                Width      = rect.width,
                Height     = rect.height,
            });
            entityManager.SetComponentEnabled<SelectionBoxData>(playerEntity, true);
        }
        else
        {
            entityManager.SetComponentEnabled<SelectionBoxData>(playerEntity, false);
        }

        if (commandPressed)
        {
            HandleCommand();
            commandPressed = false;
        }

        if (entityManager.IsComponentEnabled<OnCycleGroupInput>(playerEntity))
        {
            OnCycleGroupInput cycleInput = entityManager.GetComponentData<OnCycleGroupInput>(playerEntity);
            HandleCycleGroup(cycleInput.delta);
            entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, false);
        }

        if (entityManager.IsComponentEnabled<OnQuickSelectGroupInput>(playerEntity))
        {
            OnQuickSelectGroupInput quickInput = entityManager.GetComponentData<OnQuickSelectGroupInput>(playerEntity);
            HandleQuickSelectGroup(quickInput.groupIndex);
            entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, false);
        }
    }

    #endregion

    #region Selection

    private void HandleSelectionComplete()
    {
        Vector2 endPos = GetPointerScreenPosition();
        Rect rect = BuildSelectionRect(selectionStartScreenPosition, endPos);

        if (rect.width + rect.height > 40f)
            SelectInRect(rect);
        else
            SelectAtCursor();

        // If in group assignment mode, assign selected minions to the group horde.
        if (!TryResolvePlayerEntity()) return;
        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);
        if (groupData.assignmentGroupIndex > 0)
            AssignSelectedToGroup(groupData.assignmentGroupIndex);
    }

    private void SelectAtCursor()
    {
        if (physicsQuery.IsEmpty) return;
        PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(GetPointerScreenPosition());

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
        if (!entityManager.HasComponent<Minion>(hit.Entity))   return;
        if (!entityManager.IsComponentEnabled<Minion>(hit.Entity)) return;
        if (!entityManager.HasComponent<Selected>(hit.Entity)) return;

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
        Selected s  = entityManager.GetComponentData<Selected>(entity);
        s.onSelected = selected;
        entityManager.SetComponentData(entity, s);
    }

    // Called by UI to draw the drag box.
    public Rect GetSelectionAreaRect()
    {
        return BuildSelectionRect(selectionStartScreenPosition, GetPointerScreenPosition());
    }

    #endregion

    #region Group Control

    private void HandleCycleGroup(int delta)
    {
        if (!TryResolvePlayerEntity()) return;
        PlayerMinionGroupsData groupData = entityManager.GetComponentData<PlayerMinionGroupsData>(playerEntity);
        int total = groupData.unlockedGroupCount + 1; // +1 for "no group" (index 0)
        int next  = ((groupData.assignmentGroupIndex + delta) % total + total) % total;
        groupData.assignmentGroupIndex = next;
        entityManager.SetComponentData(playerEntity, groupData);
    }

    private void HandleQuickSelectGroup(int groupIndex)
    {
        if (!TryResolvePlayerEntity()) return;
        DynamicBuffer<PlayerHordeSlot> slots = entityManager.GetBuffer<PlayerHordeSlot>(playerEntity);
        int slotIndex = groupIndex - 1;
        if (slotIndex < 0 || slotIndex >= slots.Length) return;
        Entity targetHorde = slots[slotIndex].hordeEntity;

        DeselectAll();

        EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion, HordeMembership>()
            .WithPresent<Selected>()
            .Build(entityManager);

        NativeArray<Entity>          entities    = query.ToEntityArray(Allocator.Temp);
        NativeArray<HordeMembership> memberships = query.ToComponentDataArray<HordeMembership>(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            if (memberships[i].hordeEntity == targetHorde)
                SetSelected(entities[i], true);
        }

        entities.Dispose();
        memberships.Dispose();
    }

    private void AssignSelectedToGroup(int groupIndex)
    {
        if (!TryResolvePlayerEntity()) return;
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
        if (!TryResolvePlayerEntity()) return;

        // Only issue commands if at least one minion is selected.
        EntityQuery selectedMinions = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, Minion>()
            .Build(entityManager);
        if (selectedMinions.IsEmpty) return;

        if (physicsQuery.IsEmpty) return;
        PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(GetPointerScreenPosition());

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
            Entity target = hit.Entity;
            bool isHostile = entityManager.HasComponent<Alive>(target)
                          && entityManager.IsComponentEnabled<Alive>(target)
                          && !entityManager.HasComponent<PlayerImmune>(target)
                          && (!entityManager.HasComponent<Minion>(target)
                              || !entityManager.IsComponentEnabled<Minion>(target));

            if (isHostile)
            {
                entityManager.SetComponentData(playerEntity, new OnMinionInteractCommand { targetEntity = target });
                entityManager.SetComponentEnabled<OnMinionInteractCommand>(playerEntity, true);
                return;
            }
        }

        // No hostile target — move to ground position under the cursor.
        Vector3 worldPos = CursorToWorldPosition(GetPointerScreenPosition());
        entityManager.SetComponentData(playerEntity, new OnMinionMoveCommand { destination = worldPos });
        entityManager.SetComponentEnabled<OnMinionMoveCommand>(playerEntity, true);
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

    private bool IsControlUnitsMode()
    {
        if (!TryResolvePlayerEntity()) return false;
        return entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap == ActionMaps.ControlUnits;
    }

    private Vector2 GetPointerScreenPosition()
    {
        if (playerEntityResolved && IsControlUnitsMode())
        {
            Unity.Mathematics.float2 pos = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
            return new Vector2(pos.x, pos.y);
        }
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
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
