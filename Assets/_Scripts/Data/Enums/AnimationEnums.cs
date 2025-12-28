public enum AnimationType : ushort
{
    None,
    Idle,
    Walk,
    Run,
    Sit,
    Sneak,
    Interact,
    Talking,
    PlayFlute,
    MailDelivery,
    FaceLeft,
    FaceRight,
    FaceLeftBackView,
    FaceRightBackView,
}

public enum BodyPart : byte // keep under 256
{
    Body,
    
    UpperLeftArm,
    UpperRightArm,
    LowerLeftArm,
    LowerRightArm,
    LeftHand,
    RightHand,
    
    ItemRightHand,
    ItemLeftHand,
    
    Pelvis, // controlls torso tilt
    UpperLeftLeg,
    UpperRightLeg,
    LowerLeftLeg,
    LowerRightLeg,
    LeftFoot,
    RightFoot,
    
    Head,
    LeftEyebrow,
    RightEyebrow,
    Eyes,
    Mouth,
    Hair,
    Mustache,
    Nose,
    Ear,
    
    Hat,
    Torso,
    JacketLeftSide,
    JacketRightSide,
    JacketInside,
    Belt,
    PantFront,
}

public enum InterpolationMode : byte
{
    Linear,
    Step,          // No interpolation, holds until next keyframe
    EaseIn,
    EaseOut,
    EaseInOut,
}

public enum BlendMode : byte
{
    Additive,      // Add to current value
    Override,      // Replace current value
}

// Flags for which properties a track affects
[System.Flags]
public enum AnimatedProperties : byte
{
    None = 0,
    PositionX = 1 << 0,
    PositionY = 1 << 1,
    PositionZ = 1 << 2,    // Layer order
    Rotation = 1 << 3,
    ScaleX = 1 << 4,
    ScaleY = 1 << 5,
    ImageIndex = 1 << 6,
    
    Position = PositionX | PositionY,
    PositionAll = PositionX | PositionY | PositionZ,
    Scale = ScaleX | ScaleY,
    All = PositionAll | Rotation | Scale | ImageIndex,
}