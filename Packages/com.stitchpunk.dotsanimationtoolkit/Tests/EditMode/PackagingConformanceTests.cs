// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
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
        private const string PackageId = "com.stitchpunk.dotsanimationtoolkit";

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
                relativePath = "Runtime/StitchPunk.AnimationToolkit.Runtime.asmdef",
                assemblyName = "StitchPunk.AnimationToolkit.Runtime",
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
                relativePath = "Authoring/StitchPunk.AnimationToolkit.Authoring.asmdef",
                assemblyName = "StitchPunk.AnimationToolkit.Authoring",
                expectedReferences = new string[]
                {
                    "StitchPunk.AnimationToolkit.Runtime",
                    "Unity.Entities",
                    "Unity.Entities.Hybrid",
                    "Unity.Burst",
                    "Unity.Collections",
                    "Unity.Mathematics",
                    "Unity.Mathematics.Extensions"
                },
                expectedIncludePlatforms = new string[0],
                expectedAllowUnsafeCode = false
            },
            new AsmdefExpectation
            {
                relativePath = "Editor/StitchPunk.AnimationToolkit.Editor.asmdef",
                assemblyName = "StitchPunk.AnimationToolkit.Editor",
                expectedReferences = new string[]
                {
                    "StitchPunk.AnimationToolkit.Runtime",
                    "StitchPunk.AnimationToolkit.Authoring",
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
                relativePath = "Tests/EditMode/StitchPunk.AnimationToolkit.Tests.EditMode.asmdef",
                assemblyName = "StitchPunk.AnimationToolkit.Tests.EditMode",
                expectedReferences = new string[]
                {
                    "StitchPunk.AnimationToolkit.Runtime",
                    "StitchPunk.AnimationToolkit.Authoring",
                    "StitchPunk.AnimationToolkit.Editor",
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
                relativePath = "Tests/PlayMode/StitchPunk.AnimationToolkit.Tests.PlayMode.asmdef",
                assemblyName = "StitchPunk.AnimationToolkit.Tests.PlayMode",
                expectedReferences = new string[]
                {
                    "StitchPunk.AnimationToolkit.Runtime",
                    "StitchPunk.AnimationToolkit.Authoring",
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

        // (d) No package file references host-game namespaces (the StitchPunk prefix outside
        //     AnimationToolkit) or host-project asset folder paths.
        [Test]
        public void Conformance_D_NoHostNamespaceOrHostAssetPathReferences()
        {
            // Assembled from fragments so this file's own source never matches the patterns.
            Regex hostNamespacePattern = new Regex("StitchPunk" + "\\.(?!AnimationToolkit)");
            Regex hostAssetPathPattern = new Regex("Asse" + "ts/");
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
                if (hostAssetPathPattern.IsMatch(fileText))
                {
                    violations.Add(relativePath + " (host asset folder path)");
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
            // -- not drift because someone edited the manifest. 0.10.0 is hierarchical billboarding
            // (amendment A44), on top of 0.9.0's editor release.
            Assert.AreEqual("0.10.0", manifest.version, "Version tracks the shipped feature set; 0.10.0 is hierarchical billboarding on top of 0.9.0's editor release.");
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
