using UnityEngine;

public class HeroDesign : CharacterDesignBase
{
    [SerializeField] private CharacterCustomizationData customizationData;

    public override void ApplyCustomization()
    {
        if (customizationData == null || animator == null) return;

        animator.SetEnum("SkinColor", customizationData.skinColor.ToString());
        animator.SetEnum("HairColor", customizationData.hairColor.ToString());
        animator.SetEnum("HairType", customizationData.hairType.ToString());
        animator.SetEnum("Eyeware", customizationData.eyeware.ToString());
        animator.SetEnum("Hats", customizationData.hats.ToString());
    }
}
