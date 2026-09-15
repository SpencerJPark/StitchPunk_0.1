// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Disk round-trip coverage for MaterialTemplateUtility.TryAssignToTargetRenderer.</summary>
    public sealed class MaterialTemplateAssignTests
    {
        private const string TestsFolderPath = "Packages/com.dotsanimationtoolkit/Tests/EditMode";
        private string scratchFolderPath;
        private GameObject sceneRootGameObject;
        private RigAsset rigAsset;

        [SetUp]
        public void SetUp()
        {
            string scratchFolderName = "MaterialAssignScratch_" + Guid.NewGuid().ToString("N");
            string createdFolderGuid = AssetDatabase.CreateFolder(TestsFolderPath, scratchFolderName);
            Assert.IsFalse(string.IsNullOrEmpty(createdFolderGuid), "Failed to create the scratch folder.");
            scratchFolderPath = AssetDatabase.GUIDToAssetPath(createdFolderGuid);
        }

        [TearDown]
        public void TearDown()
        {
            if (sceneRootGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(sceneRootGameObject);
                sceneRootGameObject = null;
            }

            if (rigAsset != null)
            {
                ScriptableObject.DestroyImmediate(rigAsset);
                rigAsset = null;
            }

            if (!string.IsNullOrEmpty(scratchFolderPath) && AssetDatabase.IsValidFolder(scratchFolderPath))
            {
                AssetDatabase.DeleteAsset(scratchFolderPath);
            }
            scratchFolderPath = null;
            AssetDatabase.Refresh();
        }

        [Test]
        public void AssignToSingleSlotRenderer_ReplacesMaterialInPrefabAsset()
        {
            sceneRootGameObject = new GameObject("AssignRoot");
            GameObject childGameObject = new GameObject("BaseHead");
            childGameObject.transform.SetParent(sceneRootGameObject.transform);
            MeshRenderer childRenderer = childGameObject.AddComponent<MeshRenderer>();

            Material oldMaterial = new Material(Shader.Find("Unlit/Color"));
            oldMaterial.name = "OldPart";
            AssetDatabase.CreateAsset(oldMaterial, scratchFolderPath + "/OldPart.mat");
            childRenderer.sharedMaterial = oldMaterial;

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                sceneRootGameObject, scratchFolderPath + "/AssignRoot.prefab");
            UnityEngine.Object.DestroyImmediate(sceneRootGameObject);
            sceneRootGameObject = null;

            Material newMaterial = new Material(Shader.Find("Unlit/Color"));
            newMaterial.name = "NewPart";
            AssetDatabase.CreateAsset(newMaterial, scratchFolderPath + "/NewPart.mat");

            rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.name = "AssignRig";
            rigAsset.sourcePrefab = savedPrefab;

            RigTargetDefinition target = new RigTargetDefinition();
            target.displayName = "BaseHead";
            target.sourceNodePath = "BaseHead";
            target.kind = TargetKind.Quad;

            bool assigned = MaterialTemplateUtility.TryAssignToTargetRenderer(
                rigAsset, target, newMaterial, null,
                out string assignedDescription, out string failureMessage);
            Assert.IsTrue(assigned, failureMessage);

            GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(scratchFolderPath + "/AssignRoot.prefab");
            Transform reloadedHeadTransform = reloaded.transform.Find("BaseHead");
            Assert.IsNotNull(reloadedHeadTransform, "Reloaded prefab is missing the BaseHead child.");
            MeshRenderer reloadedRenderer = reloadedHeadTransform.GetComponent<MeshRenderer>();
            Material reloadedNewMaterial = AssetDatabase.LoadAssetAtPath<Material>(scratchFolderPath + "/NewPart.mat");
            Assert.AreEqual(reloadedNewMaterial, reloadedRenderer.sharedMaterial);

            StringAssert.Contains("BaseHead", assignedDescription);
            StringAssert.Contains("OldPart.mat", assignedDescription);
        }
    }
}
