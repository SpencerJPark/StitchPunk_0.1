using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// E beside the driver's seat boards the caravan; E (ExitVehicle) while nearly stopped parks it and puts the
// player back on the ground (Caravan_System.md CV-D9). Runs before WorkstationMountSystem so the same E
// press near the board point drives off instead of mounting the work table at the back (CV-Q1).
[BurstCompile]
[UpdateInGroup(typeof(WorkstationInputSystemGroup))]
[UpdateAfter(typeof(CaravanLinkSystem))]
[UpdateBefore(typeof(WorkstationMountSystem))]
public partial struct CaravanBoardSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Caravan>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        foreach ((
            RefRW<DrivingCaravan> drivingCaravan,
            EnabledRefRW<DrivingCaravan> drivingCaravanEnabled,
            EnabledRefRW<OnExitVehicleInput> onExitVehicleInputEnabled,
            EnabledRefRW<OnInteractPlayerInput> onInteractPlayerInputEnabled,
            EnabledRefRO<WorkstationMounted> workstationMountedEnabled,
            EnabledRefRO<PlayerWorkstationPlacement> playerWorkstationPlacementEnabled,
            Entity playerEntity)
            in SystemAPI.Query<
                RefRW<DrivingCaravan>,
                EnabledRefRW<DrivingCaravan>,
                EnabledRefRW<OnExitVehicleInput>,
                EnabledRefRW<OnInteractPlayerInput>,
                EnabledRefRO<WorkstationMounted>,
                EnabledRefRO<PlayerWorkstationPlacement>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            bool exitVehiclePressed = onExitVehicleInputEnabled.ValueRO;
            onExitVehicleInputEnabled.ValueRW = false;

            if (drivingCaravanEnabled.ValueRO)
            {
                Entity drivenCaravan = drivingCaravan.ValueRO.caravan;
                if (!SystemAPI.Exists(drivenCaravan))
                {
                    drivingCaravanEnabled.ValueRW = false;
                    continue;
                }

                // A wrecked caravan throws the driver out and stays broken until repaired (CV-D13).
                bool caravanWrecked = SystemAPI.HasComponent<Health>(drivenCaravan)
                    && SystemAPI.GetComponent<Health>(drivenCaravan).healthAmount <= 0;
                // Knocked out at the reins: off the seat, or the drive system would keep re-seating the body.
                bool driverKnockedOut = SystemAPI.HasComponent<PlayerKnockedOut>(playerEntity)
                    && SystemAPI.IsComponentEnabled<PlayerKnockedOut>(playerEntity);
                caravanWrecked = caravanWrecked || driverKnockedOut;
                if (!exitVehiclePressed && !caravanWrecked) continue;

                float forwardSpeed = SystemAPI.GetComponent<CaravanDriveState>(drivenCaravan).forwardSpeed;
                float parkMaxSpeed = ParkMaxSpeedOf(ref state, drivenCaravan);
                if (!caravanWrecked && math.abs(forwardSpeed) > parkMaxSpeed)
                {
                    Debug.Log("Slow down to park the caravan.");
                    continue;
                }

                ParkAndStepOut(ref state, playerEntity, drivenCaravan);
                drivingCaravanEnabled.ValueRW = false;
                continue;
            }

            if (!onInteractPlayerInputEnabled.ValueRO
                || workstationMountedEnabled.ValueRO
                || playerWorkstationPlacementEnabled.ValueRO) continue;
            if (!SystemAPI.IsComponentEnabled<Target>(playerEntity)) continue;

            Entity targetEntity = SystemAPI.GetComponent<Target>(playerEntity).entity;
            if (!SystemAPI.HasComponent<Caravan>(targetEntity)) continue;
            Workstation caravanWorkstation = SystemAPI.GetComponent<Workstation>(targetEntity);
            if (caravanWorkstation.ownerPlayer != playerEntity) continue;
            if (!SystemAPI.IsComponentEnabled<CaravanParked>(targetEntity)) continue;

            // Behind the back doors, E is the workstation (WorkstationMountSystem); anywhere along the sides or the front it is the driver's seat (CV-D16).
            LocalTransform caravanTransform = SystemAPI.GetComponent<LocalTransform>(targetEntity);
            float3 playerLocalPosition = caravanTransform.InverseTransformPoint(SystemAPI.GetComponent<LocalTransform>(playerEntity).Position);
            if (playerLocalPosition.z < SystemAPI.GetComponent<Caravan>(targetEntity).tailgateEdgeLocalOffset.z) continue;

            // Consumed even when refused, so a refused board never falls through to the work table.
            onInteractPlayerInputEnabled.ValueRW = false;

            if (SystemAPI.IsComponentEnabled<CaravanBroken>(targetEntity))
            {
                Debug.Log("The caravan is broken: repair it with wood at the work table first.");
                continue;
            }

            bool summonInProgress = SystemAPI.GetBuffer<WorkstationSummonQueueEntry>(targetEntity).Length > 0
                || SystemAPI.IsComponentEnabled<CaravanExitSequence>(targetEntity);
            if (summonInProgress)
            {
                Debug.Log("Finish summoning before driving off.");
                continue;
            }

            drivingCaravan.ValueRW = new DrivingCaravan { caravan = targetEntity };
            drivingCaravanEnabled.ValueRW = true;
            SystemAPI.SetComponent(targetEntity, new CaravanDriving { driver = playerEntity });
            SystemAPI.SetComponentEnabled<CaravanDriving>(targetEntity, true);
            SystemAPI.SetComponentEnabled<CaravanParked>(targetEntity, false);
            // Start heading the way the body already faces, or the first frame would swing it toward world +Z.
            SystemAPI.SetComponent(targetEntity, new CaravanDriveState
            {
                headingDegrees = CaravanDriveMath.HeadingDegrees(math.rotate(caravanTransform.Rotation, new float3(0f, 0f, 1f))),
                wheelSpinRadians = SystemAPI.GetComponent<CaravanDriveState>(targetEntity).wheelSpinRadians,
            });
            SystemAPI.SetComponent(playerEntity, new CaravanDriveInput());
        }
    }

    // From the caravan's VehicleProfileSO; with no profile there is no speed limit on parking.
    private float ParkMaxSpeedOf(ref SystemState state, Entity caravanEntity)
    {
        if (!SystemAPI.TryGetSingleton(out VehicleLibrary vehicleLibrary) || !vehicleLibrary.library.IsCreated) return float.MaxValue;
        int vehicleIndex = (int)SystemAPI.GetComponent<Vehicle>(caravanEntity).vehicleType;
        if (vehicleIndex <= 0 || vehicleIndex >= vehicleLibrary.library.Value.vehicles.Length) return float.MaxValue;
        return vehicleLibrary.library.Value.vehicles[vehicleIndex].parkMaxSpeed;
    }

    private void ParkAndStepOut(ref SystemState state, Entity playerEntity, Entity caravanEntity)
    {
        SystemAPI.SetComponent(caravanEntity, new CaravanDriving { driver = Entity.Null });
        SystemAPI.SetComponentEnabled<CaravanDriving>(caravanEntity, false);
        SystemAPI.SetComponentEnabled<CaravanParked>(caravanEntity, true);

        RefRW<CaravanDriveState> driveState = SystemAPI.GetComponentRW<CaravanDriveState>(caravanEntity);
        driveState.ValueRW.forwardSpeed = 0f;
        driveState.ValueRW.headingTurnVelocity = 0f;
        driveState.ValueRW.yawRateDegrees = 0f;
        driveState.ValueRW.steerInput = 0f;

        LocalTransform caravanTransform = SystemAPI.GetComponent<LocalTransform>(caravanEntity);
        float3 stepOutPosition = caravanTransform.TransformPoint(SystemAPI.GetComponent<Caravan>(caravanEntity).boardPointLocalOffset);
        LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        playerTransform.Position = stepOutPosition;
        playerTransform.Rotation = caravanTransform.Rotation;
        SystemAPI.SetComponent(playerEntity, playerTransform);

        // The mover would otherwise walk the player back to wherever it last aimed before boarding.
        if (SystemAPI.HasComponent<Movement>(playerEntity))
            SystemAPI.GetComponentRW<Movement>(playerEntity).ValueRW.targetPosition = stepOutPosition;
        if (SystemAPI.HasComponent<Gravity>(playerEntity))
            SystemAPI.GetComponentRW<Gravity>(playerEntity).ValueRW.verticalVelocity = 0f;
        SystemAPI.SetComponent(playerEntity, new CaravanDriveInput());
    }
}
