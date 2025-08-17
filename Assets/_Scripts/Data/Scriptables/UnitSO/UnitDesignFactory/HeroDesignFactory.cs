using UnityEngine;
using System;

[CreateAssetMenu(menuName = "Units/Design Factories/Hero Scheme")]
public class HeroDesignFactory : UnitDesignFactory
{
    [ContextMenu("Apply Default Scheme")]
    private void ApplyDefaultScheme()
    {
        SetScheme(
            new PlayerHairFeature(),   // gives access to all hair
            new HairColorFeature(),
            new HatFeature(),
            new HatColorFeature(),
            new EyewareFeature(),
            new SkinColorFeature()
        );
    }
}
