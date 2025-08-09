using UnityEngine;

public abstract class UnitDesignFactory : ScriptableObject
{
    public abstract IUnitDesign CreateDesign();
}