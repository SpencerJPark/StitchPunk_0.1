using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : IInputProvider
{
    // public abstract bool InteractionFired { get; }

    // secondary action (sneak toggle, aim, etc...)

    // switch equipt quick

    // switch equipt select

    // Menu toggle
    private Vector2 moveInput;
    private bool actionPressed;

    public override Vector2 MoveInput => moveInput;
    public override bool ActionFired => actionPressed;

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnAction(InputAction.CallbackContext context)
    {
        if (context.started)
            actionPressed = true;
        if (context.canceled)
            actionPressed = false;
    }

    private void OnDisable()
    {
        actionPressed = false;
        moveInput = Vector2.zero;
    }
}
