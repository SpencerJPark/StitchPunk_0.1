using UnityEngine;

public abstract class TerrainAreaRecipe : ScriptableObject
{
    public bool paintsGround = true;
    public TerrainSplatLayer groundLayer = TerrainSplatLayer.Forest;
}
