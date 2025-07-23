using UnityEngine;

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
    Eyeware Eyeware { get; }
    Hats Hats { get; }
    // HatColor
    SkinColor SkinColor { get; }
    

    // Movement Info
    Vector3 MovementVector{ get; }
    MovementType Movement { get; }
    Direction DefaultDirection { get; }
    AnimationDirectionType DirectionType { get; }
    float MoveSpeed { get; }
    float Gravity { get; }
    float MaxFallSpeed { get; }
    float GroundCheckDistance { get; }
    LayerMask GroundLayer { get; }
    float GravityMultiplier { get; }
    

    // Manipulated States
    Vector3 Position { get; }
    Direction CurrentDirection { get; }
    bool IsMoving { get; }
    bool IsGrounded { get; }
    float FallSpeed { get; }
    bool Mount { get; }


    // Dynamically swapping state animations at runtime
    Actions IdleAnimation { get; }
    Actions WalkAnimation { get; }
    Actions TalkAnimation { get; }
}
