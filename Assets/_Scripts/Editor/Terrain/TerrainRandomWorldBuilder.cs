#if UNITY_EDITOR
// Seed in, explorable world out. Body is filled in by the random-world task; the panel already calls this entry point.
public static class TerrainRandomWorldBuilder
{
    public static string BuildRandomWorld(int tilesPerSide, int seed)
    {
        return "Random world builder is not wired yet.";
    }
}
#endif
