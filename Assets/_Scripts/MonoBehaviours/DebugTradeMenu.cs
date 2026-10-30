using System.Text;
using Unity.Entities;
using UnityEngine;

// Throwaway OnGUI trade cheat menu (Trade_System.md Phase 0/1): clock readout, day/week skip, and the week's offers.
// Delete once the pigeon letter UI exists.
public class DebugTradeMenu : MonoBehaviour
{
    private static readonly Rect MenuAreaRect = new Rect(10f, 300f, 560f, 420f);
    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    [SerializeField] private ClientLibrarySO library;

    private EntityManager entityManager;
    private EntityQuery clockQuery;
    private EntityQuery boardQuery;
    private DebugMenuVirtualCursorInput virtualCursorInput;
    private bool worldReady;
    private string rerollSeedText = "12345";
    private Vector2 scrollPosition;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        clockQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldClock>());
        boardQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TradeBoard>());
        virtualCursorInput = new DebugMenuVirtualCursorInput(entityManager, MenuAreaRect, IsMenuShown);
        worldReady = true;
    }

    private void OnEnable()  => virtualCursorInput?.Register();
    private void OnDisable() => virtualCursorInput?.Unregister();

    private void Update() => DebugMenuVisibility.PollToggleKey();

    private bool IsMenuShown()
    {
        return worldReady && DebugMenuVisibility.AreDebugMenusVisible;
    }

    private void OnGUI()
    {
        if (!IsMenuShown() || World.DefaultGameObjectInjectionWorld == null)
            return;

        virtualCursorInput.BeginMenuGui();
        GUILayout.BeginArea(MenuAreaRect, GUI.skin.box);
        GUILayout.Label("Trade (debug)");

        if (clockQuery.TryGetSingletonEntity<WorldClock>(out Entity clockEntity))
            DrawClock(clockEntity);
        else
            GUILayout.Label("No WorldClock entity (add WorldClockAuthoring to the subscene).");

        if (boardQuery.TryGetSingletonEntity<TradeBoard>(out Entity boardEntity))
            DrawBoard(boardEntity);
        else
            GUILayout.Label("No TradeBoard entity (add TradeBoardAuthoring to the subscene).");

        GUILayout.EndArea();
    }

    private void DrawClock(Entity clockEntity)
    {
        WorldClock clock = entityManager.GetComponentData<WorldClock>(clockEntity);
        int minuteOfDay = Mathf.FloorToInt(clock.timeOfDay01 * 24f * 60f);
        GUILayout.Label($"Week {clock.weekNumber} · {DayNames[Mathf.Clamp(clock.dayOfWeek, 0, 6)]} {minuteOfDay / 60:00}:{minuteOfDay % 60:00}  (day = {clock.secondsPerDay:0} s)");

        GUILayout.BeginHorizontal();
        if (virtualCursorInput.Button("Skip day")) QueueSkip(clockEntity, 1);
        if (virtualCursorInput.Button("Skip to next week")) QueueSkip(clockEntity, WorldClockMath.DaysPerWeek - clock.dayOfWeek);
        GUILayout.EndHorizontal();
    }

    private void QueueSkip(Entity clockEntity, int days)
    {
        entityManager.SetComponentData(clockEntity, new WorldClockSkipRequest { days = days });
        entityManager.SetComponentEnabled<WorldClockSkipRequest>(clockEntity, true);
    }

    private void DrawBoard(Entity boardEntity)
    {
        TradeBoard board = entityManager.GetComponentData<TradeBoard>(boardEntity);
        DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity, true);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Seed {board.worldSeed}  ·  signature {Signature(orders):X8}");
        rerollSeedText = GUILayout.TextField(rerollSeedText, GUILayout.Width(90f));
        if (virtualCursorInput.Button("Re-roll week with seed") && uint.TryParse(rerollSeedText, out uint rerollSeed))
        {
            entityManager.SetComponentData(boardEntity, new TradeRerollRequest { seed = rerollSeed });
            entityManager.SetComponentEnabled<TradeRerollRequest>(boardEntity, true);
        }
        GUILayout.EndHorizontal();

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            GUILayout.Label(DescribeOrder(order));
        }
        GUILayout.EndScrollView();
    }

    // Order of the rows and every rolled field, so two runs with one seed can be compared at a glance.
    private static uint Signature(DynamicBuffer<TradeOrder> orders)
    {
        uint signature = 17u;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            signature = signature * 31u + (uint)order.orderId;
            signature = signature * 31u + (uint)order.clientId;
            signature = signature * 31u + (uint)order.templateIndex;
            signature = signature * 31u + (uint)order.quantity;
            signature = signature * 31u + (uint)order.daysAllowed;
            signature = signature * 31u + (uint)order.currencyReward;
        }

        return signature;
    }

    private string DescribeOrder(TradeOrder order)
    {
        StringBuilder text = new StringBuilder();
        text.Append(order.state == TradeOrderState.Offered ? "" : $"[{order.state}] ");
        text.Append($"#{order.orderId} {ClientNameOf(order.clientId)}: {order.quantity}× {order.unitType}");
        if (order.minHealthFraction > 0f)
            text.Append($" (≥{order.minHealthFraction:P0} health)");
        text.Append($" · {order.daysAllowed} d · drop-off {order.dropOffId} · difficulty {order.difficulty:0.0} → {order.currencyReward} Currency");
        return text.ToString();
    }

    private string ClientNameOf(int clientId)
    {
        return TradeTextLookup.ClientName(library, clientId);
    }
}
