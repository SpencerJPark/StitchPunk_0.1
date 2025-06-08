using UnityEngine;

public class FacingDirection2D : FacingDirectionBase
{
    private enum FacingDirection
    {
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

        FacingDirection newDirection = Get2Direction(dir);

        if (newDirection != currentDirection)
        {
            currentDirection = newDirection;
            animator.SetEnum("Direction", currentDirection.ToString());
        }
    }

    private FacingDirection Get2Direction(Vector2 dir)
    {
        return dir.x >= 0 ? FacingDirection.SouthEast : FacingDirection.SouthWest;
    }
}
