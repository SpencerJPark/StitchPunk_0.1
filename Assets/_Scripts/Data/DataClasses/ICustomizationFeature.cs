using System;
using Data;

public interface ICustomizationFeature
{
    RiveAnimator Anim { get; }
    string FeatureID { get; }
    void Initialize(RiveAnimator anim);
    string Create();
    void ApplyEnum(string target, string value);
    void ApplyNumber(string target, float value);
}

public abstract class CustomizationFeature : ICustomizationFeature
{
    // Non-null per interface; suppress init warning and enforce via Initialize()
    public RiveAnimator Anim { get; private set; } = null!;

    public abstract string FeatureID { get; }

    public virtual void Initialize(RiveAnimator anim)
    {
        Anim = anim ?? throw new ArgumentNullException(nameof(anim));
    }

    protected void EnsureInitialized()
    {
        if (Anim == null)
            throw new InvalidOperationException($"{GetType().Name} not initialized. Call Initialize() first.");
    }

    public virtual void ApplyEnum(string target, string value)
    {
        EnsureInitialized();
        Anim.SetEnum(target, value);
    }

    public virtual void ApplyNumber(string target, float value)
    {
        EnsureInitialized();
        Anim.SetNumber(target, value);
    }

    public abstract string Create();
}

[Serializable]
public class MaleHairFeature : CustomizationFeature
{
    public override string FeatureID => "MaleHairFeature";

    public override string Create()
    {
        // TODO: implement
        return "";
    }
}

[Serializable]
public class FemaleHairFeature : CustomizationFeature
{
    public override string FeatureID => "FemaleHairFeature";

    public override string Create()
    {
        return "";
    }
}

[Serializable]
public class PlayerHairFeature : CustomizationFeature
{
    public override string FeatureID => "PlayerHairFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class HairColorFeature : CustomizationFeature
{
    public override string FeatureID => "PlayerHairFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class SkinColorFeature : CustomizationFeature
{
    public override string FeatureID => "SkinColorFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class EyewareFeature : CustomizationFeature
{
    public override string FeatureID => "EyewareFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class HatFeature : CustomizationFeature
{
    public override string FeatureID => "HatFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class HatColorFeature : CustomizationFeature
{
    public override string FeatureID => "HatFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class MaleOutfitFeature : CustomizationFeature
{
    public override string FeatureID => "MaleOutfitFeature";

    public override string Create()
    {
        return "target";
    }
}

public class FemaleOutfitFeature : CustomizationFeature
{
    public override string FeatureID => "FemaleOutfitFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class FleshworkOutfitFeature : CustomizationFeature
{
    public override string FeatureID => "FleshworkOutfitFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class HeadShapeFeature : CustomizationFeature
{
    public override string FeatureID => "HeadShapeFeature";

    public override string Create()
    {
        return "target";
    }
}

[Serializable]
public class ShoeFeature : CustomizationFeature
{
    public override string FeatureID => "ShoeFeature";

    public override string Create()
    {
        return "target";
    }
}
