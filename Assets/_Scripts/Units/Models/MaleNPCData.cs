using UnityEngine;

public class MaleNPCData : MonoBehaviour, IUnitData
{
    private readonly UnitBaseData _so;
    private readonly MovementProfile _mp;
    public MaleNPCData(UnitBaseData so, MovementProfile mp)
    {
        _so = so;
        _mp = mp;
    }

// Immutable Universal Data from Scriptable Object
    public string UnitName => _so.UnitName;
    public int MaxHealth => _so.MaxHealth;
    public int AttackDamage => _so.AttackDamage;
    public MovementType Movement => _mp.movementType;
    public float MoveSpeed => _mp.MoveSpeed;
    public float Gravity => _mp.gravity;
    public float MaxFallSpeed => _mp.maxFallSpeed;
    public float GroundCheckDistance => _mp.groundCheckDistance;
    public LayerMask GroundLayer => _mp.groundLayer;
    public float GravityMultiplier => _mp.gravityMultiplier;

// Mutable Data
    public int CurrentHealth;
    
    // Design Data
    public SkinColor SkinColor;
    public HairType HairType;
    public HairColor HairColor;
    public Hats HatType;
    public Eyeware eyeware;
    // mustasche, jacket, ect...

}