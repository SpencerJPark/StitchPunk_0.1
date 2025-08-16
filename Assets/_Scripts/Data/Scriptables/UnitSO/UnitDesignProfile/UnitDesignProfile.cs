#nullable enable
using UnityEngine;
using System;
using Data;

public class UnitDesignProfile
{
    // Never null; safe to iterate even if empty
    public CustomizationFeature[] features = Array.Empty<CustomizationFeature>();

    public void Initialize(RiveAnimator anim)
    {
        foreach (var f in features)
            f?.Initialize(anim);
    }

    public string Create(string featureID, string target, IUnitRole? role)
    {
        if (TryFindFeature(featureID, out var f))
            return f.Create(target, role);

        Debug.LogWarning($"[UnitDesignProfile.Create] FeatureID '{featureID}' not found.");
        return string.Empty;
    }

    public void Apply(string featureID, string target, string value, bool isNumber = false)
    {
        if (!TryFindFeature(featureID, out var f))
        {
            Debug.LogWarning($"[UnitDesignProfile.Apply] FeatureID '{featureID}' not found.");
            return;
        }

        if (isNumber)
        {
            if (float.TryParse(value, out float num))
                f.ApplyNumber(target, num);
            else
                Debug.LogWarning($"[Apply] Could not parse '{value}' as float for target '{target}' (FeatureID='{featureID}').");
        }
        else
        {
            f.ApplyEnum(target, value);
        }
    }

    // Helper avoids nullable returns; no CS8603
    private bool TryFindFeature(string featureID, out CustomizationFeature feature)
    {
        foreach (var f in features)
        {
            if (f != null && string.Equals(f.FeatureID, featureID, StringComparison.Ordinal))
            {
                feature = f;
                return true;
            }
        }
        feature = null!; // safe: only used when method returns false
        return false;
    }
}
