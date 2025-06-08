using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class DoorController : MonoBehaviour
{
    [Header("Hinge & Angles")]
    [Tooltip("Pivot of the door, set at the hinge point")]
    [SerializeField] private Transform hinge;

    [Tooltip("Local Y-angles for closed and open states")]
    [SerializeField] private float closedAngle = 0f;
    [SerializeField] private float openAngle = 90f;

    [Header("Animation")]
    [SerializeField] private float speed = 2f;

    private HashSet<Collider> requesters = new HashSet<Collider>();
    private Coroutine anim;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        // Snap hinge to closed rotation, preserving X and Z
        Vector3 e = hinge.localEulerAngles;
        e.y = closedAngle;
        hinge.localEulerAngles = e;
        IsOpen = false;
    }

    public void AddRequester(Collider who)
    {
        if (requesters.Add(who) && requesters.Count == 1)
            SetOpen(true);
    }

    public void RemoveRequester(Collider who)
    {
        if (requesters.Remove(who) && requesters.Count == 0)
            SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(AnimateDoor(open));
    }

    private IEnumerator AnimateDoor(bool open)
    {
        float start = hinge.localEulerAngles.y;
        float target = open ? openAngle : closedAngle;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * speed;
            float angle = Mathf.LerpAngle(start, target, t);
            // Only modify Y, preserve X and Z
            Vector3 e = hinge.localEulerAngles;
            e.y = angle;
            hinge.localEulerAngles = e;
            yield return null;
        }
        Vector3 final = hinge.localEulerAngles;
        final.y = target;
        hinge.localEulerAngles = final;
        anim = null;
    }
}
