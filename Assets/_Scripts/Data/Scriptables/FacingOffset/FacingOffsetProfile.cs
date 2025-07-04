// FacingOffsetProfile.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Vehicle/Facing Offset Profile")]
public class FacingOffsetProfile : ScriptableObject
{
    [Header("Offset for each facing direction (X, Y, Z)")]
    public Vector3 North;
    public Vector3 NorthEast;
    public Vector3 East;
    public Vector3 SouthEast;
    public Vector3 South;
    public Vector3 SouthWest;
    public Vector3 West;
    public Vector3 NorthWest;

    /// <summary>
    /// Returns the offset vector corresponding to the given Direction.
    /// </summary>
    public Vector3 GetOffset(Direction dir)
    {
        switch (dir)
        {
            case Direction.North:     return North;
            case Direction.NorthEast: return NorthEast;
            case Direction.East:      return East;
            case Direction.SouthEast: return SouthEast;
            case Direction.South:     return South;
            case Direction.SouthWest: return SouthWest;
            case Direction.West:      return West;
            case Direction.NorthWest: return NorthWest;
            default:                  return Vector3.zero;
        }
    }
}
