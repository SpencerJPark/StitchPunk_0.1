using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_EffectLibrary", menuName = "EffectSO/Effect Library")]
public class EffectLibrarySO : ScriptableObject
{
    public List<EffectSO> effects = new List<EffectSO>();

    public EffectSO GetEffectSO(EffectType type)
    {
        foreach (EffectSO so in effects)
        {
            if (so != null && so.effectType == type)
                return so;
        }
        return null;
    }
}
