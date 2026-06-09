using Unity.Collections;
using Unity.Entities;

public static class LogUtil
{
    public static void Log(
        ref EntityCommandBuffer.ParallelWriter ecb,
        int sortKey,
        FixedString512Bytes message,
        LogLevel level = LogLevel.Info,
        double timestamp = 0.0,
        int sourceSystemHash = 0)
    {
        Entity entity = ecb.CreateEntity(sortKey);
        ecb.AddComponent(sortKey, entity, new LogMessage
        {
            Message = message,
            Level = level,
            Timestamp = timestamp,
            SourceSystemHash = sourceSystemHash,
        });
    }

    public static void Log(
        ref EntityCommandBuffer ecb,
        FixedString512Bytes message,
        LogLevel level = LogLevel.Info,
        double timestamp = 0.0,
        int sourceSystemHash = 0)
    {
        Entity entity = ecb.CreateEntity();
        ecb.AddComponent(entity, new LogMessage
        {
            Message = message,
            Level = level,
            Timestamp = timestamp,
            SourceSystemHash = sourceSystemHash,
        });
    }
}
