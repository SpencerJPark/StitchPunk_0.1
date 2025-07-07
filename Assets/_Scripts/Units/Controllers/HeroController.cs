using UnityEngine;

public class HeroController : CharacterControllerBase
{
    [Header("Hero Data Assets")]
    [SerializeField] private UnitBaseData     baseDataSO;
    [SerializeField] private MovementProfile  movementProfileSO;

    protected override IUnitData CreateUnitData()
    {
        // here you pick exactly which implementation to use:
        return new HeroData(baseDataSO, movementProfileSO);
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
