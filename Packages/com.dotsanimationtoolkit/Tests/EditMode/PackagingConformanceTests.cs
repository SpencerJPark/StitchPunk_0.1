// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Packaging conformance tests (a) through (e) contracted by the Phase B architecture,
    /// section 8 module M6, plus supplementary checks against the section 1.1 identity table.
    /// Every test loads the real package.json / .asmdef / source files from the package folder
    /// on disk and asserts on their parsed contents; nothing here is hardcoded to pass.
    ///
    /// The scan patterns for host-project identifiers are assembled from concatenated string
    /// fragments so this file cannot trigger its own forbidden-text scans.
    /// </summary>
    public sealed class PackagingConformanceTests
    {
        private const string PackageId = "com.dotsanimationtoolkit";

        private static string PackageRootPath
        {
            get { return Path.GetFullPath("Packages/" + PackageId); }
        }

        // ---------------------------------------------------------------------------------
        // Serialization mirrors (parsed with JsonUtility; missing JSON fields stay default).
        // ---------------------------------------------------------------------------------

        [System.Serializable]
        private sealed class AsmdefContents
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
            public string[] excludePlatforms;
            public bool allowUnsafeCode;
        }

        [System.Serializable]
        private sealed class PackageManifestContents
        {
            public string name;
            public string displayName;
            public string version;
            public string unity;
        }

        private sealed class AsmdefExpectation
        {
            public string relativePath;
            public string assemblyName;
            public string[] expectedReferences;
            public string[] expectedIncludePlatforms;
            public bool expectedAllowUnsafeCode;
        }

        // ---------------------------------------------------------------------------------
        // Normative expectations, transcribed from architecture section 1.3.
        // ---------------------------------------------------------------------------------

        private static readonly AsmdefExpectation[] AsmdefExpectations = new AsmdefExpectation[]
        {
            new AsmdefExpectation
            {
                relativePath = "Runtime/DotsAnimationToolkit.Runtime.asmdef",
                assemblyName = "DotsAnimationToolkit.Runtime",
                expectedReferences = new string[]
                {
                    "Unity.Entities",
                    "Unity.Entities.Graphics",
                    "Unity.Burst",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Mathematics.Extensions",
                    "Unity.Transforms"
                },
                expectedIncludePlatforms = new string[0],
                expectedAllowUnsafeCode = true
            },
            new AsmdefExpectation
            {
                relativePath = "Authoring/DotsAnimationToolkit.Authoring.asmdef",
                assemblyName = "DotsAnimationToolkit.Authoring",
                expectedReferences = new string[]
                {
                    "DotsAnimationToolkit.Runtime",
                    "Unity.Entities",
                    "Unity.Entities.Hybrid",
                    "Unity.Burst",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Mathematics.Extensions",
                    // Phase D, amendment A50: RagdollRestPose's baked LocalTransform and
                    // PostTransformMatrix fields are Unity.Transforms types, and ActorBaker fills
                    // them in directly, so the assembly that types them must be referenced. See §1.3.
                    "Unity.Transforms"
                },
                expectedIncludePlatforms = new string[0],
                expectedAllowUnsafeCode = false
            },
            new AsmdefExpectation
            {
                relativePath = "Runtime.Physics/DotsAnimationToolkit.Runtime.Physics.asmdef",
                assemblyName = "DotsAnimationToolkit.Runtime.Physics",
                expectedReferences = new string[]
                {
                    // Phase D7, amendment A50: the one optional probe provider allowed to name
                    // Unity Physics (§7.5's D2 seam). Excluded from compilation entirely when Unity
                    // Physics is absent (defineConstraints on DOTS_ANIM_TOOLKIT_PHYSICS, set by this
                    // asmdef's own versionDefine on com.unity.physics >= 1.0.0), so this reference is
                    // never evaluated on a project without the package. See §1.3.
                    "DotsAnimationToolkit.Runtime",
                    "Unity.Entities",
                    "Unity.Burst",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Physics"
                },
                expectedIncludePlatforms = new string[0],
                expectedAllowUnsafeCode = false
            },
            new AsmdefExpectation
            {
                relativePath = "Editor/DotsAnimationToolkit.Editor.asmdef",
                assemblyName = "DotsAnimationToolkit.Editor",
                expectedReferences = new string[]
                {
                    "DotsAnimationToolkit.Runtime",
                    "DotsAnimationToolkit.Authoring",
                    "Unity.Entities",
                    "Unity.Entities.Hybrid",
                    "Unity.Burst",
                    "Unity.Collections",
                    "Unity.Mathematics"
                },
                expectedIncludePlatforms = new string[] { "Editor" },
                expectedAllowUnsafeCode = false
            },
            new AsmdefExpectation
            {
                relativePath = "Tests/EditMode/DotsAnimationToolkit.Tests.EditMode.asmdef",
                assemblyName = "DotsAnimationToolkit.Tests.EditMode",
                expectedReferences = new string[]
                {
                    "DotsAnimationToolkit.Runtime",
                    "DotsAnimationToolkit.Authoring",
                    "DotsAnimationToolkit.Editor",
                    "UnityEngine.TestRunner",
                    "UnityEditor.TestRunner",
                    "Unity.Entities",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Mathematics.Extensions",
                    "Unity.Burst"
                },
                expectedIncludePlatforms = new string[] { "Editor" },
                expectedAllowUnsafeCode = false
            },
            new AsmdefExpectation
            {
                relativePath = "Tests/PlayMode/DotsAnimationToolkit.Tests.PlayMode.asmdef",
                assemblyName = "DotsAnimationToolkit.Tests.PlayMode",
                expectedReferences = new string[]
                {
                    "DotsAnimationToolkit.Runtime",
                    "DotsAnimationToolkit.Authoring",
                    "UnityEngine.TestRunner",
                    "Unity.Entities",
                    "Unity.Entities.Hybrid",
                    // Amendment A33: RenderBounds is Unity.Rendering, which lives in
                    // Unity.Entities.Graphics. §5.8's bounds system writes it, so the suite that
                    // asserts on the written box must reference the defining assembly. See §1.3.
                    "Unity.Entities.Graphics",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Mathematics.Extensions",
                    "Unity.Burst",
                    "Unity.Transforms"
                },
                // Amendment A25 supersedes A17: [] , not ["Editor"]. An editor-only assembly is
                // classified by the Test Framework as an EditMode assembly, so ["Editor"] does not
                // restrict this suite — it abolishes it. See §1.3.
                expectedIncludePlatforms = new string[0],
                expectedAllowUnsafeCode = false
            }
        };

        // File extensions treated as scannable package text for the host-identifier scan (d).
        private static readonly string[] TextFileSearchPatterns = new string[]
        {
            "*.cs",
            "*.asmdef",
            "*.json",
            "*.md",
            "*.shader",
            "*.hlsl",
            "*.shadergraph",
            "*.shadersubgraph",
            "*.uxml",
            "*.uss"
        };

        // ---------------------------------------------------------------------------------
        // Conformance tests (a) through (e), section 8 M6.
        // ---------------------------------------------------------------------------------

        // (a) Every asmdef's reference list matches section 1.3 exactly (names and order).
        [Test]
        public void Conformance_A_AsmdefReferenceLists_MatchSection13Exactly()
        {
            foreach (AsmdefExpectation expectation in AsmdefExpectations)
            {
                AsmdefContents contents = LoadAsmdef(expectation.relativePath);
                Assert.AreEqual(
                    expectation.assemblyName,
                    contents.name,
                    "Assembly name mismatch in " + expectation.relativePath);
                CollectionAssert.AreEqual(
                    expectation.expectedReferences,
                    NormalizeStringArray(contents.references),
                    "Reference list of " + expectation.assemblyName +
                    " must match architecture section 1.3 exactly.");
            }
        }

        // (b) The Editor asmdef is Editor-platform-only, and so is the EditMode test asmdef.
        //     The PlayMode test asmdef is unrestricted since amendment A25, which supersedes A17:
        //     restricting it to ["Editor"] made it an editor-only assembly, which the Test Framework
        //     classifies as EditMode — silently emptying the PlayMode suite rather than narrowing
        //     its platforms. Runtime and Authoring stay unrestricted.
        [Test]
        public void Conformance_B_PlatformRestrictions_MatchSection13()
        {
            foreach (AsmdefExpectation expectation in AsmdefExpectations)
            {
                AsmdefContents contents = LoadAsmdef(expectation.relativePath);
                CollectionAssert.AreEqual(
                    expectation.expectedIncludePlatforms,
                    NormalizeStringArray(contents.includePlatforms),
                    "includePlatforms of " + expectation.assemblyName +
                    " must match architecture section 1.3.");
                CollectionAssert.AreEqual(
                    new string[0],
                    NormalizeStringArray(contents.excludePlatforms),
                    "excludePlatforms of " + expectation.assemblyName +
                    " must be empty; platform restriction uses includePlatforms only.");
            }
        }

        // (c) No file outside Editor/ or Tests/ references UnityEditor (regex scan).
        [Test]
        public void Conformance_C_NoUnityEditorReference_OutsideEditorOrTests()
        {
            Regex unityEditorPattern = new Regex("\\bUnityEditor\\b");
            List<string> violations = new List<string>();
            List<string> scannedFiles = EnumeratePackageFiles(new string[] { "*.cs", "*.asmdef" });
            foreach (string scannedFile in scannedFiles)
            {
                string relativePath = ToPackageRelativePath(scannedFile).Replace('\\', '/');

                // Any folder literally named Editor is editor-only to Unity, wherever it sits — the
                // rule is about what reaches a player build, not about top-level layout. Checking
                // only the first segment failed a sample whose editor code lives at
                // Samples~/<Name>/Editor/, which Unity never compiles into a build at all.
                if (relativePath.StartsWith("Editor/")
                    || relativePath.StartsWith("Tests/")
                    || relativePath.Contains("/Editor/"))
                {
                    continue;
                }
                string fileText = File.ReadAllText(scannedFile);
                if (unityEditorPattern.IsMatch(fileText))
                {
                    violations.Add(relativePath);
                }
            }
            Assert.IsEmpty(
                violations,
                "Files outside Editor/ and Tests/ must not reference UnityEditor: " +
                string.Join(", ", violations));
        }

        /// The only folder under the project root a package file may name: where it writes
        /// the generated vocabulary constants. Anything else there belongs to the project.
        private const string PackageOwnedGeneratedFolderName = "Generated";

        // (d) No package file references the host game at all, by name or by asset path.
        [Test]
        public void Conformance_D_NoHostNamespaceOrHostAssetPathReferences()
        {
            // Assembled from fragments so this file's own source never matches the patterns.
            //
            // Stricter since the DotsAnimationToolkit rename. This used to carve out an exception
            // for the package's own former namespace, which carried the host's name; it no longer
            // does, so the host's name may not appear in a shipped package in any form. Note that
            // this comment cannot spell that name either -- the scan reads this very file.
            //
            // Narrowed by amendment A57. The project root this matches belongs to every Unity
            // project, the buyer's included -- a package that generates code into a project has to
            // be able to name where it puts it, and banning the root outright made the
            // generated-constants destination unspellable. What may never ship is a folder of
            // *this* game, so the segment after the root is what gets judged.
            Regex hostNamespacePattern = new Regex("Stitch" + "Punk");
            Regex assetPathSegmentPattern = new Regex("Asse" + "ts/([A-Za-z_0-9]*)");
            List<string> violations = new List<string>();
            List<string> scannedFiles = EnumeratePackageFiles(TextFileSearchPatterns);
            foreach (string scannedFile in scannedFiles)
            {
                string relativePath = ToPackageRelativePath(scannedFile);
                string fileText = File.ReadAllText(scannedFile);
                if (hostNamespacePattern.IsMatch(fileText))
                {
                    violations.Add(relativePath + " (host game namespace)");
                }
                foreach (Match assetPathMatch in assetPathSegmentPattern.Matches(fileText))
                {
                    string firstSegment = assetPathMatch.Groups[1].Value;
                    if (firstSegment.Length == 0 ||
                        firstSegment == PackageOwnedGeneratedFolderName)
                    {
                        continue;
                    }
                    violations.Add(
                        relativePath + " (host asset folder path: " + assetPathMatch.Value + ")");
                }
            }
            Assert.IsEmpty(
                violations,
                "Package files must not reference host game namespaces or host asset folder paths: " +
                string.Join(", ", violations));
        }

        // (e) No OnGUI / GUILayout / Handles usage in Editor sources (UI Toolkit only, section 7).
        [Test]
        public void Conformance_E_NoImguiApis_InEditorSources()
        {
            Regex imguiPattern = new Regex("\\bOnGUI\\b|\\bGUILayout\\b|\\bHandles\\.");
            List<string> violations = new List<string>();
            string editorFolderPath = Path.Combine(PackageRootPath, "Editor");
            string[] editorSourceFiles;
            if (Directory.Exists(editorFolderPath))
            {
                editorSourceFiles = Directory.GetFiles(editorFolderPath, "*.cs", SearchOption.AllDirectories);
            }
            else
            {
                editorSourceFiles = new string[0];
            }
            foreach (string editorSourceFile in editorSourceFiles)
            {
                // Comments are stripped first. The ban is on USING these APIs, not on naming them —
                // and a file explaining why it avoids IMGUI is exactly the file most likely to spell
                // them out. Checking raw text would push that explanation out of the source, which
                // is the opposite of what a conformance rule should encourage.
                string fileCode = StripComments(File.ReadAllText(editorSourceFile));
                if (imguiPattern.IsMatch(fileCode))
                {
                    violations.Add(ToPackageRelativePath(editorSourceFile));
                }
            }
            Assert.IsEmpty(
                violations,
                "Editor sources must not use IMGUI APIs (OnGUI, GUILayout, Handles): " +
                string.Join(", ", violations));
        }

        // Folders scanned by the code-style conformance checks (f), (g) and (h) — Amendment A69.
        private static readonly string[] StyleScanFolders = new string[]
        {
            "Runtime", "Runtime.Physics", "Authoring", "Editor"
        };

        private static readonly string[] StaticClassAllowedSuffixes = new string[]
        {
            "Api", "Builder", "Sampler", "Resolver", "Math", "Validation", "Utility", "Editing"
        };

        private static readonly string[] StaticClassBannedSuffixes = new string[]
        {
            "Util", "Utils", "Helper", "Helpers", "Query", "Manager", "Common", "Misc", "Ext", "Extensions"
        };

        // "AnimEventMask" in the spec's own allowlist names the IComponentData struct, not the
        // static class in the same file — the static class is "AnimEventMaskKeys" (drift, A69 §6).
        // "RagdollSolver" was not in the spec's table at all; it is a plain-noun static class same
        // as the others here, not one of the eight role suffixes (drift, A69 §6).
        private static readonly HashSet<string> PlainNounStaticClasses = new HashSet<string>
        {
            "EasingPresets", "ClipKeyClipboard", "RestPoseCapture", "AnimEventMaskKeys", "ConstantsGenerator",
            "CutsceneBlockTiming", "CutsceneFacingVariants", "AuthoringPathHash", "AuthoringPathText",
            "CutsceneDerivedHolds", "CutsceneDirectionVariants", "CutsceneKeySampler", "CutsceneMarkMerge",
            "CutsceneAssetOpener", "DirectionSetAssetOpener", "TimelineRangeShading", "VocabularySettingsProvider",
            "RagdollPreviewSceneryProvider", "VocabularyRegistryProvider", "CutsceneEventInspectorProviders",
            "BindingReconciler", "ClipEditorDocking", "PrefabAuthoringBridge", "RigStructureEditor",
            "ClipComponentModel", "GizmoDragRouting", "EventLaneAddressing", "PreviewLineMaterial",
            "PreviewScenePicker", "RagdollPreviewProbe", "VatMeshPreparer", "VatTentacleRigBuilder",
            "VatTextureBaker", "ClipKeyConversion", "CutsceneSceneBinding", "RagdollSolver", "StableIdMinting"
        };

        // (f) No doc-comment essays or spec citations survive in shipped sources (Amendment A69, section 2.3).
        [Test]
        public void Conformance_F_NoDocEssaysOrSpecCitations_InSources()
        {
            string[] bannedLiterals = new string[] { "<remarks>", "<para>", "<strong>", "<em>", "<list ", "architecture section", "§" };
            Regex[] bannedPatterns = new Regex[]
            {
                new Regex("amendment A[0-9]"),
                new Regex("\\bPhase [A-G]\\b"),
                new Regex("\\brule V[0-9]{2}\\b")
            };

            List<string> violations = new List<string>();
            int totalHitCount = 0;
            List<string> scannedFiles = EnumerateFolderFiles(StyleScanFolders, "*.cs");
            foreach (string scannedFile in scannedFiles)
            {
                string relativePath = ToPackageRelativePath(scannedFile).Replace('\\', '/');
                string[] lines = File.ReadAllLines(scannedFile, System.Text.Encoding.UTF8);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    bool hit = false;
                    foreach (string bannedLiteral in bannedLiterals)
                    {
                        if (line.Contains(bannedLiteral))
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (!hit)
                    {
                        foreach (Regex bannedPattern in bannedPatterns)
                        {
                            if (bannedPattern.IsMatch(line))
                            {
                                hit = true;
                                break;
                            }
                        }
                    }
                    if (hit)
                    {
                        totalHitCount++;
                        if (violations.Count < 50)
                        {
                            violations.Add(relativePath + ":" + (lineIndex + 1));
                        }
                    }
                }
            }
            Assert.IsEmpty(
                violations,
                "Doc-comment essays and spec citations must not appear in shipped sources (" + totalHitCount +
                " total hit(s), first " + violations.Count + " shown): " + string.Join(", ", violations));
        }

        // (g) Every static class in Runtime/Runtime.Physics/Authoring/Editor uses one suffix per role
        // (Amendment A69, section 2.1).
        [Test]
        public void Conformance_G_StaticClassSuffixVocabulary()
        {
            Regex staticClassPattern = new Regex("\\b(?:public|internal)\\s+static\\s+(?:partial\\s+)?class\\s+(\\w+)");
            List<string> violations = new List<string>();
            List<string> scannedFiles = EnumerateFolderFiles(StyleScanFolders, "*.cs");
            foreach (string scannedFile in scannedFiles)
            {
                string relativePath = ToPackageRelativePath(scannedFile).Replace('\\', '/');
                string[] lines = File.ReadAllLines(scannedFile, System.Text.Encoding.UTF8);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    Match match = staticClassPattern.Match(lines[lineIndex]);
                    if (!match.Success)
                    {
                        continue;
                    }
                    string className = match.Groups[1].Value;
                    string location = relativePath + ":" + (lineIndex + 1) + " (" + className + ")";

                    bool hasBannedSuffix = false;
                    foreach (string bannedSuffix in StaticClassBannedSuffixes)
                    {
                        if (className.EndsWith(bannedSuffix, System.StringComparison.Ordinal))
                        {
                            hasBannedSuffix = true;
                            break;
                        }
                    }
                    if (hasBannedSuffix)
                    {
                        violations.Add(location + " uses a banned suffix");
                        continue;
                    }

                    if (className.EndsWith("Utility", System.StringComparison.Ordinal))
                    {
                        if (!relativePath.Contains("Editor/ClipUtilities/"))
                        {
                            violations.Add(location + " uses Utility outside Editor/ClipUtilities/");
                        }
                        continue;
                    }

                    bool hasAllowedSuffix = false;
                    foreach (string allowedSuffix in StaticClassAllowedSuffixes)
                    {
                        if (allowedSuffix == "Utility")
                        {
                            continue;
                        }
                        if (className.EndsWith(allowedSuffix, System.StringComparison.Ordinal))
                        {
                            hasAllowedSuffix = true;
                            break;
                        }
                    }
                    if (hasAllowedSuffix || PlainNounStaticClasses.Contains(className))
                    {
                        continue;
                    }
                    violations.Add(location + " does not use a recognised role suffix or allowlisted plain noun");
                }
            }
            Assert.IsEmpty(
                violations,
                "Static classes must use one suffix per role (Api/Builder/Sampler/Resolver/Math/Validation/" +
                "Utility/Editing) or appear in the plain-noun allowlist: " + string.Join(", ", violations));
        }

        // (h) Every public static class in Runtime/Api/ ends in Api (Amendment A69, section 2.1).
        [Test]
        public void Conformance_H_ApiFolderClassesEndInApi()
        {
            Regex staticClassPattern = new Regex("\\bpublic\\s+static\\s+(?:partial\\s+)?class\\s+(\\w+)");
            List<string> violations = new List<string>();
            string apiFolderPath = Path.Combine(PackageRootPath, "Runtime", "Api");
            string[] apiSourceFiles;
            if (Directory.Exists(apiFolderPath))
            {
                apiSourceFiles = Directory.GetFiles(apiFolderPath, "*.cs", SearchOption.AllDirectories);
            }
            else
            {
                apiSourceFiles = new string[0];
            }
            foreach (string apiSourceFile in apiSourceFiles)
            {
                string relativePath = ToPackageRelativePath(apiSourceFile).Replace('\\', '/');
                string[] lines = File.ReadAllLines(apiSourceFile, System.Text.Encoding.UTF8);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    Match match = staticClassPattern.Match(lines[lineIndex]);
                    if (!match.Success)
                    {
                        continue;
                    }
                    string className = match.Groups[1].Value;
                    if (!className.EndsWith("Api", System.StringComparison.Ordinal))
                    {
                        violations.Add(relativePath + ":" + (lineIndex + 1) + " (" + className + ")");
                    }
                }
            }
            Assert.IsEmpty(
                violations,
                "Every public static class in Runtime/Api/ must end in Api: " + string.Join(", ", violations));
        }

        // ---------------------------------------------------------------------------------
        // Supplementary structural checks against the section 1.1 identity table.
        // ---------------------------------------------------------------------------------

        [Test]
        public void Supplementary_PackageManifest_MatchesSection11Identity()
        {
            string manifestPath = Path.Combine(PackageRootPath, "package.json");
            Assert.IsTrue(File.Exists(manifestPath), "package.json is missing from the package root.");
            string manifestText = File.ReadAllText(manifestPath);
            PackageManifestContents manifest = JsonUtility.FromJson<PackageManifestContents>(manifestText);
            Assert.AreEqual(PackageId, manifest.name, "Package id must match architecture section 1.1.");
            Assert.AreEqual("DOTS Animation Toolkit", manifest.displayName, "Display name must match architecture section 1.1.");
            // Pinned deliberately, like the golden content hash: a version bump is a claim about
            // what shipped, so it should be made once, on purpose, in the same change that ships it
            // -- not drift because someone edited the manifest. 0.15.0 is Amendment A69, the code
            // style unification: one suffix per static-class role and the breaking renames it forced.
            Assert.AreEqual("0.15.0", manifest.version, "Version tracks the shipped feature set; 0.15.0 is Amendment A69's breaking renames.");
            Assert.AreEqual("6000.5", manifest.unity, "Minimum Unity version must match architecture section 1.1.");
        }

        [Test]
        public void Supplementary_PackageManifest_PinsSection11Dependencies()
        {
            string manifestPath = Path.Combine(PackageRootPath, "package.json");
            Assert.IsTrue(File.Exists(manifestPath), "package.json is missing from the package root.");
            string manifestText = File.ReadAllText(manifestPath);
            AssertDependencyPinned(manifestText, "com.unity.entities", "6.5.0");
            AssertDependencyPinned(manifestText, "com.unity.entities.graphics", "6.5.0");
            AssertDependencyPinned(manifestText, "com.unity.burst", "1.8.29");
            AssertDependencyPinned(manifestText, "com.unity.collections", "6.5.0");
            AssertDependencyPinned(manifestText, "com.unity.mathematics", "1.4.0");
            AssertDependencyPinned(manifestText, "com.unity.render-pipelines.universal", "17.5.0");
        }

        [Test]
        public void Supplementary_UnsafeCodeFlags_MatchSection13()
        {
            foreach (AsmdefExpectation expectation in AsmdefExpectations)
            {
                AsmdefContents contents = LoadAsmdef(expectation.relativePath);
                Assert.AreEqual(
                    expectation.expectedAllowUnsafeCode,
                    contents.allowUnsafeCode,
                    "allowUnsafeCode of " + expectation.assemblyName +
                    " must match architecture section 1.3 (true only for the Runtime assembly).");
            }
        }

        [Test]
        public void Supplementary_NoShaderUsesTheBuiltInPipeline()
        {
            // Section 6 makes this package URP-only, and M4's C5 acceptance is that every shader in
            // it compiles for the URP target with warnings as errors. A built-in-pipeline shader
            // would fail that sweep — and would do so late, in a module that has nothing to do with
            // whoever added the file.
            //
            // This covers Tests/ as well as the shipped folders on purpose. Unless Tests/ is
            // excluded from the published tarball, a consumer project imports and variant-compiles
            // whatever sits there, so a stray CGPROGRAM is their problem as much as ours. The first
            // shader in this package was a test-only probe that had exactly that defect.
            Regex builtInPipelinePattern = new Regex("\\bCGPROGRAM\\b|\\bCGINCLUDE\\b|UnityCG\\.cginc");
            List<string> violations = new List<string>();
            List<string> scannedFiles = EnumeratePackageFiles(
                new string[] { "*.shader", "*.hlsl", "*.cginc" });
            foreach (string scannedFile in scannedFiles)
            {
                if (builtInPipelinePattern.IsMatch(File.ReadAllText(scannedFile)))
                {
                    violations.Add(ToPackageRelativePath(scannedFile));
                }
            }
            Assert.IsEmpty(
                violations,
                "Shaders must target URP (HLSLPROGRAM plus the URP ShaderLibrary), not the built-in " +
                "pipeline: " + string.Join(", ", violations));
        }

        // ---------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------

        private static AsmdefContents LoadAsmdef(string relativePath)
        {
            string fullPath = Path.Combine(PackageRootPath, relativePath);
            Assert.IsTrue(File.Exists(fullPath), "Missing asmdef file: " + relativePath);
            return JsonUtility.FromJson<AsmdefContents>(File.ReadAllText(fullPath));
        }

        private static string[] NormalizeStringArray(string[] values)
        {
            if (values == null)
            {
                return new string[0];
            }
            return values;
        }

        private static List<string> EnumeratePackageFiles(string[] searchPatterns)
        {
            List<string> matchingFiles = new List<string>();
            foreach (string searchPattern in searchPatterns)
            {
                string[] foundFiles = Directory.GetFiles(PackageRootPath, searchPattern, SearchOption.AllDirectories);
                foreach (string foundFile in foundFiles)
                {
                    matchingFiles.Add(foundFile);
                }
            }
            return matchingFiles;
        }

        private static List<string> EnumerateFolderFiles(string[] folderNames, string searchPattern)
        {
            List<string> matchingFiles = new List<string>();
            foreach (string folderName in folderNames)
            {
                string folderPath = Path.Combine(PackageRootPath, folderName);
                if (!Directory.Exists(folderPath))
                {
                    continue;
                }
                matchingFiles.AddRange(Directory.GetFiles(folderPath, searchPattern, SearchOption.AllDirectories));
            }
            return matchingFiles;
        }

        /// <summary>
        /// Blanks out C# comments so a scan sees only code.
        /// </summary>
        /// <remarks>
        /// Comments are replaced with spaces rather than removed so that reported positions still
        /// line up with the original file. String literals are honoured, otherwise a path like
        /// <c>"http://…"</c> would swallow the rest of its line. Verbatim and interpolated strings
        /// are not modelled — for a ban on API identifiers that is immaterial, since a false
        /// negative would require the banned call to sit inside a string that this misreads.
        /// </remarks>
        private static string StripComments(string sourceText)
        {
            char[] characters = sourceText.ToCharArray();
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            char stringQuote = '"';

            for (int index = 0; index < characters.Length; index++)
            {
                char current = characters[index];
                char next = index + 1 < characters.Length ? characters[index + 1] : '\0';

                if (inLineComment)
                {
                    if (current == '\n')
                    {
                        inLineComment = false;
                        continue;
                    }
                    characters[index] = ' ';
                }
                else if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        characters[index] = ' ';
                        characters[index + 1] = ' ';
                        index++;
                        inBlockComment = false;
                        continue;
                    }
                    if (current != '\n')
                    {
                        characters[index] = ' ';
                    }
                }
                else if (inString)
                {
                    if (current == '\\')
                    {
                        index++;
                        continue;
                    }
                    if (current == stringQuote)
                    {
                        inString = false;
                    }
                }
                else if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    characters[index] = ' ';
                }
                else if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    characters[index] = ' ';
                }
                else if (current == '"' || current == '\'')
                {
                    inString = true;
                    stringQuote = current;
                }
            }

            return new string(characters);
        }

        private static string ToPackageRelativePath(string absolutePath)
        {
            string normalizedAbsolutePath = absolutePath.Replace('\\', '/');
            string normalizedRootPath = PackageRootPath.Replace('\\', '/').TrimEnd('/');
            if (normalizedAbsolutePath.StartsWith(normalizedRootPath))
            {
                return normalizedAbsolutePath.Substring(normalizedRootPath.Length).TrimStart('/');
            }
            return normalizedAbsolutePath;
        }

        private static void AssertDependencyPinned(string manifestText, string dependencyId, string pinnedVersion)
        {
            string pinPattern = "\"" + Regex.Escape(dependencyId) + "\"\\s*:\\s*\"" + Regex.Escape(pinnedVersion) + "\"";
            Assert.IsTrue(
                Regex.IsMatch(manifestText, pinPattern),
                "package.json must pin " + dependencyId + " to exactly " + pinnedVersion +
                " per architecture section 1.1.");
        }
    }
}
