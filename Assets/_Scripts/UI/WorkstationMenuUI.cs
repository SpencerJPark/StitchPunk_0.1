using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// Placeholder until the Rive artboard exists (CampLoop_System.md CL-D11).
public class WorkstationMenuUI : MonoBehaviour
{
    public static WorkstationMenuUI ActiveInstance { get; private set; }

    [SerializeField] private UIDocument menuDocument;

    private VisualElement menuRoot;
    private VisualElement summonList;
    private VisualElement queueProgressFill;
    private Label healthLabel;
    private Label queueLabel;
    private Button craftRoundButton;
    private Button repairButton;
    private Button furnaceButton;
    private Button packUpButton;
    private Button dismountButton;

    private EntityQuery playerQuery;
    private EntityQuery unitLibraryQuery;
    private World queryWorld;

    private Entity currentPlayer = Entity.Null;
    private Entity currentWorkstation = Entity.Null;
    private int builtSummonOptionCount = -1;
    private Entity builtForWorkstation = Entity.Null;
    private bool wasVisible;

    private readonly Dictionary<Button, System.Action> buttonActions = new Dictionary<Button, System.Action>();
    private Button hoveredButton;

    private string lastHealthText;
    private string lastQueueText;
    private string lastCraftText;
    private string lastRepairText;
    private string lastFurnaceText;
    private float lastFillPercent = -1f;

    private void OnEnable()
    {
        if (menuDocument == null)
            return;

        VisualElement root = menuDocument.rootVisualElement;
        menuRoot = root.Q<VisualElement>("workstation-menu-root");
        summonList = root.Q<VisualElement>("summon-list");
        queueProgressFill = root.Q<VisualElement>("queue-progress-fill");
        healthLabel = root.Q<Label>("health-label");
        queueLabel = root.Q<Label>("queue-label");
        craftRoundButton = root.Q<Button>("craft-round-button");
        repairButton = root.Q<Button>("repair-button");
        furnaceButton = root.Q<Button>("furnace-button");
        packUpButton = root.Q<Button>("pack-up-button");
        dismountButton = root.Q<Button>("dismount-button");

        ActiveInstance = this;
        VirtualCursorUiRouter.isScreenPositionOverUi = IsScreenPositionOverMenu;
        VirtualCursorUiRouter.tryClickAtScreenPosition = TryClickAtScreenPosition;
        buttonActions.Clear();
        hoveredButton = null;

        RegisterClick(craftRoundButton, OnCraftRoundClicked, true);
        RegisterClick(repairButton, OnRepairClicked, true);
        RegisterClick(furnaceButton, OnFurnaceClicked, true);
        RegisterClick(packUpButton, OnPackUpClicked, true);
        RegisterClick(dismountButton, OnDismountClicked, true);

        builtSummonOptionCount = -1;
        ResetCachedText();
    }

    private void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
        VirtualCursorUiRouter.isScreenPositionOverUi = null;
        VirtualCursorUiRouter.tryClickAtScreenPosition = null;
        SetHoveredButton(null);
        buttonActions.Clear();
        UnregisterClick(craftRoundButton, OnCraftRoundClicked);
        UnregisterClick(repairButton, OnRepairClicked);
        UnregisterClick(furnaceButton, OnFurnaceClicked);
        UnregisterClick(packUpButton, OnPackUpClicked);
        UnregisterClick(dismountButton, OnDismountClicked);
    }

    private static void RegisterClick(Button button, System.Action handler)
    {
        if (button != null)
            button.clicked += handler;
    }

    private void RegisterClick(Button button, System.Action handler, bool trackForVirtualCursor)
    {
        RegisterClick(button, handler);
        if (button != null && trackForVirtualCursor)
            buttonActions[button] = handler;
    }

    private static void UnregisterClick(Button button, System.Action handler)
    {
        if (button != null)
            button.clicked -= handler;
    }

    private void ResetCachedText()
    {
        lastHealthText = null;
        lastQueueText = null;
        lastCraftText = null;
        lastRepairText = null;
        lastFurnaceText = null;
        lastFillPercent = -1f;
    }

    private void LateUpdate()
    {
        if (menuRoot == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
            unitLibraryQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<UnitDataLibrary>());
        }

        EntityManager entityManager = world.EntityManager;
        currentPlayer = Entity.Null;
        currentWorkstation = Entity.Null;

        if (!playerQuery.IsEmptyIgnoreFilter)
        {
            Entity playerEntity = playerQuery.GetSingletonEntity();
            if (entityManager.HasComponent<WorkstationMounted>(playerEntity)
                && entityManager.IsComponentEnabled<WorkstationMounted>(playerEntity))
            {
                Entity workstation = entityManager.GetComponentData<WorkstationMounted>(playerEntity).workstation;
                if (entityManager.Exists(workstation))
                {
                    currentPlayer = playerEntity;
                    currentWorkstation = workstation;
                }
            }
        }

        bool shouldShow = currentWorkstation != Entity.Null;
        if (shouldShow != wasVisible)
        {
            wasVisible = shouldShow;
            menuRoot.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
            if (shouldShow)
            {
                builtSummonOptionCount = -1;
                ResetCachedText();
            }
        }

        if (!shouldShow)
        {
            SetHoveredButton(null);
            return;
        }

        RebuildSummonListIfNeeded(entityManager);
        RefreshVirtualCursorHover(entityManager);
        RefreshHealth(entityManager);
        RefreshQueue(entityManager);
        RefreshRecipeButtons(entityManager);
    }

    private bool IsMenuVisible()
    {
        return menuRoot != null && menuRoot.panel != null && menuRoot.resolvedStyle.display != DisplayStyle.None && wasVisible;
    }

    private VisualElement PickMenuElementAtScreenPosition(Vector2 screenPositionBottomLeft)
    {
        if (!IsMenuVisible())
            return null;

        // UI Toolkit screen space has Y down, Input.mousePosition-style space has Y up.
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
            menuRoot.panel, new Vector2(screenPositionBottomLeft.x, Screen.height - screenPositionBottomLeft.y));
        VisualElement picked = menuRoot.panel.Pick(panelPosition);
        for (VisualElement current = picked; current != null; current = current.parent)
        {
            if (current == menuRoot)
                return picked;
        }
        return null;
    }

    private static Button FindAncestorOrSelfButton(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current is Button button)
                return button;
        }
        return null;
    }

    public bool IsScreenPositionOverMenu(Vector2 screenPositionBottomLeft)
    {
        return PickMenuElementAtScreenPosition(screenPositionBottomLeft) != null;
    }

    // Returns true when the click landed on the menu at all, so it never also commands minions.
    public bool TryClickAtScreenPosition(Vector2 screenPositionBottomLeft)
    {
        VisualElement picked = PickMenuElementAtScreenPosition(screenPositionBottomLeft);
        if (picked == null)
            return false;

        Button button = FindAncestorOrSelfButton(picked);
        if (button != null && button.enabledInHierarchy && buttonActions.TryGetValue(button, out System.Action action))
            action();
        return true;
    }

    private void RefreshVirtualCursorHover(EntityManager entityManager)
    {
        Button buttonUnderCursor = null;
        if (currentPlayer != Entity.Null && entityManager.HasComponent<CursorScreenPosition>(currentPlayer))
        {
            float2 cursorPosition = entityManager.GetComponentData<CursorScreenPosition>(currentPlayer).Value;
            VisualElement picked = PickMenuElementAtScreenPosition(new Vector2(cursorPosition.x, cursorPosition.y));
            buttonUnderCursor = FindAncestorOrSelfButton(picked);
        }
        SetHoveredButton(buttonUnderCursor);
    }

    private void SetHoveredButton(Button button)
    {
        if (button == hoveredButton)
            return;
        if (hoveredButton != null)
            hoveredButton.style.backgroundColor = StyleKeyword.Null;
        hoveredButton = button;
        if (hoveredButton != null)
            hoveredButton.style.backgroundColor = new Color(0.45f, 0.45f, 0.5f, 1f);
    }

    private void RebuildSummonListIfNeeded(EntityManager entityManager)
    {
        if (summonList == null || !entityManager.HasBuffer<WorkstationSummonOption>(currentWorkstation))
            return;

        DynamicBuffer<WorkstationSummonOption> options = entityManager.GetBuffer<WorkstationSummonOption>(currentWorkstation, true);
        if (options.Length == builtSummonOptionCount && builtForWorkstation == currentWorkstation)
            return;

        builtSummonOptionCount = options.Length;
        builtForWorkstation = currentWorkstation;
        hoveredButton = null;
        foreach (VisualElement staleChild in summonList.Children())
        {
            if (staleChild is Button staleButton)
                buttonActions.Remove(staleButton);
        }
        summonList.Clear();

        bool hasLibrary = !unitLibraryQuery.IsEmptyIgnoreFilter;
        BlobAssetReference<UnitLibraryBlob> library = default;
        if (hasLibrary)
            library = unitLibraryQuery.GetSingleton<UnitDataLibrary>().library;

        for (int index = 0; index < options.Length; index++)
        {
            UnitType unitType = options[index].unitType;
            float buildSeconds = options[index].buildSeconds;
            string costText = "?";
            if (hasLibrary && library.IsCreated)
            {
                int unitIndex = library.Value.FindByUnitType(unitType);
                if (unitIndex >= 0)
                {
                    SummonCostBlob cost = library.Value.units[unitIndex].summonCost;
                    costText = cost.corpses + "/" + cost.scrapMetal + "/" + cost.electricity;
                }
            }

            Button summonButton = new Button();
            summonButton.text = unitType + " — " + costText + " (" + buildSeconds + "s)";
            summonButton.style.minHeight = 32;
            summonButton.style.fontSize = 14;
            summonButton.style.marginBottom = 4;
            UnitType capturedUnitType = unitType;
            System.Action summonAction = () => OnSummonClicked(capturedUnitType);
            summonButton.clicked += summonAction;
            buttonActions[summonButton] = summonAction;
            summonList.Add(summonButton);
        }
    }

    private void RefreshHealth(EntityManager entityManager)
    {
        if (healthLabel == null)
            return;

        string text = "Station ?/?";
        if (entityManager.HasComponent<Health>(currentWorkstation))
        {
            Health health = entityManager.GetComponentData<Health>(currentWorkstation);
            text = "Station " + health.healthAmount + "/" + health.healthAmountMax;
        }
        SetLabelText(healthLabel, ref lastHealthText, text);
    }

    private void RefreshQueue(EntityManager entityManager)
    {
        string text = "Queue empty";
        float fillPercent = 0f;
        if (entityManager.HasBuffer<WorkstationSummonQueueEntry>(currentWorkstation))
        {
            DynamicBuffer<WorkstationSummonQueueEntry> queue = entityManager.GetBuffer<WorkstationSummonQueueEntry>(currentWorkstation, true);
            if (queue.Length > 0)
            {
                WorkstationSummonQueueEntry head = queue[0];
                text = "Building " + head.unitType;
                if (queue.Length > 1)
                    text += " (+" + (queue.Length - 1) + " queued)";
                if (head.totalSeconds > 0f)
                    fillPercent = Mathf.Clamp01(1f - head.remainingSeconds / head.totalSeconds) * 100f;
            }
        }

        if (queueLabel != null)
            SetLabelText(queueLabel, ref lastQueueText, text);

        if (queueProgressFill != null && !Mathf.Approximately(fillPercent, lastFillPercent))
        {
            lastFillPercent = fillPercent;
            queueProgressFill.style.width = Length.Percent(fillPercent);
        }
    }

    private void RefreshRecipeButtons(EntityManager entityManager)
    {
        if (entityManager.HasComponent<WorkstationRecipes>(currentWorkstation))
        {
            WorkstationRecipes recipes = entityManager.GetComponentData<WorkstationRecipes>(currentWorkstation);
            SetButtonText(craftRoundButton, ref lastCraftText,
                "Craft revival round (" + recipes.revivalRoundScrapCost + " scrap, " + recipes.revivalRoundElectricityCost + " elec)");
            SetButtonText(repairButton, ref lastRepairText,
                "Repair +" + recipes.repairHealthAmount + " (" + recipes.repairWoodCost + " wood)");
        }

        if (entityManager.HasComponent<WorkstationFurnace>(currentWorkstation))
        {
            WorkstationFurnace furnace = entityManager.GetComponentData<WorkstationFurnace>(currentWorkstation);
            SetButtonText(furnaceButton, ref lastFurnaceText,
                "Furnace: " + (furnace.isBurning ? "ON" : "OFF")
                + " (1 wood → " + furnace.electricityPerWood + " elec / " + furnace.burnIntervalSeconds + "s)");
        }
    }

    private static void SetLabelText(Label label, ref string lastText, string text)
    {
        if (text == lastText)
            return;
        lastText = text;
        label.text = text;
    }

    private static void SetButtonText(Button button, ref string lastText, string text)
    {
        if (button == null || text == lastText)
            return;
        lastText = text;
        button.text = text;
    }

    private bool TryGetEntityManager(out EntityManager entityManager)
    {
        entityManager = default;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || currentWorkstation == Entity.Null)
            return false;
        entityManager = world.EntityManager;
        return entityManager.Exists(currentWorkstation);
    }

    private void OnSummonClicked(UnitType unitType)
    {
        if (!TryGetEntityManager(out EntityManager entityManager)
            || !entityManager.HasBuffer<WorkstationSummonRequest>(currentWorkstation))
            return;
        entityManager.GetBuffer<WorkstationSummonRequest>(currentWorkstation).Add(new WorkstationSummonRequest { unitType = unitType });
    }

    private void OnCraftRoundClicked()
    {
        if (!TryGetEntityManager(out EntityManager entityManager)
            || !entityManager.HasComponent<CraftRevivalRoundRequest>(currentWorkstation))
            return;
        entityManager.SetComponentData(currentWorkstation, new CraftRevivalRoundRequest { count = 1 });
        entityManager.SetComponentEnabled<CraftRevivalRoundRequest>(currentWorkstation, true);
    }

    private void OnRepairClicked()
    {
        EnableWorkstationRequest<WorkstationRepairRequest>();
    }

    private void OnFurnaceClicked()
    {
        EnableWorkstationRequest<WorkstationFurnaceToggleRequest>();
    }

    private void OnPackUpClicked()
    {
        EnableWorkstationRequest<WorkstationPackUpRequest>();
    }

    private void EnableWorkstationRequest<TRequest>() where TRequest : unmanaged, IComponentData, IEnableableComponent
    {
        if (!TryGetEntityManager(out EntityManager entityManager)
            || !entityManager.HasComponent<TRequest>(currentWorkstation))
            return;
        entityManager.SetComponentEnabled<TRequest>(currentWorkstation, true);
    }

    private void OnDismountClicked()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || currentPlayer == Entity.Null)
            return;
        EntityManager entityManager = world.EntityManager;
        if (entityManager.Exists(currentPlayer) && entityManager.HasComponent<WorkstationDismountRequest>(currentPlayer))
            entityManager.SetComponentEnabled<WorkstationDismountRequest>(currentPlayer, true);
    }
}
