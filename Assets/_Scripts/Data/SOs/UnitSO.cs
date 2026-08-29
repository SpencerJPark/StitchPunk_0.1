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
    public DirectionSetAsset idleAnimation;
    public DirectionSetAsset movingAnimation;

    [Tooltip("The rig this unit's clips are authored against. Validate-only: the prefab's " +
             "ActorAuthoring stays the runtime source of truth, and a disagreement warns rather " +
             "than overriding it.")]
    public RigAsset rig;

    [Tooltip("The clip set this unit's animations come from. Validate-only, like the rig above.")]
    public ClipSetAsset clipSet;

    [Header("Combat")]
    [Tooltip("Spawn and maximum health. Baked into UnitLibrary and stamped onto every unit at " +
             "spawn — this, not the prefab's HealthAuthoring numbers, is what a spawned unit gets.")]
    public int maxHealth = 100;

    [SearchableEnum] public FactionType[] attackFactions;
    // Attack damage lives on AttackSO, keyed by DamageSource and reached through these mappings.
    // Duplicating it here would give two numbers that can disagree about one hit.
    public AttackActionMapping[] attacks;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float runSpeed = 9f;
    public float rotationSpeed = 10f;
    
    // [Header("Spawn Cost")]
    // public ResourceAmount[] spawnCostResourceAmountArray;
    // public float progressMax;
    // public Sprite sprite;

    public DirectionSetAsset GetAnimation(ActionType actionType, bool isMoving)
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

    // Whether this unit's declared rig/clip set disagree with what its prefab's ActorAuthoring
    // actually carries. Shared by OnValidate and UnitLibraryBakingSystem so the inspector and the
    // bake describe the same disagreement in the same words. Null when there is nothing to say —
    // an unset field is "not declared", not "declared wrong".
    public string DescribeRigMismatch()
    {
        if (prefab == null || (rig == null && clipSet == null))
            return null;

        ActorAuthoring actor = prefab.GetComponentInChildren<ActorAuthoring>(true);
        if (actor == null)
            return $"'{name}' declares a rig/clip set but its prefab '{prefab.name}' has no ActorAuthoring.";

        if (rig != null && actor.rig != rig)
            return $"'{name}' declares rig '{rig.name}' but its prefab animates on " +
                   $"'{(actor.rig != null ? actor.rig.name : "<none>")}'.";

        if (clipSet != null && (actor.clipSets == null || !actor.clipSets.Contains(clipSet)))
            return $"'{name}' declares clip set '{clipSet.name}' but its prefab's ActorAuthoring " +
                   "does not list it.";

        return null;
    }

    // Reported once per domain load per asset, not on every keystroke: OnValidate fires on each
    // inspector edit, and a mismatch that has not changed is not news.
    [NonSerialized] private bool hasReportedRigMismatch;

    private void OnValidate()
    {
        string mismatch = DescribeRigMismatch();
        if (mismatch == null)
        {
            hasReportedRigMismatch = false;
            return;
        }
        if (hasReportedRigMismatch)
            return;

        hasReportedRigMismatch = true;
        Debug.LogWarning($"[UnitSO] {mismatch}", this);
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
    public DirectionSetAsset animation;
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
    public DirectionSetAsset idleAnimation;
    public DirectionSetAsset movingAnimation;
}

