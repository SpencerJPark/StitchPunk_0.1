using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class InteractableZone : MonoBehaviour, IUpdateObserver
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
        Debug.Log($"{name}: Player entered zone");
        _zonesInRange.Add(this);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        Debug.Log($"{name}: Player exited zone");
        _zonesInRange.Remove(this);
        _hasTriggeredThisPress = false;
    }


    void OnEnable() => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);


    public void ObservedUpdate()
    {
        // 1) Early-out if no zones
        if (_zonesInRange.Count == 0) return;

        // 2) Grab the one, true input provider
        var input = PlayerInputHandler.Instance;
        if (input == null)
        {
            Debug.LogError($"{name}: No PlayerInputHandler.Instance!");
            return;
        }

        // 3) Debug log so we can see the flag
        Debug.Log($"{name}: raw InteractFired = {input.InteractFired}");

        // 4) Reset when released
        if (!input.InteractFired)
        {
            _hasTriggeredThisPress = false;
            return;
        }

        // 5) Only fire once per down-press
        if (_hasTriggeredThisPress) return;

        // 6) Find top-priority zone
        var topZone = _zonesInRange.OrderByDescending(z => z.priority).First();
        Debug.Log($"{name}: topZone = {topZone.name} (priority {topZone.priority})");

        // 7) If it’s us, fire!
        if (topZone == this)
        {
            Debug.Log($"{name}: OnInteract about to fire!");
            _hasTriggeredThisPress = true;
            OnInteract();
        }
    }


    /// <summary>Override this in subclasses for the actual behavior.</summary>
    protected abstract void OnInteract();
}
