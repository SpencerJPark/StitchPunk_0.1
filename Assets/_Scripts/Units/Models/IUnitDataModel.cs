using UnityEngine;
using Data;

public interface IUnitDataModel
{
    UnitData ImutableDate { get; }

    // State
    UnitStateData CurrentState { get; }
    ActionType IdleAnimation { get; }
    ActionType WalkAnimation { get; }
    ActionType TalkAnimation { get; }

    // Health
    Health UnitHealth { get; }

    // Movement Info
    UnitMovementData MovementData { get; }
    AnimationDirectionType DirectionType { get; }

    // Design Info
    HairType HairType { get; }
    HairColor HairColor { get; }
    EyewareType Eyeware { get; }
    HatType Hat { get; }
    // HatColor
    SkinColor SkinColor { get; }


    // Runtime States
    Vector3 Position { get; }
    Direction CurrentDirection { get; }
    bool IsMoving { get; }
    bool IsGrounded { get; }
    float FallSpeed { get; }
    bool Mount { get; }
}
