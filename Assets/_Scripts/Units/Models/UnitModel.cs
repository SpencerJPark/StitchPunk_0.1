using UnityEngine;
using Data;

public abstract class UnitModel : MonoBehaviour, IUnitDataModel
{
    protected UnitData baseData;

    public void Initialize(UnitData baseData)
    {
        this.baseData = baseData;
        this.currentState = baseData.DefaultState;

        UnitHealth = new Health(baseData.MaxHealth);

        CreateDesignProfile();
    }

    // Immutable References
    public UnitData ImutableDate => baseData;
    public virtual UnitStateData CurrentState => currentState != null ? currentState : baseData.DefaultState;


    // State
    protected UnitStateData currentState;
    [RuntimeWatch] public virtual ActionType IdleAnimation => currentState.IdleAnimation;
    [RuntimeWatch] public virtual ActionType WalkAnimation => currentState.WalkAnimation;
    [RuntimeWatch] public virtual ActionType TalkAnimation => currentState.TalkAnimation;


    // Health
    [RuntimeWatch] public virtual Health UnitHealth { get; protected set; }


    // Movement Config (pass-through to MovementData)
    public virtual UnitMovementData MovementData => baseData.MovementData;
    [RuntimeWatch] public virtual AnimationDirectionType DirectionType => MovementData.directionType;


    // Role
    //public virtual UnitRoleFactory RoleFactory => baseData.RoleFactory;


    // Appearance
    public virtual UnitDesignFactory DesignFactory => baseData.DesignFactory;
    public virtual UnitDesignProfile DesignProfile { get; protected set; }


    [RuntimeWatch] public virtual string HairType { get; protected set; }
    [RuntimeWatch] public virtual string HairColor { get; protected set; }
    [RuntimeWatch] public virtual string FacialHairColor { get; protected set; }
    [RuntimeWatch] public virtual string SkinColor { get; protected set; }
    [RuntimeWatch] public virtual string Outfit { get; protected set; }


    // Runtime State
    [RuntimeWatch] public virtual Vector3 Position { get; protected set; }
    [RuntimeWatch] public virtual Direction CurrentDirection { get; protected set; }
    public virtual bool IsMoving { get; protected set; }
    public virtual bool IsGrounded { get; protected set; }
    public virtual float FallSpeed { get; protected set; }
    [RuntimeWatch] public virtual bool Mount { get; protected set; }


    // Setters
    public virtual void SetState(UnitStateData newState) => currentState = newState;
    public void SetPosition(Vector3 newPos) => Position = newPos;
    public void SetDirection(Direction newDir) => CurrentDirection = newDir;
    public void SetFallSpeed(float newSpeed) => FallSpeed = newSpeed;
    public void SetMount(bool newVal) => Mount = newVal;
    public void SetGrounding(bool newVal) => IsGrounded = newVal;
    public void SetMoving(bool newVal) => IsMoving = newVal;


    // Abstract Design Setters
    public void CreateDesignProfile() => DesignProfile = DesignFactory.BuildProfile();
    public abstract void CreateDesign();
    public abstract void ApplyDesign();


    // Health Passthrough
    public void SetMaxHealth(float newMax, bool clampCurrent = true) => UnitHealth.SetMaxHealth(newMax, clampCurrent);
    public void Damage(float amount) => UnitHealth.Damage(amount);
    public void Heal(float amount) => UnitHealth.Heal(amount);
    public void Kill() => UnitHealth.Kill();
}
