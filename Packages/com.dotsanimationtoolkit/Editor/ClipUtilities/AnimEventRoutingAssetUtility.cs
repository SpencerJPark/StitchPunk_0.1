// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Finds, creates and writes the project's <see cref="AnimEventRoutingAsset"/>, the write path behind the Events tab's routes column.</summary>
    public static class AnimEventRoutingAssetUtility
    {
        public const string DefaultAssetPath = "Assets/Settings/DotsAnimationToolkit/AnimEventRouting.asset";

        public static event Action RoutingChanged;

        // STUB (A93-T1): T5 replaces every body below.
        public static AnimEventRoutingAsset FindDefault()
        {
            return null;
        }

        public static AnimEventRoutingAsset GetOrCreateDefault()
        {
            return null;
        }

        public static List<AnimEventRoute> RoutesForKey(AnimEventRoutingAsset routingAsset, uint eventKey)
        {
            return new List<AnimEventRoute>();
        }

        public static AnimEventRoute AddRoute(AnimEventRoutingAsset routingAsset, uint eventKey)
        {
            return null;
        }

        public static bool RemoveRoute(AnimEventRoutingAsset routingAsset, AnimEventRoute route)
        {
            return false;
        }

        public static void Persist(AnimEventRoutingAsset routingAsset)
        {
            RoutingChanged?.Invoke();
        }
    }
}
