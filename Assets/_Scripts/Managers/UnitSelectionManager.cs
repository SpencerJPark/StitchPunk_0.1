using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class UnitSelectionManager : Singleton<UnitSelectionManager>, IUpdateObserver
{
    #region Events
    public event EventHandler OnSelectionAreaStart;
    public event EventHandler OnSelectionAreaEnd;
    public event EventHandler OnSelectedEntitiesChanged;
    #endregion

    #region Serialized Fields
    [Header("Input / Cursor")]
    [SerializeField] private UnitSelectionCursorUI cursorUI;   // assign in inspector
    
    [Header("Input System")]
    [SerializeField] private PlayerInput playerInput;
    #endregion

    #region Private Fields
    // selection start in screen space (pixels)
    private Vector2 selectionStartScreenPosition;
    
    // Input state tracking
    private bool selectPressed;
    private bool selectReleased;
    private bool commandPressed;
    
    private EntityManager entityManager;
    private Entity inputEntity;
    
    // Action names in ControlUnits action map
    private const string SELECT_ACTION_NAME = "Select";
    private const string COMMAND_ACTION_NAME = "Command";
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        // Setup Input
        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        if (playerInput == null)
        {
            Debug.LogError("UnitSelectionManager: No PlayerInput component found on this GameObject.");
        }
        
        // Setup Entities
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // Try to find existing PlayerInputData entity
        EntityQuery query = entityManager.CreateEntityQuery(typeof(PlayerInputData));
        if (query.CalculateEntityCount() > 0)
        {
            inputEntity = query.GetSingletonEntity();
        }
        else
        {
            Debug.LogWarning("UnitSelectionManager: No PlayerInputData entity found!");
        }
    }

    private void OnEnable()
    {
        UpdateManager.RegisterObserver(this);
        
        // Subscribe to ControlUnits action map inputs
        if (playerInput != null && playerInput.actions != null)
        {
            InputAction selectAction = playerInput.actions.FindAction(SELECT_ACTION_NAME);
            InputAction commandAction = playerInput.actions.FindAction(COMMAND_ACTION_NAME);
            
            if (selectAction != null)
            {
                selectAction.started += OnSelectStarted;
                selectAction.canceled += OnSelectCanceled;
            }
            
            if (commandAction != null)
            {
                commandAction.started += OnCommandStarted;
            }
        }
    }

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        
        // Unsubscribe from input events
        if (playerInput != null && playerInput.actions != null)
        {
            InputAction selectAction = playerInput.actions.FindAction(SELECT_ACTION_NAME);
            InputAction commandAction = playerInput.actions.FindAction(COMMAND_ACTION_NAME);
            
            if (selectAction != null)
            {
                selectAction.started -= OnSelectStarted;
                selectAction.canceled -= OnSelectCanceled;
            }
            
            if (commandAction != null)
            {
                commandAction.started -= OnCommandStarted;
            }
        }
    }

    private void Start()
    {
        BuildingPlacementManager.Instance.OnActiveBuildingTypeSOChanged += BuildingPlacementManager_OnActiveBuildingTypeSOChanged;
    }
    #endregion

    #region Event Handlers
    private void BuildingPlacementManager_OnActiveBuildingTypeSOChanged(object sender, EventArgs e)
    {
        if (BuildingPlacementManager.Instance.GetActiveBuildingTypeSO() != GlobalGameData.Instance.buildingTypeListSO.none)
        {
            // Selected some building
            DeselectAllUnits();
        }
    }
    #endregion

    #region Input Callbacks
    private void OnSelectStarted(InputAction.CallbackContext context)
    {
        // Only process if we're in ControlUnits mode
        if (!IsInControlUnitsMode())
            return;
            
        selectPressed = true;
    }

    private void OnSelectCanceled(InputAction.CallbackContext context)
    {
        // Only process if we're in ControlUnits mode
        if (!IsInControlUnitsMode())
            return;
            
        selectReleased = true;
    }

    private void OnCommandStarted(InputAction.CallbackContext context)
    {
        // Only process if we're in ControlUnits mode
        if (!IsInControlUnitsMode())
            return;
            
        commandPressed = true;
    }
    
    private bool IsInControlUnitsMode()
    {
        if (inputEntity == Entity.Null || !entityManager.Exists(inputEntity))
            return false;
            
        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        return data.activeActionMap == ActionMaps.ControlUnits;
    }
    #endregion

    #region Input Helpers
    /// <summary>
    /// Unified pointer position. Uses fake HUD cursor if active, otherwise falls back to real mouse.
    /// </summary>
    private Vector2 GetPointerScreenPosition()
    {
        if (cursorUI != null && cursorUI.IsActive)
            return cursorUI.ScreenPosition;

        // Fallback to real mouse when not in ControlUnits mode
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }
    #endregion

    #region Main Update Logic
    public void ObservedUpdate()
    {
        // Only process if we're in ControlUnits mode
        if (!IsInControlUnitsMode())
            return;
            
        if (!BuildingPlacementManager.Instance.GetActiveBuildingTypeSO().IsNone())
            return;

        // =========================
        // SELECTION COMPLETE
        // =========================
        if (selectReleased)
        {
            HandleSelectionComplete(entityManager);
        }

        // =========================
        // UI hit-testing
        // =========================
        if (EventSystem.current.IsPointerOverGameObject())
            return;

        // =========================
        // SELECTION START
        // =========================
        if (selectPressed)
        {
            HandleSelectionStart();
        }

        // =========================
        // COMMAND (move / attack)
        // =========================
        if (commandPressed)
        {
            HandleCommand(entityManager);
        }
    }
    #endregion

    #region Selection Logic
    private void HandleSelectionStart()
    {
        selectPressed = false; // Consume the event
        
        selectionStartScreenPosition = GetPointerScreenPosition();
        OnSelectionAreaStart?.Invoke(this, EventArgs.Empty);
    }

    private void HandleSelectionComplete(EntityManager entityManager)
    {
        selectReleased = false; // Consume the event
        
        Vector2 selectionEndScreenPosition = GetPointerScreenPosition();

        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
        NativeArray<Selected> selectedArray = entityQuery.ToComponentDataArray<Selected>(Allocator.Temp);

        // Is Barracks selected?
        if (entityArray.Length == 1 && entityManager.HasComponent<BuildingBarracks>(entityArray[0]))
        {
            // Is clicking on the Barracks UI?
            if (EventSystem.current.IsPointerOverGameObject())
            {
                // Don't deselect
                return;
            }
        }

        // Deselect all
        for (int i = 0; i < entityArray.Length; i++)
        {
            entityManager.SetComponentEnabled<Selected>(entityArray[i], false);
            Selected selected = selectedArray[i];
            selected.onDeselected = true;
            entityManager.SetComponentData(entityArray[i], selected);
        }

        Rect selectionAreaRect = GetSelectionAreaRect(selectionEndScreenPosition);
        float selectionAreaSize = selectionAreaRect.width + selectionAreaRect.height;
        float multipleSelectionSizeMin = 40f;
        bool isMultipleSelection = selectionAreaSize > multipleSelectionSizeMin;

        if (isMultipleSelection)
        {
            HandleMultipleSelection(entityManager);
        }
        else
        {
            HandleSingleSelection(entityManager);
        }

        OnSelectionAreaEnd?.Invoke(this, EventArgs.Empty);
        OnSelectedEntitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleMultipleSelection(EntityManager entityManager)
    {
        Vector2 selectionEndScreenPosition = GetPointerScreenPosition();
        Rect selectionAreaRect = GetSelectionAreaRect(selectionEndScreenPosition);

        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<LocalTransform, Unit>()
            .WithPresent<Selected>()
            .Build(entityManager);

        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> localTransformArray = entityQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        for (int i = 0; i < localTransformArray.Length; i++)
        {
            LocalTransform unitLocalTransform = localTransformArray[i];
            Vector2 unitScreenPosition = Camera.main.WorldToScreenPoint(unitLocalTransform.Position);

            if (selectionAreaRect.Contains(unitScreenPosition))
            {
                entityManager.SetComponentEnabled<Selected>(entityArray[i], true);
                Selected selected = entityManager.GetComponentData<Selected>(entityArray[i]);
                selected.onSelected = true;
                entityManager.SetComponentData(entityArray[i], selected);
            }
        }
    }

    private void HandleSingleSelection(EntityManager entityManager)
    {
        EntityQuery entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
        PhysicsWorldSingleton physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

        Vector2 pointerPos = GetPointerScreenPosition();
        UnityEngine.Ray cameraRay = Camera.main.ScreenPointToRay(pointerPos);

        RaycastInput raycastInput = new RaycastInput
        {
            Start = cameraRay.GetPoint(0f),
            End = cameraRay.GetPoint(9999f),
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = (1u << GlobalGameData.UNITS_LAYER) | (1u << GlobalGameData.STRUCTURES_LAYER),
                GroupIndex = 0,
            }
        };

        if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit raycastHit))
        {
            if (entityManager.HasComponent<Selected>(raycastHit.Entity))
            {
                entityManager.SetComponentEnabled<Selected>(raycastHit.Entity, true);
                Selected selected = entityManager.GetComponentData<Selected>(raycastHit.Entity);
                selected.onSelected = true;
                entityManager.SetComponentData(raycastHit.Entity, selected);
            }
        }
    }

    /// <summary>
    /// Builds the selection rect based on where drag started and the current pointer position.
    /// </summary>
    public Rect GetSelectionAreaRect()
    {
        Vector2 selectionEndScreenPosition = GetPointerScreenPosition();
        return GetSelectionAreaRect(selectionEndScreenPosition);
    }

    private Rect GetSelectionAreaRect(Vector2 selectionEndScreenPosition)
    {
        Vector2 lowerLeftCorner = new Vector2(
            Mathf.Min(selectionStartScreenPosition.x, selectionEndScreenPosition.x),
            Mathf.Min(selectionStartScreenPosition.y, selectionEndScreenPosition.y));

        Vector2 upperRightCorner = new Vector2(
            Mathf.Max(selectionStartScreenPosition.x, selectionEndScreenPosition.x),
            Mathf.Max(selectionStartScreenPosition.y, selectionEndScreenPosition.y));

        return new Rect(
            lowerLeftCorner.x,
            lowerLeftCorner.y,
            upperRightCorner.x - lowerLeftCorner.x,
            upperRightCorner.y - lowerLeftCorner.y
        );
    }

    public static void DeselectAllUnits()
    {
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .Build(entityManager);

        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
        NativeArray<Selected> selectedArray = entityQuery.ToComponentDataArray<Selected>(Allocator.Temp);

        for (int i = 0; i < entityArray.Length; i++)
        {
            entityManager.SetComponentEnabled<Selected>(entityArray[i], false);
            Selected selected = selectedArray[i];
            selected.onDeselected = true;
            entityManager.SetComponentData(entityArray[i], selected);
        }
    }
    #endregion

    #region Command Logic
    private void HandleCommand(EntityManager entityManager)
    {
        commandPressed = false; // Consume the event
        
        Vector2 pointerPos = GetPointerScreenPosition();
        UnityEngine.Ray cameraRay = Camera.main.ScreenPointToRay(pointerPos);

        EntityQuery entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
        PhysicsWorldSingleton physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

        // First raycast into ECS physics for attack target selection
        RaycastInput raycastInput = new RaycastInput
        {
            Start = cameraRay.GetPoint(0f),
            End = cameraRay.GetPoint(9999f),
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = (1u << GlobalGameData.UNITS_LAYER) | (1u << GlobalGameData.STRUCTURES_LAYER),
                GroupIndex = 0,
            }
        };

        bool isAttackingSingleTarget = false;
        if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit raycastHit))
        {
            if (entityManager.HasComponent<Faction>(raycastHit.Entity))
            {
                Faction faction = entityManager.GetComponentData<Faction>(raycastHit.Entity);
                if (faction.factionType == FactionType.Zombie)
                {
                    HandleAttackCommand(entityManager, raycastHit.Entity);
                    isAttackingSingleTarget = true;
                }
            }
        }

        // If not attacking a zombie, this is a move-command
        if (!isAttackingSingleTarget)
        {
            HandleMoveCommand(entityManager, pointerPos);
        }

        // Handle Barracks Rally Position
        HandleBarracksRallyPosition(entityManager, pointerPos);
    }

    private void HandleAttackCommand(EntityManager entityManager, Entity targetEntity)
    {
        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<TargetOverride>()
            .Build(entityManager);

        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
        NativeArray<TargetOverride> targetOverrideArray = entityQuery.ToComponentDataArray<TargetOverride>(Allocator.Temp);

        for (int i = 0; i < targetOverrideArray.Length; i++)
        {
            TargetOverride targetOverride = targetOverrideArray[i];
            targetOverride.targetEntity = targetEntity;
            targetOverrideArray[i] = targetOverride;
            entityManager.SetComponentEnabled<MoveOverride>(entityArray[i], false);
        }

        entityQuery.CopyFromComponentDataArray(targetOverrideArray);
    }

    private void HandleMoveCommand(EntityManager entityManager, Vector2 pointerPos)
    {
        // Use the pointer position to get a move destination in world space
        Vector3 moveWorldPosition = GetWorldPositionFromScreen(pointerPos);

        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected>()
            .WithPresent<MoveOverride, TargetOverride, TargetPositionPathQueued, FlowFieldPathRequest, FlowFieldFollower>()
            .Build(entityManager);

        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
        NativeArray<MoveOverride> moveOverrideArray = entityQuery.ToComponentDataArray<MoveOverride>(Allocator.Temp);
        NativeArray<TargetOverride> targetOverrideArray = entityQuery.ToComponentDataArray<TargetOverride>(Allocator.Temp);
        NativeArray<TargetPositionPathQueued> targetPositionPathQueuedArray = entityQuery.ToComponentDataArray<TargetPositionPathQueued>(Allocator.Temp);
        NativeArray<float3> movePositionArray = GenerateMovePositionArray(moveWorldPosition, entityArray.Length);

        for (int i = 0; i < moveOverrideArray.Length; i++)
        {
            MoveOverride moveOverride = moveOverrideArray[i];
            moveOverride.targetPosition = movePositionArray[i];
            moveOverrideArray[i] = moveOverride;
            entityManager.SetComponentEnabled<MoveOverride>(entityArray[i], true);

            TargetOverride targetOverride = targetOverrideArray[i];
            targetOverride.targetEntity = Entity.Null;
            targetOverrideArray[i] = targetOverride;

            TargetPositionPathQueued targetPositionPathQueued = targetPositionPathQueuedArray[i];
            targetPositionPathQueued.targetPosition = movePositionArray[i];
            targetPositionPathQueuedArray[i] = targetPositionPathQueued;
            entityManager.SetComponentEnabled<TargetPositionPathQueued>(entityArray[i], true);

            entityManager.SetComponentEnabled<FlowFieldPathRequest>(entityArray[i], false);
            entityManager.SetComponentEnabled<FlowFieldFollower>(entityArray[i], false);
        }

        entityQuery.CopyFromComponentDataArray(moveOverrideArray);
        entityQuery.CopyFromComponentDataArray(targetOverrideArray);
        entityQuery.CopyFromComponentDataArray(targetPositionPathQueuedArray);
    }

    private void HandleBarracksRallyPosition(EntityManager entityManager, Vector2 pointerPos)
    {
        Vector3 rallyWorldPosition = GetWorldPositionFromScreen(pointerPos);

        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Selected, BuildingBarracks, LocalTransform>()
            .Build(entityManager);

        NativeArray<BuildingBarracks> buildingBarracksArray = entityQuery.ToComponentDataArray<BuildingBarracks>(Allocator.Temp);
        NativeArray<LocalTransform> localTransformArray = entityQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        for (int i = 0; i < buildingBarracksArray.Length; i++)
        {
            BuildingBarracks buildingBarracks = buildingBarracksArray[i];
            buildingBarracks.rallyPositionOffset = (float3)rallyWorldPosition - localTransformArray[i].Position;
            buildingBarracksArray[i] = buildingBarracks;
        }

        entityQuery.CopyFromComponentDataArray(buildingBarracksArray);
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Converts the pointer screen position to a world position using the main camera and a ground plane.
    /// Uses Physics.Raycast into the 3D world.
    /// </summary>
    private Vector3 GetWorldPositionFromScreen(Vector2 screenPos)
    {
        UnityEngine.Ray ray = Camera.main.ScreenPointToRay(screenPos);

        // You can refine this with a ground layer mask if you want
        if (Physics.Raycast(ray, out UnityEngine.RaycastHit hitInfo, 1000f))
        {
            return hitInfo.point;
        }

        // Fallback: project onto XZ plane at y = 0
        float distance;
        UnityEngine.Plane groundPlane = new UnityEngine.Plane(Vector3.up, Vector3.zero);
        if (groundPlane.Raycast(ray, out distance))
        {
            return ray.GetPoint(distance);
        }

        // Last resort: something in front of camera
        return ray.GetPoint(20f);
    }

    private NativeArray<float3> GenerateMovePositionArray(float3 targetPosition, int positionCount)
    {
        NativeArray<float3> positionArray = new NativeArray<float3>(positionCount, Allocator.Temp);
        if (positionCount == 0)
            return positionArray;

        positionArray[0] = targetPosition;
        if (positionCount == 1)
            return positionArray;

        float ringSize = 2.2f;
        int ring = 0;
        int positionIndex = 1;

        while (positionIndex < positionCount)
        {
            int ringPositionCount = 3 + ring * 2;

            for (int i = 0; i < ringPositionCount; i++)
            {
                float angle = i * (math.PI2 / ringPositionCount);
                float3 ringVector = math.rotate(quaternion.RotateY(angle), new float3(ringSize * (ring + 1), 0, 0));
                float3 ringPosition = targetPosition + ringVector;

                positionArray[positionIndex] = ringPosition;
                positionIndex++;

                if (positionIndex >= positionCount)
                    break;
            }

            ring++;
        }

        return positionArray;
    }
    #endregion
}