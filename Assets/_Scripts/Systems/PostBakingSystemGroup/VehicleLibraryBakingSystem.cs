using System.Collections.Generic;
using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct VehicleLibraryBakingSystem : ISystem
{
    private const int DIRECTION_COUNT = 8;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<VehicleLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        VehicleLibrarySO librarySO = null;
        foreach (RefRO<VehicleLibraryReference> reference in SystemAPI.Query<RefRO<VehicleLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        Dictionary<int, VehicleProfileSO> profileByType =
            BlobLibraryUtils.BuildEnumLookup(librarySO.vehicles ?? new VehicleProfileSO[0], profile => (int)profile.vehicleType);
        int typeCount = BlobLibraryUtils.EnumCount<VehicleType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref VehicleLibraryBlob root = ref builder.ConstructRoot<VehicleLibraryBlob>();
        BlobBuilderArray<VehicleProfileBlob> vehiclesBuilder = builder.Allocate(ref root.vehicles, typeCount);

        for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
        {
            ref VehicleProfileBlob blob = ref vehiclesBuilder[typeIndex];
            if (!profileByType.TryGetValue(typeIndex, out VehicleProfileSO profile))
            {
                blob.vehicleType = (VehicleType)typeIndex;
                builder.Allocate(ref blob.driverSeatOffsets, 0);
                builder.Allocate(ref blob.pullerOffsets, 0);
                continue;
            }

            blob.vehicleType = profile.vehicleType;
            blob.propulsion = profile.propulsion;
            blob.moveSpeed = profile.moveSpeed;
            blob.turnSpeed = profile.turnSpeed;
            blob.forwardAcceleration = profile.forwardAcceleration;
            blob.forwardDeceleration = profile.forwardDeceleration;
            blob.idleTurnSpeedFactor = profile.idleTurnSpeedFactor;
            blob.turnSmoothTime = profile.turnSmoothTime;
            blob.parkMaxSpeed = profile.parkMaxSpeed;
            blob.wheelRadius = profile.wheelRadius;

            blob.frontBumperLocalZ = profile.frontBumperLocalZ;
            blob.rearBumperLocalZ = profile.rearBumperLocalZ;
            blob.halfWidth = profile.halfWidth;
            blob.bumperGroundClearance = profile.bumperGroundClearance;

            blob.minimumImpactSpeed = profile.minimumImpactSpeed;
            blob.damagePerMetrePerSecond = profile.damagePerMetrePerSecond;
            blob.stunSecondsPerMetrePerSecond = profile.stunSecondsPerMetrePerSecond;
            blob.shoveDistance = profile.shoveDistance;
            blob.shoveSeconds = profile.shoveSeconds;
            blob.selfDamagePerHit = profile.selfDamagePerHit;
            blob.speedLossPerHit = profile.speedLossPerHit;
            blob.rehitCooldownSeconds = profile.rehitCooldownSeconds;

            blob.rideMaxSpeed = profile.rideMaxSpeed;
            blob.speedTiltDegrees = profile.speedTiltDegrees;
            blob.rideNoiseFrequency = profile.rideNoiseFrequency;
            blob.turnTiltDegrees = profile.turnTiltDegrees;
            blob.maxYawRate = profile.maxYawRate;
            blob.pitchRangeDegrees = profile.pitchRangeDegrees;
            blob.rollRangeDegrees = profile.rollRangeDegrees;

            BlobBuilderArray<float3> seatBuilder = builder.Allocate(ref blob.driverSeatOffsets, DIRECTION_COUNT);
            for (int directionIndex = 0; directionIndex < DIRECTION_COUNT; directionIndex++)
                seatBuilder[directionIndex] = profile.driverSeat.Get((Direction)directionIndex);

            bool hasPuller = profile.propulsion == VehiclePropulsion.Drawn;
            BlobBuilderArray<float3> pullerBuilder = builder.Allocate(ref blob.pullerOffsets, hasPuller ? DIRECTION_COUNT : 0);
            for (int directionIndex = 0; hasPuller && directionIndex < DIRECTION_COUNT; directionIndex++)
                pullerBuilder[directionIndex] = profile.puller.Get((Direction)directionIndex);
            blob.walkThreshold = profile.walkThreshold;
            blob.runThreshold = profile.runThreshold;

            if (profile.runThreshold < profile.walkThreshold)
                Debug.LogWarning($"[VehicleLibraryBaking] {profile.name}: runThreshold is below walkThreshold, so the puller never walks.", profile);
        }

        BlobAssetReference<VehicleLibraryBlob> blobReference =
            builder.CreateBlobAssetReference<VehicleLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<VehicleLibrary> holder in SystemAPI.Query<RefRW<VehicleLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobReference;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<VehicleLibrary> holder in SystemAPI.Query<RefRW<VehicleLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }
}
