using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// Project-wide Playtest Copilot preferences, persisted outside version control at
    /// ProjectSettings/PlaytestCopilot.asset via ScriptableSingleton's own serialisation.
    [FilePath("ProjectSettings/PlaytestCopilot.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class PlaytestCopilotSettings : ScriptableSingleton<PlaytestCopilotSettings>
    {
        [SerializeField]
        private PlaytestVoiceMode voiceMode = PlaytestVoiceMode.PushToTalk;

        [SerializeField]
        private KeyCode recordKey = KeyCode.BackQuote;

        [SerializeField]
        private KeyCode annotateKey = KeyCode.F2;

        [SerializeField]
        private int captureWidth = 1280;

        [SerializeField]
        private int captureHeight = 720;

        [SerializeField]
        private int microphoneSampleRate = 16000;

        [SerializeField]
        private bool captureGameObjectState = true;

        [SerializeField]
        private bool captureEntitiesState = false;

        [SerializeField]
        private bool showOnScreenRecordButton = true;

        [SerializeField]
        private float voiceActivityThreshold = 0.02f;

        // The owner's first transcribed session asked for this directly: four notes were recorded
        // where one was meant, because every natural pause mid-sentence closed the marker. 2 s is
        // long enough to think mid-sentence and short enough to still separate two remarks.
        [SerializeField]
        private float silenceHangSeconds = 2.0f;

        [SerializeField]
        private string transcriptionModel = "small.en";

        [SerializeField]
        private string sessionsRootPath = string.Empty;

        public PlaytestVoiceMode VoiceMode
        {
            get => this.voiceMode;
            set => this.voiceMode = value;
        }

        public KeyCode RecordKey
        {
            get => this.recordKey;
            set => this.recordKey = value;
        }

        public KeyCode AnnotateKey
        {
            get => this.annotateKey;
            set => this.annotateKey = value;
        }

        public int CaptureWidth
        {
            get => this.captureWidth;
            set => this.captureWidth = value;
        }

        public int CaptureHeight
        {
            get => this.captureHeight;
            set => this.captureHeight = value;
        }

        public int MicrophoneSampleRate
        {
            get => this.microphoneSampleRate;
            set => this.microphoneSampleRate = value;
        }

        public bool CaptureGameObjectState
        {
            get => this.captureGameObjectState;
            set => this.captureGameObjectState = value;
        }

        public bool CaptureEntitiesState
        {
            get => this.captureEntitiesState;
            set => this.captureEntitiesState = value;
        }

        public bool ShowOnScreenRecordButton
        {
            get => this.showOnScreenRecordButton;
            set => this.showOnScreenRecordButton = value;
        }

        public float VoiceActivityThreshold
        {
            get => this.voiceActivityThreshold;
            set => this.voiceActivityThreshold = value;
        }

        /// How long the tester may stay quiet before an utterance is treated as finished.
        public float SilenceHangSeconds
        {
            get => this.silenceHangSeconds;
            set => this.silenceHangSeconds = value;
        }

        /// A faster-whisper model name. "small.en" transcribes roughly eight times realtime on CPU;
        /// "base.en" is faster and rougher, "medium.en" slower and better.
        public string TranscriptionModel
        {
            get => this.transcriptionModel;
            set => this.transcriptionModel = value;
        }

        public string SessionsRootPath
        {
            get => this.sessionsRootPath;
            set => this.sessionsRootPath = value;
        }

        /// Absolute, forward-slashed sessions root: the explicit override when it is a rooted
        /// path, otherwise a PlaytestSessions folder beside Assets/.
        public const string DefaultSessionsFolderName = "PlaytestSessions";

        /// The project-relative form ("Assets/PlaytestSessions/...") that AssetDatabase needs.
        /// Returns empty when the sessions root has been pointed outside Assets/, where the
        /// AssetDatabase cannot reach and the Project window will not show anything.
        public string ResolvedSessionsRootAssetPath
        {
            get
            {
                string assetsPath = Application.dataPath.Replace('\\', '/');
                string sessionsRoot = this.ResolvedSessionsRoot;
                if (!sessionsRoot.StartsWith(assetsPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return "Assets" + sessionsRoot.Substring(assetsPath.Length);
            }
        }

        public string ResolvedSessionsRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(this.sessionsRootPath) && Path.IsPathRooted(this.sessionsRootPath))
                {
                    return this.sessionsRootPath.Replace('\\', '/');
                }

                // Under Assets/, not the project root: a folder outside Assets/ never appears in the
                // Project window, so a session cannot be opened, moved or deleted from inside Unity.
                // It costs an import of each session's files, which is the price of being manageable.
                string defaultSessionsRootPath = Path.Combine(Application.dataPath, DefaultSessionsFolderName);
                return defaultSessionsRootPath.Replace('\\', '/');
            }
        }

        public void SaveSettings()
        {
            this.Save(true);
        }

        public static PlaytestCopilotSettings Current => instance;
    }
}
