using UnityEngine;

[CreateAssetMenu(fileName = "MaleDesignFactory", menuName = "Units/Unit Design Factory/Male Design Factory")]
public class MaleDesignFactory: UnitDesignFactory {
    public override IUnitDesign CreateDesign()
    {
        return new MaleDesign();
    }
}
