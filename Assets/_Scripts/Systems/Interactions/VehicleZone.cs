using UnityEngine;

[RequireComponent(typeof(Collider))]
public class VehicleZone : InteractableZone
{
    [SerializeField] private VehicleControllerBase vehicleControllerBase;

    protected override void OnInteract()
    {
        var playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null) 
        {
            Debug.LogError("VehicleZone: no GameObject tagged 'Player'.");
            return;
        }

        var playerT = playerGO.transform;
        var inputProvider = PlayerInputManager.Instance.InputHandler;
        if (inputProvider == null)
        {
            Debug.LogError("VehicleZone: PlayerInputManager.InputHandler is null.");
            return;
        }

        // Seat & bind the player’s IInputProvider
        vehicleControllerBase.EnableVehicle(playerT, inputProvider);

        // Switch action map & camera
        PlayerInputManager.Instance.SwitchToVehicle();
        CameraManager.Instance.SwitchCamera(CameraType.Vehicle);
    }
}
