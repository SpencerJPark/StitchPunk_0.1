using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// PlayerInputManager watches WorkstationMounted to switch action map/camera (CampLoop_System.md CL-A4).
[BurstCompile]
[UpdateInGroup(typeof(WorkstationInputSystemGroup))]
[UpdateAfter(typeof(WorkstationDeploySystem))]
public partial struct WorkstationMountSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((
            RefRW<WorkstationMounted> workstationMounted,
            EnabledRefRW<WorkstationMounted> workstationMountedEnabled,
            EnabledRefRW<WorkstationDismountRequest> workstationDismountRequestEnabled,
            EnabledRefRW<OnInteractPlayerInput> onInteractPlayerInputEnabled,
            EnabledRefRO<PlayerWorkstationPlacement> playerWorkstationPlacementEnabled,
            Entity playerEntity)
            in SystemAPI.Query<
                RefRW<WorkstationMounted>,
                EnabledRefRW<WorkstationMounted>,
                EnabledRefRW<WorkstationDismountRequest>,
                EnabledRefRW<OnInteractPlayerInput>,
                EnabledRefRO<PlayerWorkstationPlacement>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            bool dismountRequested = workstationDismountRequestEnabled.ValueRO;
            workstationDismountRequestEnabled.ValueRW = false;

            bool playerHasHealth = SystemAPI.HasComponent<Health>(playerEntity);

            if (workstationMountedEnabled.ValueRO)
            {
                Entity mountedWorkstation = workstationMounted.ValueRO.workstation;
                bool workstationGone = !SystemAPI.Exists(mountedWorkstation);
                bool tookDamage = playerHasHealth
                    && SystemAPI.GetComponent<Health>(playerEntity).healthAmount < workstationMounted.ValueRO.healthWhenMounted;

                if (dismountRequested || tookDamage || workstationGone)
                {
                    workstationMountedEnabled.ValueRW = false;
                }

                continue;
            }

            if (!onInteractPlayerInputEnabled.ValueRO || playerWorkstationPlacementEnabled.ValueRO)
            {
                continue;
            }

            if (!SystemAPI.IsComponentEnabled<Target>(playerEntity))
            {
                continue;
            }

            Entity targetEntity = SystemAPI.GetComponent<Target>(playerEntity).entity;
            if (!SystemAPI.HasComponent<Workstation>(targetEntity))
            {
                continue;
            }

            Workstation workstation = SystemAPI.GetComponent<Workstation>(targetEntity);
            if (workstation.ownerPlayer != playerEntity)
            {
                continue;
            }

            LocalTransform workstationTransform = SystemAPI.GetComponent<LocalTransform>(targetEntity);
            LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
            playerTransform.Position = workstationTransform.Position
                + math.rotate(workstationTransform.Rotation, workstation.mountLocalOffset);
            SystemAPI.SetComponent(playerEntity, playerTransform);

            workstationMounted.ValueRW = new WorkstationMounted
            {
                workstation = targetEntity,
                healthWhenMounted = playerHasHealth ? SystemAPI.GetComponent<Health>(playerEntity).healthAmount : int.MinValue,
            };
            workstationMountedEnabled.ValueRW = true;
            onInteractPlayerInputEnabled.ValueRW = false;
        }
    }
}
