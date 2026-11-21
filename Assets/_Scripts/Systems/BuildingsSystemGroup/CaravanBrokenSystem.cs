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

// the zoom out doesnt work anymore, the hatch looks bad on moving objects, it maybe should only exist for static, the hatch also moves when the camera turns which looks bad, it casts really weird on characters being static on them when they are in shade, overall it has a very bad feel to it, I also think the hatch should be smaller and finer like the example, still too big in general, it also fully disapears when zoomed out which is noticable and looks bad.