// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One part's bake output, paired back with the source that produced it.</summary>
    public sealed class VatBakePartResult
    {
        public VatBakeSource Source;
        public VatBakeResult Result;
    }

    /// <summary>Writes a bake's textures, runtime meshes and the VatTextureSetAsset that indexes them.</summary>
    public static class VatTextureSetBuilder
    {
        /// <returns>The asset path of the written set.</returns>
        public static string WriteSet(
            ClipSetAsset clipSet,
            RigAsset rig,
            VatFlavor flavor,
            string outputFolder,
            List<VatBakePartResult> partResults)
        {
            EnsureFolderPath(outputFolder);

            bool isBoneFlavor = flavor == VatFlavor.BoneMatrix;
            string setBaseName = clipSet.name + "Vat";

            VatTextureSetAsset textureSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            textureSet.flavor = flavor;
            textureSet.sourceRigKey = rig.StableId;
            textureSet.schemaVersion = 1;

            for (int partResultIndex = 0; partResultIndex < partResults.Count; partResultIndex++)
            {
                VatBakePartResult partResult = partResults[partResultIndex];
                VatBakeSource source = partResult.Source;
                VatBakeResult bakeResult = partResult.Result;

                bool isUntargetedPart = source.TargetId == 0u;
                string partNameSegment = isUntargetedPart ? string.Empty : SanitizePartName(source.DisplayName);
                string partBaseName = setBaseName + partNameSegment;

                string texturePath = outputFolder + "/" + partBaseName + (isBoneFlavor ? "Bone" : "Position") + ".asset";
                CreateOrReplaceAsset(bakeResult.boneOrPositionTexture, texturePath);

                VatPartTextures partTextures = new VatPartTextures();
                partTextures.targetId = source.TargetId;
                partTextures.displayName = source.DisplayName;
                if (isBoneFlavor)
                {
                    partTextures.boneTexture = bakeResult.boneOrPositionTexture;
                }
                else
                {
                    partTextures.positionTexture = bakeResult.boneOrPositionTexture;
                    if (bakeResult.normalTexture != null)
                    {
                        CreateOrReplaceAsset(bakeResult.normalTexture, outputFolder + "/" + partBaseName + "Normal.asset");
                    }
                    partTextures.normalTexture = bakeResult.normalTexture;
                }
                partTextures.boneCount = bakeResult.boneCount;
                partTextures.vertexCount = bakeResult.vertexCount;
                partTextures.textureWidth = bakeResult.textureWidth;
                partTextures.rowsPerFrame = bakeResult.rowsPerFrame;

                // Bone flavour only: a vertex-flavour shader reads baked positions and never touches
                // bone influences, so packing them would be dead weight in the vertex stream.
                if (isBoneFlavor)
                {
                    Mesh runtimeMesh;
                    string meshFailureMessage;
                    if (VatMeshPreparer.TryCreateRuntimeMesh(source.PrefabRenderer, out runtimeMesh, out meshFailureMessage))
                    {
                        CreateOrReplaceAsset(runtimeMesh, outputFolder + "/" + partBaseName + "RuntimeMesh.asset");
                        partTextures.runtimeMesh = runtimeMesh;
                    }
                    else
                    {
                        // Warned rather than failed: the textures are valid and a host may already have
                        // its own prepared mesh. But a null runtimeMesh renders as a motionless clump
                        // rather than an error, so silence here would be the worst option.
                        Debug.LogWarning(
                            "VAT bake produced no runtime mesh for part '" + source.DisplayName + "': " + meshFailureMessage +
                            " The shader needs bone influences in UV1 — see the package's Documentation~/shader-contract.md.");
                    }
                }

                textureSet.parts.Add(partTextures);

                if (bakeResult.clipRanges != null)
                {
                    textureSet.clipRanges.AddRange(bakeResult.clipRanges);
                }

                if (partResultIndex == 0)
                {
                    textureSet.sourceHash = bakeResult.sourceHash;
                    if (bakeResult.socketTracks != null)
                    {
                        textureSet.socketTracks.AddRange(bakeResult.socketTracks);
                    }
                }
            }

            string setPath = outputFolder + "/" + setBaseName + "Set.asset";
            CreateOrReplaceAsset(textureSet, setPath);

            // Assigning the set back onto the clip set is what clears a clip's VAT source from
            // pointing at a set with no textures for it.
            clipSet.vatTextures = textureSet;
            EditorUtility.SetDirty(clipSet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return setPath;
        }

        private static string SanitizePartName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return string.Empty;
            }

            char[] sanitizedCharacters = new char[displayName.Length];
            for (int characterIndex = 0; characterIndex < displayName.Length; characterIndex++)
            {
                char currentCharacter = displayName[characterIndex];
                sanitizedCharacters[characterIndex] = char.IsLetterOrDigit(currentCharacter) ? currentCharacter : '_';
            }
            return new string(sanitizedCharacters);
        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string accumulated = segments[0];
            for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                string next = accumulated + "/" + segments[segmentIndex];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(accumulated, segments[segmentIndex]);
                }
                accumulated = next;
            }
        }

        private static void CreateOrReplaceAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
