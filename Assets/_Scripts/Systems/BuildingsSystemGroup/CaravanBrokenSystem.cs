using Unity.Burst;
using Unity.Entities;
using UnityEngine;

// A caravan at 0 HP is broken, not lost (Caravan_System.md CV-D13): CaravanBoardSystem throws the driver out and
// refuses to board, WorkstationSummonSystem stops building. A wood repair lifts it straight back to working.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(WorkstationRepairSystem))]
[UpdateBefore(typeof(WorkstationSummonSystem))]
public partial struct CaravanBrokenSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((RefRO<Health> health, EnabledRefRW<CaravanBroken> caravanBrokenEnabled)
                 in SystemAPI.Query<RefRO<Health>, EnabledRefRW<CaravanBroken>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            bool broken = health.ValueRO.healthAmount <= 0;
            if (broken == caravanBrokenEnabled.ValueRO) continue;
            caravanBrokenEnabled.ValueRW = broken;
            if (broken)
                Debug.Log("Caravan: broken. Repair it with wood before it can drive or summon again.");
            else
                Debug.Log("Caravan: repaired.");
        }
    }
}
