using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throwaway P9 play-check tool: sends the unit nearest the player (abstract or active) on a straight route across the streamed world
/// (AbstractTravel.BeginRoute), then shows whether it is abstract or active. Drop it on any scene object; F9 or the button.
/// </summary>
public class TerrainDebugRoute : MonoBehaviour
{
    [Tooltip("The route ends this far from the world centre on the opposite side of the unit.")]
    [SerializeField] private float routeEndDistanceFromCentreMetres = 140f;

    [Tooltip("Walking speed of the route. A quick test wants more than a citizen's stroll.")]
    [SerializeField] private float routeSpeedMetresPerSecond = 4f;

    private const string ToggleName = "Terrain route test";

    private EntityManager entityManager;
    private EntityQuery candidateQuery;
    private EntityQuery focusQuery;
    private EntityQuery clockQuery;
    private bool worldReady;
    private Entity trackedUnit = Entity.Null;
    private float trackedRouteLengthMetres;
    private string lastMessage = "";

    private void Awake()
    {
        DebugMenuVisibility.RegisterToggle(ToggleName);
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        candidateQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<AbstractRoute, AbstractSimulation, LocalTransform>()
            .WithNone<PinnedActive, TerrainNavFocus>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build(entityManager);
        focusQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TerrainNavFocus, LocalTransform>().Build(entityManager);
        clockQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<WorldClock>().Build(entityManager);
        worldReady = true;
    }

    private void Update()
    {
        if (!DebugMenuVisibility.IsToggleOn(ToggleName))
            return;

        if (worldReady && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            SendNearestUnitOnRoute();
    }

    private void OnGUI()
    {
        if (!worldReady || !DebugMenuVisibility.IsToggleOn(ToggleName))
            return;

        GUILayout.BeginArea(new Rect(10f, 220f, 260f, 150f), GUI.skin.box);
        GUILayout.Label("Terrain route test (F9)");
        if (GUILayout.Button("Send nearest unit across the world", GUILayout.Height(32f)))
            SendNearestUnitOnRoute();
        GUILayout.Label(lastMessage);
        if (trackedUnit != Entity.Null && entityManager.Exists(trackedUnit))
        {
            bool isAbstract = entityManager.IsComponentEnabled<AbstractSimulation>(trackedUnit);
            float3 position = entityManager.GetComponentData<LocalTransform>(trackedUnit).Position;
            GUILayout.Label(isAbstract ? "tracked: ABSTRACT (position is stale)" : $"tracked: ACTIVE at {position.x:0},{position.z:0}");
        }
        GUILayout.EndArea();
    }

    // An abstract unit's transform is stale; its rest anchor is where it really is. One already on a route is left alone.
    private bool TryGetUnitPosition(Entity unit, out float3 position)
    {
        position = entityManager.GetComponentData<LocalTransform>(unit).Position;
        if (!entityManager.IsComponentEnabled<AbstractSimulation>(unit))
            return true;
        if (entityManager.GetComponentData<AbstractRoute>(unit).totalLengthMetres > 0f)
            return false;
        position = entityManager.GetComponentData<AbstractRestAnchor>(unit).position;
        return true;
    }

    private void SendNearestUnitOnRoute()
    {
        if (focusQuery.IsEmpty || clockQuery.IsEmpty)
        {
            lastMessage = "no player focus or world clock yet";
            return;
        }

        float3 playerPosition = focusQuery.GetSingleton<LocalTransform>().Position;
        NativeArray<Entity> candidates = candidateQuery.ToEntityArray(Allocator.Temp);
        Entity nearestUnit = Entity.Null;
        float nearestDistanceSquared = float.MaxValue;
        foreach (Entity candidate in candidates)
        {
            if (entityManager.IsComponentEnabled<Dead>(candidate) || entityManager.IsComponentEnabled<PlayerUnitBrain>(candidate))
                continue;
            if (!TryGetUnitPosition(candidate, out float3 candidatePosition))
                continue;
            float distanceSquared = math.distancesq(candidatePosition, playerPosition);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestUnit = candidate;
            }
        }
        candidates.Dispose();

        if (nearestUnit == Entity.Null)
        {
            lastMessage = "no unit to send";
            return;
        }

        TryGetUnitPosition(nearestUnit, out float3 start);
        float sideSign = start.x >= 0f ? -1f : 1f;
        float3 destination = new float3(sideSign * routeEndDistanceFromCentreMetres, start.y, start.z);
        float speed = routeSpeedMetresPerSecond;

        NativeArray<float3> waypoints = new NativeArray<float3>(2, Allocator.Temp);
        waypoints[0] = start;
        waypoints[1] = destination;
        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        AbstractTravel.BeginRoute(commandBuffer, nearestUnit, in waypoints, speed, clockQuery.GetSingleton<WorldClock>().totalWorldSeconds, AbstractRouteEnd.Stop);
        commandBuffer.Playback(entityManager);
        commandBuffer.Dispose();
        waypoints.Dispose();

        trackedUnit = nearestUnit;
        trackedRouteLengthMetres = math.distance(start, destination);
        lastMessage = $"route {trackedRouteLengthMetres:0} m at {speed:0.0} m/s, about {trackedRouteLengthMetres / speed:0} s";
    }
}
