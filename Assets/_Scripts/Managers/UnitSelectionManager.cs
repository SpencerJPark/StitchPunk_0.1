using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;

public class UnitSelectionManager : Singleton<UnitSelectionManager>, IUpdateObserver
{
    public event EventHandler OnSelectionAreaStart;
    public event EventHandler OnSelectionAreaEnd;
    public event EventHandler OnSelectedEntitiesChanged;

    [Header("Input / Cursor")]
    [SerializeField] private UnitSelectionCursorUI cursorUI;   // assign in inspector

    // selection start in screen space (pixels)
    private Vector2 selectionStartScreenPosition;

    private void OnEnable() => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    private void Start()
    {
        BuildingPlacementManager.Instance.OnActiveBuildingTypeSOChanged += BuildingPlacementManager_OnActiveBuildingTypeSOChanged;
    }

    private void BuildingPlacementManager_OnActiveBuildingTypeSOChanged(object sender, EventArgs e)
    {
        if (BuildingPlacementManager.Instance.GetActiveBuildingTypeSO() != GameAssets.Instance.buildingTypeListSO.none)
        {
            // Selected some building
            DeselectAllUnits();
        }
    }

    /// <summary>
    /// Unified pointer position. Uses fake HUD cursor if active, otherwise falls back to real mouse.
    /// </summary>
    private Vector2 GetPointerScreenPosition()
    {
        if (cursorUI != null && cursorUI.IsActive)
            return cursorUI.ScreenPosition;

        return new Vector2();
    }

    public void ObservedUpdate()
    {
        if (!BuildingPlacementManager.Instance.GetActiveBuildingTypeSO().IsNone())
            return;

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // =========================
        // LEFT MOUSE BUTTON UP (selection completed)
        // =========================
        if (Input.GetMouseButtonUp(0))
        {
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
                // Multiple select
                entityQuery = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<LocalTransform, Unit>()
                    .WithPresent<Selected>()
                    .Build(entityManager);

                entityArray = entityQuery.ToEntityArray(Allocator.Temp);
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
            else
            {
                // Single select
                entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
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
                        CollidesWith = (1u << GameAssets.UNITS_LAYER) | (1u << GameAssets.BUILDINGS_LAYER),
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

            OnSelectionAreaEnd?.Invoke(this, EventArgs.Empty);
            OnSelectedEntitiesChanged?.Invoke(this, EventArgs.Empty);
        }

        // =========================
        // UI hit-testing (still uses real EventSystem pointer)
        // =========================
        if (EventSystem.current.IsPointerOverGameObject())
            return;

        // =========================
        // LEFT MOUSE BUTTON DOWN (start selection)
        // =========================
        if (Input.GetMouseButtonDown(0))
        {
            selectionStartScreenPosition = GetPointerScreenPosition();
            OnSelectionAreaStart?.Invoke(this, EventArgs.Empty);
        }

        // =========================
        // RIGHT MOUSE BUTTON DOWN (move / attack)
        // =========================
        if (Input.GetMouseButtonDown(1))
        {
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
                    CollidesWith = (1u << GameAssets.UNITS_LAYER) | (1u << GameAssets.BUILDINGS_LAYER),
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
                        // Right clicking on a Zombie
                        isAttackingSingleTarget = true;

                        entityQuery = new EntityQueryBuilder(Allocator.Temp)
                            .WithAll<Selected>()
                            .WithPresent<TargetOverride>()
                            .Build(entityManager);

                        NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                        NativeArray<TargetOverride> targetOverrideArray = entityQuery.ToComponentDataArray<TargetOverride>(Allocator.Temp);

                        for (int i = 0; i < targetOverrideArray.Length; i++)
                        {
                            TargetOverride targetOverride = targetOverrideArray[i];
                            targetOverride.targetEntity = raycastHit.Entity;
                            targetOverrideArray[i] = targetOverride;
                            entityManager.SetComponentEnabled<MoveOverride>(entityArray[i], false);
                        }

                        entityQuery.CopyFromComponentDataArray(targetOverrideArray);
                    }
                }
            }

            // If not attacking a zombie, this is a move-command
            if (!isAttackingSingleTarget)
            {
                // Use the pointer position to get a move destination in world space
                Vector3 moveWorldPosition = GetWorldPositionFromScreen(pointerPos);

                entityQuery = new EntityQueryBuilder(Allocator.Temp)
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

            // Handle Barracks Rally Position
            {
                Vector3 rallyWorldPosition = GetWorldPositionFromScreen(pointerPos);

                entityQuery = new EntityQueryBuilder(Allocator.Temp)
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
}
