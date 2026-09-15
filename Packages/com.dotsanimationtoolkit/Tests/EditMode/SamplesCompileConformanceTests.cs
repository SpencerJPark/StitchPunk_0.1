// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Samples~ is excluded from Unity compilation, so this fixture checks on-disk
    /// asmdef references and using directives against the package's real assemblies.
    /// </summary>
    public sealed class SamplesCompileConformanceTests
    {
        [System.Serializable]
        private sealed class SampleAsmdefContents
        {
            public string name;
            public string[] references;
            public string[] includePlatforms;
        }

        private static readonly Regex NamespaceDeclarationPattern =
            new Regex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline);

        private static readonly Regex UsingDirectivePattern =
            new Regex(@"^\s*using\s+(?:static\s+)?(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?([A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Multiline);

        private static readonly string[] PackageAssemblyFolders =
        {
            "Runtime",
            "Runtime.Physics",
            "Authoring",
            "Editor",
        };

        private static Dictionary<string, List<string>> BuildPackageAssemblyNamespaceMap()
        {
            Dictionary<string, List<string>> assemblyNamespaces = new Dictionary<string, List<string>>();
            foreach (string assemblyFolderName in PackageAssemblyFolders)
            {
                string assemblyFolderPath = Path.Combine(PackagingConformanceTests.PackageRootPath, assemblyFolderName);
                string[] asmdefFiles = Directory.GetFiles(assemblyFolderPath, "*.asmdef", SearchOption.TopDirectoryOnly);
                SampleAsmdefContents asmdefContents = JsonUtility.FromJson<SampleAsmdefContents>(File.ReadAllText(asmdefFiles[0]));

                List<string> declaredNamespaces = new List<string>();
                string[] sourceFiles = Directory.GetFiles(assemblyFolderPath, "*.cs", SearchOption.AllDirectories);
                foreach (string sourceFile in sourceFiles)
                {
                    string strippedText = PackagingConformanceTests.StripComments(File.ReadAllText(sourceFile));
                    foreach (Match namespaceMatch in NamespaceDeclarationPattern.Matches(strippedText))
                    {
                        string declaredNamespace = namespaceMatch.Groups[1].Value;
                        if (!declaredNamespaces.Contains(declaredNamespace))
                        {
                            declaredNamespaces.Add(declaredNamespace);
                        }
                    }
                }

                assemblyNamespaces[asmdefContents.name] = declaredNamespaces;
            }

            return assemblyNamespaces;
        }

        private static List<string> BuildKnownAssemblyNames()
        {
            List<string> knownAssemblyNames = new List<string>();
            foreach (string assemblyFolderName in PackageAssemblyFolders)
            {
                string assemblyFolderPath = Path.Combine(PackagingConformanceTests.PackageRootPath, assemblyFolderName);
                string[] asmdefFiles = Directory.GetFiles(assemblyFolderPath, "*.asmdef", SearchOption.TopDirectoryOnly);
                SampleAsmdefContents asmdefContents = JsonUtility.FromJson<SampleAsmdefContents>(File.ReadAllText(asmdefFiles[0]));
                List<string> definedAndReferencedNames = new List<string> { asmdefContents.name };
                definedAndReferencedNames.AddRange(asmdefContents.references ?? new string[0]);
                foreach (string assemblyName in definedAndReferencedNames)
                {
                    if (!knownAssemblyNames.Contains(assemblyName))
                    {
                        knownAssemblyNames.Add(assemblyName);
                    }
                }
            }

            return knownAssemblyNames;
        }

        private static string[] FindSampleFolders()
        {
            string samplesRootPath = Path.Combine(PackagingConformanceTests.PackageRootPath, "Samples~");
            return Directory.GetDirectories(samplesRootPath);
        }

        private static string MapNonPackageAssemblyToNamespacePrefix(string assemblyName)
        {
            switch (assemblyName)
            {
                case "Unity.Entities.Graphics":
                    return "Unity.Rendering";
                case "Unity.RenderPipelines.Universal.Runtime":
                    return "UnityEngine.Rendering.Universal";
                case "Unity.Mathematics.Extensions":
                    return "Unity.Mathematics";
                case "Unity.Entities.Hybrid":
                    return "Unity.Entities";
                default:
                    return assemblyName;
            }
        }

        [Test]
        public void EverySampleAsmdef_ReferencesOnlyKnownAssemblies()
        {
            List<string> knownAssemblyNames = BuildKnownAssemblyNames();

            List<string> violations = new List<string>();
            string[] sampleFolderPaths = FindSampleFolders();
            Assert.IsTrue(sampleFolderPaths.Length > 0, "Expected at least one sample folder under Samples~.");

            foreach (string sampleFolderPath in sampleFolderPaths)
            {
                string sampleFolderName = new DirectoryInfo(sampleFolderPath).Name;
                string[] asmdefFiles = Directory.GetFiles(sampleFolderPath, "*.asmdef", SearchOption.AllDirectories);
                if (asmdefFiles.Length != 1)
                {
                    violations.Add(sampleFolderName + ": expected exactly one asmdef, found " + asmdefFiles.Length);
                    continue;
                }

                SampleAsmdefContents asmdefContents = JsonUtility.FromJson<SampleAsmdefContents>(File.ReadAllText(asmdefFiles[0]));
                string[] asmdefReferences = asmdefContents.references ?? new string[0];
                foreach (string referenceName in asmdefReferences)
                {
                    if (!knownAssemblyNames.Contains(referenceName))
                    {
                        violations.Add(PackagingConformanceTests.ToPackageRelativePath(asmdefFiles[0]) + ": unknown reference '" + referenceName + "'");
                    }
                }
            }

            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        [Test]
        public void EverySampleSource_UsesOnlyResolvableNamespaces()
        {
            Dictionary<string, List<string>> packageAssemblyNamespaces = BuildPackageAssemblyNamespaceMap();

            List<string> violations = new List<string>();
            int scannedSourceFileCount = 0;
            string[] sampleFolderPaths = FindSampleFolders();

            foreach (string sampleFolderPath in sampleFolderPaths)
            {
                string[] asmdefFiles = Directory.GetFiles(sampleFolderPath, "*.asmdef", SearchOption.AllDirectories);
                if (asmdefFiles.Length != 1)
                {
                    continue;
                }

                SampleAsmdefContents asmdefContents = JsonUtility.FromJson<SampleAsmdefContents>(File.ReadAllText(asmdefFiles[0]));
                string[] asmdefReferences = asmdefContents.references ?? new string[0];
                string[] includePlatforms = asmdefContents.includePlatforms ?? new string[0];
                bool isEditorOnlySample = new List<string>(includePlatforms).Contains("Editor");

                // Package namespaces are matched exactly on purpose: a using directive for a
                // sub-namespace that does not exist (e.g. DotsAnimationToolkit.DoesNotExist)
                // must still fail even though the parent namespace DotsAnimationToolkit does.
                List<string> exactAllowedNamespaces = new List<string>();
                List<string> prefixAllowedNamespaces = new List<string> { "System", "UnityEngine" };
                if (isEditorOnlySample)
                {
                    prefixAllowedNamespaces.Add("UnityEditor");
                }

                foreach (string referenceName in asmdefReferences)
                {
                    if (packageAssemblyNamespaces.TryGetValue(referenceName, out List<string> declaredNamespaces))
                    {
                        exactAllowedNamespaces.AddRange(declaredNamespaces);
                    }
                    else
                    {
                        prefixAllowedNamespaces.Add(MapNonPackageAssemblyToNamespacePrefix(referenceName));
                    }
                }

                string[] sampleSourceFiles = Directory.GetFiles(sampleFolderPath, "*.cs", SearchOption.AllDirectories);
                foreach (string sampleSourceFile in sampleSourceFiles)
                {
                    string strippedText = PackagingConformanceTests.StripComments(File.ReadAllText(sampleSourceFile));
                    foreach (Match namespaceMatch in NamespaceDeclarationPattern.Matches(strippedText))
                    {
                        exactAllowedNamespaces.Add(namespaceMatch.Groups[1].Value);
                    }
                }

                foreach (string sampleSourceFile in sampleSourceFiles)
                {
                    scannedSourceFileCount++;
                    string strippedText = PackagingConformanceTests.StripComments(File.ReadAllText(sampleSourceFile));
                    foreach (Match usingMatch in UsingDirectivePattern.Matches(strippedText))
                    {
                        string usedNamespace = usingMatch.Groups[1].Value;
                        if (!IsNamespaceResolvable(usedNamespace, exactAllowedNamespaces, prefixAllowedNamespaces))
                        {
                            violations.Add(PackagingConformanceTests.ToPackageRelativePath(sampleSourceFile) + ": 'using " + usedNamespace + ";' does not resolve in the assemblies its asmdef references");
                        }
                    }
                }
            }

            Assert.IsTrue(scannedSourceFileCount > 0, "Expected at least one sample .cs file to be scanned.");
            Assert.IsEmpty(violations, string.Join("\n", violations));
        }

        private static bool IsNamespaceResolvable(string usedNamespace, List<string> exactAllowedNamespaces, List<string> prefixAllowedNamespaces)
        {
            if (exactAllowedNamespaces.Contains(usedNamespace))
            {
                return true;
            }

            foreach (string allowedPrefix in prefixAllowedNamespaces)
            {
                if (usedNamespace == allowedPrefix || usedNamespace.StartsWith(allowedPrefix + "."))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
