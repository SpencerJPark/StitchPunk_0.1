using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PlaytestCopilot.Editor
{
    /// The Phase 1 deliverable that makes the tool useful before any LLM exists: one markdown file
    /// a developer pastes into any coding agent, with no follow-up questions needed and no knowledge
    /// that this tool exists. Build/WriteToFile is the only place this document gets formatted.
    public static class PlaytestAgentMarkdownExport
    {
        // Below this top reference confidence the note says so in words, so an agent asks rather
        // than editing the wrong file on a guess it cannot see is shaky.
        private const float LowConfidenceThreshold = 0.6f;

        public static string Build(PlaytestSessionDescriptor descriptor, List<PlaytestMarker> markers, string sessionFolder)
        {
            StringBuilder markdown = new StringBuilder();
            AppendHeader(markdown, descriptor);

            if (markers == null || markers.Count == 0)
            {
                markdown.Append("## No notes recorded\n\n");
                markdown.Append("This session recorded no notes. There is nothing to act on.\n\n");
            }
            else
            {
                for (int noteIndex = 0; noteIndex < markers.Count; noteIndex++)
                {
                    AppendNoteSection(markdown, markers[noteIndex], noteIndex, sessionFolder);
                }
            }

            AppendClosing(markdown);
            return markdown.ToString();
        }

        public static void WriteToFile(string absolutePath, PlaytestSessionDescriptor descriptor,
                                        List<PlaytestMarker> markers, string sessionFolder)
        {
            string markdown = Build(descriptor, markers, sessionFolder);
            string directoryPath = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(absolutePath, markdown);
        }

        private static void AppendHeader(StringBuilder markdown, PlaytestSessionDescriptor descriptor)
        {
            string unityVersion = descriptor != null && !string.IsNullOrEmpty(descriptor.UnityVersion) ? descriptor.UnityVersion : "unknown";
            string scenePath = descriptor != null && !string.IsNullOrEmpty(descriptor.ScenePath) ? descriptor.ScenePath : "unknown";
            string gitCommit = descriptor != null && !string.IsNullOrEmpty(descriptor.GitCommit) ? descriptor.GitCommit : "no git repository detected";
            string sessionId = descriptor != null && !string.IsNullOrEmpty(descriptor.SessionId) ? descriptor.SessionId : "unknown";
            string startedUtc = descriptor != null && !string.IsNullOrEmpty(descriptor.StartedUtc) ? descriptor.StartedUtc : "unknown";
            string endedUtc = descriptor != null && !string.IsNullOrEmpty(descriptor.EndedUtc) ? descriptor.EndedUtc : "unknown";

            markdown.Append("# Playtest Session Notes For Agent\n\n");
            markdown.Append("**Unity version:** ").Append(unityVersion).Append("\n\n");
            markdown.Append("**Scene played:** ").Append(scenePath).Append("\n\n");
            markdown.Append("**Git commit:** ").Append(gitCommit).Append("\n\n");
            markdown.Append("**Session:** ").Append(sessionId).Append(" (").Append(startedUtc).Append(" to ").Append(endedUtc).Append(")\n\n");
            markdown.Append("This is a transcript of a playtest session: every note below is something the tester said or typed ");
            markdown.Append("while playing, with the game objects it refers to already resolved to hierarchy paths and prefab ");
            markdown.Append("asset paths, alongside the confidence of that resolution and the tracked state captured at that moment.\n\n");
        }

        private static void AppendNoteSection(StringBuilder markdown, PlaytestMarker marker, int noteIndex, string sessionFolder)
        {
            string noteId = marker != null && !string.IsNullOrEmpty(marker.Id) ? marker.Id : PlaytestMarkerId.For(noteIndex);
            markdown.Append("## Note ").Append((noteIndex + 1).ToString(CultureInfo.InvariantCulture)).Append(" \u2014 ").Append(noteId).Append("\n\n");
            markdown.Append("**When:** ").Append(FormatTimeRange(marker)).Append("\n\n");

            string transcriptText = marker != null && !string.IsNullOrEmpty(marker.TranscriptText) ? marker.TranscriptText : "(none)";
            string typedNote = marker != null && !string.IsNullOrEmpty(marker.TypedNote) ? marker.TypedNote : "(none)";
            string intent = marker != null && !string.IsNullOrEmpty(marker.Intent) ? marker.Intent : PlaytestNoteIntent.Unclassified.ToString();

            markdown.Append("**Said:** ").Append(transcriptText).Append("\n\n");
            markdown.Append("**Typed note:** ").Append(typedNote).Append("\n\n");
            markdown.Append("**Intent:** ").Append(intent).Append("\n\n");

            AppendReferences(markdown, marker);
            AppendState(markdown, marker);
            AppendImage(markdown, marker, sessionFolder);
        }

        private static string FormatTimeRange(PlaytestMarker marker)
        {
            if (marker == null)
            {
                return "unknown";
            }

            string startSeconds = marker.Start.SecondsSinceSessionStart.ToString("F2", CultureInfo.InvariantCulture);
            string endSeconds = marker.End.SecondsSinceSessionStart.ToString("F2", CultureInfo.InvariantCulture);
            string startFrame = marker.Start.FrameIndex.ToString(CultureInfo.InvariantCulture);
            string endFrame = marker.End.FrameIndex.ToString(CultureInfo.InvariantCulture);

            return string.Format(CultureInfo.InvariantCulture, "{0}s to {1}s (frames {2} to {3})",
                                  startSeconds, endSeconds, startFrame, endFrame);
        }

        private static void AppendReferences(StringBuilder markdown, PlaytestMarker marker)
        {
            List<PlaytestObjectReference> references = marker != null ? marker.References : null;
            markdown.Append("**Objects referenced:**\n\n");

            if (references == null || references.Count == 0)
            {
                markdown.Append("No object could be resolved for this note. Treat it as free-floating feedback with no target.\n\n");
                return;
            }

            float topConfidence = 0f;
            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                if (references[referenceIndex].Confidence > topConfidence)
                {
                    topConfidence = references[referenceIndex].Confidence;
                }
            }

            if (topConfidence < LowConfidenceThreshold)
            {
                markdown.Append("The referenced object is uncertain (top confidence ")
                        .Append(topConfidence.ToString("F2", CultureInfo.InvariantCulture))
                        .Append("). Confirm what this note refers to before acting on it.\n\n");
            }

            markdown.Append("| Object | Hierarchy path | Prefab asset path | Confidence | Resolved by |\n");
            markdown.Append("| --- | --- | --- | --- | --- |\n");

            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                PlaytestObjectReference reference = references[referenceIndex];
                string objectName = EscapeTableCell(reference.ObjectName);
                string hierarchyPath = EscapeTableCell(reference.HierarchyPath);
                string prefabAssetPath = string.IsNullOrEmpty(reference.PrefabAssetPath)
                    ? "(not a prefab instance)"
                    : EscapeTableCell(reference.PrefabAssetPath);
                string confidence = reference.Confidence.ToString("F2", CultureInfo.InvariantCulture);
                string resolvedBy = EscapeTableCell(reference.ResolvedBy);

                markdown.Append("| ").Append(objectName)
                        .Append(" | ").Append(hierarchyPath)
                        .Append(" | ").Append(prefabAssetPath)
                        .Append(" | ").Append(confidence)
                        .Append(" | ").Append(resolvedBy)
                        .Append(" |\n");
            }

            markdown.Append("\n");
        }

        private static void AppendState(StringBuilder markdown, PlaytestMarker marker)
        {
            List<PlaytestStateField> stateFields = marker != null ? marker.State : null;
            markdown.Append("**Tracked state at this moment:**\n\n");

            if (stateFields == null || stateFields.Count == 0)
            {
                markdown.Append("No tracked state was captured for this note.\n\n");
                return;
            }

            markdown.Append("| Field | Value |\n");
            markdown.Append("| --- | --- |\n");

            for (int fieldIndex = 0; fieldIndex < stateFields.Count; fieldIndex++)
            {
                PlaytestStateField stateField = stateFields[fieldIndex];
                markdown.Append("| ").Append(EscapeTableCell(stateField.Name))
                        .Append(" | ").Append(EscapeTableCell(stateField.Value))
                        .Append(" |\n");
            }

            markdown.Append("\n");
        }

        private static void AppendImage(StringBuilder markdown, PlaytestMarker marker, string sessionFolder)
        {
            if (marker == null || !marker.HasAnnotation || string.IsNullOrEmpty(sessionFolder))
            {
                markdown.Append("**Image:** none captured for this note.\n\n");
                return;
            }

            string markerId = !string.IsNullOrEmpty(marker.Id) ? marker.Id : PlaytestMarkerId.For(marker.Index);
            string combinedAbsolutePath = PlaytestSessionPaths.NoteCombinedFile(sessionFolder, markerId);
            string frameAbsolutePath = PlaytestSessionPaths.NoteFrameFile(sessionFolder, markerId);

            string imageAbsolutePath = File.Exists(combinedAbsolutePath) ? combinedAbsolutePath : frameAbsolutePath;
            if (!File.Exists(imageAbsolutePath))
            {
                markdown.Append("**Image:** none captured for this note.\n\n");
                return;
            }

            string imageRelativePath = PlaytestSessionPaths.RelativeFromSessionFolder(sessionFolder, imageAbsolutePath);
            markdown.Append("**Image:** [").Append(imageRelativePath).Append("](").Append(imageRelativePath).Append(")");
            markdown.Append(" \u2014 reference only, do not rely on it: everything needed is in the words above.\n\n");
        }

        private static void AppendClosing(StringBuilder markdown)
        {
            markdown.Append("## What to do with this\n\n");
            markdown.Append("These are raw playtest notes. Each one needs a change proposed and reviewed before it is implemented \u2014 ");
            markdown.Append("do not edit code straight from this file without proposing the change first.\n");
        }

        private static string EscapeTableCell(string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue))
            {
                return string.Empty;
            }

            return rawValue.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }
    }
}
