using UnityEngine;

public class VehicleZone : InteractableZone
{
    protected override void OnInteract()
    {
        Debug.Log("Enter vehicle");
        playerManager.SwitchToVehicle();
    }
}
