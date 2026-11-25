using System;
using UnityEngine;

public static class TerrainTerraceEvents
{
    public static event Action<Vector2Int> TerraceChanged;

    public static void RaiseTerraceChanged(Vector2Int tile)
    {
        if (Application.isPlaying)
        {
            return;
        }

        TerraceChanged?.Invoke(tile);
    }
}
