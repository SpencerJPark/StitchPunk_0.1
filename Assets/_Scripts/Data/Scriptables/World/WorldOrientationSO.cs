using UnityEngine;

[CreateAssetMenu(menuName = "Globals/World Orientation")]
public class WorldOrientationSO : ScriptableObject
{
    [Tooltip("Defines what direction is considered 'North' in world space.")]
    public Vector3 WorldForward = Vector3.forward;

    [Tooltip("Defines what direction is considered 'East' in world space.")]
    public Vector3 WorldRight = Vector3.right;
}
