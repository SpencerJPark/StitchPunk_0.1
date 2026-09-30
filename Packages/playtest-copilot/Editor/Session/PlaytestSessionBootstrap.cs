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

            Debug.Log("Playtest Copilot: session written to " + activeDescriptor.AbsoluteFolderPath);
            activeDescriptor = null;
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
