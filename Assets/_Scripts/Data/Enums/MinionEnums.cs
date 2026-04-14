public enum CommandType : byte
{
    Move,       // Move to a world position
    Interact,   // Interact with a target entity
    Attack,     // Engage a specific target entity
    Defend,     // Hold a position; auto-attack enemies within a radius
    Follow,     // Shadow the player entity
}

public enum FormationType : byte
{
    Blob,    // No offset — members spread naturally via collision (default)
    Line,    // Spread perpendicular to world X axis, 1 unit apart
    Square,  // Grid arrangement, ceil(sqrt(n)) columns
    Circle,  // Evenly spaced ring, radius scales with sqrt(n)
}

