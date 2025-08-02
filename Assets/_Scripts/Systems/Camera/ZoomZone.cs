using UnityEngine;
using Data;

[RequireComponent(typeof(Collider))]
public class ZoomZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            CameraManager.Instance.SwitchCamera(CameraTypeEnum.PlayerZoom);
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            CameraManager.Instance.SwitchCamera(CameraTypeEnum.Player);
    }
}
