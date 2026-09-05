// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Bakes a <see cref="CutsceneAsset"/> and its scene-bound cast into one <see cref="CutsceneStage"/>
    /// entity (amendment A61): the asset baked to a <see cref="CutsceneBlob"/>, plus a
    /// <see cref="CutsceneStageBinding"/> per bound slot, ready for a host to hand to
    /// <c>CutscenePlaybackApi.CreatePlayRequestFromStage</c>.
    /// </summary>
    [AddComponentMenu("DOTS Animation Toolkit/Cutscene Stage")]
    [DisallowMultipleComponent]
    public sealed class CutsceneStageAuthoring : MonoBehaviour
    {
        /// <summary>The cutscene this stage bakes. An unassigned cutscene bakes nothing.</summary>
        public CutsceneAsset cutscene;

        /// <summary>Which live scene object plays each of <see cref="cutscene"/>'s slots.</summary>
        public List<CutsceneStageSlotBinding> bindings = new List<CutsceneStageSlotBinding>();
    }

    /// <summary>One slot's scene binding, authored by the cast panel's Sync to Stage action (amendment A61).</summary>
    [Serializable]
    public sealed class CutsceneStageSlotBinding
    {
        /// <summary>The bound <see cref="CutsceneSlot.SlotId"/> — never the slot's name or list index.</summary>
        public uint slotId;

        /// <summary>The actor root (Actor slot) or transform-only object (Prop slot) that plays this slot.</summary>
        public GameObject target;
    }

    /// <summary>Bakes a <see cref="CutsceneStageAuthoring"/> into a <see cref="CutsceneStage"/> entity plus its <see cref="CutsceneStageBinding"/> buffer.</summary>
    /// <remarks>
    /// <strong>A baker's <c>GetEntity(GameObject, TransformUsageFlags)</c> only resolves GameObjects
    /// baked in the same subscene as this component.</strong> A binding whose target lives in another
    /// scene bakes that entry to <c>Entity.Null</c> — the host must supply that binding at play time
    /// instead (the game's own runtime-spawned-unit override path does this).
    /// </remarks>
    public sealed class CutsceneStageBaker : Baker<CutsceneStageAuthoring>
    {
        public override void Bake(CutsceneStageAuthoring authoring)
        {
            if (authoring.cutscene == null)
            {
                // An unconfigured stage bakes to nothing, the same SocketAttachmentBaker rule and
                // reason: a stage entity with no blob would read as broken cutscene playback rather
                // than as an unfinished prefab.
                return;
            }

            DependsOn(authoring.cutscene);
            DependOnEverySlotAsset(authoring.cutscene);

            List<string> validationWarnings = new List<string>();
            BlobAssetReference<CutsceneBlob> cutsceneBlob;
            CutsceneBlobBuilder.Build(authoring.cutscene, out cutsceneBlob, validationWarnings);
            for (int warningIndex = 0; warningIndex < validationWarnings.Count; warningIndex++)
            {
                Debug.LogWarning("[DOTS Animation Toolkit] " + validationWarnings[warningIndex], authoring);
            }

            Unity.Entities.Hash128 blobHash;
            AddBlobAsset(ref cutsceneBlob, out blobHash);

            Entity stageEntity = GetEntity(TransformUsageFlags.None);
            AddComponent(stageEntity, new CutsceneStage
            {
                blob = cutsceneBlob,
                cutsceneKey = authoring.cutscene.StableId
            });

            DynamicBuffer<CutsceneStageBinding> stageBindings = AddBuffer<CutsceneStageBinding>(stageEntity);
            AddEveryBinding(authoring, stageBindings);
        }

        private void AddEveryBinding(
            CutsceneStageAuthoring authoring, DynamicBuffer<CutsceneStageBinding> stageBindings)
        {
            if (authoring.bindings == null)
            {
                return;
            }
            for (int bindingIndex = 0; bindingIndex < authoring.bindings.Count; bindingIndex++)
            {
                CutsceneStageSlotBinding binding = authoring.bindings[bindingIndex];
                if (binding == null || binding.target == null)
                {
                    continue;
                }
                if (!SlotExists(authoring.cutscene, binding.slotId))
                {
                    Debug.LogWarning(
                        "[DOTS Animation Toolkit] Cutscene Stage '" + authoring.name + "' binds slot id "
                        + binding.slotId + ", which cutscene '" + authoring.cutscene.name
                        + "' does not declare. Skipped.",
                        authoring);
                    continue;
                }
                stageBindings.Add(new CutsceneStageBinding
                {
                    slotId = binding.slotId,
                    target = GetEntity(binding.target, TransformUsageFlags.Dynamic)
                });
            }
        }

        private static bool SlotExists(CutsceneAsset cutscene, uint slotId)
        {
            if (cutscene.slots == null)
            {
                return false;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot != null && slot.SlotId == slotId)
                {
                    return true;
                }
            }
            return false;
        }

        private void DependOnEverySlotAsset(CutsceneAsset cutscene)
        {
            if (cutscene.slots == null)
            {
                return;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                if (slot.rig != null)
                {
                    DependsOn(slot.rig);
                }
                if (slot.clipSets == null)
                {
                    continue;
                }
                for (int clipSetIndex = 0; clipSetIndex < slot.clipSets.Count; clipSetIndex++)
                {
                    ClipSetAsset clipSet = slot.clipSets[clipSetIndex];
                    if (clipSet != null)
                    {
                        DependsOn(clipSet);
                    }
                }
            }
        }
    }
}
