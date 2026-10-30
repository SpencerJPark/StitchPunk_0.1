using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Corner list of accepted orders (red on the due day) plus the button that opens the letter. The button blinks while a pigeon
// is perched with an unread letter (Trade_System.md section 6). Lives in ResourceHud.uxml's "orders-hud" block.
public class TradeOrdersHudUI : MonoBehaviour
{
    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);
    private static readonly Color DueTodayColor = new Color(1f, 0.35f, 0.3f);
    private const float BlinkSeconds = 0.5f;
    private const float ToastSeconds = 4f;

    [SerializeField] private UIDocument hudDocument;
    [SerializeField] private ClientLibrarySO library;

    private Button letterButton;
    private Label toastLabel;
    private VisualElement ordersList;
    private VirtualCursorPanel cursorPanel;
    private VisualElement ordersHudRoot;

    private EntityQuery boardQuery;
    private EntityQuery clockQuery;
    private EntityQuery pigeonQuery;
    private EntityQuery playerQuery;
    private World queryWorld;
    private uint lastRenderedSignature;
    private bool letterButtonShown;
    private System.Action openLetterAction;
    private int lastToastSequence;
    private float toastHideUnscaledTime;

    private void OnEnable()
    {
        if (hudDocument == null)
            return;

        VisualElement root = hudDocument.rootVisualElement;
        ordersHudRoot = root.Q<VisualElement>("orders-hud");
        letterButton = root.Q<Button>("letter-button");
        ordersList = root.Q<VisualElement>("orders-list");
        toastLabel = root.Q<Label>("toast-label");
        lastToastSequence = -1;

        cursorPanel = new VirtualCursorPanel(ordersHudRoot, () => letterButtonShown);
        cursorPanel.Register();
        lastRenderedSignature = 0u;
    }

    private void OnDisable()
    {
        if (cursorPanel != null)
            cursorPanel.Unregister();
        cursorPanel = null;
        if (letterButton != null && openLetterAction != null)
            letterButton.clicked -= openLetterAction;
        openLetterAction = null;
    }

    private void LateUpdate()
    {
        if (letterButton == null || ordersList == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            EntityManager newEntityManager = world.EntityManager;
            boardQuery = newEntityManager.CreateEntityQuery(ComponentType.ReadOnly<TradeBoard>());
            clockQuery = newEntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldClock>());
            pigeonQuery = newEntityManager.CreateEntityQuery(ComponentType.ReadOnly<CarrierPigeon>(), ComponentType.ReadOnly<PigeonFlight>());
            playerQuery = newEntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        }

        if (boardQuery.IsEmptyIgnoreFilter || clockQuery.IsEmptyIgnoreFilter)
            return;

        EntityManager entityManager = world.EntityManager;
        Entity boardEntity = boardQuery.GetSingletonEntity();
        WorldClock clock = clockQuery.GetSingleton<WorldClock>();
        DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity, true);
        bool letterIsOpen = entityManager.IsComponentEnabled<TradeLetterOpen>(boardEntity);

        bool hasThisWeeksOffers = false;
        uint signature = 17u + (uint)clock.dayOfWeek * 13u;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            hasThisWeeksOffers |= orders[orderIndex].weekIssued == clock.weekNumber;
            signature = signature * 31u + (uint)orders[orderIndex].orderId * 7u + (uint)orders[orderIndex].state;
        }

        bool showLetterButton = hasThisWeeksOffers && !letterIsOpen;
        if (showLetterButton != letterButtonShown)
        {
            letterButtonShown = showLetterButton;
            letterButton.style.display = showLetterButton ? DisplayStyle.Flex : DisplayStyle.None;
            if (openLetterAction != null)
                letterButton.clicked -= openLetterAction;
            openLetterAction = () => entityManager.SetComponentEnabled<TradeLetterOpen>(boardEntity, true);
            cursorPanel.ClearTrackedButtons();
            cursorPanel.Track(letterButton, openLetterAction);
        }

        bool unreadLetterWaiting = PerchedPigeonExists(entityManager);
        letterButton.text = unreadLetterWaiting ? "Letter (new) [L]" : "Letter [L]";
        letterButton.style.color = unreadLetterWaiting && Mathf.FloorToInt(Time.unscaledTime / BlinkSeconds) % 2 == 0
            ? DueTodayColor
            : new StyleColor(StyleKeyword.Null);

        if (signature != lastRenderedSignature)
        {
            lastRenderedSignature = signature;
            RebuildOrdersList(orders, clock);
        }

        // On foot there is no pointer for the button, so L opens it (not while a menu has frozen the world).
        if (letterButtonShown && Time.timeScale > 0f && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            openLetterAction?.Invoke();

        UpdateToast(entityManager, boardEntity, orders);

        Entity playerEntity = playerQuery.IsEmptyIgnoreFilter ? Entity.Null : playerQuery.GetSingletonEntity();
        cursorPanel.UpdateHover(entityManager, playerEntity);
    }

    private void UpdateToast(EntityManager entityManager, Entity boardEntity, DynamicBuffer<TradeOrder> orders)
    {
        if (toastLabel == null)
            return;

        TradeToast toast = entityManager.GetComponentData<TradeToast>(boardEntity);
        // The first read only adopts the current sequence, so a message from before this HUD existed is not replayed.
        if (lastToastSequence < 0)
            lastToastSequence = toast.sequence;

        if (toast.sequence != lastToastSequence)
        {
            lastToastSequence = toast.sequence;
            string text = BuildToastText(toast, orders);
            if (text != null)
            {
                toastLabel.text = text;
                toastLabel.style.display = DisplayStyle.Flex;
                toastHideUnscaledTime = Time.unscaledTime + ToastSeconds;
            }
        }

        if (toastLabel.style.display == DisplayStyle.Flex && Time.unscaledTime >= toastHideUnscaledTime)
            toastLabel.style.display = DisplayStyle.None;
    }

    private string BuildToastText(TradeToast toast, DynamicBuffer<TradeOrder> orders)
    {
        if (toast.kind == TradeToastKind.HandOverDone)
            return $"Delivered — +{toast.amount} Currency";

        TradeOrder order = default;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (orders[orderIndex].orderId == toast.orderId)
                order = orders[orderIndex];
        }

        if (toast.kind == TradeToastKind.HandOverRefused)
        {
            string condition = order.minHealthFraction > 0f ? $" (at least {order.minHealthFraction * 100f:0}% health)" : "";
            return $"Need {toast.amount} more {TradeTextLookup.UnitLabel(order.unitType)}{condition}";
        }

        if (toast.kind == TradeToastKind.OrderFailed)
        {
            string client = TradeTextLookup.ClientName(library, order.clientId);
            return toast.amount > 0 ? $"Order failed — {client}: -{toast.amount} reputation" : $"Order failed — {client}";
        }

        return null;
    }

    private bool PerchedPigeonExists(EntityManager entityManager)
    {
        if (pigeonQuery.IsEmptyIgnoreFilter)
            return false;

        using (Unity.Collections.NativeArray<PigeonFlight> flights = pigeonQuery.ToComponentDataArray<PigeonFlight>(Unity.Collections.Allocator.Temp))
        {
            for (int flightIndex = 0; flightIndex < flights.Length; flightIndex++)
            {
                if (flights[flightIndex].phase == PigeonFlightPhase.Perched)
                    return true;
            }
        }
        return false;
    }

    private void RebuildOrdersList(DynamicBuffer<TradeOrder> orders, WorldClock clock)
    {
        ordersList.Clear();
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            if (order.state != TradeOrderState.Accepted)
                continue;

            bool dueToday = order.dueDayOfWeek == clock.dayOfWeek;
            Label orderLabel = new Label($"{TradeTextLookup.ClientName(library, order.clientId)}: {TradeTextLookup.Requirement(order)} · by {TradeTextLookup.DayName(order.dueDayOfWeek)}");
            orderLabel.style.color = dueToday ? DueTodayColor : CreamColor;
            orderLabel.style.fontSize = 13f;
            orderLabel.style.whiteSpace = WhiteSpace.Normal;
            ordersList.Add(orderLabel);
        }
    }
}
