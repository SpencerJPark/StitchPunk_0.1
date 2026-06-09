using System;
using Unity.Entities;

[Flags]
public enum LogCategory : int
{
    None         = 0,
    AI           = 1 << 0,
    StateMachine = 1 << 1,
    Combat       = 1 << 2,
    Movement     = 1 << 3,
    Social       = 1 << 4,
    Items        = 1 << 5,
    Factory      = 1 << 6,
    Health       = 1 << 7,
    General      = 1 << 8,
    All          = ~0,
}

public struct LoggingConfig : IComponentData
{
    public int EnabledCategories;
}
