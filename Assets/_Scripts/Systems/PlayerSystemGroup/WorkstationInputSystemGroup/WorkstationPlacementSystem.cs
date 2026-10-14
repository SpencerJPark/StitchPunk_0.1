using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(WorkstationInputSystemGroup))]
public partial struct WorkstationPlacementSystem : ISystem
{
    private const float PlacementDistanceInFrontOfPlayer = 3f;
    private const float PlacementRayHalfHeight = 5f;
    private const float MinimumSurfaceNormalY = 0.8f;
    private const float MinimumDistanceToOtherWorkstation = 6f;

    public void OnUpdate(ref SystemState state)
    {
        bool hasPhysicsWorld = SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out PhysicsWorldSingleton physicsWorldSingleton);

        foreach ((
            RefRO<LocalTransform> playerTransform,
            RefRO<PlayerWorkstationLink> workstationLink,
            RefRW<PlayerWorkstationPlacement> placement,
            EnabledRefRW<PlayerWorkstationPlacement> playerWorkstationPlacementEnabled,
            EnabledRefRW<OnDeployWorkstationInput> onDeployWorkstationInputEnabled,
            EnabledRefRO<WorkstationMounted> workstationMountedEnabled)
            in SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<PlayerWorkstationLink>,
                RefRW<PlayerWorkstationPlacement>,
                EnabledRefRW<PlayerWorkstationPlacement>,
                EnabledRefRW<OnDeployWorkstationInput>,
                EnabledRefRO<WorkstationMounted>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (onDeployWorkstationInputEnabled.ValueRO)
            {
                onDeployWorkstationInputEnabled.ValueRW = false;

                if (playerWorkstationPlacementEnabled.ValueRO)
                {
                    playerWorkstationPlacementEnabled.ValueRW = false;
                }
                else
                {
                    Entity linkedWorkstation = workstationLink.ValueRO.workstation;
                    bool alreadyHasWorkstation = linkedWorkstation != Entity.Null && state.EntityManager.Exists(linkedWorkstation);
                    if (alreadyHasWorkstation)
                    {
                        Debug.Log("Workstation already deployed; pack it up before deploying another.");
                    }
                    else if (workstationMountedEnabled.ValueRO)
                    {
                        Debug.Log("Cannot deploy a workstation while mounted.");
                    }
                    else
                    {
                        playerWorkstationPlacementEnabled.ValueRW = true;
                    }
                }
            }

            if (!playerWorkstationPlacementEnabled.ValueRO)
            {
                continue;
            }

            float3 flattenedForward = math.mul(playerTransform.ValueRO.Rotation, new float3(0f, 0f, 1f));
            flattenedForward.y = 0f;
            flattenedForward = math.lengthsq(flattenedForward) > 0.0001f
                ? math.normalize(flattenedForward)
                : new float3(0f, 0f, 1f);

            float3 candidatePosition = playerTransform.ValueRO.Position + flattenedForward * PlacementDistanceInFrontOfPlayer;
            float3 up = new float3(0f, 1f, 0f);
            float3 placementPosition = candidatePosition;
            bool isValid = false;

            if (hasPhysicsWorld)
            {
                RaycastInput raycastInput = new RaycastInput
                {
                    Start = candidatePosition + up * PlacementRayHalfHeight,
                    End = candidatePosition - up * PlacementRayHalfHeight,
                    Filter = CollisionFilter.Default
                };

                Unity.Physics.RaycastHit hit;
                if (physicsWorldSingleton.CollisionWorld.CastRay(raycastInput, out hit))
                {
                    placementPosition = hit.Position;
                    // The ray also hits units (and the player); a station never stands on someone's head.
                    bool hitIsUnit = SystemAPI.HasComponent<UnitData>(hit.Entity) || SystemAPI.HasComponent<Player>(hit.Entity);
                    isValid = !hitIsUnit && hit.SurfaceNormal.y >= MinimumSurfaceNormalY;
                }
            }

            if (isValid)
            {
                float minimumDistanceSquared = MinimumDistanceToOtherWorkstation * MinimumDistanceToOtherWorkstation;
                foreach (RefRO<LocalTransform> otherWorkstationTransform
                    in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Workstation>())
                {
                    if (math.distancesq(otherWorkstationTransform.ValueRO.Position, placementPosition) < minimumDistanceSquared)
                    {
                        isValid = false;
                        break;
                    }
                }
            }

            placement.ValueRW.position = placementPosition;
            placement.ValueRW.rotation = quaternion.LookRotationSafe(flattenedForward, up);
            placement.ValueRW.isValid = isValid;
        }
    }
}
