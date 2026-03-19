using Unity.Entities;
using UnityEngine;
using System;

[CreateAssetMenu(fileName = "Unit", menuName = "UnitSO/Unit")]
public class UnitSO : ScriptableObject
{
    public UnitType unitType;
    
    [Tooltip("Animation Data")]
    public ActionAnimationMapping[] actionAnimations;
    public AnimationType idleAnimation;
    public AnimationType movingAnimation;

    [Tooltip("Default Attack")] 
    public AttackType attackType;
    
    [Tooltip("Spawn Cost")]
    public ResourceAmount[] spawnCostResourceAmountArray;
    public float progressMax;
    public Sprite sprite;

    public AnimationType GetAnimation(ActionType actionType, bool isMoving)
    {
        for (int i = 0; i < actionAnimations.Length; i++)
        {
            if (actionAnimations[i].action == actionType)
            {
                return actionAnimations[i].animation;
            }
        }
        return isMoving ? movingAnimation : idleAnimation;
    }
}

[Serializable]
public struct ActionAnimationMapping
{
    public ActionType action;
    public AnimationType animation;
}