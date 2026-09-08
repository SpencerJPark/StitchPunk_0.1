using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace StitchPunk.Editor.ContentAuthoring
{
    // G6-P4 (CutsceneProfileCutover_System.md): re-runnable, so the mapping survives a future
    // profile/registry rename. Every existing clip block in every one of these seven assets was
    // authored against Walk.asset (legacy clipId 17929205651740358465, confirmed by grepping every
    // asset's raw YAML before A73's schema migration dropped the field) — there is no other clip to
    // map to, so any block this script finds still at animationKey == 0 is assumed to be that walk
    // block and named "Walk". A block already resolved (nonzero) is left alone.
    public static class CutsceneProfileReauthoring
    {
        private const string ProfilePath = "Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset";

        private static readonly string[] CutsceneAssetPaths =
        {
            "Assets/ScriptableObjects/Animations/G1CheckpointCutscene.asset",
            "Assets/ScriptableObjects/Animations/A63CheckpointCutscene.asset",
            "Assets/ScriptableObjects/Animations/A64CheckpointCutscene.asset",
            "Assets/ScriptableObjects/Animations/A65CheckpointCutscene.asset",
            "Assets/ScriptableObjects/Animations/G2CheckpointCutscene.asset",
            "Assets/ScriptableObjects/Animations/NewCutscene.asset",
            "Assets/ScriptableObjects/Cutscenes/RendezvousAndDepart.asset",
        };

        [MenuItem("Stitch Punk/Content/Re-point Cutscenes at MaleCitizen Profile")]
        public static string RepointCutscenesAtMaleCitizenProfile()
        {
            ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(ProfilePath);
            if (profile == null)
            {
                return "FAILED: " + ProfilePath + " not found.";
            }

            uint idleKey = FindAnimationKeyByName("Idle");
            uint walkKey = FindAnimationKeyByName("Walk");

            int slotsRepointed = 0;
            int blocksResolved = 0;
            List<string> unresolvedBlocks = new List<string>();

            foreach (string assetPath in CutsceneAssetPaths)
            {
                CutsceneAsset cutsceneAsset = AssetDatabase.LoadAssetAtPath<CutsceneAsset>(assetPath);
                if (cutsceneAsset == null)
                {
                    Debug.LogWarning("[CutsceneProfileReauthoring] " + assetPath + " not found — skipped.");
                    continue;
                }

                bool assetChanged = false;

                foreach (CutsceneSlot slot in cutsceneAsset.slots)
                {
                    if (slot.kind != CutsceneSlotKind.Actor)
                    {
                        continue;
                    }

                    slot.profile = profile;
                    slot.locomotion.enabled = true;
                    slot.locomotion.standingAnimationKey = idleKey;
                    slot.locomotion.movingAnimationKey = walkKey;
                    slotsRepointed++;
                    assetChanged = true;

                    foreach (CutsceneClipBlock clipBlock in slot.clipBlocks)
                    {
                        if (clipBlock.animationKey != 0)
                        {
                            continue;
                        }

                        if (walkKey != 0)
                        {
                            clipBlock.animationKey = walkKey;
                            blocksResolved++;
                        }
                        else
                        {
                            unresolvedBlocks.Add(cutsceneAsset.name + "/" + slot.name + "@" + clipBlock.start);
                        }
                    }
                }

                // The player is not a toolkit actor and walks by hand (G2 §3.4) — RendezvousAndDepart
                // authors it as a Prop slot already (no rig, no profile), so this is a no-op today;
                // kept explicit in case the slot's kind ever changes and locomotion stops being ignored.
                if (assetPath.EndsWith("RendezvousAndDepart.asset"))
                {
                    foreach (CutsceneSlot slot in cutsceneAsset.slots)
                    {
                        if (slot.name == "Player")
                        {
                            slot.locomotion.enabled = false;
                            assetChanged = true;
                        }
                    }
                }

                if (assetChanged)
                {
                    cutsceneAsset.EnsureStableIds();
                    EditorUtility.SetDirty(cutsceneAsset);
                }
            }

            AssetDatabase.SaveAssets();

            string unresolvedSummary = unresolvedBlocks.Count == 0
                ? "none"
                : string.Join(", ", unresolvedBlocks);
            return "RepointCutscenesAtMaleCitizenProfile: slots repointed=" + slotsRepointed +
                   ", blocks resolved=" + blocksResolved + ", unresolved=" + unresolvedSummary;
        }

        // Looks up an existing registry entry only — never mints. A cutscene re-pointing pass should
        // never invent vocabulary; if Idle/Walk are missing, that is a content-authoring problem to
        // surface, not paper over with a fresh key nothing else references.
        private static uint FindAnimationKeyByName(string animationName)
        {
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            IVocabularyRegistry vocabularyRegistry = registry;
            for (int entryIndex = 0; entryIndex < vocabularyRegistry.VocabularyEntryCount; entryIndex++)
            {
                if (vocabularyRegistry.VocabularyEntryName(entryIndex) == animationName)
                {
                    return vocabularyRegistry.VocabularyEntryId(entryIndex);
                }
            }
            Debug.LogWarning("[CutsceneProfileReauthoring] Animation name \"" + animationName + "\" not found in the registry.");
            return 0;
        }
    }
}
