using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : IInputProvider
{
    [SerializeField] private PlayerInput playerInput;

    // Values
    Vector2 moveInput;
    bool    actionPressed;
    bool    interactionPressed;

    // Buttons
    public override Vector2 MoveInput => moveInput;
    public override bool ActionFired => actionPressed;
    public bool InteractPressed => interactionPressed;

    public InputAction InteractAction => playerInput.actions["Interact"];

    public void OnMove(InputAction.CallbackContext ctx)
        => moveInput = ctx.ReadValue<Vector2>();

    public void OnAction(InputAction.CallbackContext ctx)
    {
        if (ctx.started)  actionPressed = true;
        if (ctx.canceled) actionPressed = false;
    }

    public void OnInteract(InputAction.CallbackContext ctx)
    {
        if (ctx.started)  interactionPressed = true;
        if (ctx.canceled) interactionPressed = false;
    }

    public void SwitchActionMap(string mapName)
        => playerInput.SwitchCurrentActionMap(mapName);

    void OnDisable()
    {
        moveInput        = Vector2.zero;
        actionPressed    = false;
        interactionPressed = false;
    }
}
