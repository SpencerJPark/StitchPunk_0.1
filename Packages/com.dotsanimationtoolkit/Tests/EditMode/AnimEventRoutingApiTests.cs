// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class AnimEventRoutingApiTests
    {
        [Test]
        public void TryGetRoutes_FindsEachKeysRangeAndRejectsAnUnroutedKey()
        {
            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            BlobAssetReference<AnimEventRoutingBlob> routingBlobReference;
            try
            {
                ref AnimEventRoutingBlob routingRoot = ref blobBuilder.ConstructRoot<AnimEventRoutingBlob>();

                BlobBuilderArray<AnimEventRouteBlob> routeArray = blobBuilder.Allocate(ref routingRoot.routes, 3);
                routeArray[0] = new AnimEventRouteBlob { eventKey = 16, kind = AnimEventRouteKind.Sound, routeId = 1 };
                routeArray[1] = new AnimEventRouteBlob { eventKey = 16, kind = AnimEventRouteKind.Vfx, routeId = 2 };
                routeArray[2] = new AnimEventRouteBlob { eventKey = 20, kind = AnimEventRouteKind.Sound, routeId = 3 };

                BlobBuilderArray<uint> keyArray = blobBuilder.Allocate(ref routingRoot.keys, 2);
                keyArray[0] = 16;
                keyArray[1] = 20;

                BlobBuilderArray<int> keyStartArray = blobBuilder.Allocate(ref routingRoot.keyStarts, 3);
                keyStartArray[0] = 0;
                keyStartArray[1] = 2;
                keyStartArray[2] = 3;

                routingBlobReference = blobBuilder.CreateBlobAssetReference<AnimEventRoutingBlob>(Allocator.Persistent);
            }
            finally
            {
                blobBuilder.Dispose();
            }

            try
            {
                ref AnimEventRoutingBlob routingBlob = ref routingBlobReference.Value;

                bool firstKeyFound = AnimEventRoutingApi.TryGetRoutes(ref routingBlob, 16, out int firstRouteStart, out int firstRouteCount);
                Assert.IsTrue(firstKeyFound);
                Assert.AreEqual(0, firstRouteStart);
                Assert.AreEqual(2, firstRouteCount);

                bool secondKeyFound = AnimEventRoutingApi.TryGetRoutes(ref routingBlob, 20, out int secondRouteStart, out int secondRouteCount);
                Assert.IsTrue(secondKeyFound);
                Assert.AreEqual(2, secondRouteStart);
                Assert.AreEqual(1, secondRouteCount);

                bool unroutedKeyFound = AnimEventRoutingApi.TryGetRoutes(ref routingBlob, 17, out int unroutedRouteStart, out int unroutedRouteCount);
                Assert.IsFalse(unroutedKeyFound);
                Assert.AreEqual(0, unroutedRouteStart);
                Assert.AreEqual(0, unroutedRouteCount);
            }
            finally
            {
                routingBlobReference.Dispose();
            }
        }
    }
}
