using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct BanditCampSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BanditCamp>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NativeHashMap<Entity, int> livingGuardsPerCamp = new NativeHashMap<Entity, int>(16, Allocator.Temp);
        foreach (RefRO<GuardPost> guardPost in SystemAPI.Query<RefRO<GuardPost>>().WithDisabled<Dead>())
        {
            Entity camp = guardPost.ValueRO.camp;
            livingGuardsPerCamp.TryGetValue(camp, out int count);
            livingGuardsPerCamp[camp] = count + 1;
        }

        NativeList<Entity> campsClearedThisFrame = new NativeList<Entity>(Allocator.Temp);
        foreach ((RefRW<BanditCamp> camp, Entity campEntity) in SystemAPI.Query<RefRW<BanditCamp>>().WithEntityAccess())
        {
            livingGuardsPerCamp.TryGetValue(campEntity, out int aliveGuards);
            int previousAliveGuards = camp.ValueRO.aliveGuards;
            camp.ValueRW.aliveGuards = aliveGuards;

            // Only a camp that has seen a living guard can be cleared, so a camp whose guards have not spawned yet stays locked.
            if (!camp.ValueRO.cleared && previousAliveGuards > 0 && aliveGuards == 0)
            {
                camp.ValueRW.cleared = true;
                campsClearedThisFrame.Add(campEntity);
                Debug.Log("[BanditCamp] Camp cleared — loot unlocked.");
            }
        }

        if (campsClearedThisFrame.Length == 0)
            return;

        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<CampLootLocked> lootLocked, Entity lootEntity) in SystemAPI.Query<RefRO<CampLootLocked>>().WithEntityAccess())
        {
            if (campsClearedThisFrame.Contains(lootLocked.ValueRO.camp))
                commandBuffer.RemoveComponent<CampLootLocked>(lootEntity);
        }
        commandBuffer.Playback(state.EntityManager);
    }
}
