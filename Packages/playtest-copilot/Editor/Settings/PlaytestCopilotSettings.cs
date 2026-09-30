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

        public string SessionsRootPath
        {
            get => this.sessionsRootPath;
            set => this.sessionsRootPath = value;
        }

        /// Absolute, forward-slashed sessions root: the explicit override when it is a rooted
        /// path, otherwise a PlaytestSessions folder beside Assets/.
        public string ResolvedSessionsRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(this.sessionsRootPath) && Path.IsPathRooted(this.sessionsRootPath))
                {
                    return this.sessionsRootPath.Replace('\\', '/');
                }

                string projectRootPath = Directory.GetParent(Application.dataPath).FullName;
                string defaultSessionsRootPath = Path.Combine(projectRootPath, "PlaytestSessions");
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
