using Unity.Entities;
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;

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
    [Tooltip("How many directions this unit's locomotion/action art turns through. A citizen and a " +
             "boss can share a rig and differ here — it's a property of the content, not the rig.")]
    public AnimationDirections animationDirections = AnimationDirections.Six;
    public ActionAnimationMapping[] actionAnimations;
    public StanceAnimationMapping[] stanceAnimations;
    public DirectionSetSO idleAnimation;
    public DirectionSetSO movingAnimation;

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

    public DirectionSetSO GetAnimation(ActionType actionType, bool isMoving)
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
    public DirectionSetSO animation;
}

[Serializable]
public struct AttackActionMapping
{
    [SearchableEnum] public ActionType action;
    [SearchableEnum] public DamageSource attack;
}

[Serializable]
public struct StanceAnimationMapping
{
    public StanceType stance;
    public DirectionSetSO idleAnimation;
    public DirectionSetSO movingAnimation;
}

