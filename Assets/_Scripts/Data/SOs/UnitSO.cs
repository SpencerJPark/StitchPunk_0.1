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
    [Tooltip("Validate-only: the prefab's ActorAuthoring.profile is the runtime truth.")]
    public ActorProfileAsset actorProfile;

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

    // Whether this unit's declared actor profile disagrees with what its prefab's ActorAuthoring
    // actually carries. Shared by OnValidate and UnitLibraryBakingSystem so the inspector and the
    // bake describe the same disagreement in the same words. Null when there is nothing to say —
    // an unset field is "not declared", not "declared wrong".
    public string DescribeProfileMismatch()
    {
        if (prefab == null || actorProfile == null)
            return null;

        ActorAuthoring actor = prefab.GetComponentInChildren<ActorAuthoring>(true);
        if (actor == null)
            return $"'{name}' declares an actor profile but its prefab '{prefab.name}' has no ActorAuthoring.";

        if (actor.profile != actorProfile)
            return $"'{name}' declares actor profile '{actorProfile.name}' but its prefab " +
                   $"'{prefab.name}' animates on '{(actor.profile != null ? actor.profile.name : "<none>")}'.";

        return null;
    }

    // Reported once per domain load per asset, not on every keystroke: OnValidate fires on each
    // inspector edit, and a mismatch that has not changed is not news.
    [NonSerialized] private bool hasReportedProfileMismatch;

    private void OnValidate()
    {
        string mismatch = DescribeProfileMismatch();
        if (mismatch == null)
        {
            hasReportedProfileMismatch = false;
            return;
        }
        if (hasReportedProfileMismatch)
            return;

        hasReportedProfileMismatch = true;
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
public struct AttackActionMapping
{
    [SearchableEnum] public ActionType action;
    [SearchableEnum] public DamageSource attack;
}

