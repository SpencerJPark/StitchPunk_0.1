using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder clock label, sits under the resource row in ResourceHud.uxml: "Week 3 · Thu 14:20".
public class WorldClockHudUI : MonoBehaviour
{
    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    [SerializeField] private UIDocument hudDocument;

    private Label clockLabel;
    private EntityQuery clockQuery;
    private World queryWorld;
    private int lastShownMinuteOfWeek = -1;

    private void OnEnable()
    {
        if (hudDocument == null)
            return;

        clockLabel = hudDocument.rootVisualElement.Q<Label>("clock-label");
    }

    private void LateUpdate()
    {
        if (clockLabel == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            clockQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldClock>());
        }

        if (clockQuery.IsEmptyIgnoreFilter)
            return;

        WorldClock clock = clockQuery.GetSingleton<WorldClock>();
        int minuteOfDay = Mathf.FloorToInt(clock.timeOfDay01 * 24f * 60f);
        int minuteOfWeek = clock.weekNumber * 100000 + clock.dayOfWeek * 2000 + minuteOfDay;
        if (minuteOfWeek == lastShownMinuteOfWeek)
            return;

        lastShownMinuteOfWeek = minuteOfWeek;
        clockLabel.text = $"Week {clock.weekNumber} · {DayNames[Mathf.Clamp(clock.dayOfWeek, 0, 6)]} {minuteOfDay / 60:00}:{minuteOfDay % 60:00}";
    }
}
