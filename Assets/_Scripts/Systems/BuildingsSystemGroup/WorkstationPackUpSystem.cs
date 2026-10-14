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
