// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// M4's structural acceptance (architecture section 8 M4, build step C5): the shaders compile,
    /// every pass displaces, and the per-instance block matches the section 6.2 contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are grep-and-reflect tests over real files on disk, not rendering tests. That is
    /// deliberate and it is what §8 M4 asks for: the defects they catch — a missing pass, a property
    /// name drifting from its component, a forbidden matrix creeping back in — are all invisible in
    /// a screenshot of a correctly-lit quad, and all catastrophic in a specific pass or a specific
    /// project. Pixels are §11.4's job.
    /// </para>
    /// <para>
    /// They also read the shipped files rather than fixtures, which matters here more than usual:
    /// amendment A36 was a shipping-blocker that survived 221 fixtures precisely because every one
    /// of them built its input in memory instead of reading what the package actually ships.
    /// </para>
    /// </remarks>
    public sealed class ShaderConformanceTests
    {
        private const string ShaderRoot = "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/";
        private const string SpriteShaderPath = ShaderRoot + "HandWritten/ToolkitSpriteUnlit.shader";
        private const string InstancingIncludePath = ShaderRoot + "Includes/ToolkitInstancing.hlsl";
        private const string BillboardIncludePath = ShaderRoot + "Includes/ToolkitBillboard.hlsl";
        private const string FlipbookIncludePath = ShaderRoot + "Includes/ToolkitFlipbook.hlsl";

        /// <summary>
        /// Every pass a displacing shader must declare (§6.3). A billboard that displaces in some
        /// passes and not others is self-inconsistent in exactly the ways that are hardest to see.
        /// </summary>
        private static readonly string[] RequiredPassNames =
        {
            "UniversalForward",
            "ShadowCaster",
            "DepthOnly",
            "DepthNormals"
        };

        /// <summary>
        /// The §6.2 rows. Each must appear exactly once as an instanced property, and each has
        /// exactly one <c>[MaterialProperty]</c> component in the Runtime assembly.
        /// </summary>
        private static readonly string[] PerInstancePropertyNames =
        {
            "_ImageIndex",
            "_AtlasFrame",
            "_VatFrameA",
            "_VatFrameB",
            "_VatBlend",
            "_BillboardParams",
            "_BaseColor"
        };

        /// <summary>
        /// Catches: shipping a shader that does not compile. Unity compiles variants lazily, so a
        /// broken shader can sit in a package looking fine until something renders it — and the
        /// Console stays clean the whole time.
        /// </summary>
        [Test]
        public void TheSpriteShader_CompilesWithoutErrors()
        {
            Shader spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(SpriteShaderPath);

            Assert.IsNotNull(spriteShader, "The sprite reference shader is missing from the package.");
            Assert.IsFalse(
                ShaderUtil.ShaderHasError(spriteShader),
                "The sprite reference shader has compile errors.");
            Assert.AreEqual(
                0,
                ShaderUtil.GetShaderMessageCount(spriteShader),
                "The sprite reference shader must ship without warnings or errors.");
            Assert.IsTrue(spriteShader.isSupported, "The sprite reference shader is unsupported on this target.");
        }

        /// <summary>
        /// Catches: dropping a pass, or adding vertex displacement to the colour pass only. §6.3's
        /// whole contract is that a billboarded quad presents the same geometry everywhere — a
        /// shader missing ShadowCaster casts the shadow of its undisplaced pose, which looks like a
        /// lighting bug and is a geometry bug.
        /// </summary>
        [Test]
        public void TheSpriteShader_DeclaresEveryPassThatMustDisplace()
        {
            Shader spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(SpriteShaderPath);
            Assert.IsNotNull(spriteShader);

            Assert.AreEqual(
                RequiredPassNames.Length,
                spriteShader.passCount,
                "The shader must declare exactly the passes §6.3 requires, no more and no fewer.");

            string source = ReadPackageFile(SpriteShaderPath);
            for (int passIndex = 0; passIndex < RequiredPassNames.Length; passIndex++)
            {
                string passName = RequiredPassNames[passIndex];
                StringAssert.Contains(
                    "\"LightMode\" = \"" + passName + "\"",
                    source,
                    "Pass '" + passName + "' is missing, so it cannot displace.");
            }
        }

        /// <summary>
        /// Catches: a pass whose vertex function forgets the shared displacement. Declaring the pass
        /// is not the same as displacing in it, and the difference is a shadow cast by the wrong
        /// shape.
        /// </summary>
        [Test]
        public void EveryPass_CallsTheSharedDisplacement()
        {
            string source = ReadPackageFile(SpriteShaderPath);

            int displaceCallCount = CountOccurrences(source, "ToolkitSpriteDisplace(");

            // One definition, one call per pass, plus DepthNormals displacing its normal with two
            // further calls. Fewer means a pass silently skipped it.
            Assert.GreaterOrEqual(
                displaceCallCount,
                1 + RequiredPassNames.Length,
                "Every declared pass must call the shared displacement; found only "
                + displaceCallCount.ToString() + " references including the definition.");
        }

        /// <summary>
        /// <strong>The normative ban of §6.3, enforced structurally.</strong> Catches: deriving
        /// billboard facing from the view matrix. During shadow rendering <c>UNITY_MATRIX_V</c>
        /// belongs to the <em>light</em>, so a view-matrix billboard turns quads to face the light
        /// and casts the shadow of a shape the camera never sees — correct-looking in the colour
        /// pass, wrong in exactly the pass nobody screenshots.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Comments are stripped before the check, and that is not a detail. The first cut of this
        /// fixture searched the raw file and failed on the include's own header, which <em>documents
        /// the ban</em>. A conformance test that punishes explaining the rule it enforces would push
        /// the explanation out of the file — the exact opposite of what it is for.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBillboardInclude_NeverUsesTheViewMatrix()
        {
            string billboardCode = StripComments(ReadPackageFile(BillboardIncludePath));

            Assert.IsFalse(
                billboardCode.Contains("UNITY_MATRIX_V"),
                "§6.3 forbids UNITY_MATRIX_V for billboard facing: it is the light's view matrix "
                + "during shadow rendering.");
            Assert.IsFalse(
                billboardCode.Contains("UNITY_MATRIX_I_V"),
                "The inverse view matrix is the same hazard by another name.");
        }

        /// <summary>
        /// Catches: an include quietly acquiring a dependency. These three are documented as
        /// standalone so a user can lift one into their own project — the moment one includes
        /// another package file or reads a global it does not declare, that promise is broken and
        /// nothing but this test would notice.
        /// </summary>
        [Test]
        public void TheIncludes_StayStandalone()
        {
            string[] standaloneIncludes = { BillboardIncludePath, FlipbookIncludePath };
            for (int includeIndex = 0; includeIndex < standaloneIncludes.Length; includeIndex++)
            {
                string source = StripComments(ReadPackageFile(standaloneIncludes[includeIndex]));
                Assert.IsFalse(
                    source.Contains("#include"),
                    standaloneIncludes[includeIndex] + " must include nothing — it is documented as "
                    + "liftable into any project.");
            }
        }

        /// <summary>
        /// Catches: the GPU half of §6.2 drifting from the CPU half. Every row of that table has one
        /// instanced property and one <c>[MaterialProperty]</c> component, and a rename on either
        /// side silently stops the value arriving — the shader reads its material default forever,
        /// which looks like an animation that never starts.
        /// </summary>
        [Test]
        public void TheInstancingBlock_DeclaresEverySection62Property()
        {
            string instancingSource = ReadPackageFile(InstancingIncludePath);

            for (int propertyIndex = 0; propertyIndex < PerInstancePropertyNames.Length; propertyIndex++)
            {
                string propertyName = PerInstancePropertyNames[propertyIndex];
                StringAssert.Contains(
                    propertyName + ")",
                    instancingSource,
                    "Section 6.2 row '" + propertyName + "' has no UNITY_DOTS_INSTANCED_PROP.");
            }

            StringAssert.Contains(
                "UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)",
                instancingSource,
                "Entities Graphics requires the block to be named MaterialPropertyMetadata.");
            StringAssert.Contains(
                "UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)",
                instancingSource);
        }

        /// <summary>
        /// Catches: dropping the non-instanced fallback. The same shader has to render in a preview
        /// window, a material inspector, and any scene without Entities — all of which compile with
        /// <c>UNITY_DOTS_INSTANCING_ENABLED</c> undefined. Without the else branch the accessors are
        /// undefined identifiers and the shader fails to compile in exactly the contexts an author
        /// uses while working.
        /// </summary>
        [Test]
        public void TheInstancingBlock_GuardsTheNonInstancedPath()
        {
            string instancingSource = ReadPackageFile(InstancingIncludePath);

            StringAssert.Contains("#if defined(UNITY_DOTS_INSTANCING_ENABLED)", instancingSource);
            StringAssert.Contains("#else", instancingSource);
            Assert.IsTrue(
                instancingSource.Contains("#define TOOLKIT_IMAGE_INDEX       _ImageIndex"),
                "The non-instanced branch must alias the accessors to the plain uniforms.");
        }

        // -------------------------------------------------------------------------------------

        private static string ReadPackageFile(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            Assert.IsTrue(File.Exists(fullPath), "Missing package file: " + assetPath);
            return File.ReadAllText(fullPath);
        }

        /// <summary>
        /// Removes <c>//</c> and <c>/* */</c> comments so a check reads code rather than prose.
        /// </summary>
        /// <remarks>
        /// Deliberately naive — it does not understand string literals, because HLSL includes here
        /// contain none and a real parser would be more machinery than the job needs. If one ever
        /// does, this becomes wrong rather than merely approximate, which is why it says so here.
        /// </remarks>
        private static string StripComments(string source)
        {
            System.Text.StringBuilder stripped = new System.Text.StringBuilder(source.Length);
            int index = 0;
            while (index < source.Length)
            {
                if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '/')
                {
                    while (index < source.Length && source[index] != '\n')
                    {
                        index++;
                    }
                    continue;
                }
                if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
                    {
                        index++;
                    }
                    index += 2;
                    continue;
                }
                stripped.Append(source[index]);
                index++;
            }
            return stripped.ToString();
        }

        private static int CountOccurrences(string source, string token)
        {
            int count = 0;
            int searchIndex = 0;
            while (true)
            {
                int foundIndex = source.IndexOf(token, searchIndex, System.StringComparison.Ordinal);
                if (foundIndex < 0)
                {
                    return count;
                }
                count++;
                searchIndex = foundIndex + token.Length;
            }
        }
    }
}
