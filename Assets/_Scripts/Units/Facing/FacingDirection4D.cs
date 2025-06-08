using UnityEngine;

public class FacingDirection4D : FacingDirectionBase
{
    private enum FacingDirection
    {
        NorthWest,
        NorthEast,
        SouthEast,
        SouthWest
    }

    private FacingDirection currentDirection;

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

        FacingDirection newDirection = Get4Direction(dir);

        if (newDirection != currentDirection)
        {
            currentDirection = newDirection;
            animator.SetEnum("Direction", currentDirection.ToString());
        }
    }

    private FacingDirection Get4Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 45f) % 360f;

        if (angle < 90) return FacingDirection.NorthEast;
        if (angle < 180) return FacingDirection.NorthWest;
        if (angle < 270) return FacingDirection.SouthWest;
        return FacingDirection.SouthEast;
    }
}
