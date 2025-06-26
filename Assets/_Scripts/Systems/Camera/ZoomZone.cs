using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ZoomZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            CameraManager.Instance.SwitchCamera(CameraType.PlayerZoom);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            CameraManager.Instance.SwitchCamera(CameraType.Player);
    }
}
