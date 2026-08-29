namespace DotsMovementToolkit
{
    public enum FormationType : byte
    {
        Blob,    // No offset — members spread naturally via collision (default)
        Line,    // Spread perpendicular to world X axis, 1 unit apart
        Square,  // Grid arrangement, ceil(sqrt(n)) columns
        Circle,  // Evenly spaced ring, radius scales with sqrt(n)
    }
}
