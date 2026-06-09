using Unity.Collections;
using Unity.Entities;

public enum LogLevel : byte
{
    Info,
    Warning,
    Error
}

public struct LogMessage : IComponentData
{
    public FixedString512Bytes Message;
    public LogLevel Level;
    public double Timestamp;
    public int SourceSystemHash;
    public LogCategory Category;
}
