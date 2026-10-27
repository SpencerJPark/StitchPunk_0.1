using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationPackUpSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRO<Workstation> workstation,
                  DynamicBuffer<WorkstationSummonQueueEntry> summonQueue,
                  EnabledRefRW<WorkstationPackUpRequest> workstationPackUpRequestEnabled,
                  Entity workstationEntity) in
            SystemAPI.Query<
                RefRO<Workstation>,
                DynamicBuffer<WorkstationSummonQueueEntry>,
                EnabledRefRW<WorkstationPackUpRequest>>()
                    .WithEntityAccess())
        {
            workstationPackUpRequestEnabled.ValueRW = false;

            // The caravan is never destroyed: driving off is how it packs up (Caravan_System.md CV-D1).
            if (SystemAPI.HasComponent<Caravan>(workstationEntity))
            {
                Debug.Log("Caravan: drive off to pack up camp");
                continue;
            }

            if (summonQueue.Length > 0)
            {
                Debug.Log("Workstation: finish or wait for the summon queue before packing up");
                continue;
            }

            Entity ownerPlayer = workstation.ValueRO.ownerPlayer;
            if (ownerPlayer != Entity.Null && SystemAPI.HasComponent<PlayerWorkstationLink>(ownerPlayer))
            {
                SystemAPI.GetComponentRW<PlayerWorkstationLink>(ownerPlayer).ValueRW.workstation = Entity.Null;
                if (SystemAPI.HasComponent<WorkstationDismountRequest>(ownerPlayer))
                    SystemAPI.SetComponentEnabled<WorkstationDismountRequest>(ownerPlayer, true);
            }

            commandBuffer.DestroyEntity(workstationEntity);
        }

        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();
    }
}
