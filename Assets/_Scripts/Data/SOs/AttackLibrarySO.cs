using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_AttackLibrary", menuName = "Units/Attack Library")]
public class AttackLibrarySO : ScriptableObject
{
    public List<AttackSO> attacks = new List<AttackSO>();

    public AttackSO GetAttack(AttackType attackType)
    {
        foreach (var attack in attacks)
        {
            if (attack != null && attack.attackType == attackType)
                return attack;
        }
        return null;
    }
}
