// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One thing the pointer is over, and how far away it is.</summary>
    public struct PreviewPickHit
    {
        /// <summary>The transform to select. Never the hierarchy root unless the root was hit.</summary>
        public Transform pickedTransform;

        /// <summary>Distance along the ray, for ordering.</summary>
        public float distance;

        /// <summary>Whether this came from a bone handle rather than from geometry.</summary>
        public bool isBoneHandle;

        /// <summary>
        /// False when the hit is against a renderer's bounding box rather than its actual surface.
        /// </summary>
        public bool isExact;
    }

    /// <summary>
    /// Hit-tests the clip viewport's preview scene against a ray built from the pointer.
    /// <c>Physics.Raycast</c> does not work against a <c>PreviewRenderUtility</c> scene (it is
    /// never simulated), so colliders are walked one at a time via <see cref="Collider.Raycast"/>.
    /// Bone handles are ordered ahead of geometry, not merged with it by distance, since a bone
    /// sits inside the mesh it drives.
    /// </summary>
    public static class PreviewScenePicker
    {
        private const float MaximumPickDistance = 1000f;

        // Builds a world-space ray through a viewport point, where (0,0) is bottom-left and (1,1)
        // top-right. Composed from the camera's transform and field of view rather than through
        // Camera.ViewportPointToRay, since a preview camera's projection means nothing between renders.
        public static Ray BuildRay(
            Transform cameraTransform, float verticalFieldOfView, float aspect, Vector2 viewportPoint)
        {
            float tangentOfHalfFieldOfView = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            float cameraSpaceX = (viewportPoint.x * 2f - 1f) * tangentOfHalfFieldOfView * aspect;
            float cameraSpaceY = (viewportPoint.y * 2f - 1f) * tangentOfHalfFieldOfView;

            Vector3 directionInCameraSpace = new Vector3(cameraSpaceX, cameraSpaceY, 1f);
            return new Ray(
                cameraTransform.position, cameraTransform.TransformDirection(directionInCameraSpace));
        }

        /// <summary>
        /// Fills <paramref name="hits"/> with everything under the ray, nearest first within each
        /// group, bone handles ahead of geometry, one entry per transform.
        /// </summary>
        public static void CollectHits(
            Transform hierarchyRoot, IReadOnlyList<Transform> boneHandles, float boneHandleRadius,
            Ray ray, List<PreviewPickHit> hits)
        {
            hits.Clear();
            if (hierarchyRoot == null)
            {
                return;
            }

            CollectBoneHandleHits(boneHandles, boneHandleRadius, ray, hits);

            // Deduplicated before ordering, keeping the most precise hit per transform — sorting
            // first could let the approximate bounding-box hit win a float-noise-close tie.
            List<PreviewPickHit> geometryHits = new List<PreviewPickHit>();
            CollectRendererHits(hierarchyRoot, ray, geometryHits);
            CollectColliderHits(hierarchyRoot, ray, geometryHits);
            geometryHits.Sort(CompareByDistance);

            for (int hitIndex = 0; hitIndex < geometryHits.Count; hitIndex++)
            {
                if (!ContainsTransform(hits, geometryHits[hitIndex].pickedTransform))
                {
                    hits.Add(geometryHits[hitIndex]);
                }
            }
        }

        // Records a hit, replacing any existing hit on the same transform when it is better. Exact
        // always beats approximate, whatever the distances say.
        private static void OfferHit(List<PreviewPickHit> geometryHits, PreviewPickHit candidate)
        {
            for (int hitIndex = 0; hitIndex < geometryHits.Count; hitIndex++)
            {
                PreviewPickHit existing = geometryHits[hitIndex];
                if (existing.pickedTransform != candidate.pickedTransform)
                {
                    continue;
                }

                bool candidateIsBetter = candidate.isExact != existing.isExact
                    ? candidate.isExact
                    : candidate.distance < existing.distance;
                if (candidateIsBetter)
                {
                    geometryHits[hitIndex] = candidate;
                }
                return;
            }
            geometryHits.Add(candidate);
        }

        private static void CollectBoneHandleHits(
            IReadOnlyList<Transform> boneHandles, float boneHandleRadius, Ray ray,
            List<PreviewPickHit> hits)
        {
            if (boneHandles == null || boneHandleRadius <= 0f)
            {
                return;
            }

            for (int boneIndex = 0; boneIndex < boneHandles.Count; boneIndex++)
            {
                Transform bone = boneHandles[boneIndex];
                if (bone == null)
                {
                    continue;
                }

                float distance;
                if (!TryIntersectSphere(ray, bone.position, boneHandleRadius, out distance))
                {
                    continue;
                }
                hits.Add(new PreviewPickHit
                {
                    pickedTransform = bone,
                    distance = distance,
                    isBoneHandle = true,
                    isExact = true
                });
            }

            hits.Sort(CompareByDistance);
        }

        private static void CollectRendererHits(
            Transform hierarchyRoot, Ray ray, List<PreviewPickHit> geometryHits)
        {
            Renderer[] renderers = hierarchyRoot.GetComponentsInChildren<Renderer>(false);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];

                // You cannot click what you cannot see. GetComponentsInChildren(false) already skips
                // inactive objects; this skips a renderer switched off on an active one.
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                float distance;
                if (!renderer.bounds.IntersectRay(ray, out distance))
                {
                    continue;
                }
                OfferHit(geometryHits, new PreviewPickHit
                {
                    pickedTransform = renderer.transform,
                    distance = distance,
                    isBoneHandle = false,
                    isExact = false
                });
            }
        }

        private static void CollectColliderHits(
            Transform hierarchyRoot, Ray ray, List<PreviewPickHit> geometryHits)
        {
            Collider[] colliders = hierarchyRoot.GetComponentsInChildren<Collider>(false);
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                RaycastHit colliderHit;
                if (!collider.Raycast(ray, out colliderHit, MaximumPickDistance))
                {
                    continue;
                }
                OfferHit(geometryHits, new PreviewPickHit
                {
                    pickedTransform = collider.transform,
                    distance = colliderHit.distance,
                    isBoneHandle = false,
                    isExact = true
                });
            }
        }

        /// <summary>
        /// Ray against a sphere. Returns the near intersection, or the far one when the ray starts
        /// inside — otherwise a handle the camera has zoomed into would stop being clickable.
        /// </summary>
        private static bool TryIntersectSphere(Ray ray, Vector3 center, float radius, out float distance)
        {
            distance = 0f;

            Vector3 originToCenter = center - ray.origin;
            float projectionLength = Vector3.Dot(originToCenter, ray.direction);
            float squaredDistanceToAxis =
                originToCenter.sqrMagnitude - projectionLength * projectionLength;
            float squaredRadius = radius * radius;
            if (squaredDistanceToAxis > squaredRadius)
            {
                return false;
            }

            float halfChordLength = Mathf.Sqrt(squaredRadius - squaredDistanceToAxis);
            float nearDistance = projectionLength - halfChordLength;
            float farDistance = projectionLength + halfChordLength;
            if (farDistance < 0f)
            {
                return false;
            }

            distance = nearDistance >= 0f ? nearDistance : farDistance;
            return true;
        }

        private static bool ContainsTransform(List<PreviewPickHit> hits, Transform candidate)
        {
            for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
            {
                if (hits[hitIndex].pickedTransform == candidate)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CompareByDistance(PreviewPickHit first, PreviewPickHit second)
        {
            return first.distance.CompareTo(second.distance);
        }
    }
}
