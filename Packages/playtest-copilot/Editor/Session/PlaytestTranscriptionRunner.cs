using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace PlaytestCopilot.Editor
{
    /// Runs the Python transcriber on a finished session, then folds the words back into every note
    /// and rewrites the two readable files. The process runs detached and is polled from
    /// EditorApplication.update, because the Editor main thread must never block on it.
    public static class PlaytestTranscriptionRunner
    {
        private const string ResultMarker = "PLAYTEST_TRANSCRIBE_RESULT ";
        private const string ToolsRelativePath = "Tools~/transcribe";
        private const string VenvPythonRelativePath = ".venv/Scripts/python.exe";
        private const string ScriptFileName = "transcribe_session.py";

        private static Process runningProcess;
        private static StringBuilder standardOutputBuffer;
        private static string pendingSessionFolder;

        public static bool IsRunning
        {
            get { return runningProcess != null; }
        }

        /// Starts transcription if the toolchain is present. Returns false and explains once when it
        /// is not — a missing venv must read as a setup step, not as a broken session.
        public static bool TryBeginTranscription(string sessionFolder)
        {
            if (runningProcess != null)
            {
                Debug.LogWarning("Playtest Copilot: a transcription is already running; skipping " + sessionFolder);
                return false;
            }

            string toolsFolder = ResolveToolsFolder();
            string pythonPath = PlaytestSessionPaths.Normalise(Path.Combine(toolsFolder, VenvPythonRelativePath));
            string scriptPath = PlaytestSessionPaths.Normalise(Path.Combine(toolsFolder, ScriptFileName));

            if (!File.Exists(pythonPath) || !File.Exists(scriptPath))
            {
                Debug.Log("Playtest Copilot: transcription skipped, the local transcriber is not set up.\n"
                    + "Create it once with:\n"
                    + "  python -m venv \"" + toolsFolder + "/.venv\"\n"
                    + "  \"" + pythonPath + "\" -m pip install faster-whisper\n"
                    + "The session itself is complete; only transcript.md is missing.");
                return false;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = pythonPath;
            startInfo.Arguments = "\"" + scriptPath + "\" \"" + sessionFolder + "\" --model "
                + PlaytestCopilotSettings.Current.TranscriptionModel;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            standardOutputBuffer = new StringBuilder();
            pendingSessionFolder = sessionFolder;
            runningProcess = new Process();
            runningProcess.StartInfo = startInfo;
            runningProcess.OutputDataReceived += (sender, eventArguments) =>
            {
                if (eventArguments.Data != null)
                {
                    standardOutputBuffer.AppendLine(eventArguments.Data);
                }
            };

            try
            {
                runningProcess.Start();
                runningProcess.BeginOutputReadLine();
                runningProcess.BeginErrorReadLine();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Playtest Copilot: could not start the transcriber. " + exception.Message);
                runningProcess = null;
                return false;
            }

            EditorApplication.update += PollTranscriptionProcess;
            Debug.Log("Playtest Copilot: transcribing " + Path.GetFileName(sessionFolder) + " in the background.");
            return true;
        }

        private static void PollTranscriptionProcess()
        {
            if (runningProcess == null || !runningProcess.HasExited)
            {
                return;
            }

            EditorApplication.update -= PollTranscriptionProcess;
            string output = standardOutputBuffer.ToString();
            string sessionFolder = pendingSessionFolder;
            runningProcess.Dispose();
            runningProcess = null;
            standardOutputBuffer = null;
            pendingSessionFolder = null;

            int markerIndex = output.LastIndexOf(ResultMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                Debug.LogWarning("Playtest Copilot: the transcriber produced no result line. Output tail:\n"
                    + output.Substring(Math.Max(0, output.Length - 500)));
                return;
            }

            string resultJson = output.Substring(markerIndex + ResultMarker.Length).Trim();
            TranscriptionResult result = JsonUtility.FromJsonSafe(resultJson);
            if (result == null || !result.ok)
            {
                Debug.LogWarning("Playtest Copilot: transcription failed. " + (result == null ? resultJson : result.error));
                return;
            }

            int updatedNoteCount = ApplyTranscriptToNotes(sessionFolder);
            AssetDatabase.Refresh();
            Debug.Log("Playtest Copilot: transcribed " + result.segments + " segment(s) of "
                + result.audioSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s in "
                + result.transcribeSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s on " + result.device
                + ", filled " + updatedNoteCount + " note(s).\n" + sessionFolder);
        }

        /// A note's words are every transcript segment that overlaps its time window. Overlap rather
        /// than containment, because the voice detector opens a marker a beat before the first word
        /// and Whisper's segment boundaries do not line up with it exactly.
        public static int ApplyTranscriptToNotes(string sessionFolder)
        {
            string transcriptJsonPath = PlaytestSessionPaths.Normalise(Path.Combine(sessionFolder, "transcript.json"));
            if (!File.Exists(transcriptJsonPath))
            {
                return 0;
            }

            TranscriptDocument transcript = UnityEngine.JsonUtility.FromJson<TranscriptDocument>(
                File.ReadAllText(transcriptJsonPath));
            if (transcript == null || transcript.segments == null)
            {
                return 0;
            }

            string notesFolder = PlaytestSessionPaths.NotesFolder(sessionFolder);
            if (!Directory.Exists(notesFolder))
            {
                return 0;
            }

            List<PlaytestMarker> markers = new List<PlaytestMarker>();
            int updatedNoteCount = 0;
            string[] noteFolders = Directory.GetDirectories(notesFolder);
            Array.Sort(noteFolders, StringComparer.Ordinal);

            foreach (string noteFolder in noteFolders)
            {
                string noteJsonPath = PlaytestSessionPaths.Normalise(Path.Combine(noteFolder, "note.json"));
                if (!File.Exists(noteJsonPath))
                {
                    continue;
                }

                PlaytestMarker marker = UnityEngine.JsonUtility.FromJson<PlaytestMarker>(File.ReadAllText(noteJsonPath));
                if (marker == null)
                {
                    continue;
                }

                string spokenText = CollectOverlappingText(transcript, marker);
                if (!string.IsNullOrEmpty(spokenText))
                {
                    marker.TranscriptText = spokenText;
                    File.WriteAllText(noteJsonPath, UnityEngine.JsonUtility.ToJson(marker, true));
                    updatedNoteCount++;
                }

                markers.Add(marker);
            }

            RegenerateReadableFiles(sessionFolder, markers);
            return updatedNoteCount;
        }

        private static string CollectOverlappingText(TranscriptDocument transcript, PlaytestMarker marker)
        {
            double markerStart = marker.Start.SecondsSinceSessionStart;
            double markerEnd = marker.End.SecondsSinceSessionStart;
            StringBuilder spoken = new StringBuilder();

            foreach (TranscriptSegment segment in transcript.segments)
            {
                bool overlaps = segment.end > markerStart && segment.start < markerEnd;
                if (!overlaps || string.IsNullOrEmpty(segment.text))
                {
                    continue;
                }

                if (spoken.Length > 0)
                {
                    spoken.Append(' ');
                }

                spoken.Append(segment.text.Trim());
            }

            return spoken.ToString();
        }

        private static void RegenerateReadableFiles(string sessionFolder, List<PlaytestMarker> markers)
        {
            string descriptorPath = PlaytestSessionPaths.SessionDescriptorFile(sessionFolder);
            if (!File.Exists(descriptorPath))
            {
                return;
            }

            PlaytestSessionDescriptor descriptor = UnityEngine.JsonUtility.FromJson<PlaytestSessionDescriptor>(
                File.ReadAllText(descriptorPath));
            if (descriptor == null)
            {
                return;
            }

            PlaytestSessionIndexWriter.Write(sessionFolder, descriptor, markers);
            PlaytestAgentMarkdownExport.WriteToFile(
                PlaytestSessionPaths.Normalise(Path.Combine(sessionFolder, "session-for-agent.md")),
                descriptor, markers, sessionFolder);
        }

        private static string ResolveToolsFolder()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlaytestTranscriptionRunner).Assembly);
            string packageRoot = packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath)
                ? packageInfo.resolvedPath
                : Path.GetFullPath("Packages/playtest-copilot");
            return PlaytestSessionPaths.Normalise(Path.Combine(packageRoot, ToolsRelativePath));
        }

        [Serializable]
        private sealed class TranscriptionResult
        {
            public bool ok;
            public string error;
            public int segments;
            public string device;
            public float audioSeconds;
            public float transcribeSeconds;
        }

        [Serializable]
        private sealed class TranscriptDocument
        {
            public List<TranscriptSegment> segments;
        }

        [Serializable]
        private sealed class TranscriptSegment
        {
            public double start;
            public double end;
            public string text;
        }

        private static class JsonUtility
        {
            /// JsonUtility throws on malformed input; a transcriber crash must surface as a warning
            /// with its output, not as an exception out of an editor update callback.
            public static TranscriptionResult FromJsonSafe(string json)
            {
                try
                {
                    return UnityEngine.JsonUtility.FromJson<TranscriptionResult>(json);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
