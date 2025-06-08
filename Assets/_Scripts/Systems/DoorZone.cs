using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DoorZone : MonoBehaviour
{
    [Tooltip("Assign the DoorControllers that this zone controls")]
    [SerializeField] private DoorController[] doors;

    [Tooltip("Tags allowed to open these doors")]
    [SerializeField] private string[] allowedTags = { "Player", "NPC" };

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
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
