using UnityEngine;

[RequireComponent(typeof(Collider))]
public class VehicleZone : InteractableZone
{
    [SerializeField] private VehicleController vehicleController;

    protected override void OnTriggerEnter(Collider other)
    {
        base.OnTriggerEnter(other);
        // (optional) extra logic on enter
    }

    protected override void OnTriggerExit(Collider other)
    {
        base.OnTriggerExit(other);
    }

    public override void OnInteract()
    {
        var player = GameObject.FindWithTag("Player");
        var input = PlayerInputHandler.Instance;
        if (player == null || input == null)
        {
            Debug.LogError("VehicleZone: missing Player or InputHandler");
            return;
        }

        vehicleController.EnableVehicle(player, input);
        InteractionManager.Instance.UnregisterZone(this);
    }
}
