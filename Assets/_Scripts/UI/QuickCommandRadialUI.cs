using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Hold-to-open five-way radial for squad quick commands. Built in code on the scene's shared PanelSettings.
public class QuickCommandRadialUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static bool IsOpenOnFoot { get; private set; }

    [SerializeField] private UIDocument radialDocument;

    private const float CircleDiameterPixels = 220f;
    private const float LabelOffsetPixels = 72f;
    private const float SelectionMaxLengthPixels = 80f;
    private const float SelectionDeadzonePixels = 25f;
    private const float StickDeadzone = 0.4f;

    private static readonly Color CreamLabelColor = new Color(0.96f, 0.92f, 0.8f, 1f);
    private static readonly Color HoveredLabelColor = new Color(0.35f, 0.9f, 0.9f, 1f);

    // Wedge order: clockwise from the top, evenly spaced.
    private static readonly QuickCommandType[] WedgeCommands =
    {
        QuickCommandType.Follow, QuickCommandType.AttackTarget, QuickCommandType.Gather,
        QuickCommandType.Return, QuickCommandType.Hold
    };
    private static readonly string[] WedgeNames = { "Follow", "Attack target", "Gather", "Return", "Hold" };

    private static Vector2 WedgeDirection(int wedgeIndex)
    {
        float angleRadians = wedgeIndex * (2f * Mathf.PI / WedgeCommands.Length);
        return new Vector2(Mathf.Sin(angleRadians), Mathf.Cos(angleRadians));
    }

    private InputAction onFootHoldAction;
    private InputAction mountedHoldAction;
    private InputAction lookAction;
    private bool lookDisabledByRadial;

    private EntityQuery playerQuery;
    private bool playerQueryCreated;

    private VisualElement circleElement;
    private readonly Label[] wedgeLabels = new Label[WedgeCommands.Length];
    private InputAction activeHoldAction;
    private Vector2 selectionVector;
    private QuickCommandType hoveredCommand;

    private void Update()
    {
        InputAction holdAction = ResolveHoldActionForCurrentMap();

        if (IsOpen)
        {
            if (holdAction == null || holdAction != activeHoldAction)
            {
                Close();
                return;
            }
            AccumulateSelection();
            if (holdAction.WasReleasedThisFrame() || !holdAction.IsPressed())
            {
                QuickCommandType command = hoveredCommand;
                Close();
                if (command != QuickCommandType.None && SquadCommandManager.Instance != null)
                    SquadCommandManager.Instance.IssueQuickCommand(command);
                return;
            }
            RefreshHoverVisuals();
            return;
        }

        if (holdAction != null && holdAction.WasPressedThisFrame())
            Open(holdAction);
    }

    private void OnDisable()
    {
        Close();
    }

    private bool TryReadPlayerEntity(out Entity playerEntity, out EntityManager entityManager)
    {
        playerEntity = Entity.Null;
        entityManager = default;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        entityManager = world.EntityManager;
        if (!playerQueryCreated)
        {
            playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<PlayerActionMap>());
            playerQueryCreated = true;
        }
        if (playerQuery.IsEmpty) return false;
        playerEntity = playerQuery.GetSingletonEntity();
        return true;
    }

    private InputAction ResolveHoldActionForCurrentMap()
    {
        if (!TryReadPlayerEntity(out Entity playerEntity, out EntityManager entityManager)) return null;
        if (PlayerInput.all.Count == 0) return null;
        ActionMaps activeMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap;
        InputActionAsset actions = PlayerInput.all[0].actions;
        if (activeMap == ActionMaps.Player)
        {
            if (onFootHoldAction == null) onFootHoldAction = actions.FindAction("Player/QuickCommand");
            return onFootHoldAction;
        }
        if (activeMap == ActionMaps.ControlUnits)
        {
            if (mountedHoldAction == null) mountedHoldAction = actions.FindAction("ControlUnits/QuickCommandRadial");
            return mountedHoldAction;
        }
        return null;
    }

    private void Open(InputAction holdAction)
    {
        if (radialDocument == null || radialDocument.rootVisualElement == null) return;
        bool onFoot = holdAction == onFootHoldAction;
        BuildElementsIfNeeded();

        Vector2 screenCentre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!onFoot) screenCentre = ReadCursorScreenPosition(screenCentre);
        Vector2 panelCentre = RuntimePanelUtils.ScreenToPanel(
            radialDocument.rootVisualElement.panel, new Vector2(screenCentre.x, Screen.height - screenCentre.y));
        circleElement.style.left = panelCentre.x - CircleDiameterPixels * 0.5f;
        circleElement.style.top = panelCentre.y - CircleDiameterPixels * 0.5f;
        circleElement.style.display = DisplayStyle.Flex;

        activeHoldAction = holdAction;
        selectionVector = Vector2.zero;
        hoveredCommand = QuickCommandType.None;
        IsOpen = true;
        VirtualCursorUiRouter.isRadialOpen = true;
        IsOpenOnFoot = onFoot;

        if (onFoot) SetLookActionEnabled(false);
        RefreshHoverVisuals();
    }

    private void Close()
    {
        if (circleElement != null) circleElement.style.display = DisplayStyle.None;
        IsOpen = false;
        IsOpenOnFoot = false;
        VirtualCursorUiRouter.isRadialOpen = false;
        activeHoldAction = null;
        hoveredCommand = QuickCommandType.None;
        SetLookActionEnabled(true);
    }

    private Vector2 ReadCursorScreenPosition(Vector2 fallback)
    {
        if (!TryReadPlayerEntity(out Entity playerEntity, out EntityManager entityManager)) return fallback;
        if (!entityManager.HasComponent<CursorScreenPosition>(playerEntity)) return fallback;
        Unity.Mathematics.float2 cursor = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
        return new Vector2(cursor.x, cursor.y);
    }

    // Never leaves Look disabled: only re-enables what this radial disabled.
    private void SetLookActionEnabled(bool lookEnabled)
    {
        if (lookEnabled)
        {
            // If the map changed while open, its own Enable() restores Look; enabling here would
            // leak Look input into the Menu/ControlUnits map.
            if (lookDisabledByRadial && lookAction != null && lookAction.actionMap != null && lookAction.actionMap.enabled)
                lookAction.Enable();
            lookDisabledByRadial = false;
            return;
        }
        if (lookAction == null && PlayerInput.all.Count > 0)
            lookAction = PlayerInput.all[0].actions.FindAction("Player/Look");
        if (lookAction == null) return;
        lookAction.Disable();
        lookDisabledByRadial = true;
    }

    private void AccumulateSelection()
    {
        if (Mouse.current != null) selectionVector += Mouse.current.delta.ReadValue();
        if (selectionVector.magnitude > SelectionMaxLengthPixels)
            selectionVector = selectionVector.normalized * SelectionMaxLengthPixels;

        Vector2 stick = Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
        if (stick.magnitude > StickDeadzone) selectionVector = stick.normalized * SelectionMaxLengthPixels;

        hoveredCommand = QuickCommandType.None;
        if (selectionVector.magnitude < SelectionDeadzonePixels) return;
        int bestWedge = 0;
        float bestDot = float.MinValue;
        Vector2 direction = selectionVector.normalized;
        for (int wedgeIndex = 0; wedgeIndex < WedgeCommands.Length; wedgeIndex++)
        {
            float dot = Vector2.Dot(direction, WedgeDirection(wedgeIndex));
            if (dot <= bestDot) continue;
            bestDot = dot;
            bestWedge = wedgeIndex;
        }
        hoveredCommand = WedgeCommands[bestWedge];
    }

    private void RefreshHoverVisuals()
    {
        for (int wedgeIndex = 0; wedgeIndex < wedgeLabels.Length; wedgeIndex++)
        {
            bool isHovered = WedgeCommands[wedgeIndex] == hoveredCommand;
            wedgeLabels[wedgeIndex].style.color = isHovered ? HoveredLabelColor : CreamLabelColor;
            wedgeLabels[wedgeIndex].style.backgroundColor = isHovered
                ? new Color(0.2f, 0.6f, 0.6f, 0.45f)
                : new Color(0f, 0f, 0f, 0f);
        }
    }

    private void BuildElementsIfNeeded()
    {
        if (circleElement != null && circleElement.parent != null) return;

        circleElement = new VisualElement { name = "QuickCommandRadial", pickingMode = PickingMode.Ignore };
        circleElement.style.position = Position.Absolute;
        circleElement.style.width = CircleDiameterPixels;
        circleElement.style.height = CircleDiameterPixels;
        circleElement.style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.72f);
        circleElement.style.borderTopLeftRadius = CircleDiameterPixels * 0.5f;
        circleElement.style.borderTopRightRadius = CircleDiameterPixels * 0.5f;
        circleElement.style.borderBottomLeftRadius = CircleDiameterPixels * 0.5f;
        circleElement.style.borderBottomRightRadius = CircleDiameterPixels * 0.5f;
        circleElement.style.display = DisplayStyle.None;

        const float labelWidth = 96f;
        const float labelHeight = 26f;
        float halfCircle = CircleDiameterPixels * 0.5f;
        for (int wedgeIndex = 0; wedgeIndex < WedgeNames.Length; wedgeIndex++)
        {
            Label label = new Label(WedgeNames[wedgeIndex]) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.width = labelWidth;
            label.style.height = labelHeight;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = 15;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.borderTopLeftRadius = 6;
            label.style.borderTopRightRadius = 6;
            label.style.borderBottomLeftRadius = 6;
            label.style.borderBottomRightRadius = 6;
            label.style.left = halfCircle + WedgeDirection(wedgeIndex).x * LabelOffsetPixels - labelWidth * 0.5f;
            label.style.top = halfCircle - WedgeDirection(wedgeIndex).y * LabelOffsetPixels - labelHeight * 0.5f;
            wedgeLabels[wedgeIndex] = label;
            circleElement.Add(label);
        }
        radialDocument.rootVisualElement.Add(circleElement);
    }
}
