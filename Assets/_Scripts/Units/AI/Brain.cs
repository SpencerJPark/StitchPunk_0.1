using UnityEngine;

[RequireComponent(typeof(UnitController))]
public class Brain : InputProviderBase, IUpdateObserver
{
    // AI Components
    [SerializeField] private PathfindingComponent pathfinding;

    // IInputProvider implementation
    public override Vector2 MoveInput => pathfinding.CurrentMoveInput;
    public  Vector2 SteerInput { get; private set; }   // can later be filled with vehicle steering logic
    public override bool ExitVehicleFired => false;
    public override bool InteractFired => false;
    public override bool ActionFired => false;

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        
        // Decision logic will go here later.
        // For now, just let pathfinding update itself each frame.
        pathfinding?.Tick();
    }

    public void SetDestination(Vector3 pos)
    {
        if (pathfinding != null)
            pathfinding.SetDestination(pos);
        else
            Debug.LogError("Brain is missing PathfindingComponent!");
    }
}
