using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class UnitSelectionCursorUI : MonoBehaviour, IUpdateObserver
{
    [Header("References")]
    [SerializeField] private RectTransform cursorImageRectTransform;
    [SerializeField] private Canvas uiCanvas;

    [Header("Movement")]
    [SerializeField] private float controllerSpeed = 900f;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Screen-space position in pixels (0..Screen.width, 0..Screen.height)
    /// </summary>
    public Vector2 ScreenPosition => cursorScreenPosition;

    private Vector2 cursorScreenPosition;

    // ECS
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

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);
        bool shouldBeActive = actionMap.activeActionMap == ActionMaps.ControlUnits;

        if (shouldBeActive && !IsActive)
            ActivateFromInput();
        else if (!shouldBeActive && IsActive)
            Deactivate();

        if (!IsActive) return;

        UpdatePosition();
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

    private void ActivateFromInput()
    {
        IsActive = true;
        SetVisible(true);
        cursorScreenPosition = Input.mousePresent
            ? (Vector2)Input.mousePosition
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

    private void UpdatePosition()
    {
        float2 cursorInput = float2.zero;
        if (entityManager.IsComponentEnabled<CursorPlayerInput>(playerEntity))
            cursorInput = entityManager.GetComponentData<CursorPlayerInput>(playerEntity).cursorInput;

        Vector2 delta = new Vector2(cursorInput.x, cursorInput.y) * controllerSpeed * Time.deltaTime;
        cursorScreenPosition += delta;
        cursorScreenPosition.x = Mathf.Clamp(cursorScreenPosition.x, 0f, Screen.width);
        cursorScreenPosition.y = Mathf.Clamp(cursorScreenPosition.y, 0f, Screen.height);
    }

    private void ApplyPositionToUI()
    {
        if (hasPlayer)
            entityManager.SetComponentData(playerEntity, new CursorScreenPosition
            {
                Value = new Unity.Mathematics.float2(cursorScreenPosition.x, cursorScreenPosition.y)
            });

        if (cursorImageRectTransform == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            uiCanvas.transform as RectTransform,
            cursorScreenPosition,
            null,
            out Vector2 localPoint);

        cursorImageRectTransform.anchoredPosition = localPoint;
    }
}
