using UnityEngine;
using System;
using Data;

public class UnitDesignProfile
{
    // Never null; safe to iterate even if empty
    public CustomizationFeature[] features = Array.Empty<CustomizationFeature>();

    public void Initialize(RiveAnimator anim)
    {
        foreach (var feature in features)
            feature?.Initialize(anim);
    }

    public string Create(string featureID)
    {
        if (TryFindFeature(featureID, out var feature))
            return feature.Create();

        Debug.LogWarning($"[UnitDesignProfile.Create] FeatureID '{featureID}' not found.");
        return string.Empty;
    }

    public void Apply(string featureID, string target, string value, bool isNumber = false)
    {
        if (!TryFindFeature(featureID, out var feature))
        {
            Debug.LogWarning($"[UnitDesignProfile.Apply] FeatureID '{featureID}' not found.");
            return;
        }

        if (isNumber)
        {
            if (float.TryParse(value, out float num))
                feature.ApplyNumber(target, num);
            else
                Debug.LogWarning($"[Apply] Could not parse '{value}' as float for target '{target}' (FeatureID='{featureID}').");
        }
        else
        {
            feature.ApplyEnum(target, value);
        }
    }

    // Helper avoids nullable returns; no CS8603
    private bool TryFindFeature(string featureID, out CustomizationFeature customizationFeature)
    {
        foreach (var feature in features)
        {
            if (feature != null && string.Equals(feature.FeatureID, featureID, StringComparison.Ordinal))
            {
                customizationFeature = feature;
                return true;
            }
        }
        customizationFeature = null!; // safe: only used when method returns false
        return false;
    }
}
