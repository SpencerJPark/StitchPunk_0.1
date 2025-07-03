using UnityEngine;

// Vehicle Zone is used to create a zone for the player to enter a vehicle from, it inherits from InteractableZone
[RequireComponent(typeof(Collider))]
public class VehicleZone : InteractableZone
{
    [SerializeField] private VehicleControllerBase vehicleControllerBase;
    [SerializeField] private DoorZone doorZone;

    protected override void OnInteract()
    {
        var playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null)
        {
            Debug.LogError("VehicleZone: no GameObject tagged 'Player'.");
            return;
        }

        var playerT = playerGO;
        var inputProvider = PlayerInputHandler.Instance;
        if (inputProvider == null)
        {
            Debug.LogError("VehicleZone: PlayerInputHandler is null.");
            return;
        }

        // Seat & bind the player’s IInputProvider
        vehicleControllerBase.EnableVehicle(playerT, inputProvider);
    }
}
