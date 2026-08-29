public enum CommandType : byte
{
    Move,       // Move to a world position
    Interact,   // Interact with a target entity
    Attack,     // Engage a specific target entity
    Defend,     // Hold a position; auto-attack enemies within a radius
    Follow,     // Shadow the player entity
}

