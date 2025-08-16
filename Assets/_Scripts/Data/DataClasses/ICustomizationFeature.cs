using Data;

public interface ICustomizationFeature
{
    // Called once when the unit spawns / visuals are ready
    RiveAnimator Anim { get; }
    string FeatureID { get; }
    void Initialize(RiveAnimator Anim);
    string Create(string target, IUnitRole? role);
    void ApplyEnum(string target, string value);
    void ApplyNumber(string target, float value);
}


public abstract class CustomizationFeature : ICustomizationFeature
{
    private RiveAnimator Anim;
    public abstract string FeatureID;

    public virtual void Initialize(RiveAnimator riveAnimator)
    {
        Anim = riveAnimator;
    }

    public virtual void ApplyEnum(string target, string value)
    {
        Anim.SetEnum(target, value);
    }

    public virtual void ApplyNumber(string target, float value)
    {
        Anim.SetNumber(target, value);
    }

    public abstract string Create(string target, IUnitRole? role);

}


public class MaleHairFeature : CustomizationFeature
{
    public string FeatureID = "MaleHairFeature";
    // private HairType hairType; // Only male
    // private HairColor hairColor;
    // private FacialHairType facialHairType;

    public string Create(string target, IUnitRole? role)
    {

    }

}

public class FemaleHairFeature : CustomizationFeature
{
    public string FeatureID = "FemaleHairFeature";
    // private HairType hairType; // Only female
    // private HairColor hairColor;

    public string Create(string target, IUnitRole? role)
    {

    }

}

public class PlayerHairFeature : CustomizationFeature
{
    public string FeatureID = "PlayerHairFeature";
    // private HairType hairType; // All hair options
    // private HairColor hairColor; // more color options

    public string Create(string target, IUnitRole? role)
    {

    }

}


public class EyewareFeature : CustomizationFeature
{
    public string FeatureID = "EyewareFeature";
    // private EyewareType eyewareType;

    public string Create(string target, IUnitRole? role)
    {

    }
}


public class HatFeature : CustomizationFeature
{
    public string FeatureID = "HatFeature";
    // private HatType hatType;
    // private HatColor hatColor;

    public string Create(string target, IUnitRole? role)
    {
        
    }
}


public class MaleOutfitFeature : CustomizationFeature
{
    public string FeatureID = "MaleOutfitFeature";

    public string Create(string target, IUnitRole? role)
    {

    }
}


public class FemaleOutfitFeature : CustomizationFeature
{
    public string FeatureID = "MaleOutfitFeature";

    public string Create(string target, IUnitRole? role)
    {

    }
}


public class FleshworkOutfitFeature : CustomizationFeature
{
    public string FeatureID = "MaleOutfitFeature";
    // Outfit color
    // Hat type or other customization

    public string Create(string target, IUnitRole? role)
    {

    }
}


public class HeadShapeFeature : CustomizationFeature
{
    public string FeatureID = "HeadShapeFeature";
    // NoseCurve;
    // NoseWidth;
    // NoseLength;
    // ChinLength;
    // ChinWidth;

    public string Create(string target, IUnitRole? role)
    {
        // random value between 0-100
    }
}


public class ShoeFeature : CustomizationFeature
{
    public string FeatureID = "ShoeFeature";
    // Type
    // Color

    public string Create(string target, IUnitRole? role)
    {

    }
}





