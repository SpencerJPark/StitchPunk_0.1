#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public struct TerrainAreaFillResult
{
    public int interactableCount;
    public int cardCount;
    public int demotedCount;
}

// Fills forest-style areas with real props near the edge and billboard cards deeper inside.
public static class TerrainAreaFiller
{
    public const string CardsRootName = "Cards";
    public const string CardMeshPrefix = "Cards_";
    // camera yaw the cards face; WF-S1 confirms the value
    public const float CardCameraYawDegrees = 0f;

    private const string ScatterChildName = "Scatter";
    private const string NavigationWallLayerName = "PathfindingWalls";
    private static readonly Vector2 FallbackCardSizeMetres = new Vector2(3f, 5f);

    private struct EdgeSortEntry
    {
        public int placementIndex;
        public float edgeDistanceMetres;
    }

    public static void SplitTiers(
        IReadOnlyList<TerrainScatterPlacement> placements,
        IReadOnlyList<float> edgeDistanceMetres,
        IReadOnlyList<TerrainScatterTier> ruleTiers,
        float interiorDemotionMetres,
        int interactableCapRemaining,
        List<int> interactableIndicesOut,
        List<int> cardIndicesOut,
        out int demotedCount)
    {
        interactableIndicesOut.Clear();
        cardIndicesOut.Clear();
        demotedCount = 0;
        int cap = Mathf.Max(0, interactableCapRemaining);

        List<EdgeSortEntry> interactableCandidates = new List<EdgeSortEntry>();
        for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
        {
            if (ruleTiers[placements[placementIndex].ruleIndex] == TerrainScatterTier.Dressing)
            {
                cardIndicesOut.Add(placementIndex);
                continue;
            }
            EdgeSortEntry entry = new EdgeSortEntry();
            entry.placementIndex = placementIndex;
            entry.edgeDistanceMetres = edgeDistanceMetres[placementIndex];
            interactableCandidates.Add(entry);
        }

        interactableCandidates.Sort((EdgeSortEntry left, EdgeSortEntry right) =>
        {
            int byDistance = left.edgeDistanceMetres.CompareTo(right.edgeDistanceMetres);
            return byDistance != 0 ? byDistance : left.placementIndex.CompareTo(right.placementIndex);
        });

        for (int candidateIndex = 0; candidateIndex < interactableCandidates.Count; candidateIndex++)
        {
            EdgeSortEntry candidate = interactableCandidates[candidateIndex];
            bool tooDeep = candidate.edgeDistanceMetres > interiorDemotionMetres;
            if (tooDeep || interactableIndicesOut.Count >= cap)
            {
                cardIndicesOut.Add(candidate.placementIndex);
                demotedCount++;
            }
            else
            {
                interactableIndicesOut.Add(candidate.placementIndex);
            }
        }
    }

    public static TerrainAreaFillResult FillTile(TerrainTile tile, IReadOnlyList<TerrainAreaStamp> areas)
    {
        TerrainAreaFillResult result = new TerrainAreaFillResult();
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return result;
        }

        Vector2Int coordinate = tile.tileCoordinate;
        List<TerrainCardAtlas> atlasOrder = new List<TerrainCardAtlas>();
        Dictionary<TerrainCardAtlas, List<TerrainCard>> cardsByAtlas = new Dictionary<TerrainCardAtlas, List<TerrainCard>>();

        using (TerrainGeneratedLockWatcher.SuppressLocking())
        {
            Transform scatterParent = GetOrCreateScatterParent(tile);
            List<GameObject> doomed = new List<GameObject>();
            for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
            {
                Transform child = scatterParent.GetChild(childIndex);
                TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
                if (marker != null && !string.IsNullOrEmpty(marker.sourceFeatureId) && !marker.locked)
                {
                    doomed.Add(child.gameObject);
                }
            }
            for (int doomedIndex = 0; doomedIndex < doomed.Count; doomedIndex++)
            {
                Object.DestroyImmediate(doomed[doomedIndex]);
            }

            int interactableUsed = 0;
            List<TerrainScatterObstacle> obstacles = new List<TerrainScatterObstacle>();
            for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
            {
                Transform child = scatterParent.GetChild(childIndex);
                TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
                if (marker == null)
                {
                    continue;
                }
                interactableUsed++;
                if (marker.locked)
                {
                    TerrainScatterObstacle obstacle = new TerrainScatterObstacle();
                    obstacle.position = child.position;
                    obstacle.radius = marker.footprintRadiusMetres;
                    obstacles.Add(obstacle);
                }
            }
            int capRemaining = TerrainPlatformProfile.Active.maxInteractablePropsPerTile - interactableUsed;

            int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;
            byte[] ownerAreaIndex = new byte[resolution * resolution];
            byte[] edgeDistanceTexels = new byte[resolution * resolution];
            TerrainAreaRasterizer.RasteriseAreaMask(areas, coordinate, resolution, ownerAreaIndex, edgeDistanceTexels);

            List<int> stampOrder = new List<int>();
            for (int stampIndex = 0; stampIndex < areas.Count; stampIndex++)
            {
                stampOrder.Add(stampIndex);
            }
            stampOrder.Sort((int left, int right) => areas[left].areaIndex.CompareTo(areas[right].areaIndex));

            int navigationLayer = -1;
            bool navigationLayerResolved = false;
            for (int orderIndex = 0; orderIndex < stampOrder.Count; orderIndex++)
            {
                TerrainAreaStamp stamp = areas[stampOrder[orderIndex]];
                int entryIndex = stamp.areaIndex - 1;
                if (tileData.areaEntries == null || entryIndex < 0 || entryIndex >= tileData.areaEntries.Count)
                {
                    continue;
                }
                TerrainTileAreaEntry entry = tileData.areaEntries[entryIndex];
                TerrainForestRecipe recipe = entry.recipe as TerrainForestRecipe;
                if (recipe == null || recipe.scatterSet == null)
                {
                    continue;
                }

                TerrainScatterSet scatterSet = recipe.scatterSet;
                List<TerrainScatterRule> rules = TerrainScatterGenerator.RulesFrom(scatterSet);
                TerrainTileSampler tileSampler = new TerrainTileSampler(tile, true);
                TerrainAreaRestrictedSampler sampler = new TerrainAreaRestrictedSampler(tileSampler, stamp, recipe.clearingNoiseScaleMetres, recipe.clearingThreshold01);
                List<TerrainScatterPlacement> planned = TerrainScatterPlanner.Plan(rules, sampler, tileSampler.WorldRect, TerrainDecalScatterer.SeedFor(entry.seed, coordinate), obstacles);

                Rect tileRect = tileSampler.WorldRect;
                List<TerrainScatterPlacement> owned = new List<TerrainScatterPlacement>();
                List<float> edgeDistances = new List<float>();
                for (int plannedIndex = 0; plannedIndex < planned.Count; plannedIndex++)
                {
                    TerrainScatterPlacement placement = planned[plannedIndex];
                    int texelX = Mathf.Clamp(Mathf.FloorToInt((placement.position.x - tileRect.xMin) / tileRect.width * resolution), 0, resolution - 1);
                    int texelZ = Mathf.Clamp(Mathf.FloorToInt((placement.position.z - tileRect.yMin) / tileRect.height * resolution), 0, resolution - 1);
                    if (ownerAreaIndex[texelZ * resolution + texelX] != stamp.areaIndex)
                    {
                        continue;
                    }
                    owned.Add(placement);
                    edgeDistances.Add(sampler.EdgeDistanceMetresAt(placement.position.x, placement.position.z));
                }

                List<TerrainScatterTier> ruleTiers = new List<TerrainScatterTier>();
                for (int ruleIndex = 0; ruleIndex < scatterSet.rules.Count; ruleIndex++)
                {
                    ruleTiers.Add(scatterSet.rules[ruleIndex].tier);
                }

                List<int> interactableIndices = new List<int>();
                List<int> cardIndices = new List<int>();
                int demoted;
                SplitTiers(owned, edgeDistances, ruleTiers, recipe.interiorDemotionMetres, capRemaining, interactableIndices, cardIndices, out demoted);
                result.demotedCount += demoted;

                for (int listIndex = 0; listIndex < interactableIndices.Count; listIndex++)
                {
                    int placementIndex = interactableIndices[listIndex];
                    TerrainScatterPlacement placement = owned[placementIndex];
                    System.Random pickRandom = new System.Random(unchecked(entry.seed * 73856093) ^ (placementIndex * 19349663));
                    GameObject prefab = scatterSet.PickPrefab(placement.ruleIndex, (float)pickRandom.NextDouble());
                    if (prefab == null)
                    {
                        continue;
                    }
                    TerrainScatterRuleData ruleData = scatterSet.rules[placement.ruleIndex];
                    GameObject instance = InstantiateScatterInstance(scatterParent, prefab, placement, ruleData);
                    if (instance == null)
                    {
                        continue;
                    }
                    instance.GetComponent<TerrainScatterMarker>().sourceFeatureId = entry.featureId;
                    if (ruleData.blocksNavigation)
                    {
                        if (!navigationLayerResolved)
                        {
                            navigationLayerResolved = true;
                            navigationLayer = LayerMask.NameToLayer(NavigationWallLayerName);
                        }
                        if (navigationLayer >= 0)
                        {
                            SetLayerRecursively(instance, navigationLayer);
                        }
                    }
                    result.interactableCount++;
                    capRemaining--;
                }

                TerrainCardAtlas atlas = recipe.cardAtlas;
                if (atlas == null || atlas.spriteUvRects == null || atlas.spriteUvRects.Count == 0)
                {
                    continue;
                }
                List<TerrainCard> atlasCards;
                if (!cardsByAtlas.TryGetValue(atlas, out atlasCards))
                {
                    atlasCards = new List<TerrainCard>();
                    cardsByAtlas[atlas] = atlasCards;
                    atlasOrder.Add(atlas);
                }
                for (int listIndex = 0; listIndex < cardIndices.Count; listIndex++)
                {
                    TerrainScatterPlacement placement = owned[cardIndices[listIndex]];
                    TerrainScatterRuleData ruleData = scatterSet.rules[placement.ruleIndex];
                    Vector2 baseSize = ruleData.cardSizeMetres;
                    if (baseSize.x <= 0f || baseSize.y <= 0f)
                    {
                        baseSize = FallbackCardSizeMetres;
                    }
                    TerrainCard card = new TerrainCard();
                    card.basePosition = new Vector3(
                        placement.position.x,
                        tileData.SampleBilinear(placement.position.x, placement.position.z) + TerrainTileData.GroundTopHeightMetres,
                        placement.position.z);
                    card.sizeMetres = baseSize * placement.scale;
                    int uvIndex = ((ruleData.cardAtlasEntry % atlas.spriteUvRects.Count) + atlas.spriteUvRects.Count) % atlas.spriteUvRects.Count;
                    card.uvRect = atlas.spriteUvRects[uvIndex];
                    card.tint01 = TintFromPosition(placement.position);
                    atlasCards.Add(card);
                    result.cardCount++;
                }
            }

            BakeCardMeshes(tile, tileData, atlasOrder, cardsByAtlas);
        }
        return result;
    }

    private static void BakeCardMeshes(TerrainTile tile, TerrainTileData tileData, List<TerrainCardAtlas> atlasOrder, Dictionary<TerrainCardAtlas, List<TerrainCard>> cardsByAtlas)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        Vector3 meshOrigin = new Vector3(coordinate.x * TerrainTileData.TileSizeMetres, 0f, coordinate.y * TerrainTileData.TileSizeMetres);

        string dataPath = TerrainTileAssets.TileDataPath(coordinate);
        Dictionary<string, Mesh> existingMeshes = new Dictionary<string, Mesh>();
        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(dataPath))
        {
            Mesh existingMesh = subAsset as Mesh;
            if (existingMesh != null && existingMesh.name.StartsWith(CardMeshPrefix))
            {
                existingMeshes[existingMesh.name] = existingMesh;
            }
        }

        Transform cardsRoot = tile.transform.Find(CardsRootName);
        bool anythingChanged = false;
        HashSet<string> usedNames = new HashSet<string>();
        int meshNumber = 0;

        for (int atlasIndex = 0; atlasIndex < atlasOrder.Count; atlasIndex++)
        {
            TerrainCardAtlas atlas = atlasOrder[atlasIndex];
            List<TerrainCard> allCards = cardsByAtlas[atlas];
            for (int chunkStart = 0; chunkStart < allCards.Count; chunkStart += TerrainCardBatchMeshBuilder.MaxCardsPerMesh)
            {
                int chunkCount = Mathf.Min(TerrainCardBatchMeshBuilder.MaxCardsPerMesh, allCards.Count - chunkStart);
                List<TerrainCard> chunk = allCards.GetRange(chunkStart, chunkCount);
                int vertexCount;
                Mesh mesh = TerrainCardBatchMeshBuilder.Build(chunk, meshOrigin, CardCameraYawDegrees, out vertexCount);
                if (mesh == null)
                {
                    continue;
                }

                string meshName = CardMeshPrefix + meshNumber;
                meshNumber++;
                usedNames.Add(meshName);
                anythingChanged = true;

                Mesh persistedMesh;
                if (existingMeshes.TryGetValue(meshName, out persistedMesh))
                {
                    EditorUtility.CopySerialized(mesh, persistedMesh);
                    persistedMesh.name = meshName;
                    Object.DestroyImmediate(mesh);
                }
                else
                {
                    persistedMesh = mesh;
                    persistedMesh.name = meshName;
                    AssetDatabase.AddObjectToAsset(persistedMesh, tileData);
                }

                if (cardsRoot == null)
                {
                    GameObject cardsRootObject = new GameObject(CardsRootName);
                    cardsRootObject.transform.SetParent(tile.transform, false);
                    cardsRoot = cardsRootObject.transform;
                }

                Transform child = cardsRoot.Find(meshName);
                if (child == null)
                {
                    GameObject childObject = new GameObject(meshName, typeof(MeshFilter), typeof(MeshRenderer));
                    childObject.transform.SetParent(cardsRoot, false);
                    child = childObject.transform;
                }
                child.position = meshOrigin;
                child.GetComponent<MeshFilter>().sharedMesh = persistedMesh;
                MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = atlas.material;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
                meshRenderer.receiveShadows = true;
            }
        }

        foreach (KeyValuePair<string, Mesh> existing in existingMeshes)
        {
            if (!usedNames.Contains(existing.Key) && existing.Value.vertexCount > 0)
            {
                existing.Value.Clear();
                anythingChanged = true;
            }
        }

        if (cardsRoot != null)
        {
            List<GameObject> obsoleteChildren = new List<GameObject>();
            foreach (Transform child in cardsRoot)
            {
                if (!usedNames.Contains(child.name))
                {
                    obsoleteChildren.Add(child.gameObject);
                }
            }
            foreach (GameObject obsoleteChild in obsoleteChildren)
            {
                Object.DestroyImmediate(obsoleteChild);
                anythingChanged = true;
            }

            if (cardsRoot.childCount == 0)
            {
                Object.DestroyImmediate(cardsRoot.gameObject);
            }
        }

        if (anythingChanged)
        {
            EditorUtility.SetDirty(tileData);
        }
    }

    private static float TintFromPosition(Vector3 position)
    {
        uint hash = unchecked((uint)(Mathf.RoundToInt(position.x * 100f) * 73856093) ^ (uint)(Mathf.RoundToInt(position.z * 100f) * 19349663));
        hash ^= hash >> 13;
        hash = unchecked(hash * 1274126177u);
        hash ^= hash >> 16;
        return (hash & 0xFFFFu) / 65535f;
    }

    // Same construction as TerrainScatterGenerator's private helper so area props match hand-scattered ones.
    private static GameObject InstantiateScatterInstance(Transform scatterParent, GameObject prefab, TerrainScatterPlacement placement, TerrainScatterRuleData ruleData)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scatterParent.gameObject.scene) as GameObject;
        if (instance == null)
        {
            return null;
        }
        Vector3 prefabScale = prefab.transform.localScale;
        instance.transform.SetParent(scatterParent, true);
        Quaternion yaw = Quaternion.Euler(0f, placement.yawDegrees, 0f);
        Quaternion rotation = ruleData.alignToNormal ? Quaternion.FromToRotation(Vector3.up, placement.normal) * yaw : yaw;
        instance.transform.SetPositionAndRotation(placement.position, rotation);
        instance.transform.localScale = prefabScale * placement.scale;
        instance.name = prefab.name;

        TerrainScatterMarker marker = instance.AddComponent<TerrainScatterMarker>();
        marker.locked = false;
        marker.ruleIndex = placement.ruleIndex;
        marker.footprintRadiusMetres = ruleData.minSpacingMetres * 0.5f;
        return instance;
    }

    private static Transform GetOrCreateScatterParent(TerrainTile tile)
    {
        Transform existing = tile.transform.Find(ScatterChildName);
        if (existing != null)
        {
            return existing;
        }
        GameObject created = new GameObject(ScatterChildName);
        created.transform.SetParent(tile.transform, false);
        return created.transform;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int childIndex = 0; childIndex < root.transform.childCount; childIndex++)
        {
            SetLayerRecursively(root.transform.GetChild(childIndex).gameObject, layer);
        }
    }
}
#endif
