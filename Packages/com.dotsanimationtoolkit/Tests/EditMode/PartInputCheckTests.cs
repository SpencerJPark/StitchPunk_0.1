// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class PartInputCheckTests
    {
        private const string AtlasShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlit.shadergraph";
        private const string ArrayShaderPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitSpriteUnlitArray.shadergraph";

        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }
            createdObjects.Clear();
        }

        private T Track<T>(T created) where T : Object
        {
            createdObjects.Add(created);
            return created;
        }

        [Test]
        public void Evaluate_SliceSpriteTrackOnPartWhoseShaderLacksImageIndex_ReportsMissing()
        {
            Shader atlasShader = AssetDatabase.LoadAssetAtPath<Shader>(AtlasShaderPath);
            Shader arrayShader = AssetDatabase.LoadAssetAtPath<Shader>(ArrayShaderPath);
            Assert.IsNotNull(atlasShader, AtlasShaderPath);
            Assert.IsNotNull(arrayShader, ArrayShaderPath);

            GameObject root = Track(new GameObject("Root"));
            GameObject part = new GameObject("Part");
            part.transform.SetParent(root.transform);
            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            Material material = Track(new Material(atlasShader));
            partRenderer.sharedMaterial = material;

            RigAsset rig = Track(ScriptableObject.CreateInstance<RigAsset>());
            rig.sourcePrefab = root;
            RigTargetDefinition target = new RigTargetDefinition { sourceNodePath = "Part", displayName = "Part" };
            rig.targets.Add(target);
            rig.EnsureStableIds();

            ClipAsset clip = Track(ScriptableObject.CreateInstance<ClipAsset>());
            clip.spriteTracks.Add(new SpriteTrack { targetId = target.Id.Value, mode = SpriteFrameMode.Slice });
            ClipSetAsset clipSet = Track(ScriptableObject.CreateInstance<ClipSetAsset>());
            clipSet.clips.Add(clip);

            List<PartInputReport> reports = PartInputCheck.Evaluate(rig, clipSet);

            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(PartInputState.MissingInputs, reports[0].State);
            Assert.Contains("_ImageIndex", reports[0].MissingPropertyNames);

            material.shader = arrayShader;
            reports = PartInputCheck.Evaluate(rig, clipSet);

            Assert.AreEqual(PartInputState.AllPresent, reports[0].State);
        }
    }
}
