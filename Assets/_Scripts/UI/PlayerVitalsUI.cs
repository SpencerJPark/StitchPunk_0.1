using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder until the Rive HUD exists; health bar and knockout overlay built in code.
public class PlayerVitalsUI : MonoBehaviour
{
    private const float HealthBarWidthPixels = 220f;
    private const float HealthBarHeightPixels = 16f;
    private const float HudEdgePixels = 16f;

    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);
    private static readonly Color HealthTrackColor = new Color(0.12f, 0.1f, 0.1f, 0.9f);
    private static readonly Color HealthFillColor = new Color(0.78f, 0.18f, 0.16f);
    private static readonly Color KnockoutOverlayColor = new Color(0f, 0f, 0f, 0.6f);

    [SerializeField] private UIDocument vitalsDocument;

    private VisualElement healthBarRoot;
    private VisualElement healthFillElement;
    private Label healthLabel;
    private VisualElement knockoutOverlay;
    private Label knockoutLabel;
    private bool elementsBuilt;

    private EntityQuery playerQuery;
    private World queryWorld;
    private int lastHealthAmount = int.MinValue;
    private int lastHealthMax = int.MinValue;
    private int lastKnockoutSecondsShown = -1;
    private bool lastHealthBarVisible;
    private bool lastKnockoutVisible;

    private void OnEnable()
    {
        TryBuildElements();
    }

    private void OnDisable()
    {
        if (healthBarRoot != null && healthBarRoot.parent != null)
            healthBarRoot.RemoveFromHierarchy();
        if (knockoutOverlay != null && knockoutOverlay.parent != null)
            knockoutOverlay.RemoveFromHierarchy();

        healthBarRoot = null;
        healthFillElement = null;
        healthLabel = null;
        knockoutOverlay = null;
        knockoutLabel = null;
        elementsBuilt = false;
        lastHealthAmount = int.MinValue;
        lastHealthMax = int.MinValue;
        lastKnockoutSecondsShown = -1;
        lastHealthBarVisible = false;
        lastKnockoutVisible = false;
    }

    private void LateUpdate()
    {
        if (!elementsBuilt && !TryBuildElements())
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        }

        if (playerQuery.IsEmptyIgnoreFilter)
            return;

        EntityManager entityManager = world.EntityManager;
        Entity playerEntity = playerQuery.GetSingletonEntity();
        UpdateHealthBar(entityManager, playerEntity);
        UpdateKnockoutOverlay(entityManager, playerEntity);
    }

    private bool TryBuildElements()
    {
        if (elementsBuilt || vitalsDocument == null)
            return elementsBuilt;

        VisualElement root = vitalsDocument.rootVisualElement;
        if (root == null)
            return false;

        healthBarRoot = BuildHealthBar();
        knockoutOverlay = BuildKnockoutOverlay();
        root.Add(healthBarRoot);
        root.Add(knockoutOverlay);
        elementsBuilt = true;
        return true;
    }

    private VisualElement BuildHealthBar()
    {
        VisualElement container = new VisualElement();
        container.pickingMode = PickingMode.Ignore;
        container.style.position = Position.Absolute;
        container.style.left = HudEdgePixels;
        container.style.bottom = HudEdgePixels;
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.display = DisplayStyle.None;

        VisualElement track = new VisualElement();
        track.pickingMode = PickingMode.Ignore;
        track.style.width = HealthBarWidthPixels;
        track.style.height = HealthBarHeightPixels;
        track.style.backgroundColor = HealthTrackColor;
        container.Add(track);

        healthFillElement = new VisualElement();
        healthFillElement.pickingMode = PickingMode.Ignore;
        healthFillElement.style.position = Position.Absolute;
        healthFillElement.style.left = 0f;
        healthFillElement.style.top = 0f;
        healthFillElement.style.bottom = 0f;
        healthFillElement.style.width = new Length(0f, LengthUnit.Percent);
        healthFillElement.style.backgroundColor = HealthFillColor;
        track.Add(healthFillElement);

        healthLabel = new Label();
        healthLabel.pickingMode = PickingMode.Ignore;
        healthLabel.style.color = CreamColor;
        healthLabel.style.marginLeft = 8f;
        container.Add(healthLabel);

        return container;
    }

    private VisualElement BuildKnockoutOverlay()
    {
        VisualElement overlay = new VisualElement();
        overlay.pickingMode = PickingMode.Ignore;
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.right = 0f;
        overlay.style.top = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = KnockoutOverlayColor;
        overlay.style.flexDirection = FlexDirection.Column;
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.display = DisplayStyle.None;

        knockoutLabel = new Label();
        knockoutLabel.pickingMode = PickingMode.Ignore;
        knockoutLabel.style.color = CreamColor;
        knockoutLabel.style.fontSize = 32f;
        knockoutLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        overlay.Add(knockoutLabel);

        return overlay;
    }

    private void UpdateHealthBar(EntityManager entityManager, Entity playerEntity)
    {
        bool hasHealth = entityManager.HasComponent<Health>(playerEntity);
        if (hasHealth != lastHealthBarVisible)
        {
            lastHealthBarVisible = hasHealth;
            healthBarRoot.style.display = hasHealth ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!hasHealth)
            return;

        Health health = entityManager.GetComponentData<Health>(playerEntity);
        if (health.healthAmount == lastHealthAmount && health.healthAmountMax == lastHealthMax)
            return;
        lastHealthAmount = health.healthAmount;
        lastHealthMax = health.healthAmountMax;

        float fillPercent = health.healthAmountMax > 0
            ? Mathf.Clamp01((float)health.healthAmount / health.healthAmountMax) * 100f
            : 0f;
        healthFillElement.style.width = new Length(fillPercent, LengthUnit.Percent);
        healthLabel.text = "HP " + health.healthAmount + "/" + health.healthAmountMax;
    }

    private void UpdateKnockoutOverlay(EntityManager entityManager, Entity playerEntity)
    {
        bool isKnockedOut = entityManager.HasComponent<PlayerKnockedOut>(playerEntity)
            && entityManager.IsComponentEnabled<PlayerKnockedOut>(playerEntity);
        if (isKnockedOut != lastKnockoutVisible)
        {
            lastKnockoutVisible = isKnockedOut;
            knockoutOverlay.style.display = isKnockedOut ? DisplayStyle.Flex : DisplayStyle.None;
            if (!isKnockedOut)
                lastKnockoutSecondsShown = -1;
        }

        if (!isKnockedOut)
            return;

        PlayerKnockedOut knockout = entityManager.GetComponentData<PlayerKnockedOut>(playerEntity);
        int secondsRemaining = Mathf.CeilToInt(knockout.remainingSeconds);
        if (secondsRemaining == lastKnockoutSecondsShown)
            return;
        lastKnockoutSecondsShown = secondsRemaining;
        knockoutLabel.text = "Knocked out — waking at the workstation… " + secondsRemaining;
    }
}
