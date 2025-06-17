using UnityEngine;

public class FacingDirection4D : FacingDirectionBase
{

    [SerializeField] private Direction _currentDirection = Direction.SouthEast;
    public override Direction CurrentDirection => _currentDirection;

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

        Direction newDirection = Get4Direction(dir);

       if (newDirection != _currentDirection)
        {
            _currentDirection = newDirection;
            animator.SetEnum("Direction", _currentDirection.ToString());
        }
    }

    private Direction Get4Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 45f) % 360f;

        if (angle < 90) return Direction.NorthEast;
        if (angle < 180) return Direction.NorthWest;
        if (angle < 270) return Direction.SouthWest;
        return Direction.SouthEast;
    }
}
