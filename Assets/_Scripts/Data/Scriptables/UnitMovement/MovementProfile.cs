using UnityEngine;

[CreateAssetMenu(fileName = "MovementProfile", menuName = "Characters/Movement Profile", order = 1)]
public class MovementProfile : ScriptableObject
{
    public MovementType movementType = MovementType.Grounded;

    [Header("Speed Settings")]
    public float MoveSpeed = 3f;

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
