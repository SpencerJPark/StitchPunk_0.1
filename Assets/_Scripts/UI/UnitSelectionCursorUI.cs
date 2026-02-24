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
    private EntityManager entityManager;
    private EntityQuery inputQuery;
    private Entity inputEntity;
    private bool hasInputEntity;

    private void Awake()
    {
        if (cursorImageRectTransform == null)
        {
            Debug.LogError("UnitSelectionCursorUI: cursorImageRectTransform is not assigned.");
        }

        if (uiCanvas == null)
        {
            uiCanvas = GetComponentInParent<Canvas>();
        }

        if (uiCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("UnitSelectionCursorUI: For HUD-style cursor, Canvas should be Screen Space - Overlay.");
        }

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        inputQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerInputData>());

        // Start hidden
        SetVisible(false);
        Cursor.visible = true;

        hasInputEntity = false;
    }

    private void OnEnable()
    {
        UpdateManager.RegisterObserver(this);
    }

    private void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
        Cursor.visible = true;
    }

    public void ObservedUpdate()
    {
        if (!EnsureInputEntity())
        {
            if (IsActive)
            {
                Deactivate();
            }
            return;
        }

        PlayerInputData inputData = entityManager.GetComponentData<PlayerInputData>(inputEntity);

        // Decide if the fake cursor *should* be active in this frame
        bool shouldBeActive = (inputData.activeActionMap == ActionMaps.ControlUnits);

        // Handle visibility toggling based on ECS state
        if (shouldBeActive && !IsActive)
        {
            ActivateFromInput();
        }
        else if (!shouldBeActive && IsActive)
        {
            Deactivate();
        }

        if (!IsActive)
        {
            return;
        }

        // When active in ControlUnits mode, move the cursor
        UpdatePosition(inputData);
        ApplyPositionToUI();
    }

    private bool EnsureInputEntity()
    {
        if (hasInputEntity)
        {
            if (entityManager.Exists(inputEntity))
            {
                return true;
            }

            hasInputEntity = false;
        }

        if (inputQuery.IsEmpty)
        {
            return false;
        }

        inputEntity = inputQuery.GetSingletonEntity();
        hasInputEntity = true;
        return true;
    }

    /// <summary>
    /// Activate the fake cursor and initialize its position.
    /// </summary>
    private void ActivateFromInput()
    {
        IsActive = true;
        SetVisible(true);

        // Start at current mouse pos if available, so transition feels natural
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
        {
            cursorImageRectTransform.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Move the cursor in screen space based on PlayerInputData.cursorInput.
    /// cursorInput should be driven by mouse delta or right stick in PlayerInputManager.
    /// </summary>
    private void UpdatePosition(PlayerInputData inputData)
    {
        float2 cursorInput = inputData.cursorInput;

        // Debug to see if we're actually getting input
        // Debug.Log($"CursorInput: {cursorInput}");

        Vector2 delta = new Vector2(cursorInput.x, cursorInput.y) * controllerSpeed * Time.deltaTime;

        cursorScreenPosition += delta;

        // Clamp to screen bounds
        cursorScreenPosition.x = Mathf.Clamp(cursorScreenPosition.x, 0f, Screen.width);
        cursorScreenPosition.y = Mathf.Clamp(cursorScreenPosition.y, 0f, Screen.height);
    }

    private void ApplyPositionToUI()
    {
        if (cursorImageRectTransform == null)
        {
            return;
        }

        RectTransform canvasRect = uiCanvas.transform as RectTransform;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            cursorScreenPosition,
            null, // null because ScreenSpaceOverlay
            out Vector2 localPoint);

        cursorImageRectTransform.anchoredPosition = localPoint;
    }
}

