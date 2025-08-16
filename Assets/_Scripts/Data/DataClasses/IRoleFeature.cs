using UnityEngine;
using Data;

public interface IUnitRole
{
    RoleType  Role { get; }
    Transform Home { get; }
    Transform Work { get; } // expose Work if callers use IUnitRole
}

public class CitizenRole : IUnitRole
{
    public RoleType        Role        { get; protected set; }
    public SocialClassType SocialClass { get; protected set; }
    public GenderType      Gender      { get; protected set; }
    public AgeType         Age         { get; protected set; }
    public Transform       Home        { get; protected set; }
    public Transform       Work        { get; protected set; }

    public void SetRoleType(RoleType role)
    {
        Role = role;
        // TODO: assign Work based on role (you were calling a non-existent overload)
        // e.g.: Work = FindWorkTransformForRole(role);
    }

    public void SetSocialClassType(SocialClassType socialClass) => SocialClass = socialClass;
    public void SetGenderType(GenderType gender) => Gender = gender;
    public void SetAgeType(AgeType age) => Age = age;
    public void SetHome(Transform home) => Home = home;
    public void SetWork(Transform work) => Work = work;

    // private Transform FindWorkTransformForRole(RoleType role) { ... }
}
