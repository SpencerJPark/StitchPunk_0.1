using UnityEngine;

public class HeroController : CharacterControllerBase
{
    [Header("Hero Settings")]
    [SerializeField] private string attackAnimation = "Attack";

    protected override void HandleAction()
    {
        base.HandleAction();

        if (input.ActionPressed)
        {
            FireTriggerAnimation(attackAnimation);
        }

        // Add more player-specific actions here
    }

    // You can override UpdateMovementAnimation or UpdateFacingDirection
    // if heroes have custom animation logic.
}
