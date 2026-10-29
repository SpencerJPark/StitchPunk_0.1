using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Time stops with Time.timeScale (the pause menu) because it ticks off the simulation delta time.
[BurstCompile]
[UpdateInGroup(typeof(WorldTimeSystemGroup))]
public partial struct WorldClockSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity clockEntity = SystemAPI.GetSingletonEntity<WorldClock>();
        RefRW<WorldClock> clock = SystemAPI.GetComponentRW<WorldClock>(clockEntity);

        SystemAPI.SetComponentEnabled<DayStarted>(clockEntity, false);
        SystemAPI.SetComponentEnabled<WeekStarted>(clockEntity, false);

        bool dayStarted = false;
        bool weekStarted = false;

        // A fresh game bakes weekNumber 0: begin week 1 on Monday at the day-start time.
        if (clock.ValueRO.weekNumber == 0)
        {
            clock.ValueRW.weekNumber = 1;
            clock.ValueRW.dayOfWeek = 0;
            clock.ValueRW.timeOfDay01 = clock.ValueRO.dayStartTimeOfDay01;
            RaiseSignals(ref state, clockEntity, true, true);
            return;
        }

        if (SystemAPI.IsComponentEnabled<WorldClockSkipRequest>(clockEntity))
        {
            int skipDays = SystemAPI.GetComponent<WorldClockSkipRequest>(clockEntity).days;
            SystemAPI.SetComponentEnabled<WorldClockSkipRequest>(clockEntity, false);
            for (int skipIndex = 0; skipIndex < skipDays; skipIndex++)
            {
                weekStarted |= WorldClockMath.AdvanceDay(ref clock.ValueRW);
                dayStarted = true;
            }

            if (dayStarted)
                clock.ValueRW.timeOfDay01 = clock.ValueRO.dayStartTimeOfDay01;
        }
        else if (!clock.ValueRO.isPaused && clock.ValueRO.secondsPerDay > 0f)
        {
            float previousTimeOfDay = clock.ValueRO.timeOfDay01;
            float advancedTimeOfDay = previousTimeOfDay + SystemAPI.Time.DeltaTime / clock.ValueRO.secondsPerDay;
            int dayBoundaries = WorldClockMath.CountDayBoundaries(
                previousTimeOfDay, advancedTimeOfDay, clock.ValueRO.dayStartTimeOfDay01);

            clock.ValueRW.timeOfDay01 = advancedTimeOfDay - math.floor(advancedTimeOfDay);
            for (int boundaryIndex = 0; boundaryIndex < dayBoundaries; boundaryIndex++)
            {
                weekStarted |= WorldClockMath.AdvanceDay(ref clock.ValueRW);
                dayStarted = true;
            }
        }

        RaiseSignals(ref state, clockEntity, dayStarted, weekStarted);
    }

    private void RaiseSignals(ref SystemState state, Entity clockEntity, bool dayStarted, bool weekStarted)
    {
        if (dayStarted)
            SystemAPI.SetComponentEnabled<DayStarted>(clockEntity, true);
        if (weekStarted)
            SystemAPI.SetComponentEnabled<WeekStarted>(clockEntity, true);
    }
}
