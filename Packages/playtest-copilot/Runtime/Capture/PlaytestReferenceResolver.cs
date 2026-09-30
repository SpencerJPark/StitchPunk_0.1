using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PlaytestCopilot
{
    /// Turns "change this" into a ranked list of candidate GameObjects. Six independent signals
    /// each vote for objects they believe are on screen; agreement across signals, not cleverness
    /// in any one of them, is what makes the ranking trustworthy.
    public static class PlaytestReferenceResolver
    {
        [Serializable]
        public sealed class ResolveRequest
        {
            public List<PlaytestScreenRegion> CircledRegions = new List<PlaytestScreenRegion>();
            public Camera CaptureCamera;
            public GameObject ObjectUnderCursor;
            public GameObject EditorSelection;
            public GameObject ObjectAtCameraCenter;
            public GameObject LastInteractedObject;
            public string TranscriptText = string.Empty;
            public Transform PlayerTransform;
            public float NearbyRadius = 20f;
            public int MaxResults = 5;
        }

        // Tracks, per candidate GameObject, the distinct signal types that named it and the
        // strongest base confidence among them (the source that "wins" the ResolvedBy label).
        private sealed class CandidateAgreement
        {
            public float HighestBaseConfidence;
            public PlaytestReferenceSource WinningSource;
            public readonly List<PlaytestReferenceSource> AgreeingSources = new List<PlaytestReferenceSource>();
        }

        private const int CircledRegionSampleGridSize = 5;
        private const float MaximumCombinedConfidence = 0.98f;
        private const float AgreementBonusPerExtraSource = 0.05f;

        private static readonly char[] TranscriptWordSeparators =
        {
            ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\''
        };

        private static readonly string[] TranscriptStopwords =
        {
            "the", "this", "that", "with", "and", "for", "its", "was", "are", "too", "make", "feels"
        };

        public static List<PlaytestObjectReference> Resolve(ResolveRequest request)
        {
            List<PlaytestObjectReference> resolvedReferences = new List<PlaytestObjectReference>();
            if (request == null || request.CaptureCamera == null)
            {
                return resolvedReferences;
            }

            Dictionary<GameObject, CandidateAgreement> candidatesByGameObject = new Dictionary<GameObject, CandidateAgreement>();

            CollectCircledRegionCandidates(request, candidatesByGameObject);
            RegisterDirectCandidate(candidatesByGameObject, request.ObjectUnderCursor, PlaytestReferenceSource.PointerUnderCursor);
            RegisterDirectCandidate(candidatesByGameObject, request.EditorSelection, PlaytestReferenceSource.EditorSelection);
            RegisterDirectCandidate(candidatesByGameObject, request.ObjectAtCameraCenter, PlaytestReferenceSource.NearCameraCenter);
            RegisterDirectCandidate(candidatesByGameObject, request.LastInteractedObject, PlaytestReferenceSource.RecentInteraction);
            CollectTranscriptNameCandidates(request, candidatesByGameObject);

            if (candidatesByGameObject.Count == 0)
            {
                return resolvedReferences;
            }

            foreach (KeyValuePair<GameObject, CandidateAgreement> candidateEntry in candidatesByGameObject)
            {
                CandidateAgreement agreement = candidateEntry.Value;
                float agreementBonus = AgreementBonusPerExtraSource * (agreement.AgreeingSources.Count - 1);
                float combinedConfidence = Mathf.Min(agreement.HighestBaseConfidence + agreementBonus, MaximumCombinedConfidence);

                PlaytestObjectReference reference = Describe(candidateEntry.Key, agreement.WinningSource, combinedConfidence);
                reference.ResolvedBy = BuildResolvedByLabel(agreement);
                resolvedReferences.Add(reference);
            }

            resolvedReferences.Sort((PlaytestObjectReference left, PlaytestObjectReference right) =>
                right.Confidence.CompareTo(left.Confidence));

            int maxResultsToKeep = request.MaxResults > 0 ? request.MaxResults : resolvedReferences.Count;
            if (resolvedReferences.Count > maxResultsToKeep)
            {
                resolvedReferences.RemoveRange(maxResultsToKeep, resolvedReferences.Count - maxResultsToKeep);
            }

            return resolvedReferences;
        }

        public static PlaytestObjectReference Describe(GameObject gameObject, PlaytestReferenceSource source, float confidence)
        {
            PlaytestObjectReference reference = new PlaytestObjectReference();
            reference.ObjectName = gameObject.name;
            reference.HierarchyPath = BuildHierarchyPath(gameObject);
            reference.PrefabAssetPath = string.Empty;
#if UNITY_EDITOR
            reference.PrefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject) ?? string.Empty;
#endif
            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                reference.ComponentTypeNames.Add(component.GetType().Name);
            }

            reference.Confidence = confidence;
            reference.ResolvedBy = source.ToString();
            return reference;
        }

        public static string BuildHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            List<string> pathSegmentsFromRootToLeaf = new List<string>();
            Transform currentTransform = gameObject.transform;
            while (currentTransform != null)
            {
                pathSegmentsFromRootToLeaf.Add(currentTransform.name);
                currentTransform = currentTransform.parent;
            }

            pathSegmentsFromRootToLeaf.Reverse();
            return string.Join("/", pathSegmentsFromRootToLeaf);
        }

        public static float ConfidenceFor(PlaytestReferenceSource source)
        {
            switch (source)
            {
                case PlaytestReferenceSource.CircledRegion:
                    return 0.92f;
                case PlaytestReferenceSource.PointerUnderCursor:
                    return 0.80f;
                case PlaytestReferenceSource.EditorSelection:
                    return 0.70f;
                case PlaytestReferenceSource.TranscriptName:
                    return 0.60f;
                case PlaytestReferenceSource.NearCameraCenter:
                    return 0.45f;
                case PlaytestReferenceSource.RecentInteraction:
                    return 0.40f;
                default:
                    return 0f;
            }
        }

        // Fires a 5x5 lattice of rays per circled region instead of one per pixel, then folds every
        // 3D and 2D hit into the same agreement map CircledRegion uses for every other candidate.
        private static void CollectCircledRegionCandidates(
            ResolveRequest request, Dictionary<GameObject, CandidateAgreement> candidatesByGameObject)
        {
            if (request.CircledRegions == null)
            {
                return;
            }

            foreach (PlaytestScreenRegion circledRegion in request.CircledRegions)
            {
                for (int rowIndex = 0; rowIndex < CircledRegionSampleGridSize; rowIndex++)
                {
                    float verticalFraction = (float)rowIndex / (CircledRegionSampleGridSize - 1);
                    float sampleScreenY = circledRegion.ScreenRect.y + verticalFraction * circledRegion.ScreenRect.height;

                    for (int columnIndex = 0; columnIndex < CircledRegionSampleGridSize; columnIndex++)
                    {
                        float horizontalFraction = (float)columnIndex / (CircledRegionSampleGridSize - 1);
                        float sampleScreenX = circledRegion.ScreenRect.x + horizontalFraction * circledRegion.ScreenRect.width;

                        Ray sampleRay = request.CaptureCamera.ScreenPointToRay(new Vector3(sampleScreenX, sampleScreenY, 0f));

                        if (Physics.Raycast(sampleRay, out RaycastHit hit3D))
                        {
                            RegisterAgreement(candidatesByGameObject, hit3D.collider.gameObject, PlaytestReferenceSource.CircledRegion);
                        }

                        RaycastHit2D hit2D = Physics2D.GetRayIntersection(sampleRay);
                        if (hit2D.collider != null)
                        {
                            RegisterAgreement(candidatesByGameObject, hit2D.collider.gameObject, PlaytestReferenceSource.CircledRegion);
                        }
                    }
                }
            }
        }

        // Matches remaining transcript words against the same neighbourhood the state backend
        // samples (Physics.OverlapSphere / Physics2D.OverlapCircleAll), never the whole scene.
        private static void CollectTranscriptNameCandidates(
            ResolveRequest request, Dictionary<GameObject, CandidateAgreement> candidatesByGameObject)
        {
            if (string.IsNullOrEmpty(request.TranscriptText))
            {
                return;
            }

            List<string> transcriptKeywords = ExtractTranscriptKeywords(request.TranscriptText);
            if (transcriptKeywords.Count == 0)
            {
                return;
            }

            Vector3 nearbySearchOrigin = request.PlayerTransform != null
                ? request.PlayerTransform.position
                : request.CaptureCamera.transform.position;
            float nearbySearchRadius = request.NearbyRadius > 0f ? request.NearbyRadius : 20f;

            HashSet<GameObject> nearbyGameObjects = new HashSet<GameObject>();

            Collider[] nearbyColliders3D = Physics.OverlapSphere(nearbySearchOrigin, nearbySearchRadius);
            foreach (Collider nearbyCollider3D in nearbyColliders3D)
            {
                nearbyGameObjects.Add(nearbyCollider3D.gameObject);
            }

            Collider2D[] nearbyColliders2D = Physics2D.OverlapCircleAll(nearbySearchOrigin, nearbySearchRadius);
            foreach (Collider2D nearbyCollider2D in nearbyColliders2D)
            {
                nearbyGameObjects.Add(nearbyCollider2D.gameObject);
            }

            foreach (GameObject nearbyGameObject in nearbyGameObjects)
            {
                if (GameObjectMatchesAnyTranscriptKeyword(nearbyGameObject, transcriptKeywords))
                {
                    RegisterAgreement(candidatesByGameObject, nearbyGameObject, PlaytestReferenceSource.TranscriptName);
                }
            }
        }

        private static List<string> ExtractTranscriptKeywords(string transcriptText)
        {
            List<string> transcriptKeywords = new List<string>();
            string[] rawTranscriptWords = transcriptText.Split(TranscriptWordSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawTranscriptWord in rawTranscriptWords)
            {
                if (rawTranscriptWord.Length < 3)
                {
                    continue;
                }

                if (Array.IndexOf(TranscriptStopwords, rawTranscriptWord.ToLowerInvariant()) >= 0)
                {
                    continue;
                }

                transcriptKeywords.Add(rawTranscriptWord);
            }

            return transcriptKeywords;
        }

        private static bool GameObjectMatchesAnyTranscriptKeyword(GameObject candidateGameObject, List<string> transcriptKeywords)
        {
            string candidateTag = candidateGameObject.tag;
            Component[] candidateComponents = candidateGameObject.GetComponents<Component>();

            foreach (string transcriptKeyword in transcriptKeywords)
            {
                if (candidateGameObject.name.IndexOf(transcriptKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(candidateTag)
                    && candidateTag.IndexOf(transcriptKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                foreach (Component candidateComponent in candidateComponents)
                {
                    if (candidateComponent == null)
                    {
                        continue;
                    }

                    if (candidateComponent.GetType().Name.IndexOf(transcriptKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RegisterDirectCandidate(
            Dictionary<GameObject, CandidateAgreement> candidatesByGameObject,
            GameObject candidateGameObject,
            PlaytestReferenceSource source)
        {
            if (candidateGameObject == null)
            {
                return;
            }

            RegisterAgreement(candidatesByGameObject, candidateGameObject, source);
        }

        private static void RegisterAgreement(
            Dictionary<GameObject, CandidateAgreement> candidatesByGameObject,
            GameObject candidateGameObject,
            PlaytestReferenceSource source)
        {
            if (!candidatesByGameObject.TryGetValue(candidateGameObject, out CandidateAgreement agreement))
            {
                agreement = new CandidateAgreement();
                candidatesByGameObject[candidateGameObject] = agreement;
            }

            if (!agreement.AgreeingSources.Contains(source))
            {
                agreement.AgreeingSources.Add(source);
            }

            float baseConfidenceForSource = ConfidenceFor(source);
            if (baseConfidenceForSource > agreement.HighestBaseConfidence)
            {
                agreement.HighestBaseConfidence = baseConfidenceForSource;
                agreement.WinningSource = source;
            }
        }

        private static string BuildResolvedByLabel(CandidateAgreement agreement)
        {
            List<PlaytestReferenceSource> extraAgreeingSources = new List<PlaytestReferenceSource>();
            foreach (PlaytestReferenceSource agreeingSource in agreement.AgreeingSources)
            {
                if (agreeingSource != agreement.WinningSource)
                {
                    extraAgreeingSources.Add(agreeingSource);
                }
            }

            if (extraAgreeingSources.Count == 0)
            {
                return agreement.WinningSource.ToString();
            }

            extraAgreeingSources.Sort((PlaytestReferenceSource left, PlaytestReferenceSource right) =>
                ConfidenceFor(right).CompareTo(ConfidenceFor(left)));

            StringBuilder resolvedByLabelBuilder = new StringBuilder(agreement.WinningSource.ToString());
            foreach (PlaytestReferenceSource extraSource in extraAgreeingSources)
            {
                resolvedByLabelBuilder.Append("+");
                resolvedByLabelBuilder.Append(extraSource.ToString());
            }

            return resolvedByLabelBuilder.ToString();
        }
    }
}
