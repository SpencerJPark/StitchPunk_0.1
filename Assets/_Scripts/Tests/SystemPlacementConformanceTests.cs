using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace StitchPunk.Tests
{
    // Structural conformance gate for Assets/_Scripts/Systems/ (see SystemGroups.cs header and
    // _Vault/Tasks/Claude/Structural_Review_2026-07.md):
    //   1. Every system file declares [UpdateInGroup] — a forgotten attribute silently
    //      auto-creates the system at an arbitrary point in SimulationSystemGroup.
    //   2. A system file lives in the folder named after the group it updates in, so the
    //      folder tree IS the group tree (documented exemptions below).
    //   3. Every ComponentSystemGroup subclass is declared in SystemGroups.cs — the manifest.
    //   4. No commented-out system corpses parked in Systems/ — dead code goes to Core/Unused.
    [TestFixture]
    public sealed class SystemPlacementConformanceTests
    {
        private static readonly Regex SystemDeclarationRegex = new Regex(
            @"partial\s+(?:struct|class)\s+(\w+)\s*:\s*(?:ISystem|SystemBase)",
            RegexOptions.Compiled);

        private static readonly Regex UpdateInGroupRegex = new Regex(
            @"\[UpdateInGroup\(typeof\((\w+)\)",
            RegexOptions.Compiled);

        private static readonly Regex CommentedOutSystemRegex = new Regex(
            @"//\s*(?:public\s+)?partial\s+(?:struct|class)\s+\w+\s*:\s*(?:ISystem|SystemBase)",
            RegexOptions.Compiled);

        // Files allowed to update in a group that does not match their parent folder.
        // Every entry needs a reason — grow this list only with the same scrutiny as a
        // suppression attribute.
        private static readonly Dictionary<string, string> PlacementExemptions =
            new Dictionary<string, string>
            {
                // Physics-driven ragdoll sync must run after all simulation transforms settle;
                // it lives with the Health feature that owns ragdoll lifecycle.
                { "Ragdoll2DSystem.cs", "LateSimulationSystemGroup" },
                // Presentation-only marker/selection visuals deliberately run after simulation.
                { "OrderMarkerSystem.cs", "LateSimulationSystemGroup" },
                { "SelectedVisualSystem.cs", "LateSimulationSystemGroup" },
            };

        private static string SystemsRootPath
        {
            get { return Path.Combine(Application.dataPath, "_Scripts", "Systems"); }
        }

        private static string ManifestPath
        {
            get { return Path.Combine(SystemsRootPath, "SystemGroups.cs"); }
        }

        // Strips full-line comments so commented-out code is not miscounted as live code.
        private static string ReadLiveCode(string filePath)
        {
            string[] rawLines = File.ReadAllLines(filePath);
            List<string> liveLines = new List<string>(rawLines.Length);
            foreach (string rawLine in rawLines)
            {
                if (rawLine.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                liveLines.Add(rawLine);
            }
            return string.Join("\n", liveLines);
        }

        private static IEnumerable<string> AllSystemSourceFiles()
        {
            foreach (string filePath in Directory.GetFiles(SystemsRootPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(filePath) == "SystemGroups.cs") continue;
                yield return filePath;
            }
        }

        [Test]
        public void EverySystemFileDeclaresAnUpdateGroup()
        {
            List<string> offenders = new List<string>();
            foreach (string filePath in AllSystemSourceFiles())
            {
                string liveCode = ReadLiveCode(filePath);
                int systemCount = SystemDeclarationRegex.Matches(liveCode).Count;
                if (systemCount == 0) continue; // utility/data files are allowed in Systems/
                int groupAttributeCount = UpdateInGroupRegex.Matches(liveCode).Count;
                if (groupAttributeCount < systemCount)
                {
                    offenders.Add(Path.GetFileName(filePath)
                        + " declares " + systemCount + " system(s) but " + groupAttributeCount
                        + " [UpdateInGroup] attribute(s) — missing attributes auto-create in SimulationSystemGroup");
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void SystemFileFolderMatchesItsUpdateGroup()
        {
            List<string> offenders = new List<string>();
            foreach (string filePath in AllSystemSourceFiles())
            {
                string liveCode = ReadLiveCode(filePath);
                string fileName = Path.GetFileName(filePath);
                string parentFolderName = new DirectoryInfo(Path.GetDirectoryName(filePath)).Name;

                foreach (Match groupMatch in UpdateInGroupRegex.Matches(liveCode))
                {
                    string targetGroupName = groupMatch.Groups[1].Value;
                    if (targetGroupName == parentFolderName) continue;

                    string exemptedGroupName;
                    bool exempted = PlacementExemptions.TryGetValue(fileName, out exemptedGroupName)
                        && exemptedGroupName == targetGroupName;
                    if (exempted) continue;

                    offenders.Add(fileName + " updates in " + targetGroupName
                        + " but lives in folder " + parentFolderName);
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void EveryComponentSystemGroupIsDeclaredInTheManifest()
        {
            string manifestSource = File.ReadAllText(ManifestPath);
            Assembly systemsAssembly = typeof(GameManagerSystemGroup).Assembly;

            List<string> offenders = new List<string>();
            foreach (Type candidateType in systemsAssembly.GetTypes())
            {
                if (!typeof(ComponentSystemGroup).IsAssignableFrom(candidateType)) continue;
                if (candidateType.IsAbstract) continue;
                if (!manifestSource.Contains("class " + candidateType.Name + " "))
                {
                    offenders.Add(candidateType.Name + " is a ComponentSystemGroup but is not declared in SystemGroups.cs");
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void EverySystemTypeCarriesAnExplicitGroupAttribute()
        {
            Assembly systemsAssembly = typeof(GameManagerSystemGroup).Assembly;

            List<string> offenders = new List<string>();
            foreach (Type candidateType in systemsAssembly.GetTypes())
            {
                if (candidateType.IsAbstract || candidateType.ContainsGenericParameters) continue;

                bool isUnmanagedSystem = typeof(ISystem).IsAssignableFrom(candidateType) && candidateType.IsValueType;
                bool isManagedSystem = typeof(ComponentSystemBase).IsAssignableFrom(candidateType)
                    && !typeof(ComponentSystemGroup).IsAssignableFrom(candidateType);
                if (!isUnmanagedSystem && !isManagedSystem) continue;

                bool hasGroup = Attribute.IsDefined(candidateType, typeof(UpdateInGroupAttribute));
                bool optedOut = Attribute.IsDefined(candidateType, typeof(DisableAutoCreationAttribute));
                if (!hasGroup && !optedOut)
                {
                    offenders.Add(candidateType.Name
                        + " has neither [UpdateInGroup] nor [DisableAutoCreation] — it will auto-create in SimulationSystemGroup");
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void NoCommentedOutSystemsParkedInSystemsFolder()
        {
            List<string> offenders = new List<string>();
            foreach (string filePath in AllSystemSourceFiles())
            {
                string rawSource = File.ReadAllText(filePath);
                if (CommentedOutSystemRegex.IsMatch(rawSource))
                {
                    offenders.Add(Path.GetFileName(filePath)
                        + " contains a commented-out system declaration — park dead systems in Core/Unused/ instead");
                }
            }
            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }
    }
}
