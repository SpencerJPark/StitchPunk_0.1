using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(PlayerResourceSystemGroup))]
public partial struct ResourceChangeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ResourceChangeJob().Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Player))]
public partial struct ResourceChangeJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<ResourceStack> stacks,
        ref DynamicBuffer<ResourceChangeRequest> requests,
        in DynamicBuffer<StorageCapacity> capacities)
    {
        for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
        {
            ResourceChangeRequest request = requests[requestIndex];

            int stackIndex = -1;
            for (int searchIndex = 0; searchIndex < stacks.Length; searchIndex++)
            {
                if (stacks[searchIndex].resourceType == request.resourceType)
                {
                    stackIndex = searchIndex;
                    break;
                }
            }
            if (stackIndex < 0)
                continue;

            long cap = int.MaxValue;
            for (int capIndex = 0; capIndex < capacities.Length; capIndex++)
            {
                if (capacities[capIndex].resourceType == request.resourceType)
                {
                    cap = capacities[capIndex].cap;
                    break;
                }
            }

            ResourceStack stack = stacks[stackIndex];
            // long math so Add near int.MaxValue cannot wrap before the clamp
            long target = request.changeType == ResourceChangeType.Set
                ? request.value
                : (long)stack.amount + request.value;
            stack.amount = (int)math.clamp(target, 0L, math.max(cap, 0L));
            stacks[stackIndex] = stack;
        }
        requests.Clear();
    }
}
