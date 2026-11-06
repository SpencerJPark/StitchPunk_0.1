#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Plane = UnityEngine.Plane;
using Ray = UnityEngine.Ray;

public static class UnitInspectorPicker
{
    public const string ToggleName = "Unit inspector";

    private const uint UNITS_PHYSICS_LAYER = 6;
    private const float RAY_LENGTH = 1000f;
    private const float GROUND_FALLBACK_RADIUS = 1.5f;

    private static World cachedWorld;
    private static EntityQuery physicsWorldQuery;
    private static EntityQuery unitsQuery;
    private static EntityQuery watchedUnitQuery;

    public static Entity WatchedUnit
    {
        get
        {
            if (!TryPrepareWorld(out World world))
                return Entity.Null;
            if (watchedUnitQuery.IsEmptyIgnoreFilter)
                return Entity.Null;
            Entity watched = watchedUnitQuery.GetSingleton<DebugWatchedUnit>().unit;
            return world.EntityManager.Exists(watched) ? watched : Entity.Null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedWorld = null;
        physicsWorldQuery = default;
        unitsQuery = default;
        watchedUnitQuery = default;
    }

    public static void Tick(Rect panelScreenRect)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        Vector2 mousePosition = mouse.position.ReadValue();
        // OnGUI rects are y-down, the Input System position is y-up.
        Vector2 mouseInGuiSpace = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        if (panelScreenRect.Contains(mouseInGuiSpace))
            return;

        if (!TryPrepareWorld(out World world))
            return;

        Entity pickedUnit = FindUnitUnderMouse(world, mousePosition);
        if (pickedUnit == Entity.Null)
            ClearWatchedUnit();
        else
            SetWatchedUnit(world.EntityManager, pickedUnit);
    }

    public static void ClearWatchedUnit()
    {
        if (!TryPrepareWorld(out World world))
            return;
        EntityManager entityManager = world.EntityManager;
        if (watchedUnitQuery.IsEmptyIgnoreFilter)
            return;

        Entity watchedSingleton = watchedUnitQuery.GetSingletonEntity();
        RemoveHistoryBuffer(entityManager, watchedUnitQuery.GetSingleton<DebugWatchedUnit>().unit);
        entityManager.SetComponentData(watchedSingleton, new DebugWatchedUnit { unit = Entity.Null });
    }

    private static bool TryPrepareWorld(out World world)
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        if (world != cachedWorld)
        {
            EntityManager entityManager = world.EntityManager;
            physicsWorldQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
            unitsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<UnitData>(), ComponentType.ReadOnly<LocalTransform>());
            watchedUnitQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DebugWatchedUnit>());
            cachedWorld = world;
        }
        return true;
    }

    private static Entity FindUnitUnderMouse(World world, Vector2 mousePosition)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return Entity.Null;

        EntityManager entityManager = world.EntityManager;
        Ray mouseRay = mainCamera.ScreenPointToRay(mousePosition);

        if (!physicsWorldQuery.IsEmptyIgnoreFilter)
        {
            CollisionWorld collisionWorld = physicsWorldQuery.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            RaycastInput rayInput = new RaycastInput
            {
                Start = mouseRay.origin,
                End = mouseRay.origin + mouseRay.direction * RAY_LENGTH,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = 1u << (int)UNITS_PHYSICS_LAYER,
                },
            };
            if (collisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit)
                && entityManager.Exists(hit.Entity)
                && entityManager.HasComponent<UnitData>(hit.Entity))
            {
                return hit.Entity;
            }
        }

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (!groundPlane.Raycast(mouseRay, out float rayDistance))
            return Entity.Null;
        float3 groundPoint = mouseRay.GetPoint(rayDistance);

        NativeArray<Entity> candidates = unitsQuery.ToEntityArray(Allocator.Temp);
        Entity nearestUnit = Entity.Null;
        float nearestDistanceSq = GROUND_FALLBACK_RADIUS * GROUND_FALLBACK_RADIUS;
        foreach (Entity candidate in candidates)
        {
            float distanceSq = math.distancesq(entityManager.GetComponentData<LocalTransform>(candidate).Position, groundPoint);
            if (distanceSq > nearestDistanceSq)
                continue;
            nearestDistanceSq = distanceSq;
            nearestUnit = candidate;
        }
        candidates.Dispose();
        return nearestUnit;
    }

    private static void SetWatchedUnit(EntityManager entityManager, Entity unit)
    {
        Entity watchedSingleton;
        if (watchedUnitQuery.IsEmptyIgnoreFilter)
        {
            watchedSingleton = entityManager.CreateEntity(typeof(DebugWatchedUnit));
        }
        else
        {
            watchedSingleton = watchedUnitQuery.GetSingletonEntity();
            Entity previousUnit = entityManager.GetComponentData<DebugWatchedUnit>(watchedSingleton).unit;
            if (previousUnit != unit)
                RemoveHistoryBuffer(entityManager, previousUnit);
        }

        entityManager.SetComponentData(watchedSingleton, new DebugWatchedUnit { unit = unit });
        if (!entityManager.HasBuffer<DecisionHistoryEntry>(unit))
            entityManager.AddBuffer<DecisionHistoryEntry>(unit);
    }

    private static void RemoveHistoryBuffer(EntityManager entityManager, Entity unit)
    {
        if (unit != Entity.Null && entityManager.Exists(unit) && entityManager.HasBuffer<DecisionHistoryEntry>(unit))
            entityManager.RemoveComponent<DecisionHistoryEntry>(unit);
    }
}
#endif
