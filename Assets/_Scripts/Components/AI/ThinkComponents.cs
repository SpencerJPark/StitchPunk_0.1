using Unity.Entities;

// Units with ThinkRate re-run awareness (enable ActionRequest) only every `interval` seconds, staggered,
// plus every frame they are between behaviours. Units without it keep ActionRequest as baked (always on).
public struct ThinkRate : IComponentData
{
    public const float IDLE_THINK_INTERVAL = 0.1f; // idle units re-think this often instead of every frame (AF-D8)

    public float interval;
    public float timeUntilNextThink;
    public bool  wasIdle;              // detects the frame a unit becomes idle so it thinks immediately
}

// A spotter that broadcasts AllyAlert to same-faction units within radius when it picks a combat target.
public struct RaiseAlarm : IComponentData
{
    public float radius;
    public float cooldownRemaining;
}

// Set on an ally by a spotter's RaiseAlarm; the unit joins the fight against `target` until `remaining` runs out.
public struct AllyAlert : IComponentData, IEnableableComponent
{
    public Entity target;
    public float  remaining;
}

// Enabled by the RequestThrow behaviour command; AiThrowItemSystem launches the held item at `target`.
public struct AiThrowRequest : IComponentData, IEnableableComponent
{
    public Entity target;
}
