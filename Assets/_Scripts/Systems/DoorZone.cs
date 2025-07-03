using System.Collections.Generic;
using UnityEngine;

// Door Zone is to be attached to the zone that triggers doors opening for the player, it should be purely visual
[RequireComponent(typeof(Collider))]
public class DoorZone : MonoBehaviour
{
    [Tooltip("Assign the DoorControllers that this zone controls")]
    [SerializeField] private DoorController[] doors;

    [Tooltip("Tags allowed to open these doors")]
    [SerializeField] private string[] allowedTags = { "Player", "NPC" };

    private void OnDisable()
    {
        ResetDoors();
    }

    /// <summary>
    /// Force all doors in this zone to clear their queues and close.
    /// </summary>
    public void ResetDoors()
    {
        Debug.Log($"[DoorZone] ResetDoors() called, controlling {doors.Length} doors.");
        foreach (var door in doors)
        {
            if (door != null)
                door.ForceClose(smooth: true);
            else
                Debug.LogWarning("[DoorZone] Null door in array!");
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        // Only respond to allowed tags
        foreach (var tag in allowedTags)
        {
            if (!other.CompareTag(tag))
                continue;

            // Notify all doors
            foreach (var door in doors)
            {
                if (door != null)
                    door.AddRequester(other);
            }
            break;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        foreach (var tag in allowedTags)
        {
            if (!other.CompareTag(tag))
                continue;

            foreach (var door in doors)
            {
                if (door != null)
                    door.RemoveRequester(other);
            }
            break;
        }
    }
}
