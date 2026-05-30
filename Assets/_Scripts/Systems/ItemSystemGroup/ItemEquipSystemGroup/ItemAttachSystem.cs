using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Parents an item entity to its socket and resets its local transform so it
/// sits at the socket origin. Runs after ItemEquipSystem so all equip data is final.
/// </summary>
[UpdateInGroup(typeof(ItemEquipSystemGroup))]
[UpdateAfter(typeof(ItemEquipSystem))]
public partial struct ItemAttachSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (attachedTo, entity) in
            SystemAPI.Query<RefRO<AttachedTo>>()
                .WithAll<AttachItemRequest>()
                .WithEntityAccess())
        {
            Entity socket = attachedTo.ValueRO.socket;
            if (socket == Entity.Null) continue;

            ecb.AddComponent(entity, new Parent { Value = socket });
            ecb.SetComponent(entity, LocalTransform.Identity);
            ecb.SetComponentEnabled<AttachItemRequest>(entity, false);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    public void OnDestroy(ref SystemState state) { }
}
