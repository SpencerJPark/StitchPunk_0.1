using UnityEngine;

public abstract class UnitModel : IUnitDataModel
{
    protected UnitData baseData;
    protected UnitStateData currentState;

    public UnitModel(UnitData baseData)
    {
        this.baseData = baseData;
        this.currentState = baseData.DefaultState;

        CurrentHealth = baseData.MaxHealth;
    }

    // Immutable References
    public UnitData ImutableDate => baseData;
    public virtual UnitStateData CurrentState => currentState != null ? currentState : baseData.DefaultState;


    // Health
    public virtual int MaxHealth => baseData.MaxHealth;
    public virtual int CurrentHealth { get; protected set; }

    // Appearance
    public virtual HairType HairType { get; protected set; } = HairType.Buzzed;
    public virtual HairColor HairColor { get; protected set; } = HairColor.Black;
    public virtual Eyeware Eyeware { get; protected set; } = Eyeware.None;
    public virtual Hats Hats { get; protected set; } = Hats.None;
    public virtual SkinColor SkinColor { get; protected set; } = SkinColor.White;


    // Movement Config (pass-through to MovementData)
    protected UnitMovementData MovementData => baseData.MovementData;

    public virtual Vector3 MovementVector { get; protected set; }
    public virtual MovementType Movement => MovementData.movementType;
    public virtual Direction DefaultDirection => MovementData.defaultDirection;
    public virtual AnimationDirectionType DirectionType => MovementData.directionType;
    public virtual float MoveSpeed => MovementData.moveSpeed;
    public virtual float Gravity => MovementData.gravity;
    public virtual float MaxFallSpeed => MovementData.maxFallSpeed;
    public virtual float GroundCheckDistance => MovementData.groundCheckDistance;
    public virtual LayerMask GroundLayer => MovementData.groundLayer;
    public virtual float GravityMultiplier => MovementData.gravityMultiplier;

    // Runtime State
    public virtual Vector3 Position { get; protected set; }
    public virtual Direction CurrentDirection { get; protected set; } = Direction.South;
    public virtual bool IsMoving { get; protected set; }
    public virtual bool IsGrounded { get; protected set; }
    public virtual float FallSpeed { get; protected set; }
    public virtual bool Mount { get; protected set; }

    // Current State Animations
    public virtual Actions IdleAnimation => currentState.IdleAnimation;
    public virtual Actions WalkAnimation => currentState.WalkAnimation;
    public virtual Actions TalkAnimation => currentState.TalkAnimation;

    public virtual void SetState(UnitStateData newState)
    {
        currentState = newState;
    }

    public void SetPosition(Vector3 newPos)
    {
        Position = newPos;
    }

    public void SetMovementVector(Vector3 newVec)
    {
        MovementVector = newVec;
    }

    public void SetDirection(Direction newDir)
    {
        CurrentDirection = newDir;
    }

    public void SetFallSpeed(float newSpeed)
    {
        FallSpeed = newSpeed;
    }

    public void SetMount(bool newVal)
    {
        Mount = newVal;
    }

    public void SetGrounding(bool newVal)
    {
        IsGrounded = newVal;
    }

    public void SetMoving(bool newVal)
    {
        IsMoving = newVal;
    }
    
}
