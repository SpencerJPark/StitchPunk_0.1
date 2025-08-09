using UnityEngine;
using Data;

public interface IUnitDesign
{
    void NewDesign(IUnitRole unitRole);

    // May require newer C# in Unity; otherwise move to a factory class
    static IUnitDesign CreateDefault()
    {
        return new MaleDesign();
    }
}

public class MaleDesign : IUnitDesign
{
    // Head
    // HatType
    // HatColor
    // HairType
    // HairColor
    // EyewareType
    // FaceDetails
    // Mustache
    public float NoseCurve { get; protected set; }
    public float NoseWidth { get; protected set; }
    public float NoseLength { get; protected set; }
    public float ChinWidth { get; protected set; }
    public float ChinLenght { get; protected set; }

    // Body
    // BodyStyle influenced by Role
    // TieColor
    // JacketColor
    // PantColor
    // VestButtonColor
    // VestColor
    // ShirtColor
    // ShoeColor
    // ShoeType

    public void NewDesign(IUnitRole unitRole)
    {
        // TODO: populate properties based on role
    }
}

public class FemaleDesign : IUnitDesign
{
    // Head
    // HatType
    // HatColor
    // HairType
    // HairColor
    // EyewareType
    // FaceDetails
    // Mustache
    public float NoseCurve { get; protected set; }
    public float NoseWidth { get; protected set; }
    public float NoseLength { get; protected set; }
    public float ChinWidth { get; protected set; }
    public float ChinLenght { get; protected set; }

    // Body
    // BodyStyle influenced by Role
    // TieColor
    // JacketColor
    // PantColor
    // VestButtonColor
    // VestColor
    // ShirtColor
    // ShoeColor
    // ShoeType

    public void NewDesign(IUnitRole unitRole)
    {
        // TODO: populate properties based on role
    }

}