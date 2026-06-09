using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct LoggingSystem : ISystem
{
    private EntityQuery logQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        logQuery = state.GetEntityQuery(ComponentType.ReadOnly<LogMessage>());
    }

    public void OnUpdate(ref SystemState state)
    {
        if (logQuery.IsEmpty)
            return;

        NativeArray<LogMessage> messages = logQuery.ToComponentDataArray<LogMessage>(Allocator.Temp);

        for (int index = 0; index < messages.Length; index++)
        {
            LogMessage log = messages[index];
            string formatted = $"[{log.Timestamp:F3}] {log.Message}";

            switch (log.Level)
            {
                case LogLevel.Info:    Debug.Log(formatted);        break;
                case LogLevel.Warning: Debug.LogWarning(formatted); break;
                case LogLevel.Error:   Debug.LogError(formatted);   break;
            }
        }

        state.EntityManager.DestroyEntity(logQuery);
        messages.Dispose();
    }
}
