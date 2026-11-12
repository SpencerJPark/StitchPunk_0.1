using System.Collections.Generic;
using NUnit.Framework;
using PaintedGround;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainScattererTests
    {
        private sealed class OpenFlatSampler : ITerrainScatterSampler
        {
            public float dirt = 0f;
            public float grass = 1f;
            public float forest = 0f;
            public float stone = 0f;

            public float HeightAt(float worldX, float worldZ)
            {
                return 0f;
            }

            public Vector3 NormalAt(float worldX, float worldZ)
            {
                return Vector3.up;
            }

            public bool TryGetBiome(float worldX, float worldZ, out float outDirt, out float outGrass, out float outForest, out float outStone)
            {
                outDirt = dirt;
                outGrass = grass;
                outForest = forest;
                outStone = stone;
                return true;
            }
        }

        private const string TempFolder = "Assets/Terrain";
        private const string TempPrefabPath = "Assets/Terrain/TEMP_TestProp.prefab";
        private const float PropSpacingMetres = 3f;
        private static readonly Rect SixtyMetreRect = new Rect(0f, 0f, 60f, 60f);

        private Scene testScene;
        private GameObject rootObject;
        private Transform decalParent;
        private Transform scatterParent;
        private TerrainDecalSet decalSet;
        private TerrainScatterSet scatterSet;
        private Texture2D atlasTexture;
        private Material multiplyMaterial;
        private Material normalMaterial;
        private OpenFlatSampler sampler;

        [SetUp]
        public void SetUp()
        {
            // Preview scene: the test runner leaves an unsaved untitled scene open, which makes an additive NewScene throw
            testScene = EditorSceneManager.NewPreviewScene();
            rootObject = new GameObject("TerrainScattererTestsRoot");
            SceneManager.MoveGameObjectToScene(rootObject, testScene);
            decalParent = new GameObject("Decals").transform;
            decalParent.SetParent(rootObject.transform, false);
            scatterParent = new GameObject("Scatter").transform;
            scatterParent.SetParent(rootObject.transform, false);
            sampler = new OpenFlatSampler();

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            multiplyMaterial = new Material(shader);
            normalMaterial = new Material(shader);
            atlasTexture = new Texture2D(4, 4);

            decalSet = ScriptableObject.CreateInstance<TerrainDecalSet>();
            decalSet.atlas = atlasTexture;
            decalSet.atlasGrid = new Vector2Int(4, 2);
            decalSet.multiplyMaterial = multiplyMaterial;
            decalSet.normalMaterial = normalMaterial;
            decalSet.FillWithDefaultRules();
            for (int entryIndex = 0; entryIndex < decalSet.entries.Count; entryIndex++)
            {
                TerrainDecalEntry entry = decalSet.entries[entryIndex];
                entry.densityPer100SquareMetres *= 4f;
                decalSet.entries[entryIndex] = entry;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
            Object.DestroyImmediate(decalSet);
            Object.DestroyImmediate(scatterSet);
            Object.DestroyImmediate(multiplyMaterial);
            Object.DestroyImmediate(normalMaterial);
            Object.DestroyImmediate(atlasTexture);
            AssetDatabase.DeleteAsset(TempPrefabPath);
        }

        private TerrainScatterSet BuildScatterSet(bool blocksNavigation)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Terrain");
            }
            GameObject sourceObject = new GameObject("TestProp");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(sourceObject, TempPrefabPath);
            Object.DestroyImmediate(sourceObject);

            TerrainScatterRuleData rule = TerrainScatterSet.CreateRule("TestProp");
            rule.prefabs.Add(new TerrainScatterPrefab { prefab = prefab, weight = 1f });
            rule.onDirt = 1f;
            rule.onGrass = 1f;
            rule.onForest = 1f;
            rule.onStone = 1f;
            rule.densityPer100SquareMetres = 3f;
            rule.minSpacingMetres = PropSpacingMetres;
            rule.blocksNavigation = blocksNavigation;

            scatterSet = ScriptableObject.CreateInstance<TerrainScatterSet>();
            scatterSet.rules.Add(rule);
            return scatterSet;
        }

        private static List<Vector3> ChildPositions(Transform parent)
        {
            List<Vector3> positions = new List<Vector3>();
            for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
            {
                positions.Add(parent.GetChild(childIndex).position);
            }
            return positions;
        }

        private static float DistanceXZ(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(new Vector2(first.x, first.z), new Vector2(second.x, second.z));
        }

        [Test]
        public void DecalScatter_CreatesMarkedDecalsUnderTheParent()
        {
            int created = TerrainDecalScatterer.ScatterWithSampler(decalParent, decalSet, sampler, SixtyMetreRect, 11);

            Assert.Greater(created, 0);
            Assert.AreEqual(created, decalParent.childCount);
            for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
            {
                Transform child = decalParent.GetChild(childIndex);
                TerrainDecalMarker marker = child.GetComponent<TerrainDecalMarker>();
                Assert.IsNotNull(marker);
                Assert.IsFalse(marker.locked);
                Assert.IsNotNull(child.GetComponent<PaintedDecal>());
            }
        }

        [Test]
        public void DecalScatter_SameSeedOnAFreshParent_GivesTheSamePositions()
        {
            Transform otherParent = new GameObject("DecalsOther").transform;
            SceneManager.MoveGameObjectToScene(otherParent.gameObject, testScene);
            otherParent.SetParent(rootObject.transform, false);

            TerrainDecalScatterer.ScatterWithSampler(decalParent, decalSet, sampler, SixtyMetreRect, 21);
            TerrainDecalScatterer.ScatterWithSampler(otherParent, decalSet, sampler, SixtyMetreRect, 21);

            List<Vector3> first = ChildPositions(decalParent);
            List<Vector3> second = ChildPositions(otherParent);
            Assert.Greater(first.Count, 0);
            Assert.AreEqual(first.Count, second.Count);
            for (int positionIndex = 0; positionIndex < first.Count; positionIndex++)
            {
                Assert.AreEqual(0f, Vector3.Distance(first[positionIndex], second[positionIndex]), 1e-4f);
            }
        }

        [Test]
        public void DecalClearUnlocked_NeverRemovesLockedDecals()
        {
            TerrainDecalScatterer.ScatterWithSampler(decalParent, decalSet, sampler, SixtyMetreRect, 31);
            int total = decalParent.childCount;
            Assert.Greater(total, 5);
            List<GameObject> lockedObjects = new List<GameObject>();
            for (int childIndex = 0; childIndex < 5; childIndex++)
            {
                Transform child = decalParent.GetChild(childIndex);
                child.GetComponent<TerrainDecalMarker>().locked = true;
                lockedObjects.Add(child.gameObject);
            }

            int removed = TerrainDecalScatterer.ClearUnlocked(decalParent);

            Assert.AreEqual(total - 5, removed);
            Assert.AreEqual(5, decalParent.childCount);
            foreach (GameObject lockedObject in lockedObjects)
            {
                Assert.IsTrue(lockedObject != null);
            }
        }

        [Test]
        public void DecalRegenerate_KeepsLockedDecals_AndNewOnesAvoidThem()
        {
            TerrainDecalScatterer.ScatterWithSampler(decalParent, decalSet, sampler, SixtyMetreRect, 41);
            int lockedCount = TerrainDecalScatterer.LockAll(decalParent);
            Assert.Greater(lockedCount, 0);
            List<Transform> lockedDecals = new List<Transform>();
            for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
            {
                lockedDecals.Add(decalParent.GetChild(childIndex));
            }

            TerrainDecalScatterer.ScatterWithSampler(decalParent, decalSet, sampler, SixtyMetreRect, 42);

            foreach (Transform lockedDecal in lockedDecals)
            {
                Assert.IsTrue(lockedDecal != null);
            }
            Assert.Greater(decalParent.childCount, lockedCount);
            for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
            {
                Transform child = decalParent.GetChild(childIndex);
                if (child.GetComponent<TerrainDecalMarker>().locked)
                {
                    continue;
                }
                foreach (Transform lockedDecal in lockedDecals)
                {
                    float halfScale = lockedDecal.localScale.x * 0.5f;
                    Assert.GreaterOrEqual(DistanceXZ(child.position, lockedDecal.position), halfScale - 1e-3f);
                }
            }
        }

        [Test]
        public void PropGenerate_ThroughTileSampler_PlantsNothingInsideAnArea()
        {
            const int maskResolution = 16;
            TerrainScatterSet set = BuildScatterSet(false);
            TerrainTileData tileData = ScriptableObject.CreateInstance<TerrainTileData>();
            try
            {
                tileData.coordinate = Vector2Int.zero;
                tileData.EnsureHeightArrays();
                Color32[] grassComposite = new Color32[maskResolution * maskResolution];
                Color32[] areaMask = new Color32[maskResolution * maskResolution];
                for (int texelIndex = 0; texelIndex < grassComposite.Length; texelIndex++)
                {
                    grassComposite[texelIndex] = new Color32(255, 0, 0, 255);
                    bool westHalf = texelIndex % maskResolution < maskResolution / 2;
                    areaMask[texelIndex] = westHalf ? new Color32(1, 0, 0, 255) : new Color32(0, 0, 0, 0);
                }
                TerrainTileSampler tileSampler = new TerrainTileSampler(tileData, grassComposite, null, areaMask, maskResolution, false);

                int created = TerrainScatterGenerator.GenerateWithSampler(scatterParent, set, tileSampler, tileSampler.WorldRect, 81);

                Assert.Greater(created, 0);
                foreach (Vector3 propPosition in ChildPositions(scatterParent))
                {
                    Assert.GreaterOrEqual(propPosition.x, 0f, "a prop was planted on area-owned ground (west half of tile 0,0)");
                }
            }
            finally
            {
                Object.DestroyImmediate(tileData);
            }
        }

        [Test]
        public void PropGenerate_CreatesInstancesWithMarkers_AndRespectsSpacing()
        {
            TerrainScatterSet set = BuildScatterSet(false);

            int created = TerrainScatterGenerator.GenerateWithSampler(scatterParent, set, sampler, SixtyMetreRect, 51);

            Assert.Greater(created, 1);
            Assert.AreEqual(created, scatterParent.childCount);
            for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
            {
                Transform child = scatterParent.GetChild(childIndex);
                Assert.IsNotNull(child.GetComponent<TerrainScatterMarker>());
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(child.gameObject));
                for (int otherIndex = childIndex + 1; otherIndex < scatterParent.childCount; otherIndex++)
                {
                    float distance = DistanceXZ(child.position, scatterParent.GetChild(otherIndex).position);
                    Assert.GreaterOrEqual(distance, PropSpacingMetres - 1e-3f);
                }
            }
        }

        [Test]
        public void PropGenerate_DressingTierRule_NeverInstantiatesPrefabs()
        {
            TerrainScatterSet set = BuildScatterSet(false);
            TerrainScatterRuleData dressingRule = set.rules[0];
            dressingRule.tier = TerrainScatterTier.Dressing;
            set.rules[0] = dressingRule;

            int created = TerrainScatterGenerator.GenerateWithSampler(scatterParent, set, sampler, SixtyMetreRect, 51);

            Assert.AreEqual(0, created);
            Assert.AreEqual(0, scatterParent.childCount);
        }

        [Test]
        public void PropClearUnlocked_NeverRemovesLockedProps()
        {
            TerrainScatterSet set = BuildScatterSet(false);
            TerrainScatterGenerator.GenerateWithSampler(scatterParent, set, sampler, SixtyMetreRect, 61);
            int total = scatterParent.childCount;
            Assert.Greater(total, 5);
            List<GameObject> lockedObjects = new List<GameObject>();
            for (int childIndex = 0; childIndex < 5; childIndex++)
            {
                Transform child = scatterParent.GetChild(childIndex);
                child.GetComponent<TerrainScatterMarker>().locked = true;
                lockedObjects.Add(child.gameObject);
            }

            int removed = TerrainScatterGenerator.ClearUnlocked(scatterParent);

            Assert.AreEqual(total - 5, removed);
            Assert.AreEqual(5, scatterParent.childCount);
            foreach (GameObject lockedObject in lockedObjects)
            {
                Assert.IsTrue(lockedObject != null);
            }
        }

        [Test]
        public void PropBlocksNavigation_PutsInstancesOnTheWallsLayerWhenItExists()
        {
            int wallsLayer = LayerMask.NameToLayer("PathfindingWalls");
            if (wallsLayer < 0)
            {
                Assert.Ignore("No 'PathfindingWalls' layer in this project.");
            }
            TerrainScatterSet set = BuildScatterSet(true);

            int created = TerrainScatterGenerator.GenerateWithSampler(scatterParent, set, sampler, SixtyMetreRect, 71);

            Assert.Greater(created, 0);
            for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
            {
                Assert.AreEqual(wallsLayer, scatterParent.GetChild(childIndex).gameObject.layer);
            }
        }
    }
}
