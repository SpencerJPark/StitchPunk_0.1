using UnityEngine;

public class BillboardListener : MonoBehaviour
{
    private void OnEnable()
    {
        BillboardManager.OnCameraRotationChanged += UpdateFacing;
        if (BillboardManager.Instance != null)
        {
            UpdateFacing(BillboardManager.Instance.targetCamera.transform.eulerAngles);
        }
    }

    private void OnDisable()
    {
        BillboardManager.OnCameraRotationChanged -= UpdateFacing;
    }

    void UpdateFacing(Vector3 cameraEuler)
    {
        if (BillboardManager.Instance == null) return;

        Camera cam = BillboardManager.Instance.targetCamera;

        // Use the camera's forward but flatten Y
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Quaternion flatYRotation = Quaternion.LookRotation(camForward);
        Vector3 euler = flatYRotation.eulerAngles;
        float xTilt = cam.transform.eulerAngles.x;

        transform.rotation = Quaternion.Euler(xTilt, euler.y, 0f);
    }
}