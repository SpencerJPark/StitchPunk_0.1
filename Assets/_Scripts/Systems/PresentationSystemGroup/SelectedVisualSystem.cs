using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// The ring follows the Selected enabled bit every frame. It used to react to one-shot
// onSelected/onDeselected flags that nothing ever cleared, so after one deselect both stayed
// true and the ring was hidden then re-shown each frame.
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
[BurstCompile]
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
        foreach ((RefRO<Selected> selected, EnabledRefRO<Selected> selectedEnabled) in
            SystemAPI.Query<RefRO<Selected>, EnabledRefRO<Selected>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            Entity visual = selected.ValueRO.visualEntity;
            if (visual == Entity.Null || !SystemAPI.HasComponent<LocalTransform>(visual)) continue;

            float targetScale = selectedEnabled.ValueRO ? selected.ValueRO.showScale : 0f;
            RefRW<LocalTransform> visualTransform = SystemAPI.GetComponentRW<LocalTransform>(visual);
            if (visualTransform.ValueRO.Scale != targetScale)
                visualTransform.ValueRW.Scale = targetScale;

            // Set ring color to match the current command type.
            if (selectedEnabled.ValueRO && SystemAPI.HasComponent<SelectionColor>(visual))
                SystemAPI.GetComponentRW<SelectionColor>(visual).ValueRW.Value = CommandTypeToColor(selected.ValueRO.commandType);
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
