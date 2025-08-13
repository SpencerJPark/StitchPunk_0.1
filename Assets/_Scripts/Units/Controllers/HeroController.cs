using UnityEngine;

public class HeroController : UnitController
{
    protected override void Awake()
    {
        // Let the base class create unitData and validate it
        base.Awake();

        // Now do any Hero‑specific setup:
        // e.g. ensure we have an input provider wired in
        if (input == null)
            input = PlayerInputHandler.Instance;

        // or initialize hero‑only systems:
        //InitializeHeroAbilities();
    }
    
    protected override UnitModel CreateUnitModel()
    {
        // here you pick exactly which implementation to use:
        return new HeroModel(unitData);
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
