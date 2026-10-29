using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder letter (Trade_System.md TR-A12), built so a Rive artboard can replace it. Open while TradeLetterOpen is enabled on
// the board; the clock is paused for as long as it is up (TR-D10).
public class TradeLetterUI : MonoBehaviour
{
    private static readonly Color InkColor = new Color(0.24f, 0.14f, 0.08f);
    private static readonly Color MutedInkColor = new Color(0.45f, 0.35f, 0.28f);

    [SerializeField] private UIDocument letterDocument;
    [SerializeField] private ClientLibrarySO library;

    private VisualElement letterRoot;
    private VisualElement offerList;
    private Label footerLabel;
    private Button closeButton;
    private VirtualCursorPanel cursorPanel;

    private EntityQuery boardQuery;
    private EntityQuery clockQuery;
    private EntityQuery playerQuery;
    private World queryWorld;

    private bool isShown;
    private uint lastRenderedSignature;

    private void OnEnable()
    {
        if (letterDocument == null)
            return;

        VisualElement root = letterDocument.rootVisualElement;
        letterRoot = root.Q<VisualElement>("trade-letter-root");
        offerList = root.Q<VisualElement>("offer-list");
        footerLabel = root.Q<Label>("client-footer");
        closeButton = root.Q<Button>("close-button");

        cursorPanel = new VirtualCursorPanel(letterRoot, () => isShown);
        cursorPanel.Register();
        lastRenderedSignature = 0u;
    }

    private void OnDisable()
    {
        if (isShown)
            SetClockPaused(false);
        isShown = false;
        if (cursorPanel != null)
            cursorPanel.Unregister();
        cursorPanel = null;
    }

    private void LateUpdate()
    {
        if (letterRoot == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            boardQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TradeBoard>());
            clockQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldClock>());
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        }

        EntityManager entityManager = world.EntityManager;
        if (boardQuery.IsEmptyIgnoreFilter)
            return;

        Entity boardEntity = boardQuery.GetSingletonEntity();
        bool shouldShow = entityManager.HasComponent<TradeLetterOpen>(boardEntity)
            && entityManager.IsComponentEnabled<TradeLetterOpen>(boardEntity);

        if (shouldShow != isShown)
        {
            isShown = shouldShow;
            letterRoot.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            SetClockPaused(shouldShow);
            lastRenderedSignature = 0u;
            if (shouldShow)
                BindCloseButton(entityManager, boardEntity);
        }

        if (!isShown)
            return;

        Refresh(entityManager, boardEntity);
        Entity playerEntity = playerQuery.IsEmptyIgnoreFilter ? Entity.Null : playerQuery.GetSingletonEntity();
        cursorPanel.UpdateHover(entityManager, playerEntity);
    }

    private void SetClockPaused(bool paused)
    {
        if (queryWorld == null || !queryWorld.IsCreated || clockQuery.IsEmptyIgnoreFilter)
            return;

        EntityManager entityManager = queryWorld.EntityManager;
        Entity clockEntity = clockQuery.GetSingletonEntity();
        WorldClock clock = entityManager.GetComponentData<WorldClock>(clockEntity);
        clock.isPaused = paused;
        entityManager.SetComponentData(clockEntity, clock);
    }

    private void BindCloseButton(EntityManager entityManager, Entity boardEntity)
    {
        cursorPanel.ClearTrackedButtons();
        closeButton.clicked -= closeAction;
        closeAction = () => entityManager.SetComponentEnabled<TradeLetterOpen>(boardEntity, false);
        cursorPanel.Track(closeButton, closeAction);
    }

    private System.Action closeAction;

    private void Refresh(EntityManager entityManager, Entity boardEntity)
    {
        DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity, true);
        DynamicBuffer<ClientRelationship> relationships = entityManager.GetBuffer<ClientRelationship>(boardEntity, true);
        int weekNumber = clockQuery.IsEmptyIgnoreFilter ? 0 : clockQuery.GetSingleton<WorldClock>().weekNumber;

        uint signature = 17u;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
            signature = signature * 31u + (uint)orders[orderIndex].orderId * 7u + (uint)orders[orderIndex].state;
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
            signature = signature * 31u + (uint)relationships[relationshipIndex].reputation;
        if (signature == lastRenderedSignature)
            return;
        lastRenderedSignature = signature;

        offerList.Clear();
        cursorPanel.ClearTrackedButtons();
        cursorPanel.MapForVirtualCursor(closeButton, closeAction);

        int shownCount = 0;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            bool thisWeek = order.weekIssued == weekNumber;
            if (!thisWeek && order.state != TradeOrderState.Accepted)
                continue;
            offerList.Add(BuildCard(entityManager, boardEntity, order));
            shownCount++;
        }

        if (shownCount == 0)
            offerList.Add(MakeLabel("No offers this week.", 15, FontStyle.Italic, MutedInkColor));

        footerLabel.text = BuildFooter(relationships);
    }

    private VisualElement BuildCard(EntityManager entityManager, Entity boardEntity, TradeOrder order)
    {
        VisualElement card = new VisualElement();
        card.style.backgroundColor = new Color(1f, 1f, 1f, 0.35f);
        card.style.paddingLeft = card.style.paddingRight = 8f;
        card.style.paddingTop = card.style.paddingBottom = 6f;
        card.style.marginBottom = 6f;
        bool decided = order.state != TradeOrderState.Offered;
        card.style.opacity = order.state == TradeOrderState.Declined || order.state == TradeOrderState.Expired ? 0.5f : 1f;

        card.Add(MakeLabel(TradeTextLookup.ClientName(library, order.clientId), 17, FontStyle.Bold, InkColor));

        OrderTemplateSO template = TradeTextLookup.FindTemplate(library, order);
        if (template != null && !string.IsNullOrEmpty(template.flavourText))
            card.Add(MakeLabel("“" + template.flavourText + "”", 13, FontStyle.Italic, MutedInkColor));

        card.Add(MakeLabel(TradeTextLookup.Requirement(order) + $" · within {order.daysAllowed} days", 14, FontStyle.Normal, InkColor));
        card.Add(MakeLabel(BuildPayoutText(order, template), 14, FontStyle.Bold, InkColor));

        if (!decided)
        {
            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 4f;
            buttonRow.Add(MakeButton("Accept", 38f, 2f, () => Respond(entityManager, boardEntity, order.orderId, true)));
            buttonRow.Add(MakeButton("Decline", 38f, 1f, () => Respond(entityManager, boardEntity, order.orderId, false)));
            card.Add(buttonRow);
        }
        else
        {
            string statusText = order.state == TradeOrderState.Accepted
                ? $"Accepted — due {TradeTextLookup.DayName(order.dueDayOfWeek)}"
                : order.state.ToString();
            card.Add(MakeLabel(statusText, 14, FontStyle.Bold, MutedInkColor));
        }

        return card;
    }

    private static string BuildPayoutText(TradeOrder order, OrderTemplateSO template)
    {
        StringBuilder payout = new StringBuilder($"Pays {order.currencyReward} Currency");
        if (template != null && template.bonusResources != null)
        {
            foreach (ResourceBonus bonus in template.bonusResources)
                payout.Append($" + {bonus.amount} {bonus.resourceType}");
        }
        return payout.ToString();
    }

    private string BuildFooter(DynamicBuffer<ClientRelationship> relationships)
    {
        StringBuilder footer = new StringBuilder();
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            ClientRelationship relationship = relationships[relationshipIndex];
            ClientSO client = TradeTextLookup.FindClient(library, relationship.clientId);
            if (client == null)
                continue;

            ReputationTier tier = TradeMath.TierFromReputation(relationship.reputation, client.tiers.ToInt3());
            if (footer.Length > 0)
                footer.Append("   ·   ");
            footer.Append($"{client.displayName}: {tier}");
        }
        return footer.ToString();
    }

    private static void Respond(EntityManager entityManager, Entity boardEntity, int orderId, bool accept)
    {
        entityManager.GetBuffer<TradeOrderResponseRequest>(boardEntity).Add(new TradeOrderResponseRequest { orderId = orderId, accept = accept });
    }

    private Button MakeButton(string text, float height, float flexGrow, System.Action handler)
    {
        Button button = new Button { text = text };
        button.style.minHeight = height;
        button.style.fontSize = 15f;
        button.style.flexGrow = flexGrow;
        button.style.flexBasis = 0f;
        cursorPanel.Track(button, handler);
        return button;
    }

    private static Label MakeLabel(string text, int fontSize, FontStyle fontStyle, Color color)
    {
        Label label = new Label(text);
        label.style.fontSize = fontSize;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }
}
