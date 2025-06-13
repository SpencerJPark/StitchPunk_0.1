using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ZoomZone : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            PlayerManager.Instance.SwitchToZoom();
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            PlayerManager.Instance.SwitchToHero();
    }
}
