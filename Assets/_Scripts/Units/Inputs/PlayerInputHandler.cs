using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : CharacterInputBase
{
    private Vector2 moveInput;
    private bool actionPressed;

    public override Vector2 MoveInput => moveInput;
    public override bool ActionPressed => actionPressed;

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
