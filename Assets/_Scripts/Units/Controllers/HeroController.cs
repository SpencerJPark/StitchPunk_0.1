using UnityEngine;

public class HeroController : CharacterControllerBase
{
    [Header("Hero Settings")]
    [SerializeField] private string attackAnimation = "Attack";

    protected override void HandleAction()
    {
        base.HandleAction();

        if (input.ActionFired)
        {
            FireTriggerAnimation(attackAnimation);
        }

        // Add more player-specific actions here
    }
}
