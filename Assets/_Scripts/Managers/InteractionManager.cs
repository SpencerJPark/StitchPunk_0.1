using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class InteractionManager : MonoBehaviour, IUpdateObserver
{
    public static InteractionManager Instance { get; private set; }

    readonly HashSet<IInteractableZone> _zones = new();

    void Awake()
    {
        if (Instance != null) Destroy(this);
        else Instance = this;
    }

    void OnEnable() => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void RegisterZone(IInteractableZone zone) => _zones.Add(zone);
    public void UnregisterZone(IInteractableZone zone) => _zones.Remove(zone);

    public void ObservedUpdate()
    {
        var input = PlayerInputHandler.Instance;
        if (input == null) return;

        // only on press-down
        if (!input.InteractFired) return;

        // pick the highest-priority zone
        if (_zones.Count > 0)
        {
            var top = _zones.OrderByDescending(z => z.Priority).First();
            top.OnInteract();
        }
        else
        {
            // no zone → fallback emote
            //playerEmoter.DoEmote();
        }
    }
}
