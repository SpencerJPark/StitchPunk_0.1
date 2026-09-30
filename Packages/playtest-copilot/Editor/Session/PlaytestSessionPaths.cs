using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PlaytestCopilot.Editor
{
    /// The session folder layout from the spec, in one place. Every writer goes through this so
    /// the folder stays readable by hand and by an agent that has never run the tool.
    ///
    /// PlaytestSessions/2026-09-28_1808_Level1/
    ///   session.json  audio.wav  transcript.md  state.jsonl  index.md
    ///   notes/note_014/{note.json, clip.mp4, frame.png, annotation.png, annotated.png, references.md}
    ///   specs/SPEC-007.md
    ///   tasks/SPEC-007-a.md
    public static class PlaytestSessionPaths
    {
        public const string DefaultRootFolderName = "PlaytestSessions";
        public const string SessionDescriptorFileName = "session.json";
        public const string AudioFileName = "audio.wav";
        public const string TranscriptFileName = "transcript.md";
        public const string StateLogFileName = "state.jsonl";
        public const string IndexFileName = "index.md";
        public const string NotesFolderName = "notes";
        public const string SpecsFolderName = "specs";
        public const string TasksFolderName = "tasks";
        public const string NoteDescriptorFileName = "note.json";
        public const string NoteClipFileName = "clip.mp4";
        public const string NoteFrameFileName = "frame.png";
        public const string NoteAnnotationFileName = "annotation.png";
        public const string NoteCombinedFileName = "annotated.png";
        public const string NoteReferencesFileName = "references.md";

        private static readonly Regex UnsafeFolderCharacters = new Regex("[^A-Za-z0-9_-]");

        /// "2026-09-28_1808_Level1". Scene name is sanitised because it becomes a folder name.
        public static string BuildSessionId(DateTime startedLocal, string sceneName)
        {
            string stamp = startedLocal.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
            string safeSceneName = SanitiseForFolderName(sceneName);
            if (string.IsNullOrEmpty(safeSceneName))
            {
                return stamp;
            }

            return stamp + "_" + safeSceneName;
        }

        /// "note_014". Three digits keeps the folders sorting correctly up to 999 notes a session.
        public static string BuildMarkerId(int markerIndex)
        {
            return "note_" + markerIndex.ToString("D3", CultureInfo.InvariantCulture);
        }

        public static string SanitiseForFolderName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            return UnsafeFolderCharacters.Replace(rawName, string.Empty);
        }

        public static string SessionFolder(string sessionsRootAbsolutePath, string sessionId)
        {
            return Combine(sessionsRootAbsolutePath, sessionId);
        }

        public static string SessionDescriptorFile(string sessionFolder)
        {
            return Combine(sessionFolder, SessionDescriptorFileName);
        }

        public static string AudioFile(string sessionFolder)
        {
            return Combine(sessionFolder, AudioFileName);
        }

        public static string TranscriptFile(string sessionFolder)
        {
            return Combine(sessionFolder, TranscriptFileName);
        }

        public static string StateLogFile(string sessionFolder)
        {
            return Combine(sessionFolder, StateLogFileName);
        }

        public static string IndexFile(string sessionFolder)
        {
            return Combine(sessionFolder, IndexFileName);
        }

        public static string NotesFolder(string sessionFolder)
        {
            return Combine(sessionFolder, NotesFolderName);
        }

        public static string SpecsFolder(string sessionFolder)
        {
            return Combine(sessionFolder, SpecsFolderName);
        }

        public static string TasksFolder(string sessionFolder)
        {
            return Combine(sessionFolder, TasksFolderName);
        }

        public static string NoteFolder(string sessionFolder, string markerId)
        {
            return Combine(NotesFolder(sessionFolder), markerId);
        }

        public static string NoteDescriptorFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteDescriptorFileName);
        }

        public static string NoteFrameFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteFrameFileName);
        }

        public static string NoteAnnotationFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteAnnotationFileName);
        }

        public static string NoteCombinedFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteCombinedFileName);
        }

        public static string NoteClipFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteClipFileName);
        }

        public static string NoteReferencesFile(string sessionFolder, string markerId)
        {
            return Combine(NoteFolder(sessionFolder, markerId), NoteReferencesFileName);
        }

        /// Paths inside index.md and the markdown export are relative and forward-slashed, so the
        /// files read the same on Windows and on whatever machine an agent opens them on.
        public static string RelativeFromSessionFolder(string sessionFolder, string absolutePath)
        {
            string normalisedRoot = Normalise(sessionFolder);
            string normalisedPath = Normalise(absolutePath);
            if (!normalisedRoot.EndsWith("/", StringComparison.Ordinal))
            {
                normalisedRoot += "/";
            }

            if (normalisedPath.StartsWith(normalisedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalisedPath.Substring(normalisedRoot.Length);
            }

            return normalisedPath;
        }

        public static string Normalise(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string Combine(string left, string right)
        {
            return Normalise(Path.Combine(left, right));
        }
    }
}
