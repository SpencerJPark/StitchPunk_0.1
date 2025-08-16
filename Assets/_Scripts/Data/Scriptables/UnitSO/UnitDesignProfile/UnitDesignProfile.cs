
public class UnitDesignProfile
{
    [Tooltip("Run in order; leave empty if this unit has no customization.")]
    public CustomizationFeature[] features;

    public void Initialize(RiveAnimator Anim)
    {
        // runs through list and passes rive value
    }

    public string Create(string FeatureID, string target, IUnitRole? role)
    {
        // runs through list of customizationfeatures until key matches and then activates their create, returns string value
    }

    public void Apply(string FeatureID, string target, string value, bool isNumber = false)
    {
        if (isNumber && ValidateNumberValue(value))
        {
            // Search for number value
            return;
        }

        // Search for string value
    }

    private bool ValidateNumberValue(string value)
    {
        if (float.TryParse(value, out float f))
            return true;
        else
            Debug.LogWarning($"[Apply] Could not parse '{value}' as float for target '{target}'");
            return false;
    }

}
