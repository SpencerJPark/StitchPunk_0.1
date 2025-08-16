using UnityEngine;

[CreateAssetMenu(menuName="Units/Design Profile")]
public class UnitDesignProfile : ScriptableObject
{
    [Tooltip("Run in order; leave empty if this unit has no customization.")]
    public CustomizationFeature[] features;
}
