using UnityEngine;
using Data;

public interface IUnitDataModel
{
    UnitData ImutableDate { get; }
    UnitStateData CurrentState { get; }


    // Health
    int MaxHealth { get; }
    int CurrentHealth { get; }


    // Design Info
    HairType HairType { get; }
    HairColor HairColor { get; }
    EyewareType Eyeware { get; }
    HatType Hat { get; }
    // HatColor
    SkinColor SkinColor { get; }


    // Movement Info
    UnitMovementData MovementData { get; }    


    // Manipulated States
    Vector3 Position { get; }
    Direction CurrentDirection { get; }
    bool IsMoving { get; }
    bool IsGrounded { get; }
    float FallSpeed { get; }
    bool Mount { get; }


    // Dynamically swapping state animations at runtime
    ActionType IdleAnimation { get; }
    ActionType WalkAnimation { get; }
    ActionType TalkAnimation { get; }
}
