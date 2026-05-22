using Unity.Entities;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Unit", menuName = "Units/Unit")]
public class UnitSO : ScriptableObject
{
    [SearchableEnum] public UnitType unitType;
    public FactionType factionType;
    public bool canBePlayerControlled = false;
    
    [Header("Prefab GameObject")]
    public GameObject prefab;
    
    [Header("AI")]
    public float awarenessRange;
    [Range(0f, 1f)]   public float bravery         = 0.5f;
    [Range(0f, 0.5f)] public float braveryVariance = 0f;
    [SearchableEnum] public MotivationType[] motivations;
    public int randomMotivationsTotal;
    [SearchableEnum] public MotivationType[] randomMotivations;
    public List<MotivationDecayConfig> motivationDecayRates;
    [SearchableEnum] public FactionType[] socialFactions;
    
    [Header("Animations")]
    public ActionAnimationMapping[] actionAnimations;
    public StanceAnimationMapping[] stanceAnimations;
    [SearchableEnum] public AnimationType idleAnimation;
    [SearchableEnum] public AnimationType movingAnimation;

    [Header("Combat")]
    [SearchableEnum] public FactionType[] attackFactions;
    public AttackActionMapping[] attacks;
    
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 10f;
    
    // [Header("Spawn Cost")]
    // public ResourceAmount[] spawnCostResourceAmountArray;
    // public float progressMax;
    // public Sprite sprite;

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
public struct MotivationDecayConfig
{
    [SearchableEnum] public MotivationType motivationType;
    public float decayRate;
}

[Serializable]
public struct ActionAnimationMapping
{
    [SearchableEnum] public ActionType action;
    [SearchableEnum] public AnimationType animation;
}

[Serializable]
public struct AttackActionMapping
{
    [SearchableEnum] public ActionType action;
    [SearchableEnum] public AttackType attack;
}

[Serializable]
public struct StanceAnimationMapping
{
    public StanceType stance;
    [SearchableEnum] public AnimationType idleAnimation;
    [SearchableEnum] public AnimationType movingAnimation;
}

