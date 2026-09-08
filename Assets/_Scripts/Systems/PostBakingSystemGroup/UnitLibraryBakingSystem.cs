using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Collections;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.IO;
#endif

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct UnitLibraryBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (libraryRef, entity) in
            SystemAPI.Query<RefRO<UnitDataLibraryReference>>()
            .WithNone<UnitDataLibrary>()
            .WithEntityAccess())
        {
            UnitLibrarySO librarySO = libraryRef.ValueRO.library.Value;
            if (librarySO == null || librarySO.units == null) continue;

            BlobAssetReference<UnitLibraryBlob> blobRef = CreateUnitLibraryBlob(librarySO);

            ecb.AddComponent(entity, new UnitDataLibrary
            {
                library = blobRef
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (var holder in SystemAPI.Query<RefRW<UnitDataLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    private BlobAssetReference<UnitLibraryBlob> CreateUnitLibraryBlob(UnitLibrarySO librarySO)
    {
        BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();

        BlobBuilderArray<UnitDataBlob> unitsArray = builder.Allocate(ref root.units, librarySO.units.Count);

        Array allActionTypes = Enum.GetValues(typeof(ActionType));
        Array allStanceTypes = Enum.GetValues(typeof(StanceType));

        for (int i = 0; i < librarySO.units.Count; i++)
        {
            UnitSO unitSO = librarySO.units[i];

            unitsArray[i].unitType             = unitSO.unitType;
            unitsArray[i].factionType          = unitSO.factionType;
            unitsArray[i].becomesUnitType      = unitSO.becomesUnitType;
            unitsArray[i].canBePlayerControlled = unitSO.canBePlayerControlled;
            unitsArray[i].awarenessRange       = unitSO.awarenessRange;
            unitsArray[i].maxHealth            = unitSO.maxHealth;

            // Validate-only (ActorProfileCutover_System.md G5): the prefab's ActorAuthoring stays
            // the runtime source of truth, so a disagreement is reported here rather than pushed
            // onto the entity — where it would silently demote what the prefab actually animates on.
            string profileMismatch = unitSO.DescribeProfileMismatch();
            if (profileMismatch != null)
                UnityEngine.Debug.LogWarning($"[UnitLibraryBaking] {profileMismatch}", unitSO);

            // Name-convention binding (G5 D1): every name is resolved through the project's
            // AnimationNameRegistry; anything that does not resolve bakes key 0 and is collected
            // into one warning per unit below, rather than one warning per missing name.
            List<string> unresolvedAnimationNames = new List<string>();

            unitsArray[i].idleAnimationKey = ResolveAnimationKey(AnimationNameConvention.Idle, unresolvedAnimationNames);
            unitsArray[i].walkAnimationKey = ResolveAnimationKey(AnimationNameConvention.Walk, unresolvedAnimationNames);

            BlobBuilderArray<ActionAnimationKeyBlob> actionKeysArray =
                builder.Allocate(ref unitsArray[i].actionAnimationKeys, allActionTypes.Length);
            for (int j = 0; j < allActionTypes.Length; j++)
            {
                ActionType actionType = (ActionType)allActionTypes.GetValue(j);
                actionKeysArray[j].action = actionType;
                actionKeysArray[j].animationKey =
                    ResolveAnimationKey(AnimationNameConvention.ForAction(actionType), unresolvedAnimationNames);
            }

            BlobBuilderArray<StanceAnimationKeysBlob> stanceKeysArray =
                builder.Allocate(ref unitsArray[i].stanceAnimationKeys, allStanceTypes.Length);
            for (int j = 0; j < allStanceTypes.Length; j++)
            {
                StanceType stanceType = (StanceType)allStanceTypes.GetValue(j);
                stanceKeysArray[j].stance = stanceType;
                stanceKeysArray[j].idleAnimationKey =
                    ResolveAnimationKey(AnimationNameConvention.ForStanceIdle(stanceType), unresolvedAnimationNames);
                stanceKeysArray[j].walkAnimationKey =
                    ResolveAnimationKey(AnimationNameConvention.ForStanceWalk(stanceType), unresolvedAnimationNames);
            }

            if (unresolvedAnimationNames.Count > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnitLibraryBaking] '{unitSO.name}' has {unresolvedAnimationNames.Count} unresolved " +
                    $"animation name(s) in its AnimationNameRegistry: {string.Join(", ", unresolvedAnimationNames)}",
                    unitSO);
            }

            int motivationCount = unitSO.motivations?.Length ?? 0;
            BlobBuilderArray<NeedType> motivationArray = builder.Allocate(ref unitsArray[i].motivation, motivationCount);
            for (int j = 0; j < motivationCount; j++)
                motivationArray[j] = unitSO.motivations[j];

            int randomCount = unitSO.randomMotivations?.Length ?? 0;
            BlobBuilderArray<NeedType> randomArray = builder.Allocate(ref unitsArray[i].randomMotivations, randomCount);
            for (int j = 0; j < randomCount; j++)
                randomArray[j] = unitSO.randomMotivations[j];

            int attackFactionCount = unitSO.attackFactions?.Length ?? 0;
            BlobBuilderArray<FactionType> attackFactionArray = builder.Allocate(ref unitsArray[i].attackFactions, attackFactionCount);
            for (int j = 0; j < attackFactionCount; j++)
                attackFactionArray[j] = unitSO.attackFactions[j];

            int socialFactionCount = unitSO.socialFactions?.Length ?? 0;
            BlobBuilderArray<FactionType> socialFactionArray = builder.Allocate(ref unitsArray[i].socialFactions, socialFactionCount);
            for (int j = 0; j < socialFactionCount; j++)
                socialFactionArray[j] = unitSO.socialFactions[j];

            int attackCount = unitSO.attacks?.Length ?? 0;
            BlobBuilderArray<AttackActionMappingBlob> attackArray = builder.Allocate(ref unitsArray[i].attacks, attackCount);
            for (int j = 0; j < attackCount; j++)
            {
                attackArray[j].action = unitSO.attacks[j].action;
                attackArray[j].attack = unitSO.attacks[j].attack;
            }
        }

        BlobAssetReference<UnitLibraryBlob> blobRef = builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        builder.Dispose();

        return blobRef;
    }

    // ---- Animation name resolution (G5 P2) ----
    //
    // VocabularyRegistryProvider.AnimationNames lives in DotsAnimationToolkit.Editor, which this
    // baking system's assembly (StitchPunk.Systems) does not reference. AnimationNameRegistry
    // itself lives in DotsAnimationToolkit.Authoring — already referenced here for ActorProfileAsset
    // — so this reads the same ProjectSettings JSON file directly instead of adding an
    // editor-assembly bridge. Only the read side is replicated; nothing here ever writes the file.
#if UNITY_EDITOR
    private const string AnimationNameRegistryPath = "ProjectSettings/DotsAnimationToolkitAnimationNameRegistry.asset";
    private static AnimationNameRegistry cachedAnimationNameRegistry;

    private static AnimationNameRegistry LoadAnimationNameRegistry()
    {
        if (cachedAnimationNameRegistry == null)
        {
            cachedAnimationNameRegistry = UnityEngine.ScriptableObject.CreateInstance<AnimationNameRegistry>();
            cachedAnimationNameRegistry.hideFlags = UnityEngine.HideFlags.DontSave;
            if (File.Exists(AnimationNameRegistryPath))
            {
                string storedJson = File.ReadAllText(AnimationNameRegistryPath);
                UnityEditor.EditorJsonUtility.FromJsonOverwrite(storedJson, cachedAnimationNameRegistry);
            }
        }
        return cachedAnimationNameRegistry;
    }
#endif

    private static uint ResolveAnimationKey(string animationName, List<string> unresolvedAnimationNames)
    {
        uint animationKey = 0;
#if UNITY_EDITOR
        AnimationNameRegistry registry = LoadAnimationNameRegistry();
        if (registry.entries != null)
        {
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimationNameEntry entry = registry.entries[entryIndex];
                if (entry != null && entry.name == animationName)
                {
                    animationKey = entry.animationKey;
                    break;
                }
            }
        }
#endif
        if (animationKey == 0)
            unresolvedAnimationNames.Add(animationName);
        return animationKey;
    }
}
