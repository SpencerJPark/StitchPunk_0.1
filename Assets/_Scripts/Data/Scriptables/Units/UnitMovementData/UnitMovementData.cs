using UnityEngine;

[CreateAssetMenu(menuName = "Units/Unit Movement Data", order = 3)]
public class UnitMovementData : ScriptableObject
{
    [Header("Movement Settings")]
    public MovementType movementType = MovementType.Grounded;
    public Direction defaultDirection = Direction.SouthWest;
    public AnimationDirectionType directionType = AnimationDirectionType.FourDirection;
    public float moveSpeed = 3f;


    [Header("Gravity Settings")]
    public float gravity = 9.8f;
    public float maxFallSpeed = 20f;


    [Header("Ground Detection")]
    public float groundCheckDistance = 0.2f;
    public LayerMask groundLayer;


    [Header("Floating Modifier")]
    [Range(0f, 1f)]
    public float gravityMultiplier = 0.3f;
}
