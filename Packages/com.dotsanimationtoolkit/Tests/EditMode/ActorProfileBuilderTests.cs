// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <see cref="ActorProfileBuilder"/>'s construction and determinism contract: animations flatten
    /// across layers sorted by key, a directional entry resolves through the facing fold, two builds
    /// of the same profile agree, and a P1-failing profile refuses to build.
    /// </summary>
    public sealed class ActorProfileBuilderTests
    {
        private AuthoringTestAssets assets;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        [Test]
        public void Build_FlattensAnimationsAcrossLayers_SortedAscendingByAnimationKey()
        {
            ClipAsset baseClip = assets.CreateClip("BaseClip", 0xA1UL, 1f);
            ClipAsset overrideClip = assets.CreateClip("OverrideClip", 0xB1UL, 1f);
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            int overrideLayerIndex = profile.layers.Count - 1;
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 50u,
                hasDirections = false,
                clip = baseClip
            });
            profile.layers[overrideLayerIndex].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 10u,
                hasDirections = false,
                clip = overrideClip
            });

            BlobAssetReference<ActorProfileBlob> profileBlob = ActorProfileBuilder.Build(profile, Allocator.Persistent);
            try
            {
                ref ActorProfileBlob profileRoot = ref profileBlob.Value;
                Assert.AreEqual(2, profileRoot.animations.Length, "Both entries across both layers must be baked.");
                Assert.AreEqual(10u, profileRoot.animations[0].animationKey, "The lower key must come first, regardless of layer order.");
                Assert.AreEqual((byte)overrideLayerIndex, profileRoot.animations[0].layerIndex, "The Override entry's layerIndex must be its list position.");
                Assert.AreEqual(overrideClip.Id, profileRoot.animations[0].clip);
                Assert.AreEqual(50u, profileRoot.animations[1].animationKey);
                Assert.AreEqual((byte)0, profileRoot.animations[1].layerIndex, "The Base entry's layerIndex must be 0.");
                Assert.AreEqual(baseClip.Id, profileRoot.animations[1].clip);
            }
            finally
            {
                profileBlob.Dispose();
            }
        }

        [Test]
        public void TryResolve_OnADirectionalEntry_AtAWestSideFacing_ReturnsTheMirroredEastSlotsClip()
        {
            ClipAsset southEastClip = assets.CreateClip("SouthEastClip", 0x10UL, 1f);
            ClipAsset northEastClip = assets.CreateClip("NorthEastClip", 0x20UL, 1f);
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.turnDirections = AnimationDirections.Six;
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 7u,
                hasDirections = true,
                directionSlots = new DirectionSlots { southEast = southEastClip, northEast = northEastClip }
            });

            BlobAssetReference<ActorProfileBlob> profileBlob = ActorProfileBuilder.Build(profile, Allocator.Persistent);
            try
            {
                bool resolved = ActorProfileApi.TryResolve(
                    ref profileBlob.Value, 7u, Direction.NorthWest,
                    out byte layerIndex, out ClipId clip, out int animationIndex);

                Assert.IsTrue(resolved);
                Assert.AreEqual(0, animationIndex);
                Assert.AreEqual((byte)0, layerIndex);
                Assert.AreEqual(
                    northEastClip.Id, clip,
                    "NorthWest is west-side; it must mirror onto the authored NorthEast slot.");
            }
            finally
            {
                profileBlob.Dispose();
            }
        }

        [Test]
        public void ComputeContentHash_IsStableAcrossTwoBuilds_AndChangesWhenAnEntryChanges()
        {
            ClipAsset clip = assets.CreateClip("Clip", 0x30UL, 1f);
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = 1u,
                hasDirections = false,
                clip = clip,
                speed = 1f
            });

            ulong firstHash = ActorProfileBuilder.ComputeContentHash(profile);
            ulong secondHash = ActorProfileBuilder.ComputeContentHash(profile);
            Assert.AreEqual(firstHash, secondHash, "Hashing the same profile twice must produce the same hash.");

            profile.layers[0].animations[0].speed = 2f;
            ulong changedHash = ActorProfileBuilder.ComputeContentHash(profile);
            Assert.AreNotEqual(firstHash, changedHash, "A changed entry must change the content hash.");
        }

        [Test]
        public void Build_RefusesAProfileThatFailsP1()
        {
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.layers = new List<ActorLayerDefinition>
            {
                new ActorLayerDefinition { displayName = "Solo" }
            };

            Assert.Throws<ClipValidationException>(() => ActorProfileBuilder.Build(profile, Allocator.Persistent));
        }
    }
}
