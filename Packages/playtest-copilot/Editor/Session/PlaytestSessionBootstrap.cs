using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// Turns the AI Play flag into an actual session: builds the descriptor, opens the session
    /// folder, spawns the recorder host, and tears all three down when play mode ends.
    ///
    /// This is the only place that knows both halves of the package. The Editor writers never
    /// touch the recorders, and the recorders never read Editor settings.
    [InitializeOnLoad]
    public static class PlaytestSessionBootstrap
    {
        private static PlaytestSessionDescriptor activeDescriptor;

        static PlaytestSessionBootstrap()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        public static bool IsSessionOpen
        {
            get { return activeDescriptor != null; }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.EnteredPlayMode)
            {
                if (PlaytestAIPlayToolbarButton.IsCaptureSessionActive)
                {
                    StartSession();
                }

                return;
            }

            if (stateChange == PlayModeStateChange.ExitingPlayMode)
            {
                StopSession();
            }
        }

        private static void StartSession()
        {
            if (activeDescriptor != null)
            {
                return;
            }

            PlaytestCopilotSettings settings = PlaytestCopilotSettings.Current;
            DateTime startedLocal = DateTime.Now;
            string scenePath = EditorSceneManager.GetActiveScene().path;
            string sceneName = EditorSceneManager.GetActiveScene().name;
            string sessionsRoot = settings.ResolvedSessionsRoot;
            string sessionId = PlaytestSessionPaths.BuildSessionId(startedLocal, sceneName);

            PlaytestSessionDescriptor descriptor = new PlaytestSessionDescriptor();
            descriptor.SessionId = sessionId;
            descriptor.StartedUtc = startedLocal.ToUniversalTime().ToString("o");
            descriptor.UnityVersion = Application.unityVersion;
            descriptor.ScenePath = scenePath;
            descriptor.GitCommit = PlaytestGitMetadata.TryReadHeadCommit(ProjectRoot());
            descriptor.VoiceMode = settings.VoiceMode.ToString();
            descriptor.CaptureWidth = settings.CaptureWidth;
            descriptor.CaptureHeight = settings.CaptureHeight;
            descriptor.MicrophoneSampleRate = settings.MicrophoneSampleRate;
            descriptor.AbsoluteFolderPath = PlaytestSessionPaths.SessionFolder(sessionsRoot, sessionId);
            if (settings.CaptureGameObjectState)
            {
                descriptor.StateBackends.Add("GameObject");
            }

            PlaytestSessionClock.StartSession();
            PlaytestSessionFolderWriter.BeginSession(descriptor);
            PlaytestCaptureBus.RaiseSessionStarted(descriptor);
            activeDescriptor = descriptor;

            // The resolver treats the Editor's current selection as one of its six signals, and the
            // Runtime assembly cannot reach UnityEditor.Selection, so it is injected here.
            PlaytestRecorderHost.EditorSelectionProvider = GetActiveEditorSelection;

            PlaytestRecorderConfiguration configuration = new PlaytestRecorderConfiguration();
            configuration.VoiceMode = settings.VoiceMode;
            configuration.RecordKey = settings.RecordKey;
            configuration.AnnotateKey = settings.AnnotateKey;
            configuration.VoiceActivityThreshold = settings.VoiceActivityThreshold;
            configuration.SilenceHangSeconds = settings.SilenceHangSeconds;
            configuration.CaptureGameObjectState = settings.CaptureGameObjectState;
            configuration.ShowOnScreenRecordButton = settings.ShowOnScreenRecordButton;
            configuration.AudioFileAbsolutePath = PlaytestSessionPaths.AudioFile(descriptor.AbsoluteFolderPath);

            PlaytestRecorderHost.Create(configuration);
        }

        private static void StopSession()
        {
            if (activeDescriptor == null)
            {
                return;
            }

            // The host writes audio.wav on the way down, so it must go before the folder is closed.
            PlaytestRecorderHost.DestroyInstance();
            PlaytestRecorderHost.EditorSelectionProvider = null;
            PlaytestSessionClock.StopSession();

            activeDescriptor.EndedUtc = DateTime.UtcNow.ToString("o");
            PlaytestCaptureBus.RaiseSessionEnded(activeDescriptor);
            PlaytestSessionFolderWriter.EndSession();

            // The session folder lives under Assets/, so it does not exist as far as the Project
            // window is concerned until the asset database is told. Without this the files are on
            // disk but invisible in the Editor, which is the whole reason they were moved here.
            AssetDatabase.Refresh();

            ReportSessionWritten(activeDescriptor);
            PlaytestTranscriptionRunner.TryBeginTranscription(activeDescriptor.AbsoluteFolderPath);
            activeDescriptor = null;
        }

        /// The session folder lives outside Assets/, so nothing in the Project window points at it.
        /// This console line is the main way a developer finds their recording, so it names the
        /// files that exist rather than only the folder — and says plainly what is not there yet,
        /// because "where are my specs" is the obvious question and the honest answer is "not built".
        private static void ReportSessionWritten(PlaytestSessionDescriptor descriptor)
        {
            EditorPrefs.SetString(PlaytestCopilotMenu.LastSessionFolderPreferenceKey, descriptor.AbsoluteFolderPath);

            int noteCount = PlaytestSessionFolderWriter.CompletedMarkers.Count;
            string message = "Playtest Copilot: session saved, " + noteCount + " note(s).\n"
                + descriptor.AbsoluteFolderPath + "\n"
                + "  index.md              the session, readable with Unity closed\n"
                + "  session-for-agent.md  paste this into a coding agent\n"
                + "  audio.wav             the full microphone recording\n"
                + "  state.jsonl           the game state log\n"
                + "  notes/                one folder per note\n"
                + "Menu: Tools > Playtest Copilot > Open Last Session Folder.\n"
                + "No specs yet: spec drafting is the next milestone, so hand session-for-agent.md to an agent for now.";
            Debug.Log(message);
        }

        private static GameObject GetActiveEditorSelection()
        {
            return Selection.activeGameObject;
        }

        private static string ProjectRoot()
        {
            return PlaytestSessionPaths.Normalise(Directory.GetParent(Application.dataPath).FullName);
        }
    }
}
