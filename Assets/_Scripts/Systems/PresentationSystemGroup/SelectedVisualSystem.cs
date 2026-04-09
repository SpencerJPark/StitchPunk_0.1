using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct SelectedVisualSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRO<Selected> selected in SystemAPI.Query<RefRO<Selected>>().WithPresent<Selected>())
        {
            Entity visual = selected.ValueRO.visualEntity;
            if (visual == Entity.Null) continue;

            if (selected.ValueRO.onDeselected)
            {
                SystemAPI.GetComponentRW<LocalTransform>(visual).ValueRW.Scale = 0f;
            }

            if (selected.ValueRO.onSelected)
            {
                SystemAPI.GetComponentRW<LocalTransform>(visual).ValueRW.Scale = selected.ValueRO.showScale;
                // Set ring color to match the current command type.
                if (SystemAPI.HasComponent<SelectionColor>(visual))
                    SystemAPI.GetComponentRW<SelectionColor>(visual).ValueRW.Value = CommandTypeToColor(selected.ValueRO.commandType);
            }
        }
    }

    // Color map per command type. Alpha is always 1.
    private static float4 CommandTypeToColor(CommandType commandType)
    {
        return commandType switch
        {
            CommandType.Attack  => new float4(1f,   0.2f, 0.2f, 1f), // red
            CommandType.Defend  => new float4(1f,   0.9f, 0.1f, 1f), // yellow
            CommandType.Interact => new float4(0.2f, 0.5f, 1f,  1f), // blue
            CommandType.Follow  => new float4(0.2f, 1f,   0.3f, 1f), // green
            _                   => new float4(1f,   1f,   1f,   1f), // white (Move / no order)
        };
    }
}