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
    // The form this unit becomes when revived/converted (e.g. Citizen -> PlayerZombie).
    // None = "does not convert" — revival just stands the same brain back up.
    [SearchableEnum] public UnitType becomesUnitType;
    
    [Header("Prefab GameObject")]
    public GameObject prefab;
    
    [Header("AI")]
    public float awarenessRange;
    [Range(0f, 1f)]   public float bravery              = 0.5f;
    [Range(0f, 0.5f)] public float braveryVariance      = 0f;
    [Range(0f, 1f)]   public float socialAffinity       = 0.5f;
    [Range(0f, 0.5f)] public float socialAffinityVariance = 0.1f;
    [Range(0f, 1f)]   public float wanderlust           = 0.5f;
    [Range(0f, 0.5f)] public float wanderlustVariance   = 0.1f;
    [Range(0f, 1f)]   public float gluttony             = 0.5f;
    [Range(0f, 0.5f)] public float gluttonyVariance     = 0.1f;
    [SearchableEnum] public NeedType[] motivations;
    public int randomMotivationsTotal;
    [SearchableEnum] public NeedType[] randomMotivations;
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
    public float runSpeed = 9f;
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
    [FormerlySerializedAs("motivationType")]
    [SearchableEnum] public NeedType needType;
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

