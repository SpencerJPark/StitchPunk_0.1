using UnityEngine;

public class NPC_BehaviorController : MonoBehaviour, IUpdateObserver
{
    private Vector3 lastPosition;
    private float movementThreshold = 0.1f; // Small threshold to detect movement
    private string currentAction = "human_idol"; // Start with idle action

    void OnEnable()
    {
        UpdateManager.RegisterObserver(this);

        lastPosition = transform.position;
        NPC_ActionEvents.Trigger(gameObject, currentAction); // Initialize the action to idle
    }

    void OnDisable()
    {
        UpdateManager.UnregisterObserver(this);
    }

    public void ObservedUpdate()
    {
        Vector3 currentPosition = transform.position;
        float distanceMoved = Vector3.Distance(currentPosition, lastPosition);

        // If the NPC moved more than the threshold, consider it as walking
        if (distanceMoved > movementThreshold)
        {
            if (currentAction != "human_walk")
            {
                SetAction("human_walk");
            }
        }
        else
        {
            if (currentAction != "human_idol")
            {
                SetAction("human_idol");
            }
        }

        lastPosition = currentPosition; // Update last position after checking movement
    }

    bool IsWalking()
    {
        // Check if NPC is moving
        return Mathf.Abs(transform.position.x) > 0.1f || Mathf.Abs(transform.position.z) > 0.1f;
    }

    bool IsIdle()
    {
        // Check if NPC is idle
        return Mathf.Abs(transform.position.x) <= 0.1f && Mathf.Abs(transform.position.z) <= 0.1f;
    }

    void SetAction(string action)
    {
        // Only trigger the action if it has changed
        if (currentAction != action)
        {
            currentAction = action;
            NPC_ActionEvents.Trigger(gameObject, currentAction);
        }
    }
}
