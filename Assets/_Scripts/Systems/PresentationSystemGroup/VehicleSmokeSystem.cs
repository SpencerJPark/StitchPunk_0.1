using Unity.Entities;
using UnityEngine;

// Starts and stops a baked vehicle's companion ParticleSystems (Caravan_System.md CV-D21): the chimney while
// wood burns, the broken smoke while it's at 0 HP, and one smoke burst per CaravanPresentationEvents tick.
// Not [BurstCompile]: ParticleSystem is a managed companion object.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct VehicleSmokeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<VehicleSmoke>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        foreach ((RefRW<VehicleSmoke> vehicleSmoke, RefRO<CaravanPresentationEvents> presentationEvents, Entity vehicleEntity)
                 in SystemAPI.Query<RefRW<VehicleSmoke>, RefRO<CaravanPresentationEvents>>().WithEntityAccess())
        {
            bool chimneySmoking = SystemAPI.HasComponent<CaravanChimneySmoking>(vehicleEntity) && SystemAPI.IsComponentEnabled<CaravanChimneySmoking>(vehicleEntity);
            bool broken = SystemAPI.HasComponent<CaravanBroken>(vehicleEntity) && SystemAPI.IsComponentEnabled<CaravanBroken>(vehicleEntity);
            SetLooping(entityManager, vehicleSmoke.ValueRO.chimneySmoke, chimneySmoking);
            SetLooping(entityManager, vehicleSmoke.ValueRO.brokenSmoke, broken);

            int smokeBurstCount = presentationEvents.ValueRO.smokeBurstCount;
            if (!vehicleSmoke.ValueRO.hasSeenSmokeBurstCount)
            {
                vehicleSmoke.ValueRW.lastSeenSmokeBurstCount = smokeBurstCount;
                vehicleSmoke.ValueRW.hasSeenSmokeBurstCount = true;
                continue;
            }
            if (smokeBurstCount == vehicleSmoke.ValueRO.lastSeenSmokeBurstCount) continue;
            vehicleSmoke.ValueRW.lastSeenSmokeBurstCount = smokeBurstCount;
            ParticleSystem burst = ParticleSystemOf(entityManager, vehicleSmoke.ValueRO.smokeBurst);
            if (burst != null) burst.Play(true);
        }
    }

    private static void SetLooping(EntityManager entityManager, Entity smokeEntity, bool emitting)
    {
        ParticleSystem smoke = ParticleSystemOf(entityManager, smokeEntity);
        if (smoke == null) return;
        if (emitting && !smoke.isEmitting) smoke.Play(true);
        else if (!emitting && smoke.isEmitting) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private static ParticleSystem ParticleSystemOf(EntityManager entityManager, Entity smokeEntity)
    {
        if (smokeEntity == Entity.Null || !entityManager.HasComponent<ParticleSystem>(smokeEntity)) return null;
        return entityManager.GetComponentObject<ParticleSystem>(smokeEntity);
    }
}
