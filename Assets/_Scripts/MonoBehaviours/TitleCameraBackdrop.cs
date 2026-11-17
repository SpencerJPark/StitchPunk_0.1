using Unity.Cinemachine;
using UnityEngine;

public class TitleCameraBackdrop : MonoBehaviour, ITitleBackdrop
{
    [SerializeField] private CinemachineCamera titleCamera;
    [SerializeField] private int titlePriority = 1000;

    private PrioritySettings previousPriority;
    private bool hasPreviousPriority;

    public void Enter()
    {
        if (Camera.main == null)
            Debug.Log("[TitleCameraBackdrop] No camera in the scene; the title draws over an empty background.");

        if (titleCamera == null)
            return;

        if (!hasPreviousPriority)
        {
            previousPriority = titleCamera.Priority;
            hasPreviousPriority = true;
        }

        PrioritySettings titlePrioritySettings = titleCamera.Priority;
        titlePrioritySettings.Enabled = true;
        titlePrioritySettings.Value = titlePriority;
        titleCamera.Priority = titlePrioritySettings;
        titleCamera.gameObject.SetActive(true);
    }

    public void Exit(float fadeSeconds)
    {
        if (titleCamera == null)
            return;

        if (hasPreviousPriority)
        {
            titleCamera.Priority = previousPriority;
            hasPreviousPriority = false;
        }

        titleCamera.gameObject.SetActive(false);
    }
}
