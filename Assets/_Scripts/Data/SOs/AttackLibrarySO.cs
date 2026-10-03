using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_AttackLibrary", menuName = "Units/Attack Library")]
public class AttackLibrarySO : ScriptableObject
{
    public List<AttackSO> attacks = new List<AttackSO>();

    public AttackSO GetAttack(DamageSource damageSource)
    {
        foreach (AttackSO attack in attacks)
        {
            if (attack != null && attack.damageSource == damageSource)
                return attack;
        }
        return null;
    }
}
