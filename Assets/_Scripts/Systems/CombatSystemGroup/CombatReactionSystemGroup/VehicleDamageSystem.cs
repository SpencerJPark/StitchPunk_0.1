using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// How a vehicle takes hits (Caravan_System.md CV-D19), by its VehicleProfileSO. Thrown things (rocks) barely scratch
// it and never slow it; any other single hit at or above heavyHitDamageThreshold eats momentum in proportion, and
// stops it dead at fullStopHitDamage. Sits between DamageResolutionSystem and DamageEventSystem: it drains the
// resolved queue, adjusts events aimed at vehicles, and puts every event back.
[BurstCompile]
[UpdateInGroup(typeof(CombatReactionSystemGroup))]
[UpdateAfter(typeof(DamageResolutionSystem))]
[UpdateBefore(typeof(DamageEventSystem))]
public partial struct VehicleDamageSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DamageBus>();
        state.RequireForUpdate<VehicleLibrary>();
        state.RequireForUpdate<Vehicle>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DamageBus damageBus = SystemAPI.GetSingleton<DamageBus>();
        if (damageBus.resolved.Count == 0) return;
        BlobAssetReference<VehicleLibraryBlob> vehicleLibrary = SystemAPI.GetSingleton<VehicleLibrary>().library;
        if (!vehicleLibrary.IsCreated) return;
        // Main-thread lookup writes below wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        NativeList<DamageEvent> damageEvents = new NativeList<DamageEvent>(damageBus.resolved.Count, Allocator.Temp);
        while (damageBus.resolved.TryDequeue(out DamageEvent damageEvent))
            damageEvents.Add(damageEvent);

        for (int eventIndex = 0; eventIndex < damageEvents.Length; eventIndex++)
        {
            DamageEvent damageEvent = damageEvents[eventIndex];
            Entity target = damageEvent.targetEntity;
            if (!SystemAPI.HasComponent<Vehicle>(target)) continue;

            int vehicleIndex = (int)SystemAPI.GetComponent<Vehicle>(target).vehicleType;
            if (vehicleIndex <= 0 || vehicleIndex >= vehicleLibrary.Value.vehicles.Length) continue;
            ref VehicleProfileBlob profile = ref vehicleLibrary.Value.vehicles[vehicleIndex];

            if (damageEvent.damageSource == DamageSource.Throw)
            {
                damageEvent.damageAmount = (int)math.round(damageEvent.damageAmount * profile.thrownDamageMultiplier);
                damageEvents[eventIndex] = damageEvent;
                continue;
            }

            if (!SystemAPI.HasComponent<CaravanDriveState>(target)) continue;
            // Below the threshold this saturates to zero: small hits cost nothing.
            float hitRange = math.max(1f, profile.fullStopHitDamage - profile.heavyHitDamageThreshold);
            float momentumLost = math.saturate((damageEvent.damageAmount - profile.heavyHitDamageThreshold) / hitRange);
            SystemAPI.GetComponentRW<CaravanDriveState>(target).ValueRW.forwardSpeed *= 1f - momentumLost;
        }

        for (int eventIndex = 0; eventIndex < damageEvents.Length; eventIndex++)
            damageBus.resolved.Enqueue(damageEvents[eventIndex]);
        damageEvents.Dispose();
    }
}
