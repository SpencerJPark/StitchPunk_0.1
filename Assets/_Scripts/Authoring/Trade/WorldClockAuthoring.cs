using Unity.Entities;
using UnityEngine;

// One per playable subscene; bakes the WorldClock singleton in the "not started" state.
public class WorldClockAuthoring : MonoBehaviour
{
    [Tooltip("Real seconds per game day. 1440 = 24 real minutes, so one real minute is one game hour.")]
    public float secondsPerDay = 1440f;
    [Tooltip("A day and a new week begin here. 0.25 = 06:00.")]
    [Range(0f, 1f)] public float dayStartTimeOfDay01 = 0.25f;

    public class Baker : Baker<WorldClockAuthoring>
    {
        public override void Bake(WorldClockAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new WorldClock
            {
                timeOfDay01 = authoring.dayStartTimeOfDay01,
                secondsPerDay = authoring.secondsPerDay,
                dayStartTimeOfDay01 = authoring.dayStartTimeOfDay01,
            });
            AddComponent<DayStarted>(entity);
            SetComponentEnabled<DayStarted>(entity, false);
            AddComponent<WeekStarted>(entity);
            SetComponentEnabled<WeekStarted>(entity, false);
            AddComponent<WorldClockSkipRequest>(entity);
            SetComponentEnabled<WorldClockSkipRequest>(entity, false);
        }
    }
}
