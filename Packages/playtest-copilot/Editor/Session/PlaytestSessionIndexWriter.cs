using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PlaytestCopilot.Editor
{
    /// Writes index.md: the single file the spec promises you can review a whole playtest
    /// session from with Unity closed. Every path in it is relative and forward-slashed via
    /// PlaytestSessionPaths.RelativeFromSessionFolder, so it reads correctly on any machine.
    public static class PlaytestSessionIndexWriter
    {
        public static void Write(string sessionFolder, PlaytestSessionDescriptor descriptor, List<PlaytestMarker> markers)
        {
            List<PlaytestMarker> safeMarkers = markers ?? new List<PlaytestMarker>();
            StringBuilder builder = new StringBuilder();

            AppendHeader(builder, descriptor, safeMarkers);
            AppendContents(builder, safeMarkers);
            AppendNoteSections(builder, sessionFolder, safeMarkers);
            AppendFooter(builder);

            string indexFilePath = PlaytestSessionPaths.IndexFile(sessionFolder);
            File.WriteAllText(indexFilePath, builder.ToString());
        }

        private static void AppendHeader(StringBuilder builder, PlaytestSessionDescriptor descriptor, List<PlaytestMarker> markers)
        {
            builder.Append("# Playtest Session: ").Append(EscapeTableCell(descriptor.SessionId)).Append('\n').Append('\n');
            builder.Append("| Field | Value |\n");
            builder.Append("| --- | --- |\n");
            builder.Append("| Started | ").Append(EscapeTableCell(descriptor.StartedUtc)).Append(" |\n");
            builder.Append("| Ended | ").Append(EscapeTableCell(descriptor.EndedUtc)).Append(" |\n");
            builder.Append("| Unity Version | ").Append(EscapeTableCell(descriptor.UnityVersion)).Append(" |\n");
            builder.Append("| Scene | ").Append(EscapeTableCell(descriptor.ScenePath)).Append(" |\n");
            builder.Append("| Git Commit | ").Append(EscapeTableCell(descriptor.GitCommit)).Append(" |\n");
            builder.Append("| Voice Mode | ").Append(EscapeTableCell(descriptor.VoiceMode)).Append(" |\n");
            string captureResolution = descriptor.CaptureWidth.ToString(CultureInfo.InvariantCulture) + "x"
                + descriptor.CaptureHeight.ToString(CultureInfo.InvariantCulture);
            builder.Append("| Capture Resolution | ").Append(captureResolution).Append(" |\n");
            builder.Append("| Notes | ").Append(markers.Count.ToString(CultureInfo.InvariantCulture)).Append(" |\n");
            builder.Append('\n');
        }

        private static void AppendContents(StringBuilder builder, List<PlaytestMarker> markers)
        {
            builder.Append("## Contents\n\n");
            if (markers.Count == 0)
            {
                builder.Append("_Nobody said or typed anything in this session._\n\n");
                return;
            }

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                PlaytestMarker marker = markers[markerIndex];
                string preview = FirstSixtyCharacters(PreferredNoteText(marker));
                builder.Append("- [").Append(FormatTimestamp(marker.Start)).Append(" — ").Append(preview)
                    .Append("](#").Append(marker.Id).Append(")\n");
            }

            builder.Append('\n');
        }

        private static void AppendNoteSections(StringBuilder builder, string sessionFolder, List<PlaytestMarker> markers)
        {
            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                AppendNoteSection(builder, sessionFolder, markers[markerIndex]);
            }
        }

        private static void AppendNoteSection(StringBuilder builder, string sessionFolder, PlaytestMarker marker)
        {
            // An explicit HTML anchor survives whichever heading-slug rules a previewer uses.
            builder.Append("<a id=\"").Append(marker.Id).Append("\"></a>\n");
            builder.Append("## ").Append(marker.Id).Append(" — ").Append(FormatTimestamp(marker.Start)).Append('\n').Append('\n');

            if (!string.IsNullOrEmpty(marker.TranscriptText))
            {
                builder.Append("**Said:** ").Append(marker.TranscriptText).Append('\n').Append('\n');
            }

            if (!string.IsNullOrEmpty(marker.TypedNote))
            {
                builder.Append("**Typed:** ").Append(marker.TypedNote).Append('\n').Append('\n');
            }

            if (string.IsNullOrEmpty(marker.TranscriptText) && string.IsNullOrEmpty(marker.TypedNote))
            {
                builder.Append("_No transcript or typed note for this moment._\n\n");
            }

            AppendNoteImage(builder, sessionFolder, marker);
            AppendReferencesTable(builder, sessionFolder, marker);
            AppendStateList(builder, marker);
        }

        private static void AppendNoteImage(StringBuilder builder, string sessionFolder, PlaytestMarker marker)
        {
            string combinedFile = PlaytestSessionPaths.NoteCombinedFile(sessionFolder, marker.Id);
            string frameFile = PlaytestSessionPaths.NoteFrameFile(sessionFolder, marker.Id);

            string chosenAbsolutePath = null;
            if (marker.HasAnnotation && File.Exists(combinedFile))
            {
                chosenAbsolutePath = combinedFile;
            }
            else if (File.Exists(frameFile))
            {
                chosenAbsolutePath = frameFile;
            }

            if (chosenAbsolutePath == null)
            {
                return;
            }

            string relativeImagePath = PlaytestSessionPaths.RelativeFromSessionFolder(sessionFolder, chosenAbsolutePath);
            builder.Append("![").Append(marker.Id).Append("](").Append(relativeImagePath).Append(")\n\n");
        }

        private static void AppendReferencesTable(StringBuilder builder, string sessionFolder, PlaytestMarker marker)
        {
            if (marker.References == null || marker.References.Count == 0)
            {
                builder.Append("_No resolved object references._\n\n");
                return;
            }

            builder.Append("| Object | Hierarchy Path | Prefab | Confidence | Resolved By |\n");
            builder.Append("| --- | --- | --- | --- | --- |\n");
            for (int referenceIndex = 0; referenceIndex < marker.References.Count; referenceIndex++)
            {
                PlaytestObjectReference reference = marker.References[referenceIndex];
                string confidencePercent = (reference.Confidence * 100.0f).ToString("F0", CultureInfo.InvariantCulture) + "%";
                builder.Append("| ").Append(EscapeTableCell(reference.ObjectName))
                    .Append(" | ").Append(EscapeTableCell(reference.HierarchyPath))
                    .Append(" | ").Append(EscapeTableCell(reference.PrefabAssetPath))
                    .Append(" | ").Append(confidencePercent)
                    .Append(" | ").Append(EscapeTableCell(reference.ResolvedBy))
                    .Append(" |\n");
            }

            builder.Append('\n');
        }

        private static void AppendStateList(StringBuilder builder, PlaytestMarker marker)
        {
            if (marker.State == null || marker.State.Count == 0)
            {
                builder.Append("_No tracked state at this moment._\n\n");
                return;
            }

            builder.Append("**State:** ");
            for (int fieldIndex = 0; fieldIndex < marker.State.Count; fieldIndex++)
            {
                PlaytestStateField field = marker.State[fieldIndex];
                if (fieldIndex > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(field.Name).Append('=').Append(field.Value);
            }

            builder.Append('\n').Append('\n');
        }

        private static void AppendFooter(StringBuilder builder)
        {
            builder.Append("---\n\n");
            builder.Append("Paste [session-for-agent.md](session-for-agent.md) into a coding agent to work this session.\n");
        }

        // Transcript wins over a typed note: it is what was captured in the moment.
        private static string PreferredNoteText(PlaytestMarker marker)
        {
            if (!string.IsNullOrEmpty(marker.TranscriptText))
            {
                return marker.TranscriptText;
            }

            if (!string.IsNullOrEmpty(marker.TypedNote))
            {
                return marker.TypedNote;
            }

            return string.Empty;
        }

        private static string FirstSixtyCharacters(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(no transcript or typed note)";
            }

            string singleLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (singleLine.Length <= 60)
            {
                return singleLine;
            }

            return singleLine.Substring(0, 60) + "...";
        }

        private static string FormatTimestamp(PlaytestTimestamp timestamp)
        {
            int totalSeconds = (int)Math.Floor(timestamp.SecondsSinceSessionStart);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("D2", CultureInfo.InvariantCulture);
        }

        private static string EscapeTableCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ');
        }
    }
}
