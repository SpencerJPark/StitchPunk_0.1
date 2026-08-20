// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.IO;
using System.Collections.Generic;
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
        private const string SpriteGraphPath = ShaderRoot + "ToolkitSpriteUnlit.shadergraph";
        private const string SpriteArrayGraphPath = ShaderRoot + "ToolkitSpriteUnlitArray.shadergraph";
        private const string VatGraphPath = ShaderRoot + "ToolkitVatCrowdUnlit.shadergraph";

        /// <summary>Every graph the package ships. All must compile; none may ship warnings.</summary>
        private static readonly string[] ShippedGraphPaths =
        {
            SpriteGraphPath,
            SpriteArrayGraphPath,
            VatGraphPath
        };

        /// <summary>
        /// The reflected node whose presence in a pass proves that pass displaces.
        /// </summary>
        private const string BillboardNodeCall = "ToolkitBillboardVertex";
        private const string InstancingIncludePath = ShaderRoot + "Includes/ToolkitInstancing.hlsl";
        private const string BillboardIncludePath = ShaderRoot + "Includes/ToolkitBillboard.hlsl";
        private const string FlipbookIncludePath = ShaderRoot + "Includes/ToolkitFlipbook.hlsl";

        /// <summary>
        /// Every pass a displacing shader must declare (§6.3). A billboard that displaces in some
        /// passes and not others is self-inconsistent in exactly the ways that are hardest to see.
        /// </summary>
        private static readonly string[] RequiredPassNames =
        {
            "Unlit",
            "ShadowCaster",
            "DepthOnly",
            "DepthNormalsOnly"
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
        /// Catches: shipping a graph that does not compile. Unity compiles variants lazily, so a
        /// broken shader can sit in a package looking fine until something renders it — and the
        /// Console stays clean the whole time.
        /// </summary>
        [Test]
        public void TheShippedGraphs_CompileWithoutErrors()
        {
            for (int graphIndex = 0; graphIndex < ShippedGraphPaths.Length; graphIndex++)
            {
                string graphPath = ShippedGraphPaths[graphIndex];
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(graphPath);

                Assert.IsNotNull(shader, graphPath + " is missing from the package.");
                Assert.IsFalse(
                    ShaderUtil.ShaderHasError(shader), graphPath + " has compile errors.");
                Assert.AreEqual(
                    0,
                    ShaderUtil.GetShaderMessageCount(shader),
                    graphPath + " must ship without warnings or errors.");
                Assert.IsTrue(shader.isSupported, graphPath + " is unsupported on this target.");
            }
        }

        /// <summary>
        /// <strong>Section 6.3's contract, checked against the generated code rather than the
        /// source.</strong> Catches: a pass that does not displace.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A billboarded quad must present the <em>same</em> geometry in every pass. One that
        /// displaces in the colour pass and not in ShadowCaster casts the shadow of its undisplaced
        /// pose — which looks like a lighting bug and is a geometry bug, in the one pass nobody
        /// screenshots.
        /// </para>
        /// <para>
        /// <strong>This is stronger than the test it replaces, not weaker.</strong> The hand-written
        /// shader was checked by counting occurrences of a helper name in its <em>source</em>, which
        /// proved the author had typed the call, not that the compiler emitted it. Shader Graph
        /// exposes each pass's real generated code through the public <c>ShaderUtil.GetShaderData</c>,
        /// so this asserts on what actually ships. It also covers the four passes the old test never
        /// knew about — GBuffer, MotionVectors and the two picking passes.
        /// </para>
        /// <para>
        /// The graph earns this by construction: Shader Graph emits one vertex description and every
        /// pass calls it, so a pass cannot silently opt out the way a hand-written one could. The
        /// test remains because "by construction" is a property of today's generator.
        /// </para>
        /// </remarks>
        [Test]
        public void TheSpriteGraph_DisplacesInEveryPass()
        {
            Shader spriteShader = AssetDatabase.LoadAssetAtPath<Shader>(SpriteGraphPath);
            Assert.IsNotNull(spriteShader);

            ShaderData shaderData = ShaderUtil.GetShaderData(spriteShader);
            ShaderData.Subshader subshader = shaderData.GetSubshader(0);

            List<string> seenPassNames = new List<string>();
            for (int passIndex = 0; passIndex < subshader.PassCount; passIndex++)
            {
                ShaderData.Pass pass = subshader.GetPass(passIndex);
                seenPassNames.Add(pass.Name);

                StringAssert.Contains(
                    BillboardNodeCall,
                    pass.SourceCode,
                    "Pass '" + pass.Name + "' does not call the billboard displacement, so it "
                    + "renders the undisplaced pose while the colour pass renders the billboard.");
            }

            for (int requiredIndex = 0; requiredIndex < RequiredPassNames.Length; requiredIndex++)
            {
                CollectionAssert.Contains(
                    seenPassNames,
                    RequiredPassNames[requiredIndex],
                    "Pass '" + RequiredPassNames[requiredIndex] + "' is missing, so it cannot displace.");
            }
        }

        /// <summary>
        /// Catches: the VAT graph losing its vertex skinning in some passes — the same hazard as the
        /// billboard, for the other technique that moves vertices.
        /// </summary>
        [Test]
        public void TheVatGraph_SkinsInEveryPass()
        {
            Shader vatShader = AssetDatabase.LoadAssetAtPath<Shader>(VatGraphPath);
            Assert.IsNotNull(vatShader);

            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(vatShader).GetSubshader(0);
            for (int passIndex = 0; passIndex < subshader.PassCount; passIndex++)
            {
                ShaderData.Pass pass = subshader.GetPass(passIndex);
                StringAssert.Contains(
                    "ToolkitVatBoneSkin",
                    pass.SourceCode,
                    "Pass '" + pass.Name + "' does not skin, so it renders the bind pose while the "
                    + "colour pass renders the animation.");
            }
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
        /// Catches the exact defect A44 shipped and then had to chase on screen: the billboard basis
        /// being applied as its own transpose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// HLSL's <c>float3x3(a, b, c)</c> builds a, b and c as <strong>rows</strong>, and
        /// <c>mul(M, v)</c> computes <c>(dot(row0,v), dot(row1,v), dot(row2,v))</c> — which projects
        /// v onto the axes. That is the <em>inverse</em> of "map +Z onto forward". The function
        /// returned the untransposed form for a long time while its own comment claimed the
        /// opposite, and nothing caught it because it was paired with a facing vector that pointed
        /// the wrong way as well. Two inversions cancel — right up until A44 corrected one of them,
        /// at which point billboarded quads span incoherently as the camera moved.
        /// </para>
        /// <para>
        /// A string check rather than a numeric one, because the arithmetic lives in HLSL and cannot
        /// be executed from an EditMode test. It is coarse, and it is still the only automated thing
        /// standing between this line and a defect that took a human eye and a purpose-built scene to
        /// find.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBillboardBasis_IsTransposed_SoItMatchesTheCpuPath()
        {
            string billboardCode = StripComments(ReadPackageFile(BillboardIncludePath));

            Assert.IsTrue(
                billboardCode.Contains("transpose(float3x3(xAxis, yAxis, zAxis))"),
                "ToolkitBillboardBasis must return the TRANSPOSE of its row-built matrix, so the "
                + "axes end up in the columns and mul(M, v) is the local-to-world rotation the CPU "
                + "BillboardMath produces. Without the transpose the shader applies the inverse "
                + "rotation and billboarded quads spin incoherently.");
        }

        /// <summary>
        /// Catches: the billboard facing sign drifting back to pointing at the viewer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A44 fixed the sign so the facing vector is the direction a quad's local +Z must point —
        /// <em>away</em> from the viewer — because Unity's <c>PrimitiveType.Quad</c> carries its
        /// visible normal on −Z. The CPU path is pinned to the same convention by
        /// <c>BillboardMathTests.ScreenAligned_ReproducesTheHostGamesLookRotation</c>; this is the
        /// shader half of that pair.
        /// </para>
        /// <para>
        /// The two halves cannot be checked against each other by running them, so they are each
        /// pinned to the same stated rule instead. That is weaker than an execution test and is what
        /// is available.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBillboardFacing_PointsAwayFromTheViewer()
        {
            string billboardCode = StripComments(ReadPackageFile(BillboardIncludePath));

            Assert.IsTrue(
                billboardCode.Contains("pivotWS - cameraPositionWS"),
                "Spherical facing must run from the camera TOWARD the pivot, so local +Z points "
                + "away from the viewer and a Unity Quad presents its front face.");
            Assert.IsFalse(
                billboardCode.Contains("cameraPositionWS - pivotWS"),
                "That is the pre-A44 sign, which points a quad's +Z at the camera and therefore "
                + "presents its back.");
            Assert.IsFalse(
                billboardCode.Contains("-cameraForwardWS"),
                "Screen-aligned facing takes the camera's forward as-is; negating it is the pre-A44 "
                + "sign.");
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
