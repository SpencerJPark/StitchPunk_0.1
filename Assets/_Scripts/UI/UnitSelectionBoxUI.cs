using UnityEngine;
using Rive.Components;

public class UnitSelectionBoxUI : MonoBehaviour, IUpdateObserver
{
    [SerializeField] private RiveWidget riveWidget;

    private RiveAnimatorHelper selectionRive;
    private bool wasSelecting;

    private const float LINE_WIDTH        = 100f;
    private const float MUSIC_NOTE_WIDTH  = 90f;
    private const float MUSIC_NOTE_EXTRA  = 22f;

    private void Start()
    {
        selectionRive = new RiveAnimatorHelper();
        selectionRive.Initialize(riveWidget);
        selectionRive.SetNumber("Visable", 0f);
        wasSelecting = false;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (UnitSelectionManager.Instance == null) return;

        bool isSelecting = UnitSelectionManager.Instance.IsSelecting;

        if (isSelecting && !wasSelecting)
            selectionRive.SetNumber("Visable", 1f);
        else if (!isSelecting && wasSelecting)
            selectionRive.SetNumber("Visable", 0f);

        wasSelecting = isSelecting;

        if (isSelecting)
            UpdateVisual(UnitSelectionManager.Instance.GetSelectionAreaRect());
    }

    private void UpdateVisual(Rect rect)
    {
        selectionRive.SetNumber("PositionX",            rect.x);
        selectionRive.SetNumber("PositionY",            rect.y);
        selectionRive.SetNumber("SelectionAreaWidth",   rect.width);
        selectionRive.SetNumber("SelectionAreaHeight",  rect.height);
        selectionRive.SetNumber("LineAmount",           Mathf.Floor(rect.height / LINE_WIDTH));
        selectionRive.SetNumber("MusicNoteAmount",      CalculateMusicNotes(rect.width, rect.height));
    }

    private static float CalculateMusicNotes(float width, float height)
    {
        float minSpace = MUSIC_NOTE_WIDTH + MUSIC_NOTE_EXTRA;
        if (width < minSpace) return 0f;
        float lines = Mathf.Floor(height / LINE_WIDTH);
        return Mathf.Floor((width - MUSIC_NOTE_EXTRA) / MUSIC_NOTE_WIDTH) * lines;
    }
}
