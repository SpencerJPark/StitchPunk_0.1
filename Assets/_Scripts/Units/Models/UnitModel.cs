using UnityEngine;
using Data;

public abstract class UnitModel : MonoBehaviour, IUnitDataModel
{
    protected UnitData baseData;

    public void Build(UnitData baseData)
    {
        this.baseData = baseData;
        this.currentState = baseData.DefaultState;

        UnitHealth = new Health(baseData.MaxHealth);
    }

    // Immutable References
    public UnitData ImutableDate => baseData;
    public virtual UnitStateData CurrentState => currentState != null ? currentState : baseData.DefaultState;


    // State
    protected UnitStateData currentState;
    public virtual ActionType IdleAnimation => currentState.IdleAnimation;
    public virtual ActionType WalkAnimation => currentState.WalkAnimation;
    public virtual ActionType TalkAnimation => currentState.TalkAnimation;


    // Health
    public virtual Health UnitHealth { get; protected set; }


    // Movement Config (pass-through to MovementData)
    public virtual UnitMovementData MovementData => baseData.MovementData;
    public virtual AnimationDirectionType DirectionType => MovementData.directionType;


    // Appearance
    // Factory Ref
    public virtual UnitDesignProfile DesignProfile { get; protected set; }
    public virtual string HairType { get; protected set; }
    public virtual string HairColor { get; protected set; }
    public virtual string FacialHairColor { get; protected set; }
    public virtual string SkinColor { get; protected set; }
    public virtual string Outfit { get; protected set; }


    // Runtime State
    public virtual Vector3 Position { get; protected set; }
    public virtual Direction CurrentDirection { get; protected set; }
    public virtual bool IsMoving { get; protected set; }
    public virtual bool IsGrounded { get; protected set; }
    public virtual float FallSpeed { get; protected set; }
    public virtual bool Mount { get; protected set; }


    // Setters
    public virtual void SetState(UnitStateData newState) => currentState = newState;
    public void SetPosition(Vector3 newPos) => Position = newPos;
    public void SetDirection(Direction newDir) => CurrentDirection = newDir;
    public void SetFallSpeed(float newSpeed) => FallSpeed = newSpeed;
    public void SetMount(bool newVal) => Mount = newVal;
    public void SetGrounding(bool newVal) => IsGrounded = newVal;
    public void SetMoving(bool newVal) => IsMoving = newVal;
    


    // Abstract Design Setters
    public abstract void CreateDesign();
    public abstract void ApplyDesign();



    // Health Passthrough
    public void SetMaxHealth(float newMax, bool clampCurrent = true) => UnitHealth.SetMaxHealth(newMax, clampCurrent);
    public void Damage(float amount) => UnitHealth.Damage(amount);
    public void Heal(float amount) => UnitHealth.Heal(amount);
    public void Kill() => UnitHealth.Kill();
    
}
