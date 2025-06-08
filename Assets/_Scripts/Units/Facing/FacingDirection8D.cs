using UnityEngine;

public class FacingDirection8D : FacingDirectionBase
{
    private enum FacingDirection
    {
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
        NorthWest
    }

    private FacingDirection currentDirection;
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

        FacingDirection newDirection = Get8Direction(dir);

        if (!hasInitializedDirection || newDirection != currentDirection)
        {
            currentDirection = newDirection;
            animator.SetEnum("Direction", currentDirection.ToString());
            hasInitializedDirection = true;
        }
    }

    private FacingDirection Get8Direction(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        angle = (angle + 360f + 22.5f) % 360f;

        if (angle < 45) return FacingDirection.East;
        if (angle < 90) return FacingDirection.NorthEast;
        if (angle < 135) return FacingDirection.North;
        if (angle < 180) return FacingDirection.NorthWest;
        if (angle < 225) return FacingDirection.West;
        if (angle < 270) return FacingDirection.SouthWest;
        if (angle < 315) return FacingDirection.South;
        return FacingDirection.SouthEast;
    }
}
