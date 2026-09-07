// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using System.Reflection;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the regression Phase E Task 2 fixes: <see cref="TargetTagRegistry.CreateVocabularyEntry"/>
    /// only mints a row in memory (it cannot reference <c>UnityEditor</c>), so every editor-side
    /// call site that adds a row must persist it itself or the row is lost on the next domain
    /// reload. Exercises the real <c>TargetTagRegistryEditor.AddEntry</c> button handler through
    /// reflection, then re-reads <c>ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset</c>
    /// into a brand new instance — never the cached singleton — the same way a fresh domain reload
    /// would load it.
    /// </summary>
    public sealed class VocabularyRegistryPersistenceTests
    {
        private const string TargetTagFilePath = "ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset";
        private const string AnimationNameFilePath = "ProjectSettings/DotsAnimationToolkitAnimationNameRegistry.asset";

        [Test]
        public void AddEntry_ThroughTheRegistryEditor_SurvivesReReadingTheSettingsFileFromDisk()
        {
            bool fileExistedBefore = File.Exists(TargetTagFilePath);
            TargetTagRegistry registry = VocabularyRegistryProvider.TargetTags;
            UnityEditor.Editor registryEditor = UnityEditor.Editor.CreateEditor(registry);
            uint mintedId = 0u;

            try
            {
                MethodInfo addEntryMethod = typeof(TargetTagRegistryEditor).GetMethod(
                    "AddEntry", BindingFlags.NonPublic | BindingFlags.Instance);
                addEntryMethod.Invoke(registryEditor, null);

                Assert.Greater(registry.entries.Count, 0, "AddEntry must append a row.");
                TargetTagEntry mintedEntry = registry.entries[registry.entries.Count - 1];
                Assert.AreEqual("NewTag", mintedEntry.name);
                mintedId = mintedEntry.stableId;

                Assert.IsTrue(
                    File.Exists(TargetTagFilePath),
                    "AddEntry must persist to ProjectSettings/, not merely mutate the in-memory row.");

                TargetTagRegistry freshFromDisk = ScriptableObject.CreateInstance<TargetTagRegistry>();
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(TargetTagFilePath), freshFromDisk);

                    Assert.AreEqual(
                        "NewTag",
                        freshFromDisk.FindName(mintedId),
                        "A fresh instance loaded from disk — simulating what the next domain reload " +
                            "would read — must already carry the row AddEntry just created.");
                }
                finally
                {
                    Object.DestroyImmediate(freshFromDisk);
                }
            }
            finally
            {
                registry.entries.RemoveAll(entry => entry.stableId == mintedId);
                VocabularyRegistryProvider.PersistVocabulary(registry);
                if (!fileExistedBefore && registry.entries.Count == 0 && File.Exists(TargetTagFilePath))
                {
                    File.Delete(TargetTagFilePath);
                }
                Object.DestroyImmediate(registryEditor);
            }
        }

        // Mirrors the target-tag test above for the third vocabulary, AnimationNameRegistry —
        // added by A70-T2, its CreateVocabularyEntry has the same in-memory-only mint that requires
        // an explicit persist from the editor-side caller.
        [Test]
        public void AddEntry_ThroughTheAnimationNameRegistryEditor_SurvivesReReadingTheSettingsFileFromDisk()
        {
            bool fileExistedBefore = File.Exists(AnimationNameFilePath);
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            UnityEditor.Editor registryEditor = UnityEditor.Editor.CreateEditor(registry);
            uint mintedId = 0u;

            try
            {
                MethodInfo addEntryMethod = typeof(AnimationNameRegistryEditor).GetMethod(
                    "AddEntry", BindingFlags.NonPublic | BindingFlags.Instance);
                addEntryMethod.Invoke(registryEditor, null);

                Assert.Greater(registry.entries.Count, 0, "AddEntry must append a row.");
                AnimationNameEntry mintedEntry = registry.entries[registry.entries.Count - 1];
                Assert.AreEqual("NewAnimation", mintedEntry.name);
                mintedId = mintedEntry.animationKey;

                Assert.IsTrue(
                    File.Exists(AnimationNameFilePath),
                    "AddEntry must persist to ProjectSettings/, not merely mutate the in-memory row.");

                AnimationNameRegistry freshFromDisk = ScriptableObject.CreateInstance<AnimationNameRegistry>();
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(AnimationNameFilePath), freshFromDisk);

                    Assert.AreEqual(
                        "NewAnimation",
                        freshFromDisk.FindName(mintedId),
                        "A fresh instance loaded from disk — simulating what the next domain reload " +
                            "would read — must already carry the row AddEntry just created.");
                }
                finally
                {
                    Object.DestroyImmediate(freshFromDisk);
                }
            }
            finally
            {
                registry.entries.RemoveAll(entry => entry.animationKey == mintedId);
                VocabularyRegistryProvider.PersistVocabulary(registry);
                if (!fileExistedBefore && registry.entries.Count == 0 && File.Exists(AnimationNameFilePath))
                {
                    File.Delete(AnimationNameFilePath);
                }
                Object.DestroyImmediate(registryEditor);
            }
        }
    }
}
