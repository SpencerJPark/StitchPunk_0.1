using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;
using Unity.Mathematics;

public class UnitSelectionCursorUI : MonoBehaviour, IUpdateObserver
{
    [Header("References")]
    [SerializeField] private RectTransform cursorImageRectTransform; // assign MusicCursorImage
    [SerializeField] private Canvas uiCanvas;                        // the canvas this lives on

    [Header("Movement")]
    [SerializeField] private float controllerSpeed = 900f;           // pixels per second

    public bool IsActive { get; private set; }

    /// <summary>
    /// Screen-space position in pixels (0..Screen.width, 0..Screen.height)
    /// </summary>
    public Vector2 ScreenPosition => cursorScreenPosition;

    private Vector2 cursorScreenPosition;

    // ECS
    private EntityManager _entityManager;
    private EntityQuery _inputQuery;
    private Entity _inputEntity;
    private bool _hasInputEntity;

    private void Awake()
    {
        if (cursorImageRectTransform == null)
            Debug.LogError("UnitSelectionCursorUI: cursorImageRectTransform is not assigned.");

        if (uiCanvas == null)
            uiCanvas = GetComponentInParent<Canvas>();

        if (uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            Debug.LogWarning("UnitSelectionCursorUI: For HUD-style cursor, Canvas should be Screen Space - Overlay.");

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _inputQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerInput>());

        // Start hidden
        SetVisible(false);
        Cursor.visible = true;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        Cursor.visible = true;
    }

    public void ObservedUpdate()
    {
        // Tab toggle
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsActive)
                Deactivate();
            else
                Activate();
        }

        if (!IsActive)
            return;

        if (!EnsureInputEntity())
            return; // no PlayerInput yet

        PlayerInput input = _entityManager.GetComponentData<PlayerInput>(_inputEntity);

        // Optional: only move in certain action maps
        if (input.activeActionMap != ActionMaps.ControlUnits &&
            input.activeActionMap != ActionMaps.MapUI)
        {
            // In Player/Vehicle mode maybe you don't want the fake cursor to move at all
            // Comment this out if you want it always active
            UpdatePosition(input);   // or skip
        }
        else
        {
            UpdatePosition(input);
        }

        ApplyPositionToUI();
    }

    private bool EnsureInputEntity()
    {
        if (_hasInputEntity)
        {
            if (_entityManager.Exists(_inputEntity))
                return true;

            _hasInputEntity = false;
        }

        if (_inputQuery.IsEmpty)
            return false;

        _inputEntity = _inputQuery.GetSingletonEntity();
        _hasInputEntity = true;
        return true;
    }

    public void Activate()
    {
        IsActive = true;
        SetVisible(true);

        // Start at mouse position or screen center
        if (Input.mousePresent)
        {
            cursorScreenPosition = Input.mousePosition;
        }
        else
        {
            cursorScreenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

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

    /// <summary>
    /// Move the cursor in screen space based on PlayerInput.lookInput.
    /// </summary>
    private void UpdatePosition(PlayerInput input)
    {
        // lookInput is a float2 (x,y) from mouse delta or right stick
        float2 look = input.lookInput;

        // Treat lookInput as a normalized direction or delta;
        // you can tune how big it is in your PlayerInputManager.
        Vector2 delta = new Vector2(look.x, look.y) * controllerSpeed * Time.deltaTime;

        cursorScreenPosition += delta;

        // Clamp to screen bounds
        cursorScreenPosition.x = Mathf.Clamp(cursorScreenPosition.x, 0f, Screen.width);
        cursorScreenPosition.y = Mathf.Clamp(cursorScreenPosition.y, 0f, Screen.height);
    }

    private void ApplyPositionToUI()
    {
        if (cursorImageRectTransform == null)
            return;

        RectTransform canvasRect = uiCanvas.transform as RectTransform;

        // Convert screen-space position to canvas local position
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            cursorScreenPosition,
            null, // null because ScreenSpaceOverlay
            out Vector2 localPoint);

        cursorImageRectTransform.anchoredPosition = localPoint;
    }
}
