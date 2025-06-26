using UnityEngine;

public class VehicleZone : InteractableZone
{
    [SerializeField] private VehicleControllerBase vehicleControllerBase;
    protected override void OnInteract()
    {
        Debug.Log("Enter vehicle");
        vehicleControllerBase.EnableVehicle();
        CameraManager.Instance.SwitchCamera(CameraType.Vehicle);
    }
}
