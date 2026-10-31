using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throwaway P9 play-check tool: sends the unit nearest the player on a straight route across the streamed world
/// (AbstractTravel.BeginRoute), then shows whether it is abstract or active. Drop it on any scene object; F9 or the button.
/// </summary>
public class TerrainDebugRoute : MonoBehaviour
{
    [Tooltip("The route ends this far from the world centre on the opposite side of the unit.")]
    [SerializeField] private float routeEndDistanceFromCentreMetres = 140f;

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
        if (worldReady && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            SendNearestUnitOnRoute();
    }

    private void OnGUI()
    {
        if (!worldReady)
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
            if (entityManager.IsComponentEnabled<Dead>(candidate) || entityManager.IsComponentEnabled<AbstractSimulation>(candidate))
                continue;
            float distanceSquared = math.distancesq(entityManager.GetComponentData<LocalTransform>(candidate).Position, playerPosition);
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

        float3 start = entityManager.GetComponentData<LocalTransform>(nearestUnit).Position;
        float sideSign = start.x >= 0f ? -1f : 1f;
        float3 destination = new float3(sideSign * routeEndDistanceFromCentreMetres, start.y, start.z);
        float speed = 2f;
        if (entityManager.HasComponent<DotsMovementToolkit.Movement>(nearestUnit))
            speed = math.max(0.5f, entityManager.GetComponentData<DotsMovementToolkit.Movement>(nearestUnit).moveSpeed);

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
