// using UnityEngine;
// using Rive;
// using Rive.Components;
//
// public class UnitSelectionBoxUI : MonoBehaviour, IUpdateObserver {
//     
//     // Rive Setup
//     public RiveWidget riveWidget;
//     private RiveAnimator selectionRiveVisual;
//     
//     // Values to Pass
//     private bool visable = false;
//     private float lineAmount;
//     private float musiceNoteAmount;
//     
//     // Constant Variables
//     private const float lineWidth = 100f;
//     private const float musicNoteWidth = 90f;
//     private const float musicNoteExtra = 22f;
//     private const float minMusicNoteSpace = musicNoteWidth + musicNoteExtra;
//     
//
//     private void Start() {
//        // UnitSelectionManager.Instance.OnSelectionAreaStart += UnitSelectionManager_OnSelectionAreaStart;
//        // UnitSelectionManager.Instance.OnSelectionAreaEnd += UnitSelectionManager_OnSelectionAreaEnd;
//         
//         selectionRiveVisual = new RiveAnimator();
//         selectionRiveVisual.Initialize(riveWidget);
//         
//         selectionRiveVisual.SetNumber("Visable", 0f);
//         visable = false;
//     }
//     
//     
//     private void OnEnable() => UpdateManager.RegisterObserver(this);
//     private void OnDisable() => UpdateManager.UnregisterObserver(this);
//
//     
//     public void ObservedUpdate() {
//         if (visable) {
//             UpdateVisual();
//         }
//     }
//
//     private void UnitSelectionManager_OnSelectionAreaStart(object sender, System.EventArgs e) {
//         selectionRiveVisual.SetNumber("Visable", 1f);
//         visable = true;
//
//         UpdateVisual();
//     }
//
//     private void UnitSelectionManager_OnSelectionAreaEnd(object sender, System.EventArgs e) {
//         selectionRiveVisual.SetNumber("Visable", 0f);
//         visable = false;
//     }
//
//     private void UpdateVisual() {
//         Rect selectionAreaRect = UnitSelectionManager.Instance.GetSelectionAreaRect();
//
//         CalculateLineAmount(selectionAreaRect.height);
//         CalculateMusiceNoteAmount(selectionAreaRect.width);
//         
//         selectionRiveVisual.SetNumber("PositionX", selectionAreaRect.x);
//         selectionRiveVisual.SetNumber("PositionY", selectionAreaRect.y);
//         
//         selectionRiveVisual.SetNumber("SelectionAreaWidth", selectionAreaRect.width);
//         selectionRiveVisual.SetNumber("SelectionAreaHeight", selectionAreaRect.height);
//         
//         // Debug.Log("Line Amount: " + lineAmount);
//         // Debug.Log("Music Note Amount: " + musiceNoteAmount);
//         
//         selectionRiveVisual.SetNumber("LineAmount", lineAmount);
//         selectionRiveVisual.SetNumber("MusicNoteAmount", musiceNoteAmount);
//     }
//
//     private void CalculateLineAmount(float selectionAreaHeight)
//     {
//         lineAmount = Mathf.Floor(selectionAreaHeight / lineWidth);
//     }
//
//     private void CalculateMusiceNoteAmount(float selectionAreaWidth)
//     {
//         if (selectionAreaWidth < minMusicNoteSpace)
//         {
//             musiceNoteAmount = 0;
//             return;
//         }
//         musiceNoteAmount = Mathf.Floor((selectionAreaWidth - musicNoteExtra) / musicNoteWidth) * lineAmount;
//     }
//
// }