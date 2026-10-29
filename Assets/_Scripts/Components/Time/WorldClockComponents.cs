using Unity.Entities;

// The shared world clock (Trade_System.md TR-A1). timeOfDay01 is the field Schedules reads unchanged.
public struct WorldClock : IComponentData
{
    public float timeOfDay01;          // 0 = midnight
    public int   dayOfWeek;            // 0..6, 0 = Monday
    public int   weekNumber;           // 1-based; baked as 0 = "not started", WorldClockSystem starts week 1
    public float secondsPerDay;        // 1440 = 24 real minutes per day (TR-D9)
    public float dayStartTimeOfDay01;  // 0.25 = 06:00: a day (and a new week) begins here, not at midnight
    public bool  isPaused;             // UI that wants the clock stopped without touching Time.timeScale
}

// One-frame signals on the WorldClock entity. WorldClockSystem raises them and clears them at the start of its next update.
public struct DayStarted  : IComponentData, IEnableableComponent { }
public struct WeekStarted : IComponentData, IEnableableComponent { }

// Debug/cheat entry: advance this many days now (each one raises DayStarted, a wrap raises WeekStarted).
public struct WorldClockSkipRequest : IComponentData, IEnableableComponent
{
    public int days;
}
