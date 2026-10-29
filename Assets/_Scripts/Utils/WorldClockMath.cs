using Unity.Mathematics;

// Day/week arithmetic for WorldClockSystem, kept pure so the debug skip and the tick share one path.
public static class WorldClockMath
{
    public const int DaysPerWeek = 7;

    // How many times the dayStart boundary lies in (previous, advanced]. advanced is NOT wrapped (it may exceed 1).
    public static int CountDayBoundaries(float previousTimeOfDay, float advancedTimeOfDay, float dayStartTimeOfDay)
    {
        return (int)math.floor(advancedTimeOfDay - dayStartTimeOfDay) - (int)math.floor(previousTimeOfDay - dayStartTimeOfDay);
    }

    // Returns true when the week wrapped (Sunday -> Monday).
    public static bool AdvanceDay(ref WorldClock clock)
    {
        clock.dayOfWeek++;
        if (clock.dayOfWeek < DaysPerWeek)
            return false;

        clock.dayOfWeek = 0;
        clock.weekNumber++;
        return true;
    }
}
