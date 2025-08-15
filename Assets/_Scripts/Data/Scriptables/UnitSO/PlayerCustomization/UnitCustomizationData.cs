using UnityEngine;
using Data;

[CreateAssetMenu(menuName = "Units/Unit Customization Data", order = 3)]
public class UnitCustomizationData : ScriptableObject
{
    public HatType hats = HatType.Newsboy;
    public EyewareType eyeware = EyewareType.None;
    public HairColor hairColor = HairColor.Black;
    public HairType hairType = HairType.Buzzed;
    public SkinColor skinColor = SkinColor.White;
}






    

    
