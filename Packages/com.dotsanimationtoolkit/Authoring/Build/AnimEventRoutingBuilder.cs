// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>Builds an <see cref="AnimEventRoutingBlob"/> from an <see cref="AnimEventRoutingAsset"/>, sorted so the same routes in any list order bake byte-identical.</summary>
    public static class AnimEventRoutingBuilder
    {
        public static BlobAssetReference<AnimEventRoutingBlob> Build(AnimEventRoutingAsset routingAsset, Allocator allocator)
        {
            List<AnimEventRouteBlob> orderedRoutes = new List<AnimEventRouteBlob>();
            if (routingAsset != null && routingAsset.routes != null)
            {
                for (int routeIndex = 0; routeIndex < routingAsset.routes.Count; routeIndex++)
                {
                    AnimEventRoute route = routingAsset.routes[routeIndex];
                    if (route == null || route.eventKey == 0)
                    {
                        continue;
                    }

                    orderedRoutes.Add(new AnimEventRouteBlob
                    {
                        eventKey = route.eventKey,
                        kind = route.kind,
                        routeId = route.routeId
                    });
                }
            }
            orderedRoutes.Sort(CompareRoutes);

            List<uint> distinctKeys = new List<uint>();
            List<int> routeStartsPerKey = new List<int>();
            for (int routeIndex = 0; routeIndex < orderedRoutes.Count; routeIndex++)
            {
                uint eventKey = orderedRoutes[routeIndex].eventKey;
                if (distinctKeys.Count == 0 || distinctKeys[distinctKeys.Count - 1] != eventKey)
                {
                    distinctKeys.Add(eventKey);
                    routeStartsPerKey.Add(routeIndex);
                }
            }
            routeStartsPerKey.Add(orderedRoutes.Count);

            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref AnimEventRoutingBlob root = ref blobBuilder.ConstructRoot<AnimEventRoutingBlob>();

                BlobBuilderArray<AnimEventRouteBlob> routeArray =
                    blobBuilder.Allocate(ref root.routes, orderedRoutes.Count);
                for (int routeIndex = 0; routeIndex < orderedRoutes.Count; routeIndex++)
                {
                    routeArray[routeIndex] = orderedRoutes[routeIndex];
                }

                BlobBuilderArray<uint> keyArray = blobBuilder.Allocate(ref root.keys, distinctKeys.Count);
                for (int keyIndex = 0; keyIndex < distinctKeys.Count; keyIndex++)
                {
                    keyArray[keyIndex] = distinctKeys[keyIndex];
                }

                BlobBuilderArray<int> keyStartArray =
                    blobBuilder.Allocate(ref root.keyStarts, routeStartsPerKey.Count);
                for (int keyStartIndex = 0; keyStartIndex < routeStartsPerKey.Count; keyStartIndex++)
                {
                    keyStartArray[keyStartIndex] = routeStartsPerKey[keyStartIndex];
                }

                return blobBuilder.CreateBlobAssetReference<AnimEventRoutingBlob>(allocator);
            }
            finally
            {
                blobBuilder.Dispose();
            }
        }

        private static int CompareRoutes(AnimEventRouteBlob leftRoute, AnimEventRouteBlob rightRoute)
        {
            int eventKeyComparison = leftRoute.eventKey.CompareTo(rightRoute.eventKey);
            if (eventKeyComparison != 0)
            {
                return eventKeyComparison;
            }

            int kindComparison = ((byte)leftRoute.kind).CompareTo((byte)rightRoute.kind);
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return leftRoute.routeId.CompareTo(rightRoute.routeId);
        }
    }
}
