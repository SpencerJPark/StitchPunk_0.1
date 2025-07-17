// PlayerInputHandler.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : PersistentSingleton<PlayerInputHandler>, IInputProvider
{
    Vector2 _moveInput;
    bool    _actionFired;
    bool    _interactFired;
    Vector2 _steerInput;
    bool    _exitVehicleFired;

    // IInputProvider
    public Vector2 MoveInput        => _moveInput;
    public bool    ActionFired      => _actionFired;
    public bool    InteractFired    => _interactFired;
    public Vector2 SteerInput       => _steerInput;
    public bool    ExitVehicleFired => _exitVehicleFired;

    // Called via PlayerInput (Send Messages or Unity Events)
    public void OnMove(InputAction.CallbackContext ctx)
        => _moveInput = ctx.ReadValue<Vector2>();

    public void OnAction(InputAction.CallbackContext ctx)
        => _actionFired = ctx.started;

    public void OnInteract(InputAction.CallbackContext ctx)
        => _interactFired = ctx.started;

    public void OnSteer(InputAction.CallbackContext ctx)
        => _steerInput = ctx.ReadValue<Vector2>();

    public void OnExitVehicle(InputAction.CallbackContext ctx)
        => _exitVehicleFired = ctx.started;

    /// <summary>Switch between your 'Player' and 'Vehicle' maps.</summary>
    public void SwitchActionMap(string mapName)
    {
        var pi = GetComponent<PlayerInput>();
        if (pi != null)
            pi.SwitchCurrentActionMap(mapName);
    }
}
