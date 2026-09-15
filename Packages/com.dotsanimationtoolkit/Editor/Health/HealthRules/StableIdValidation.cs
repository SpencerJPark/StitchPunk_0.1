// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Health rule for toolkit assets whose stable id exists only in memory and would re-mint on the next load.</summary>
    public static class StableIdValidation
    {
        public static void EvaluateUnpersistedStableIds(HealthScanContext context, List<HealthFinding> output)
        {
            EvaluateList(context.clips, output);
            EvaluateList(context.clipSets, output);
            EvaluateList(context.rigs, output);
            EvaluateList(context.profiles, output);
            EvaluateList(context.cutscenes, output);
            EvaluateList(context.vatTextureSets, output);
        }

        private static void EvaluateList(IReadOnlyList<Object> assets, List<HealthFinding> output)
        {
            foreach (Object asset in assets)
            {
                if (asset == null)
                {
                    continue;
                }

                IStableIdMintReporter reporter = asset as IStableIdMintReporter;
                if (reporter == null || !reporter.HasUnpersistedStableId)
                {
                    continue;
                }

                HealthFinding finding = new HealthFinding();
                finding.severity = HealthSeverity.Warning;
                finding.code = HealthFinding.UnpersistedStableIdCode;
                finding.message = asset.GetType().Name + " '" + asset.name + "' has a stable id that is not saved yet; it re-mints on the next load.";
                finding.title = "Stable id not saved";
                finding.detail = "The id re-mints on the next load and breaks references to it.";
                finding.target = asset;

                Object capturedAsset = asset;
                IStableIdMintReporter capturedReporter = reporter;
                HealthFindingAction saveAction = new HealthFindingAction();
                saveAction.label = "Save";
                saveAction.description = "Saves the asset so its stable id persists.";
                saveAction.run = delegate
                {
                    EditorUtility.SetDirty(capturedAsset);
                    AssetDatabase.SaveAssetIfDirty(capturedAsset);
                    if (!EditorUtility.IsDirty(capturedAsset))
                    {
                        capturedReporter.MarkStableIdPersisted();
                    }
                };
                finding.actions.Add(saveAction);
                finding.actions.Add(HealthFindingAction.Locate("Locate", "Selects and pings the asset.", asset));

                output.Add(finding);
            }
        }
    }
}
