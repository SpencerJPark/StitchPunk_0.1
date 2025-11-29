using UnityEngine;
using Rive;
using Rive.Components;

public class UnitSelectionBoxUI : MonoBehaviour {
    
    public RiveWidget riveWidget;

    private RiveAnimator selectionRiveVisual;
    
    private bool visable = false;
    
    private float lineAmount;
    private float musiceNoteAmount;


    private void Start() {
        UnitSelectionManager.Instance.OnSelectionAreaStart += UnitSelectionManager_OnSelectionAreaStart;
        UnitSelectionManager.Instance.OnSelectionAreaEnd += UnitSelectionManager_OnSelectionAreaEnd;
        
        selectionRiveVisual = new RiveAnimator();
        selectionRiveVisual.Initialize(riveWidget);
        
        selectionRiveVisual.SetNumber("Visable", 0f);
        visable = false;
    }

    private void Update() {
        if (visable) {
            UpdateVisual();
        }
    }

    private void UnitSelectionManager_OnSelectionAreaStart(object sender, System.EventArgs e) {
        selectionRiveVisual.SetNumber("Visable", 1f);
        visable = true;

        UpdateVisual();
    }

    private void UnitSelectionManager_OnSelectionAreaEnd(object sender, System.EventArgs e) {
        selectionRiveVisual.SetNumber("Visable", 0f);
        visable = false;
    }

    private void UpdateVisual() {
        Rect selectionAreaRect = UnitSelectionManager.Instance.GetSelectionAreaRect();

        CalculateLineAmount(selectionAreaRect.height);
        CalculateMusiceNoteAmount(selectionAreaRect.width);
        
        selectionRiveVisual.SetNumber("PositionX", selectionAreaRect.x);
        selectionRiveVisual.SetNumber("PositionY", selectionAreaRect.y);
        
        selectionRiveVisual.SetNumber("SelectionAreaWidth", selectionAreaRect.width);
        selectionRiveVisual.SetNumber("SelectionAreaHeight", selectionAreaRect.height);
        
        // Debug.Log("Line Amount: " + lineAmount);
        // Debug.Log("Music Note Amount: " + musiceNoteAmount);
        
        selectionRiveVisual.SetNumber("LineAmount", lineAmount);
        selectionRiveVisual.SetNumber("MusicNoteAmount", musiceNoteAmount);
    }

    private void CalculateLineAmount(float selectionAreaHeight)
    {
        lineAmount = Mathf.Floor(selectionAreaHeight / 100f);
    }

    private void CalculateMusiceNoteAmount(float selectionAreaWidth)
    {
        if (selectionAreaWidth < 112f)
        {
            musiceNoteAmount = 0;
            return;
        }
        musiceNoteAmount = Mathf.Floor((selectionAreaWidth - 22) / 90f) * lineAmount;
    }

}