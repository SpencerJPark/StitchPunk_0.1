using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throwaway OnGUI test menu for the zombie conversion — point at a citizen and press a button.
/// Drop it on any GameObject in the scene; it enables <c>ZombifyRequest</c> on living units and
/// <c>ZombifySystem</c> does the rest. Delete once conversion has a real in-game trigger.
/// </summary>
public class DebugZombifyMenu : MonoBehaviour
{
    [Tooltip("Seconds between the request and the conversion. 0 converts on the same frame.")]
    [SerializeField] private float conversionDelaySeconds;

    [Tooltip("Radius around the mouse used by the area button, in world units.")]
    [SerializeField] private float areaRadius = 6f;

    private EntityManager entityManager;
    private EntityQuery convertibleQuery;
    private bool worldReady;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;

        // IgnoreComponentEnabledState: ZombifyRequest is baked disabled, so a normal query would
        // match only units already mid-conversion. Life state is checked per candidate instead.
        convertibleQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<ZombifyRequest, UnitData, LocalTransform>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build(entityManager);

        worldReady = true;
    }

    private void OnGUI()
    {
        if (!worldReady || World.DefaultGameObjectInjectionWorld == null)
            return;

        GUILayout.BeginArea(new Rect(180f, 10f, 200f, 200f), GUI.skin.box);
        GUILayout.Label("Zombify (debug)");

        GUILayout.Label($"Delay {conversionDelaySeconds:0.0}s");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-")) conversionDelaySeconds = Mathf.Max(0f, conversionDelaySeconds - 0.5f);
        if (GUILayout.Button("+")) conversionDelaySeconds += 0.5f;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Nearest to mouse", GUILayout.Height(32f)))
            ConvertUnits(float.MaxValue, nearestOnly: true);

        if (GUILayout.Button($"All within {areaRadius:0}m", GUILayout.Height(32f)))
            ConvertUnits(areaRadius, nearestOnly: false);

        GUILayout.EndArea();
    }

    private void ConvertUnits(float maxDistance, bool nearestOnly)
    {
        if (!TryGetMouseWorldPosition(out float3 mousePosition))
            return;

        NativeArray<Entity> candidates = convertibleQuery.ToEntityArray(Allocator.Temp);

        Entity nearestEntity = Entity.Null;
        float nearestDistanceSq = float.MaxValue;
        int convertedCount = 0;

        foreach (Entity candidate in candidates)
        {
            if (!IsConvertible(candidate))
                continue;

            float3 candidatePosition = entityManager.GetComponentData<LocalTransform>(candidate).Position;
            float distanceSq = math.distancesq(candidatePosition, mousePosition);
            if (distanceSq > maxDistance * maxDistance)
                continue;

            if (nearestOnly)
            {
                if (distanceSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nearestEntity     = candidate;
                }
                continue;
            }

            RequestConversion(candidate);
            convertedCount++;
        }

        candidates.Dispose();

        if (nearestOnly && nearestEntity != Entity.Null)
        {
            RequestConversion(nearestEntity);
            convertedCount = 1;
        }

        Debug.Log($"[DebugZombifyMenu] Requested conversion on {convertedCount} unit(s).");
    }

    // Living, not already converting, and still resolvable — the same conditions ZombifySystem
    // filters on, checked here so the log line reports what actually happened.
    private bool IsConvertible(Entity entity)
    {
        if (!entityManager.Exists(entity))
            return false;
        if (entityManager.HasComponent<Dead>(entity) && entityManager.IsComponentEnabled<Dead>(entity))
            return false;
        return !entityManager.IsComponentEnabled<ZombifyRequest>(entity);
    }

    private void RequestConversion(Entity entity)
    {
        entityManager.SetComponentData(entity, new ZombifyRequest
        {
            // None = convert into the unit's authored becomesUnitType.
            targetUnitType = UnitType.None,
            delaySeconds   = conversionDelaySeconds,
        });
        entityManager.SetComponentEnabled<ZombifyRequest>(entity, true);
    }

    private bool TryGetMouseWorldPosition(out float3 worldPosition)
    {
        worldPosition = float3.zero;

        Camera mainCamera = Camera.main;
        if (mainCamera == null || Mouse.current == null)
            return false;

        Ray mouseRay = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        if (!groundPlane.Raycast(mouseRay, out float rayDistance))
            return false;

        Vector3 groundHit = mouseRay.GetPoint(rayDistance);
        worldPosition = new float3(groundHit.x, groundHit.y, groundHit.z);
        return true;
    }
}
