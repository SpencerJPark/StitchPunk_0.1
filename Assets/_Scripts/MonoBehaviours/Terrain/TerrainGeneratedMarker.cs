using UnityEngine;

// Pure tag: marks an object a feature generator wrote, so regeneration knows what it owns.
[DisallowMultipleComponent]
public class TerrainGeneratedMarker : MonoBehaviour
{
    public string sourceFeatureId;

    [Tooltip("A locked object survives regeneration; editing it by hand locks it automatically")]
    public bool locked;
}
