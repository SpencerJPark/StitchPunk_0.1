using UnityEngine;

[RequireComponent(typeof(UnitController))]
public class Brain : InputProviderBase, IUpdateObserver
{
    // AI Components
    [SerializeField] private PathfindingComponent pathfinding;

    // IInputProvider implementation
    public Vector2 MoveInput => pathfinding != null ? pathfinding.CurrentMoveInput : Vector2.zero;
    public Vector2 SteerInput { get; private set; }   // can later be filled with vehicle steering logic
    public bool ExitVehicleFired => false;
    public bool InteractFired => false;
    public bool ActionFired => false;

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        // Decision logic will go here later.
        // For now, just let pathfinding update itself each frame.
        pathfinding?.TickUpdate();
    }

    public void SetDestination(Vector3 pos)
    {
        if (pathfinding != null)
            pathfinding.SetDestination(pos);
    }
}
