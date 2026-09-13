// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Pins <see cref="VatSourceHashResolver"/>'s verdict against a rig-structure edit and a clip-set edit.</summary>
    public sealed class VatSourceHashResolverTests
    {
        private readonly List<Object> spawnedAssets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int assetIndex = 0; assetIndex < spawnedAssets.Count; assetIndex++)
            {
                if (spawnedAssets[assetIndex] != null)
                {
                    Object.DestroyImmediate(spawnedAssets[assetIndex]);
                }
            }
            spawnedAssets.Clear();
        }

        [Test]
        public void ChangingAPartKind_ChangesTheHash_AndReportsRigChanged()
        {
            RigAsset rig = CreateRig();
            ClipSetAsset clipSet = CreateClipSet(CreateVatBoundClip());
            VatTextureSetAsset textures = CreateBakedTextures(clipSet, rig);

            string freshReason;
            VatBakeFreshness freshVerdict = VatSourceHashResolver.Resolve(clipSet, rig, textures, out freshReason);
            Assert.AreEqual(VatBakeFreshness.Fresh, freshVerdict);

            ulong rigStructureHashBeforeEdit = VatSourceHashResolver.ComputeRigStructureHash(rig, textures.flavor);
            rig.targets[0].kind = TargetKind.VatMesh;
            ulong rigStructureHashAfterEdit = VatSourceHashResolver.ComputeRigStructureHash(rig, textures.flavor);
            Assert.AreNotEqual(rigStructureHashBeforeEdit, rigStructureHashAfterEdit);

            string staleReason;
            VatBakeFreshness staleVerdict = VatSourceHashResolver.Resolve(clipSet, rig, textures, out staleReason);
            Assert.AreEqual(VatBakeFreshness.Stale, staleVerdict);
            StringAssert.Contains("Rig changed", staleReason);
        }

        [Test]
        public void AddingAVatBoundClip_ReportsClipsChanged()
        {
            RigAsset rig = CreateRig();
            ClipSetAsset clipSet = CreateClipSet(CreateVatBoundClip());
            VatTextureSetAsset textures = CreateBakedTextures(clipSet, rig);

            string freshReason;
            VatBakeFreshness freshVerdict = VatSourceHashResolver.Resolve(clipSet, rig, textures, out freshReason);
            Assert.AreEqual(VatBakeFreshness.Fresh, freshVerdict);

            clipSet.clips.Add(CreateVatBoundClip());

            string staleReason;
            VatBakeFreshness staleVerdict = VatSourceHashResolver.Resolve(clipSet, rig, textures, out staleReason);
            Assert.AreEqual(VatBakeFreshness.Stale, staleVerdict);
            StringAssert.Contains("Clips changed", staleReason);
        }

        private RigAsset CreateRig()
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            spawnedAssets.Add(rig);
            rig.targets.Add(new RigTargetDefinition
            {
                sourceNodePath = "Body",
                kind = TargetKind.Quad
            });
            rig.EnsureStableIds();
            return rig;
        }

        private ClipAsset CreateVatBoundClip()
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            spawnedAssets.Add(clip);
            BoneTrack boneTrack = new BoneTrack
            {
                boneName = "Spine"
            };
            boneTrack.keys.Add(new BoneKey());
            clip.boneTracks.Add(boneTrack);
            return clip;
        }

        private ClipSetAsset CreateClipSet(params ClipAsset[] clips)
        {
            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            spawnedAssets.Add(clipSet);
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                clipSet.clips.Add(clips[clipIndex]);
            }
            return clipSet;
        }

        private VatTextureSetAsset CreateBakedTextures(ClipSetAsset clipSet, RigAsset rig)
        {
            VatTextureSetAsset textures = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedAssets.Add(textures);
            textures.flavor = VatFlavor.BoneMatrix;
            textures.sourceRigKey = rig.StableId;
            textures.sourceHash = VatSourceHashResolver.ComputeSourceHash(clipSet, rig, textures.flavor);
            textures.sourceRigStructureHash = VatSourceHashResolver.ComputeRigStructureHash(rig, textures.flavor);
            return textures;
        }
    }
}
