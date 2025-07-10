using UnityEngine;

public class MaleNPCData : IUnitData
{
    private readonly UnitDataProfile _so;

    public MaleNPCData(UnitDataProfile so)
    {
        _so = so;
    }

// Immutable Universal Data from Scriptable Object
    public string UnitName => _so.UnitName;
    public int MaxHealth => _so.MaxHealth;
    public int AttackDamage => _so.AttackDamage;
    public MovementType Movement => _so.movementType;
    public float MoveSpeed => _so.MoveSpeed;
    public float Gravity => _so.gravity;
    public float MaxFallSpeed => _so.maxFallSpeed;
    public float GroundCheckDistance => _so.groundCheckDistance;
    public LayerMask GroundLayer => _so.groundLayer;
    public float GravityMultiplier => _so.gravityMultiplier;

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