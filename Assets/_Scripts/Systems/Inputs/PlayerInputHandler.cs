using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : InputProviderBase
{
    public static PlayerInputHandler Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    Vector2 _moveInput;
    bool    _interactFired;
    Vector2 _steerInput;
    bool    _exitVehicleFired;

    public override Vector2 MoveInput        => _moveInput;
    public override bool    InteractFired    => _interactFired;
    public override Vector2 SteerInput       => _steerInput;
    public override bool    ExitVehicleFired => _exitVehicleFired;

    // these methods must match your action names exactly:
    public void OnMove(InputAction.CallbackContext ctx)
    {
        _moveInput = ctx.ReadValue<Vector2>();
    }

    public void OnInteract(InputAction.CallbackContext ctx)
    {
        _interactFired = ctx.started;
        Debug.Log($"[Input] Interact {(ctx.started? "DOWN":"UP")}, map={ctx.action.activeControl?.device.displayName}");
    }

    public void OnSteer(InputAction.CallbackContext ctx)
    {
        _steerInput = ctx.ReadValue<Vector2>();
    }

    public void OnExitVehicle(InputAction.CallbackContext ctx)
    {
        _exitVehicleFired = ctx.started;
    }

    public void SwitchActionMap(string mapName)
    {
        var pi = GetComponent<PlayerInput>();
        if (pi != null) pi.SwitchCurrentActionMap(mapName);
    }
}
