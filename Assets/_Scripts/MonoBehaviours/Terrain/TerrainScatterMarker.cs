using UnityEngine;

// Pure tag: tile contents bake through the normal renderer bakers, so this has no baked meaning.
[DisallowMultipleComponent]
public class TerrainScatterMarker : MonoBehaviour
{
    [Tooltip("A locked instance survives Regenerate, Clear Unlocked and erase-by-region; the scatterers also steer new instances around it")]
    public bool locked;

    public int ruleIndex;

    public string sourceFeatureId;

    public float footprintRadiusMetres = 1f;
}
