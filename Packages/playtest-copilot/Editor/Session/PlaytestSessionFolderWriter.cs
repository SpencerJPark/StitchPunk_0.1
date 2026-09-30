using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// Owns the session folder on disk: the one place that turns bus events into files a developer
    /// can read by hand. Subscribes for the lifetime of the Editor process, not just while a session
    /// is open, so every handler must no-op quietly when CurrentSessionFolder is empty.
    [InitializeOnLoad]
    public static class PlaytestSessionFolderWriter
    {
        private const int StateLogFlushLineInterval = 20;
        private const string AgentMarkdownFileName = "session-for-agent.md";

        private static readonly List<PlaytestMarker> completedMarkers = new List<PlaytestMarker>();

        /// Circled regions from an annotation that arrived before its marker closed, keyed by
        /// marker id. Applied to the marker the moment MarkerCompleted fires for that id.
        private static readonly Dictionary<string, List<PlaytestScreenRegion>> pendingCircledRegionsByMarkerId =
            new Dictionary<string, List<PlaytestScreenRegion>>();

        private static string currentSessionFolder = string.Empty;
        private static PlaytestSessionDescriptor currentDescriptor;
        private static StreamWriter stateLogWriter;
        private static int stateLogLinesSinceFlush;

        public static string CurrentSessionFolder => currentSessionFolder;

        public static List<PlaytestMarker> CompletedMarkers => completedMarkers;

        static PlaytestSessionFolderWriter()
        {
            PlaytestCaptureBus.MarkerCompleted += HandleMarkerCompleted;
            PlaytestCaptureBus.AnnotationCaptured += HandleAnnotationCaptured;
            PlaytestCaptureBus.StateSampleRecorded += HandleStateSampleRecorded;
            PlaytestCaptureBus.AudioFileWritten += HandleAudioFileWritten;
        }

        public static void BeginSession(PlaytestSessionDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            string sessionsRootAbsolutePath = PlaytestCopilotSettings.Current.ResolvedSessionsRoot;
            string sessionFolder = PlaytestSessionPaths.SessionFolder(sessionsRootAbsolutePath, descriptor.SessionId);

            Directory.CreateDirectory(sessionFolder);
            Directory.CreateDirectory(PlaytestSessionPaths.NotesFolder(sessionFolder));
            Directory.CreateDirectory(PlaytestSessionPaths.SpecsFolder(sessionFolder));
            Directory.CreateDirectory(PlaytestSessionPaths.TasksFolder(sessionFolder));

            descriptor.AbsoluteFolderPath = sessionFolder;

            currentDescriptor = descriptor;
            currentSessionFolder = sessionFolder;
            completedMarkers.Clear();
            pendingCircledRegionsByMarkerId.Clear();

            WriteSessionDescriptorFile();

            // Single StreamWriter for the whole session: opening/closing per line stalls the Editor.
            stateLogWriter = new StreamWriter(PlaytestSessionPaths.StateLogFile(sessionFolder), false);
            stateLogLinesSinceFlush = 0;
        }

        public static void EndSession()
        {
            if (string.IsNullOrEmpty(currentSessionFolder))
            {
                return;
            }

            try
            {
                if (currentDescriptor != null)
                {
                    currentDescriptor.EndedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                if (stateLogWriter != null)
                {
                    stateLogWriter.Flush();
                    stateLogWriter.Dispose();
                    stateLogWriter = null;
                }
            }

            WriteSessionDescriptorFile();

            PlaytestSessionIndexWriter.Write(currentSessionFolder, currentDescriptor, completedMarkers);

            string agentMarkdownAbsolutePath =
                PlaytestSessionPaths.Normalise(Path.Combine(currentSessionFolder, AgentMarkdownFileName));
            PlaytestAgentMarkdownExport.WriteToFile(
                agentMarkdownAbsolutePath, currentDescriptor, completedMarkers, currentSessionFolder);

            currentSessionFolder = string.Empty;
            currentDescriptor = null;
            pendingCircledRegionsByMarkerId.Clear();
        }

        private static void HandleMarkerCompleted(PlaytestMarker marker)
        {
            if (string.IsNullOrEmpty(currentSessionFolder) || marker == null)
            {
                return;
            }

            // The annotation can arrive first, since the user draws and then keeps talking.
            if (pendingCircledRegionsByMarkerId.TryGetValue(marker.Id, out List<PlaytestScreenRegion> pendingRegions))
            {
                marker.HasAnnotation = true;
                marker.CircledRegions = pendingRegions;
                pendingCircledRegionsByMarkerId.Remove(marker.Id);
            }

            completedMarkers.Add(marker);

            Directory.CreateDirectory(PlaytestSessionPaths.NoteFolder(currentSessionFolder, marker.Id));
            File.WriteAllText(
                PlaytestSessionPaths.NoteDescriptorFile(currentSessionFolder, marker.Id),
                JsonUtility.ToJson(marker, true));
            File.WriteAllText(
                PlaytestSessionPaths.NoteReferencesFile(currentSessionFolder, marker.Id),
                BuildReferencesMarkdown(marker));
        }

        private static void HandleAnnotationCaptured(PlaytestAnnotationCapture capture)
        {
            if (string.IsNullOrEmpty(currentSessionFolder) || capture == null || string.IsNullOrEmpty(capture.MarkerId))
            {
                return;
            }

            Directory.CreateDirectory(PlaytestSessionPaths.NoteFolder(currentSessionFolder, capture.MarkerId));

            WritePngIfPresent(PlaytestSessionPaths.NoteFrameFile(currentSessionFolder, capture.MarkerId), capture.FramePng);
            WritePngIfPresent(PlaytestSessionPaths.NoteAnnotationFile(currentSessionFolder, capture.MarkerId), capture.AnnotationPng);
            WritePngIfPresent(PlaytestSessionPaths.NoteCombinedFile(currentSessionFolder, capture.MarkerId), capture.CombinedPng);

            List<PlaytestScreenRegion> circledRegions = capture.CircledRegions ?? new List<PlaytestScreenRegion>();
            PlaytestMarker matchingMarker = FindCompletedMarker(capture.MarkerId);
            if (matchingMarker != null)
            {
                // Marker already closed and its note.json already written: rewrite it now that it
                // carries the drawing.
                matchingMarker.HasAnnotation = true;
                matchingMarker.CircledRegions = circledRegions;
                File.WriteAllText(
                    PlaytestSessionPaths.NoteDescriptorFile(currentSessionFolder, capture.MarkerId),
                    JsonUtility.ToJson(matchingMarker, true));
            }
            else
            {
                // Marker has not closed yet; stash the regions and apply them in HandleMarkerCompleted.
                pendingCircledRegionsByMarkerId[capture.MarkerId] = circledRegions;
            }
        }

        private static void HandleStateSampleRecorded(PlaytestStateSample sample)
        {
            if (string.IsNullOrEmpty(currentSessionFolder) || stateLogWriter == null || sample == null)
            {
                return;
            }

            stateLogWriter.WriteLine(JsonUtility.ToJson(sample, false));
            stateLogLinesSinceFlush++;
            if (stateLogLinesSinceFlush >= StateLogFlushLineInterval)
            {
                stateLogWriter.Flush();
                stateLogLinesSinceFlush = 0;
            }
        }

        private static void HandleAudioFileWritten(string absoluteAudioPath)
        {
            // The microphone recorder writes straight to PlaytestSessionPaths.AudioFile(sessionFolder);
            // the descriptor has no field for this path, so there is nothing left to copy or record.
            if (string.IsNullOrEmpty(currentSessionFolder))
            {
                return;
            }
        }

        private static PlaytestMarker FindCompletedMarker(string markerId)
        {
            for (int markerIndex = 0; markerIndex < completedMarkers.Count; markerIndex++)
            {
                if (string.Equals(completedMarkers[markerIndex].Id, markerId, StringComparison.Ordinal))
                {
                    return completedMarkers[markerIndex];
                }
            }

            return null;
        }

        private static void WritePngIfPresent(string absolutePath, byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return;
            }

            File.WriteAllBytes(absolutePath, pngBytes);
        }

        private static string BuildReferencesMarkdown(PlaytestMarker marker)
        {
            StringBuilder markdownBuilder = new StringBuilder();
            markdownBuilder.AppendLine("# References for " + marker.Id);
            markdownBuilder.AppendLine();

            if (marker.References == null || marker.References.Count == 0)
            {
                markdownBuilder.AppendLine("No object references resolved for this note.");
                return markdownBuilder.ToString();
            }

            for (int referenceIndex = 0; referenceIndex < marker.References.Count; referenceIndex++)
            {
                PlaytestObjectReference objectReference = marker.References[referenceIndex];
                string prefabAssetPathDisplay = string.IsNullOrEmpty(objectReference.PrefabAssetPath)
                    ? "(not a prefab instance)"
                    : objectReference.PrefabAssetPath;

                markdownBuilder.AppendLine("- **" + objectReference.ObjectName + "**");
                markdownBuilder.AppendLine("  - Hierarchy path: " + objectReference.HierarchyPath);
                markdownBuilder.AppendLine("  - Prefab asset path: " + prefabAssetPathDisplay);
                markdownBuilder.AppendLine(
                    "  - Confidence: " + objectReference.Confidence.ToString("F2", CultureInfo.InvariantCulture));
                markdownBuilder.AppendLine("  - Resolved by: " + objectReference.ResolvedBy);
            }

            return markdownBuilder.ToString();
        }

        private static void WriteSessionDescriptorFile()
        {
            File.WriteAllText(
                PlaytestSessionPaths.SessionDescriptorFile(currentSessionFolder),
                JsonUtility.ToJson(currentDescriptor, true));
        }
    }
}
