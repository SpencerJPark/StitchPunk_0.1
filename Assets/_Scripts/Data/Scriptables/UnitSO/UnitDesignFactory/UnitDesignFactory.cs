#nullable enable
using UnityEngine;
using System;
using System.Linq;

public abstract class UnitDesignFactory : ScriptableObject
{
    [SerializeReference] protected CustomizationFeature[] featureTemplates = Array.Empty<CustomizationFeature>();

    public virtual UnitDesignProfile BuildProfile()
    {
        var p = new UnitDesignProfile();
        p.features = featureTemplates
            .Where(t => t != null)
            .Select(CloneFeature)
            .ToArray();
        return p;
    }

    // JSON clone keeps per-template serialized values
    protected static T CloneFeature<T>(T src) where T : class
    {
        if (src == null) return null!;
        var json = JsonUtility.ToJson(src);
        return JsonUtility.FromJson<T>(json);
    }

    // (Optional) convenience for children to set their scheme
    protected void SetScheme(params CustomizationFeature[] items)
    {
        featureTemplates = items ?? Array.Empty<CustomizationFeature>();
    }
}
