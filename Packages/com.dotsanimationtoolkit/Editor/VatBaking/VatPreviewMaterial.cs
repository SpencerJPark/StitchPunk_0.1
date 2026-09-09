using System;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Builds an in-memory Material bound to a baked bone-flavour VAT texture set, rendered through the package's shipped crowd shader.</summary>
    public sealed class VatPreviewMaterial : IDisposable
    {
        public const string VatGraphPath = "Packages/com.dotsanimationtoolkit/Shaders/ToolkitVatCrowdUnlit.shadergraph";

        public Material Material { get; }

        private VatPreviewMaterial(Material material)
        {
            Material = material;
        }

        public static bool TryCreate(
            VatTextureSetAsset textureSet,
            VatPartTextures part,
            Texture mainTexture,
            out VatPreviewMaterial preview,
            out string failureMessage)
        {
            preview = null;

            if (textureSet == null)
            {
                failureMessage = "No VAT texture set assigned.";
                return false;
            }

            if (textureSet.flavor != VatFlavor.BoneMatrix)
            {
                failureMessage = "A vertex-flavour set has no runtime mesh and the package ships no vertex-fetch preview shader.";
                return false;
            }

            if (part == null || part.boneTexture == null)
            {
                failureMessage = "The VAT texture set has no bone texture baked.";
                return false;
            }

            if (part.runtimeMesh == null)
            {
                failureMessage = "The VAT texture set has no runtime mesh baked.";
                return false;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(VatGraphPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                failureMessage = "The preview shader at " + VatGraphPath + " failed to load or has compile errors.";
                return false;
            }

            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            material.SetTexture("_VatBoneTex", part.boneTexture);
            material.SetVector("_VatTexelParams", new Vector4(part.textureWidth, part.boneTexture.height, part.rowsPerFrame, part.boneCount));
            material.SetTexture("_MainTex", mainTexture != null ? mainTexture : Texture2D.whiteTexture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_VatFrameA", 0f);
            material.SetFloat("_VatFrameB", 0f);
            material.SetFloat("_VatBlend", 0f);

            preview = new VatPreviewMaterial(material);
            failureMessage = string.Empty;
            return true;
        }

        public void SetFrame(float globalFrameA, float globalFrameB, float blend)
        {
            Material.SetFloat("_VatFrameA", globalFrameA);
            Material.SetFloat("_VatFrameB", globalFrameB);
            Material.SetFloat("_VatBlend", blend);
        }

        public void Dispose()
        {
            if (Material != null)
            {
                UnityEngine.Object.DestroyImmediate(Material);
            }
        }
    }
}
