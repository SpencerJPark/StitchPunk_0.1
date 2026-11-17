using UnityEngine;
using UnityEngine.UIElements;

public sealed class SaveIndicatorUI : MonoBehaviour
{
    private const float SavedToastSeconds = 1.5f;
    private const float BlockedToastSeconds = 2.5f;
    private const float GearDegreesPerSecond = 180f;

    [SerializeField] private UIDocument document;

    private VisualElement pill;
    private Label gearLabel;
    private Label toastLabel;
    private float toastHideTime = -1f;
    private bool saving;
    private float gearAngle;

    private void OnEnable()
    {
        BuildOverlay();
        SaveNotifications.SaveStarted += OnSaveStarted;
        SaveNotifications.SaveFinished += OnSaveFinished;
        SaveNotifications.SaveBlocked += OnSaveBlocked;
        SaveNotifications.LoadFinished += OnLoadFinished;
    }

    private void OnDisable()
    {
        SaveNotifications.SaveStarted -= OnSaveStarted;
        SaveNotifications.SaveFinished -= OnSaveFinished;
        SaveNotifications.SaveBlocked -= OnSaveBlocked;
        SaveNotifications.LoadFinished -= OnLoadFinished;
        pill?.RemoveFromHierarchy();
        pill = null;
    }

    private void Update()
    {
        if (pill == null)
            return;

        if (saving)
        {
            gearAngle = (gearAngle + GearDegreesPerSecond * Time.unscaledDeltaTime) % 360f;
            gearLabel.style.rotate = new StyleRotate(new Rotate(new Angle(gearAngle, AngleUnit.Degree)));
        }
        else if (toastHideTime >= 0f && Time.unscaledTime >= toastHideTime)
        {
            toastHideTime = -1f;
            toastLabel.text = string.Empty;
            RefreshVisibility();
        }
    }

    private void BuildOverlay()
    {
        if (document == null)
            return;

        pill = new VisualElement();
        pill.pickingMode = PickingMode.Ignore;
        pill.style.position = Position.Absolute;
        pill.style.right = 24f;
        pill.style.bottom = 24f;
        pill.style.flexDirection = FlexDirection.Row;
        pill.style.alignItems = Align.Center;
        pill.style.paddingLeft = 14f;
        pill.style.paddingRight = 14f;
        pill.style.paddingTop = 6f;
        pill.style.paddingBottom = 6f;
        pill.style.borderTopLeftRadius = 18f;
        pill.style.borderTopRightRadius = 18f;
        pill.style.borderBottomLeftRadius = 18f;
        pill.style.borderBottomRightRadius = 18f;
        pill.style.backgroundColor = new Color(0.08f, 0.07f, 0.06f, 0.75f);
        pill.style.display = DisplayStyle.None;

        Color creamText = new Color32(0xED, 0xE3, 0xC8, 0xFF);

        gearLabel = new Label("⚙");
        gearLabel.pickingMode = PickingMode.Ignore;
        gearLabel.style.fontSize = 22f;
        gearLabel.style.color = creamText;
        gearLabel.style.display = DisplayStyle.None;

        toastLabel = new Label(string.Empty);
        toastLabel.pickingMode = PickingMode.Ignore;
        toastLabel.style.fontSize = 22f;
        toastLabel.style.color = creamText;

        pill.Add(gearLabel);
        pill.Add(toastLabel);
        document.rootVisualElement.Add(pill);
    }

    private void RefreshVisibility()
    {
        bool hasToast = !string.IsNullOrEmpty(toastLabel.text);
        gearLabel.style.display = saving ? DisplayStyle.Flex : DisplayStyle.None;
        toastLabel.style.display = hasToast ? DisplayStyle.Flex : DisplayStyle.None;
        pill.style.display = saving || hasToast ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ShowToast(string message, float seconds)
    {
        if (pill == null)
            return;

        toastLabel.text = message;
        toastHideTime = Time.unscaledTime + seconds;
        RefreshVisibility();
    }

    private void OnSaveStarted(string saveId, SaveKind kind)
    {
        if (pill == null)
            return;

        saving = true;
        toastHideTime = -1f;
        toastLabel.text = string.Empty;
        RefreshVisibility();
    }

    private void OnSaveFinished(string saveId, bool success)
    {
        saving = false;
        ShowToast(success ? "Saved" : "Save failed", SavedToastSeconds);
    }

    private void OnSaveBlocked(string reason)
    {
        ShowToast(reason, BlockedToastSeconds);
    }

    private void OnLoadFinished(string saveId)
    {
        ShowToast("Loaded", SavedToastSeconds);
    }
}
