using UnityEngine;

public class HeroController : CharacterControllerBase
{

    protected override IUnitData CreateUnitData()
    {
        // here you pick exactly which implementation to use:
        return new HeroData(unitDataProfile);
    }

    protected override void HandleAction()
    {
        base.HandleAction();

        if (input.ActionFired)
        {
            // FireTriggerAnimation(attackAnimation);
        }

        // Add more player-specific actions here
    }
}
