// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Finds, creates and writes the project's <see cref="AnimEventRoutingAsset"/>, the write path behind the Events tab's routes column.</summary>
    public static class AnimEventRoutingAssetUtility
    {
        // The one project folder the package may name (Conformance_D), beside the generated vocabulary constants.
        public const string DefaultAssetPath = "Assets/Generated/DotsAnimationToolkit/AnimEventRouting.asset";

        public static event Action RoutingChanged;

        public static AnimEventRoutingAsset FindDefault()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimEventRoutingAsset", new string[] { "Assets" });
            if (guids.Length == 0)
            {
                return null;
            }

            string[] assetPaths = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
            string chosenPath = assetPaths.Contains(DefaultAssetPath)
                ? DefaultAssetPath
                : assetPaths.OrderBy(path => path, StringComparer.Ordinal).First();

            return AssetDatabase.LoadAssetAtPath<AnimEventRoutingAsset>(chosenPath);
        }

        public static AnimEventRoutingAsset GetOrCreateDefault()
        {
            AnimEventRoutingAsset existingAsset = FindDefault();
            if (existingAsset != null)
            {
                return existingAsset;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Generated"))
            {
                AssetDatabase.CreateFolder("Assets", "Generated");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Generated/DotsAnimationToolkit"))
            {
                AssetDatabase.CreateFolder("Assets/Generated", "DotsAnimationToolkit");
            }

            AnimEventRoutingAsset newAsset = ScriptableObject.CreateInstance<AnimEventRoutingAsset>();
            AssetDatabase.CreateAsset(newAsset, DefaultAssetPath);

            RoutingChanged?.Invoke();
            return newAsset;
        }

        public static List<AnimEventRoute> RoutesForKey(AnimEventRoutingAsset routingAsset, uint eventKey)
        {
            if (routingAsset == null || routingAsset.routes == null)
            {
                return new List<AnimEventRoute>();
            }

            return routingAsset.routes.Where(route => route != null && route.eventKey == eventKey).ToList();
        }

        public static AnimEventRoute AddRoute(AnimEventRoutingAsset routingAsset, uint eventKey)
        {
            if (routingAsset == null || eventKey == 0)
            {
                return null;
            }

            Undo.RecordObject(routingAsset, "Add Event Route");

            AnimEventRoute newRoute = new AnimEventRoute { eventKey = eventKey, kind = AnimEventRouteKind.Sound };
            routingAsset.routes.Add(newRoute);

            Persist(routingAsset);
            return newRoute;
        }

        public static bool RemoveRoute(AnimEventRoutingAsset routingAsset, AnimEventRoute route)
        {
            if (routingAsset == null || routingAsset.routes == null || route == null)
            {
                return false;
            }

            Undo.RecordObject(routingAsset, "Remove Event Route");

            bool wasRemoved = routingAsset.routes.Remove(route);
            if (wasRemoved)
            {
                Persist(routingAsset);
            }

            return wasRemoved;
        }

        public static void Persist(AnimEventRoutingAsset routingAsset)
        {
            if (routingAsset == null)
            {
                return;
            }

            EditorUtility.SetDirty(routingAsset);
            // Never AssetDatabase.SaveAssets here: it flushes every unsaved asset the user has open.
            AssetDatabase.SaveAssetIfDirty(routingAsset);
            RoutingChanged?.Invoke();
        }
    }
}
