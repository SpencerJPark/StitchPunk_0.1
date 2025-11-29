using UnityEngine;
using UnityEngine.UI;

public class UnitSelectionCursorUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform cursorImageRectTransform;   // assign MusicCursorImage
    [SerializeField] private Canvas uiCanvas;                          // the canvas this lives on

    [Header("Movement")]
    [SerializeField] private float controllerSpeed = 900f;             // pixels per second
    [SerializeField] private bool allowMouseInput = true;

    public bool IsActive { get; private set; }

    // internal screen-space position in canvas pixels
    private Vector2 cursorPosition;

    private void Awake()
    {
        if (cursorImageRectTransform == null)
            Debug.LogError("UnitSelectionCursorUI: cursorImageRectTransform is not assigned.");

        if (uiCanvas == null)
            uiCanvas = GetComponentInParent<Canvas>();

        // Start hidden
        SetVisible(false);
        Cursor.visible = true; // make sure system cursor is visible by default
    }

    private void OnDisable()
    {
        // Safety: if this component gets disabled while active, restore OS cursor
        Cursor.visible = true;
    }

    private void Update()
    {
        // ⬇⬇⬇ CHANGE #1: Tab as a TOGGLE instead of hold ⬇⬇⬇
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (IsActive)
                Deactivate();
            else
                Activate();
        }
        // ⬆⬆⬆ END CHANGE #1 ⬆⬆⬆

        if (!IsActive)
            return;

        UpdateInput();
        ApplyPositionToUI();
    }

    public void Activate()
    {
        IsActive = true;
        SetVisible(true);

        RectTransform canvasRect = uiCanvas.transform as RectTransform;

        // Optional: start at mouse position if present, so swap feels seamless
        if (allowMouseInput && Input.mousePresent)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                Input.mousePosition,
                uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera,
                out Vector2 localPoint);

            cursorPosition = localPoint + canvasRect.rect.size / 2f;
        }
        else
        {
            // Otherwise initialize position to center of canvas
            cursorPosition = canvasRect.rect.size / 2f;
        }

        // ⬇⬇⬇ CHANGE #2: hide OS cursor when fake cursor is active ⬇⬇⬇
        Cursor.visible = false;
        // ⬆⬆⬆ END CHANGE #2 ⬆⬆⬆
    }

    public void Deactivate()
    {
        IsActive = false;
        SetVisible(false);

        // ⬇⬇⬇ CHANGE #3: show OS cursor again ⬇⬇⬇
        Cursor.visible = true;
        // ⬆⬆⬆ END CHANGE #3 ⬆⬆⬆
    }

    private void SetVisible(bool visible)
    {
        if (cursorImageRectTransform != null)
            cursorImageRectTransform.gameObject.SetActive(visible);
    }

    private void UpdateInput()
    {
        // 1. Controller stick movement
        Vector2 controllerDelta = new Vector2(
            Input.GetAxis("Horizontal"),
            Input.GetAxis("Vertical")
        );

        // Convert to pixels-per-frame
        controllerDelta *= controllerSpeed * Time.deltaTime;

        // 2. Optional mouse input: snap to mouse position if mouse moved
        if (allowMouseInput)
        {
            // NOTE: parentheses to keep logic correct
            if (Input.mousePresent && (Input.GetAxis("Mouse X") != 0f || Input.GetAxis("Mouse Y") != 0f))
            {
                RectTransform canvasRect = uiCanvas.transform as RectTransform;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    Input.mousePosition,
                    uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera,
                    out Vector2 localPoint);

                cursorPosition = localPoint + canvasRect.rect.size / 2f;
            }
        }

        // 3. Apply controller delta
        cursorPosition += controllerDelta;

        // Clamp to canvas bounds
        RectTransform canvasRectClamp = uiCanvas.transform as RectTransform;
        Vector2 size = canvasRectClamp.rect.size;
        cursorPosition.x = Mathf.Clamp(cursorPosition.x, 0f, size.x);
        cursorPosition.y = Mathf.Clamp(cursorPosition.y, 0f, size.y);
    }

    private void ApplyPositionToUI()
    {
        if (cursorImageRectTransform == null)
            return;

        RectTransform canvasRect = uiCanvas.transform as RectTransform;

        // Canvas (0,0) is bottom-left in cursorPosition;
        // RectTransform anchoredPosition is relative to center:
        Vector2 anchored = cursorPosition - (canvasRect.rect.size / 2f);
        cursorImageRectTransform.anchoredPosition = anchored;
    }
}
