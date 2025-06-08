using UnityEngine;

[CreateAssetMenu(menuName = "Characters/Customization Data")]
public class CharacterCustomizationData : ScriptableObject
{
    public Hats hats = Hats.Newsboy;
    public Eyeware eyeware = Eyeware.None;
    public HairColor hairColor = HairColor.Black;
    public HairType hairType = HairType.Buzzed;
    public SkinColor skinColor = SkinColor.White;
}

public enum Hats
    {
        None,
        TopHat,
        Bowler,
        Mask,
        Newsboy
    }

    public enum SkinColor
    {
        White,
        Tan,
        Brown,
        Dark
    }

    public enum Eyeware
    {
        None,
        Monicle,
        Glasses
    }

    public enum HairColor
    {
        Black,
        DarkBrown,
        LightBrown,
        Blonde,
        Grey,
        Red
    }

    public enum HairType
    {
        Combed,
        Spiked,
        Buzzed,
        Curly,
        PonyTail,
        BobCut,
        Kinky,
        HairDown
    }
