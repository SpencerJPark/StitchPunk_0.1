using Unity.Entities;
using UnityEngine;
using System;

[CreateAssetMenu(fileName = "Unit", menuName = "UnitSO/Unit")]
public class UnitSO : ScriptableObject
{
    [SearchableEnum] public UnitType unitType;
    public GameObject prefab;
    
    [Tooltip("Animation Data")]
    public ActionAnimationMapping[] actionAnimations;
    [SearchableEnum] public AnimationType idleAnimation;
    [SearchableEnum] public AnimationType movingAnimation;

    [Tooltip("Default Attack")] 
    [SearchableEnum] public AttackType[] attackType;
    
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f;
    
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

