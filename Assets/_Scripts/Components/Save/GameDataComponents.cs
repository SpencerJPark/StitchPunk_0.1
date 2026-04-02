using Unity.Entities;

public struct GameDataTag : IComponentData { }

public struct SaveRequest : IComponentData, IEnableableComponent
{
    public int slot; // 0 = autosave, 1–3 = manual slots
}

public struct LoadRequest : IComponentData, IEnableableComponent
{
    public int slot;
}

public struct AutoSaveTimer : IComponentData
{
    public float elapsedSeconds;
    public float intervalSeconds;
}

public struct PlayTimeTracker : IComponentData
{
    public double totalSeconds; // double for long-session precision
}

public struct GameSettings : IComponentData
{
    public int animationFrameRate;
}
