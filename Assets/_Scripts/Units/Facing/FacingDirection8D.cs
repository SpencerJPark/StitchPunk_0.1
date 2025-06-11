using UnityEngine;

public class FacingDirection8D : FacingDirectionBase
{
    [SerializeField] private Direction currentDirection = Direction.SouthWest;
    
    private bool hasInitializedDirection = false;

    public override void UpdateFacing(Vector3 movementDirection)
    {
        if (mainCamera == null || animator == null)
            return;

        // Convert movement to camera-relative direction
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;

        Quaternion camRot = Quaternion.LookRotation(camForward);
        Vector3 camRelativeMove = camRot * movementDirection;

        Vector2 dir = new Vector2(camRelativeMove.x, camRelativeMove.z).normalized;

        Direction newDirection = Get8Direction(dir);

        if (!hasInitializedDirection || newDirection != currentDirection)
        {
            currentDirection = newDirection;
            animator.SetEnum("Direction", currentDirection.ToString());
            hasInitializedDirection = true;
        }
    }

    private Direction Get8Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 22.5f) % 360f;

        if (angle < 45) return Direction.East;
        if (angle < 90) return Direction.NorthEast;
        if (angle < 135) return Direction.North;
        if (angle < 180) return Direction.NorthWest;
        if (angle < 225) return Direction.West;
        if (angle < 270) return Direction.SouthWest;
        if (angle < 315) return Direction.South;
        return Direction.SouthEast;
    }
}
