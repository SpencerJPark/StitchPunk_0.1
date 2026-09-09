// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Rig-to-skinned-mesh resolution and throwaway-instance behaviour of <see cref="VatBakeSourceResolver"/>.</summary>
    public sealed class VatBakeSourceResolverTests
    {
        private RigAsset rig;
        private List<GameObject> spawnedRoots;

        [SetUp]
        public void SetUp()
        {
            rig = ScriptableObject.CreateInstance<RigAsset>();
            spawnedRoots = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int rootIndex = 0; rootIndex < spawnedRoots.Count; rootIndex++)
            {
                if (spawnedRoots[rootIndex] != null)
                {
                    Object.DestroyImmediate(spawnedRoots[rootIndex]);
                }
            }
            Object.DestroyImmediate(rig);
        }

        private GameObject CreateRoot(string name)
        {
            GameObject root = new GameObject(name);
            spawnedRoots.Add(root);
            return root;
        }

        private static GameObject CreateChildWithSkinnedMesh(GameObject parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            child.AddComponent<SkinnedMeshRenderer>();
            return child;
        }

        private RigTargetDefinition AddTarget(string sourceNodePath, string displayName, TargetKind kind)
        {
            RigTargetDefinition target = new RigTargetDefinition
            {
                sourceNodePath = sourceNodePath,
                displayName = displayName,
                kind = kind,
            };
            rig.targets.Add(target);
            rig.EnsureStableIds();
            return target;
        }

        [Test]
        public void TryResolve_OneSkinnedMeshAndNoTargets_ResolvesItUntargeted()
        {
            GameObject root = CreateRoot("Root");
            CreateChildWithSkinnedMesh(root, "TentacleMesh");
            rig.sourcePrefab = root;

            bool result = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> sources, out string failureMessage);

            Assert.IsTrue(result);
            Assert.AreEqual(1, sources.Count);
            Assert.AreEqual(0u, sources[0].TargetId);
            Assert.AreEqual("TentacleMesh", sources[0].SourceNodePath);
        }

        [Test]
        public void TryResolve_TwoTargetedSkinnedMeshes_ReturnsBothInRigTargetOrder()
        {
            GameObject root = CreateRoot("Root");
            CreateChildWithSkinnedMesh(root, "MeshA");
            CreateChildWithSkinnedMesh(root, "MeshB");
            rig.sourcePrefab = root;
            RigTargetDefinition firstTarget = AddTarget("MeshB", "Mesh B", TargetKind.Quad);
            RigTargetDefinition secondTarget = AddTarget("MeshA", "Mesh A", TargetKind.Quad);

            bool result = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> sources, out string failureMessage);

            Assert.IsTrue(result);
            Assert.AreEqual(2, sources.Count);
            Assert.AreEqual(firstTarget.Id.Value, sources[0].TargetId);
            Assert.AreEqual(secondTarget.Id.Value, sources[1].TargetId);
        }

        [Test]
        public void TryResolve_TargetOnANodeWithNoSkinnedMesh_IsNotASource()
        {
            GameObject root = CreateRoot("Root");
            GameObject plainNode = new GameObject("PlainNode");
            plainNode.transform.SetParent(root.transform);
            plainNode.AddComponent<MeshRenderer>();
            CreateChildWithSkinnedMesh(root, "TentacleMesh");
            rig.sourcePrefab = root;
            AddTarget("PlainNode", "Plain Node", TargetKind.Quad);

            bool result = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> sources, out string failureMessage);

            Assert.IsTrue(result);
            Assert.AreEqual(1, sources.Count);
            Assert.AreEqual(0u, sources[0].TargetId);
            Assert.AreEqual("TentacleMesh", sources[0].SourceNodePath);
        }

        [Test]
        public void TryResolve_TwoSkinnedMeshesAndNoTarget_RefusesAndNamesBoth()
        {
            GameObject root = CreateRoot("Root");
            CreateChildWithSkinnedMesh(root, "MeshA");
            CreateChildWithSkinnedMesh(root, "MeshB");
            rig.sourcePrefab = root;

            bool result = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> sources, out string failureMessage);

            Assert.IsFalse(result);
            StringAssert.Contains("MeshA", failureMessage);
            StringAssert.Contains("MeshB", failureMessage);
        }

        [Test]
        public void TryResolve_PrefersVatMeshTargets_OverTargetsThatMerelyCarryAMesh()
        {
            GameObject root = CreateRoot("Root");
            CreateChildWithSkinnedMesh(root, "MeshA");
            CreateChildWithSkinnedMesh(root, "MeshB");
            rig.sourcePrefab = root;
            AddTarget("MeshA", "Mesh A", TargetKind.Quad);
            RigTargetDefinition vatMeshTarget = AddTarget("MeshB", "Mesh B", TargetKind.VatMesh);

            bool firstResult = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> firstSources, out string firstFailureMessage);

            Assert.IsTrue(firstResult);
            Assert.AreEqual(1, firstSources.Count);
            Assert.AreEqual(vatMeshTarget.Id.Value, firstSources[0].TargetId);

            vatMeshTarget.kind = TargetKind.Quad;

            bool secondResult = VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> secondSources, out string secondFailureMessage);

            Assert.IsTrue(secondResult);
            Assert.AreEqual(2, secondSources.Count);
        }

        [Test]
        public void TryCreateBakeInstance_MakesAHiddenCopy_AndFindInInstanceHitsTheSameNode()
        {
            GameObject root = CreateRoot("Root");
            CreateChildWithSkinnedMesh(root, "TentacleMesh");
            rig.sourcePrefab = root;

            bool result = VatBakeSourceResolver.TryCreateBakeInstance(rig, out GameObject instanceRoot, out string failureMessage);

            Assert.IsTrue(result);
            Assert.AreNotEqual(root, instanceRoot);
            Assert.AreEqual(HideFlags.HideAndDontSave, instanceRoot.hideFlags);

            SkinnedMeshRenderer foundRenderer = VatBakeSourceResolver.FindInInstance(instanceRoot, "TentacleMesh");

            Assert.IsNotNull(foundRenderer);
            Assert.AreEqual(instanceRoot.transform, foundRenderer.transform.parent);

            Object.DestroyImmediate(instanceRoot);
        }
    }
}
