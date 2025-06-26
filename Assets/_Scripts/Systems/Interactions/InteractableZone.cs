using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class InteractableZone : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Higher = this zone wins when multiple overlap")]
    [SerializeField] private int priority = 0;

    // static list of all zones the player is currently inside
    static readonly List<InteractableZone> _zonesInRange = new List<InteractableZone>();

    bool _hasTriggeredThisPress = false;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _zonesInRange.Add(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _zonesInRange.Remove(this);
        _hasTriggeredThisPress = false;
    }

    void Update()
    {
        // nothing to do if player isn't in any zone
        if (_zonesInRange.Count == 0) return;

        var input = PlayerInputManager.Instance?.InputHandler;
        if (input == null) return;

        // reset trigger when button is released
        if (!input.InteractPressed)
        {
            _hasTriggeredThisPress = false;
            return;
        }

        // only fire once per press
        if (_hasTriggeredThisPress) return;

        // pick the zone with the highest priority
        var topZone = _zonesInRange
            .OrderByDescending(z => z.priority)
            .FirstOrDefault();

        if (topZone == this)
        {
            _hasTriggeredThisPress = true;
            OnInteract();
        }
    }

    /// <summary>Override this in subclasses for the actual behavior.</summary>
    protected abstract void OnInteract();
}
