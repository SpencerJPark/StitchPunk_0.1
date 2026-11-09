using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Entities;
using Unity.Mathematics;

// The game's own pointer: it replaces the OS cursor in every gameplay mode and sits exactly where the OS cursor would
// be, at the same speed. A gamepad stick (ControlUnits) steers it instead until the mouse moves again. Menus (Menu /
// MapUI maps) hand back the OS cursor, because a UI Toolkit panel can draw over this overlay canvas.
public class UnitSelectionCursorUI : MonoBehaviour, IUpdateObserver
{
    // Above every other overlay canvas in the HUD.
    private const int CURSOR_CANVAS_SORTING_ORDER = 32000;

    [Header("References")]
    [SerializeField] private RectTransform cursorImageRectTransform;
    [SerializeField] private Canvas uiCanvas;

    [Header("Gamepad")]
    [Tooltip("Full stick tilt moves the cursor this many screen heights per second.")]
    [SerializeField] private float gamepadScreenHeightsPerSecond = 0.9f;

    public bool IsActive { get; private set; }

    // Screen-space position in pixels, bottom-left origin.
    public Vector2 ScreenPosition => cursorScreenPosition;

    private Vector2 cursorScreenPosition;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool hasPlayer;

    private void Awake()
    {
        if (cursorImageRectTransform == null)
            Debug.LogError("UnitSelectionCursorUI: cursorImageRectTransform is not assigned.");

        if (uiCanvas == null)
            uiCanvas = GetComponentInParent<Canvas>();

        if (uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            Debug.LogWarning("UnitSelectionCursorUI: For HUD-style cursor, Canvas should be Screen Space - Overlay.");

        if (cursorImageRectTransform != null)
        {
            // The flag tip at the image's top-left corner is the click point, like the OS arrow.
            cursorImageRectTransform.pivot = new Vector2(0f, 1f);
            Canvas cursorCanvas = cursorImageRectTransform.GetComponent<Canvas>();
            if (cursorCanvas == null) cursorCanvas = cursorImageRectTransform.gameObject.AddComponent<Canvas>();
            cursorCanvas.overrideSorting = true;
            cursorCanvas.sortingOrder = CURSOR_CANVAS_SORTING_ORDER;
        }

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerActionMap>());

        SetVisible(false);
        Cursor.visible = true;
        hasPlayer = false;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        Cursor.visible = true;
    }

    public void ObservedUpdate()
    {
        if (!EnsurePlayerEntity())
        {
            if (IsActive) Deactivate();
            return;
        }

        ActionMaps activeActionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap;
        bool shouldBeActive = activeActionMap == ActionMaps.Player
            || activeActionMap == ActionMaps.Vehicle
            || activeActionMap == ActionMaps.ControlUnits;

        if (shouldBeActive && !IsActive)
            Activate();
        else if (!shouldBeActive && IsActive)
            Deactivate();

        if (!IsActive) return;

        // Re-asserted every frame: closing a menu or the game-over screen turns the OS cursor back on.
        Cursor.visible = false;
        UpdatePosition(activeActionMap);
        ApplyPositionToUI();
    }

    private bool EnsurePlayerEntity()
    {
        if (hasPlayer)
        {
            if (entityManager.Exists(playerEntity)) return true;
            hasPlayer = false;
        }

        if (playerQuery.IsEmpty) return false;

        playerEntity = playerQuery.GetSingletonEntity();
        hasPlayer = true;
        return true;
    }

    private void Activate()
    {
        IsActive = true;
        SetVisible(true);
        cursorScreenPosition = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Cursor.visible = false;
    }

    public void Deactivate()
    {
        IsActive = false;
        SetVisible(false);
        Cursor.visible = true;
    }

    private void SetVisible(bool visible)
    {
        if (cursorImageRectTransform != null)
            cursorImageRectTransform.gameObject.SetActive(visible);
    }

    private void UpdatePosition(ActionMaps activeActionMap)
    {
        // The mouse always wins the frame it moves, so the pointer never drifts from where the OS would put it.
        bool mouseMovedThisFrame = Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0f;
        if (mouseMovedThisFrame)
        {
            cursorScreenPosition = Mouse.current.position.ReadValue();
            return;
        }

        // CursorMovement carries the stick only in ControlUnits; its mouse binding is a delta and is ignored here.
        bool stickSteers = activeActionMap == ActionMaps.ControlUnits
            && entityManager.IsComponentEnabled<CursorPlayerInput>(playerEntity)
            && Gamepad.current != null
            && Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0f;
        if (!stickSteers) return;

        float2 stickInput = entityManager.GetComponentData<CursorPlayerInput>(playerEntity).cursorInput;
        float pixelsPerSecond = gamepadScreenHeightsPerSecond * Screen.height;
        cursorScreenPosition += new Vector2(stickInput.x, stickInput.y) * pixelsPerSecond * Time.unscaledDeltaTime;
        cursorScreenPosition.x = Mathf.Clamp(cursorScreenPosition.x, 0f, Screen.width);
        cursorScreenPosition.y = Mathf.Clamp(cursorScreenPosition.y, 0f, Screen.height);
    }

    private void ApplyPositionToUI()
    {
        if (hasPlayer)
            entityManager.SetComponentData(playerEntity, new CursorScreenPosition
            {
                Value = new float2(cursorScreenPosition.x, cursorScreenPosition.y)
            });

        // On a Screen Space - Overlay canvas a world position is a screen pixel, whatever the parent rects' anchors.
        if (cursorImageRectTransform != null)
            cursorImageRectTransform.position = new Vector3(cursorScreenPosition.x, cursorScreenPosition.y, 0f);
    }
}
