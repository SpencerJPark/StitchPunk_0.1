using Data;

public interface ICustomizationFeature
{
    // Called once when the unit spawns / visuals are ready
    void Apply(RiveAnimator anim, IUnitRole? role);
}

public class HairFeature : ICustomizationFeature
{
    public void Apply(RiveAnimator anim, IUnitRole? role)
    {
        // Call to logic for random weighted value selection based on role data

        // Set values in Rive
        anim.SetEnum("HairType", );
        anim.SetNumber("NoseCurve", 20);

        // if logic
        anim.SetEnum("FacialHair")
    }
}

public class FacialHairFeature : ICustomizationFeature
{

}

public class HeadShapeFeature : ICustomizationFeature
{
    float NoseCurve;
    float NoseWidth;
    float NoseLength;
    float ChinLength;
    float ChinWidth;
}


public class ShoeFeature : ICustomizationFeature
{

}





