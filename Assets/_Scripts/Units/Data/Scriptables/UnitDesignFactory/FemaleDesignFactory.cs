using UnityEngine;

[CreateAssetMenu(fileName = "FemaleDesignFactory", menuName = "Units/Unit Design Factory/Female Design Factory")]
public class FemaleDesignFactory: UnitDesignFactory {
    public override IUnitDesign CreateDesign()
    {
        return new FemaleDesign();
    }
}