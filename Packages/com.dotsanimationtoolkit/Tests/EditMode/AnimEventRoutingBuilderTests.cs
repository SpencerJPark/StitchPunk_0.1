// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Proves <see cref="AnimEventRoutingBuilder.Build"/> sorts routes before baking, so authoring order never affects the output.</summary>
    public sealed class AnimEventRoutingBuilderTests
    {
        [Test]
        public void SameRoutesInDifferentListOrder_BakeIdenticalBlobs()
        {
            AnimEventRoutingAsset forwardAsset = ScriptableObject.CreateInstance<AnimEventRoutingAsset>();
            AnimEventRoutingAsset reversedAsset = ScriptableObject.CreateInstance<AnimEventRoutingAsset>();
            BlobAssetReference<AnimEventRoutingBlob> forwardBlob = default(BlobAssetReference<AnimEventRoutingBlob>);
            BlobAssetReference<AnimEventRoutingBlob> reversedBlob = default(BlobAssetReference<AnimEventRoutingBlob>);
            try
            {
                forwardAsset.routes.Add(CreateRoute(20, AnimEventRouteKind.Sound, 5));
                forwardAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Vfx, 2));
                forwardAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Sound, 9));
                forwardAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Sound, 1));

                reversedAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Sound, 1));
                reversedAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Sound, 9));
                reversedAsset.routes.Add(CreateRoute(16, AnimEventRouteKind.Vfx, 2));
                reversedAsset.routes.Add(CreateRoute(20, AnimEventRouteKind.Sound, 5));

                forwardBlob = AnimEventRoutingBuilder.Build(forwardAsset, Allocator.Persistent);
                reversedBlob = AnimEventRoutingBuilder.Build(reversedAsset, Allocator.Persistent);

                AssertBlobsMatch(forwardBlob, reversedBlob);

                ref AnimEventRoutingBlob builtBlob = ref forwardBlob.Value;
                Assert.AreEqual(2, builtBlob.keys.Length);
                Assert.AreEqual(16u, builtBlob.keys[0]);
                Assert.AreEqual(20u, builtBlob.keys[1]);

                Assert.AreEqual(3, builtBlob.keyStarts.Length);
                Assert.AreEqual(0, builtBlob.keyStarts[0]);
                Assert.AreEqual(3, builtBlob.keyStarts[1]);
                Assert.AreEqual(4, builtBlob.keyStarts[2]);
            }
            finally
            {
                if (forwardBlob.IsCreated)
                {
                    forwardBlob.Dispose();
                }
                if (reversedBlob.IsCreated)
                {
                    reversedBlob.Dispose();
                }
                Object.DestroyImmediate(forwardAsset);
                Object.DestroyImmediate(reversedAsset);
            }
        }

        private static AnimEventRoute CreateRoute(uint eventKey, AnimEventRouteKind kind, uint routeId)
        {
            return new AnimEventRoute
            {
                eventKey = eventKey,
                kind = kind,
                routeId = routeId
            };
        }

        private static void AssertBlobsMatch(
            BlobAssetReference<AnimEventRoutingBlob> firstBlobReference,
            BlobAssetReference<AnimEventRoutingBlob> secondBlobReference)
        {
            ref AnimEventRoutingBlob firstBlob = ref firstBlobReference.Value;
            ref AnimEventRoutingBlob secondBlob = ref secondBlobReference.Value;

            Assert.AreEqual(firstBlob.routes.Length, secondBlob.routes.Length);
            for (int routeIndex = 0; routeIndex < firstBlob.routes.Length; routeIndex++)
            {
                Assert.AreEqual(firstBlob.routes[routeIndex].eventKey, secondBlob.routes[routeIndex].eventKey);
                Assert.AreEqual(firstBlob.routes[routeIndex].kind, secondBlob.routes[routeIndex].kind);
                Assert.AreEqual(firstBlob.routes[routeIndex].routeId, secondBlob.routes[routeIndex].routeId);
            }

            Assert.AreEqual(firstBlob.keys.Length, secondBlob.keys.Length);
            for (int keyIndex = 0; keyIndex < firstBlob.keys.Length; keyIndex++)
            {
                Assert.AreEqual(firstBlob.keys[keyIndex], secondBlob.keys[keyIndex]);
            }

            Assert.AreEqual(firstBlob.keyStarts.Length, secondBlob.keyStarts.Length);
            for (int keyStartIndex = 0; keyStartIndex < firstBlob.keyStarts.Length; keyStartIndex++)
            {
                Assert.AreEqual(firstBlob.keyStarts[keyStartIndex], secondBlob.keyStarts[keyStartIndex]);
            }
        }
    }
}
