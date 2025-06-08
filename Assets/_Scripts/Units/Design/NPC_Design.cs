using UnityEngine;
using Rive;
using Rive.Components;

public class NPC_Design : MonoBehaviour
{
    [SerializeField] private RiveWidget riveWidget;

    private ViewModelInstance viewModelInstance;

    private void OnEnable()
    {
        riveWidget.OnWidgetStatusChanged += HandleWidgetStatusChanged;
    }

    private void OnDisable()
    {
        riveWidget.OnWidgetStatusChanged -= HandleWidgetStatusChanged;
    }

    private void HandleWidgetStatusChanged()
    {
        if (riveWidget.Status == WidgetStatus.Loaded)
        {
            viewModelInstance = riveWidget.StateMachine.ViewModelInstance;

            RandomDesign("shoe_color", 7);
            RandomDesign("tie_color", 6);
            RandomDesign("jacket_color", 11);
            RandomDesign("pant_color", 10);
            RandomDesign("vest_button_color", 5);
            RandomDesign("vest_color", 11);
            RandomDesign("shirt_color", 7);
            RandomDesign("body_style", 13);
            RandomDesign("face_details", 5);
            RandomDesign("eye_ware", 4);
            RandomDesign("hair", 26);
            RandomDesign("hair_color", 6);
            RandomDesign("mustache", 12);
            RandomDesign("nose", 5);
            RandomDesign("chin", 6);
        }
    }

    private void RandomDesign(string dataName, int maxRange)
    {
        int value = Random.Range(0, maxRange);
        var numberProperty = viewModelInstance.GetNumberProperty(dataName);
        numberProperty.Value = value;
    }
}
