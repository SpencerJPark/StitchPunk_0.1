using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlaytestCopilot.Editor
{
    /// Project Settings page for every Playtest Copilot preference. Every field writes straight
    /// through to PlaytestCopilotSettings.Current and calls SaveSettings() so nothing is lost on
    /// domain reload.
    public static class PlaytestCopilotSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreatePlaytestCopilotSettingsProvider()
        {
            SettingsProvider settingsProvider = new SettingsProvider("Project/Playtest Copilot", SettingsScope.Project)
            {
                label = "Playtest Copilot",
                activateHandler = (string searchContext, VisualElement rootElement) => BuildRootElement(rootElement),
                keywords = new HashSet<string>(new[]
                {
                    "playtest", "copilot", "voice", "record key", "microphone", "capture",
                    "annotate", "session folder", "entities", "clash", "push to talk", "always on",
                })
            };

            return settingsProvider;
        }

        private static void BuildRootElement(VisualElement rootElement)
        {
            PlaytestCopilotSettings settings = PlaytestCopilotSettings.Current;

            rootElement.style.marginLeft = 10;
            rootElement.style.marginTop = 10;
            rootElement.style.marginRight = 10;

            rootElement.Add(BuildVoiceSection(settings));
            rootElement.Add(BuildCaptureSection(settings));
            rootElement.Add(BuildStateBackendsSection(settings));
            rootElement.Add(BuildSessionFolderSection(settings));
        }

        private static Label BuildSectionHeaderLabel(string headerText)
        {
            Label headerLabel = new Label(headerText);
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.marginTop = 12;
            headerLabel.style.marginBottom = 4;
            return headerLabel;
        }

        private static VisualElement BuildVoiceSection(PlaytestCopilotSettings settings)
        {
            VisualElement sectionContainer = new VisualElement();
            sectionContainer.Add(BuildSectionHeaderLabel("Voice"));

            EnumField voiceModeField = new EnumField("Voice Mode", settings.VoiceMode);
            EnumField recordKeyField = new EnumField("Record Key", settings.RecordKey);
            Slider voiceActivityThresholdSlider = new Slider("Voice Activity Threshold", 0f, 0.2f)
            {
                value = settings.VoiceActivityThreshold,
            };
            Toggle showOnScreenRecordButtonToggle = new Toggle("Show On-Screen Record Button")
            {
                value = settings.ShowOnScreenRecordButton,
            };

            // A record key that also drives a game control silently eats input during a playtest,
            // so this warning has to be impossible to miss and always current.
            HelpBox recordKeyClashHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            recordKeyClashHelpBox.style.display = DisplayStyle.None;

            void RefreshVoiceActivityThresholdEnabled()
            {
                voiceActivityThresholdSlider.SetEnabled(settings.VoiceMode == PlaytestVoiceMode.AlwaysOn);
            }

            void RefreshRecordKeyClashHelpBox(KeyCode candidateRecordKey)
            {
                List<PlaytestKeyClash> recordKeyClashes = PlaytestRecordKeyClashDetector.FindClashes(candidateRecordKey);
                if (recordKeyClashes.Count == 0)
                {
                    recordKeyClashHelpBox.style.display = DisplayStyle.None;
                    return;
                }

                StringBuilder clashMessageBuilder = new StringBuilder();
                clashMessageBuilder.Append("This record key is already bound in the project and will silently eat that input during a playtest:");
                foreach (PlaytestKeyClash keyClash in recordKeyClashes)
                {
                    clashMessageBuilder.Append('\n');
                    clashMessageBuilder.Append(keyClash.AssetPath);
                    clashMessageBuilder.Append(" - ");
                    clashMessageBuilder.Append(keyClash.ActionMapName);
                    clashMessageBuilder.Append('/');
                    clashMessageBuilder.Append(keyClash.ActionName);
                }

                recordKeyClashHelpBox.text = clashMessageBuilder.ToString();
                recordKeyClashHelpBox.style.display = DisplayStyle.Flex;
            }

            voiceModeField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.VoiceMode = (PlaytestVoiceMode)changeEvent.newValue;
                settings.SaveSettings();
                RefreshVoiceActivityThresholdEnabled();
            });

            recordKeyField.RegisterValueChangedCallback(changeEvent =>
            {
                KeyCode newRecordKey = (KeyCode)changeEvent.newValue;
                settings.RecordKey = newRecordKey;
                settings.SaveSettings();
                RefreshRecordKeyClashHelpBox(newRecordKey);
            });

            voiceActivityThresholdSlider.RegisterValueChangedCallback(changeEvent =>
            {
                settings.VoiceActivityThreshold = changeEvent.newValue;
                settings.SaveSettings();
            });

            showOnScreenRecordButtonToggle.RegisterValueChangedCallback(changeEvent =>
            {
                settings.ShowOnScreenRecordButton = changeEvent.newValue;
                settings.SaveSettings();
            });

            RefreshVoiceActivityThresholdEnabled();
            RefreshRecordKeyClashHelpBox(settings.RecordKey);

            sectionContainer.Add(voiceModeField);
            sectionContainer.Add(recordKeyField);
            sectionContainer.Add(recordKeyClashHelpBox);
            // The owner's first transcribed session asked for this by name: four notes where one was
            // meant, because every mid-sentence pause closed the marker.
            Slider silenceHangSlider = new Slider("Silence Before Note Ends", 0.3f, 5f)
            {
                value = settings.SilenceHangSeconds,
                showInputField = true,
                tooltip = "How long you may pause mid-thought before the note is closed. Raise it if "
                    + "one remark keeps arriving as several notes.",
            };
            silenceHangSlider.RegisterValueChangedCallback(changeEvent =>
            {
                settings.SilenceHangSeconds = changeEvent.newValue;
                settings.SaveSettings();
            });

            TextField transcriptionModelField = new TextField("Transcription Model")
            {
                value = settings.TranscriptionModel,
                tooltip = "A faster-whisper model name: base.en is fastest, small.en is the default, "
                    + "medium.en is slower and more accurate.",
            };
            transcriptionModelField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.TranscriptionModel = changeEvent.newValue;
                settings.SaveSettings();
            });

            sectionContainer.Add(voiceActivityThresholdSlider);
            sectionContainer.Add(silenceHangSlider);
            sectionContainer.Add(transcriptionModelField);
            sectionContainer.Add(showOnScreenRecordButtonToggle);
            return sectionContainer;
        }

        private static VisualElement BuildCaptureSection(PlaytestCopilotSettings settings)
        {
            VisualElement sectionContainer = new VisualElement();
            sectionContainer.Add(BuildSectionHeaderLabel("Capture"));

            IntegerField captureWidthField = new IntegerField("Capture Width") { value = settings.CaptureWidth };
            IntegerField captureHeightField = new IntegerField("Capture Height") { value = settings.CaptureHeight };
            IntegerField microphoneSampleRateField = new IntegerField("Microphone Sample Rate")
            {
                value = settings.MicrophoneSampleRate,
            };
            EnumField annotateKeyField = new EnumField("Annotate Key", settings.AnnotateKey);

            captureWidthField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.CaptureWidth = changeEvent.newValue;
                settings.SaveSettings();
            });

            captureHeightField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.CaptureHeight = changeEvent.newValue;
                settings.SaveSettings();
            });

            microphoneSampleRateField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.MicrophoneSampleRate = changeEvent.newValue;
                settings.SaveSettings();
            });

            annotateKeyField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.AnnotateKey = (KeyCode)changeEvent.newValue;
                settings.SaveSettings();
            });

            sectionContainer.Add(captureWidthField);
            sectionContainer.Add(captureHeightField);
            sectionContainer.Add(microphoneSampleRateField);
            sectionContainer.Add(annotateKeyField);
            return sectionContainer;
        }

        private static VisualElement BuildStateBackendsSection(PlaytestCopilotSettings settings)
        {
            VisualElement sectionContainer = new VisualElement();
            sectionContainer.Add(BuildSectionHeaderLabel("State backends"));

            Toggle captureGameObjectStateToggle = new Toggle("Capture GameObject State")
            {
                value = settings.CaptureGameObjectState,
            };
            Toggle captureEntitiesStateToggle = new Toggle("Capture Entities State")
            {
                value = settings.CaptureEntitiesState,
                tooltip = "The Entities backend arrives in Milestone 1b.",
            };
            captureEntitiesStateToggle.SetEnabled(false);

            captureGameObjectStateToggle.RegisterValueChangedCallback(changeEvent =>
            {
                settings.CaptureGameObjectState = changeEvent.newValue;
                settings.SaveSettings();
            });

            captureEntitiesStateToggle.RegisterValueChangedCallback(changeEvent =>
            {
                settings.CaptureEntitiesState = changeEvent.newValue;
                settings.SaveSettings();
            });

            sectionContainer.Add(captureGameObjectStateToggle);
            sectionContainer.Add(captureEntitiesStateToggle);
            return sectionContainer;
        }

        private static VisualElement BuildSessionFolderSection(PlaytestCopilotSettings settings)
        {
            VisualElement sectionContainer = new VisualElement();
            sectionContainer.Add(BuildSectionHeaderLabel("Session folder"));

            TextField sessionsRootPathField = new TextField("Sessions Root")
            {
                value = settings.SessionsRootPath,
            };
            Button browseSessionsRootButton = new Button { text = "Browse..." };
            Label resolvedSessionsRootLabel = new Label();
            resolvedSessionsRootLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            resolvedSessionsRootLabel.style.marginLeft = 3;

            void RefreshResolvedSessionsRootLabel()
            {
                resolvedSessionsRootLabel.text = "Resolves to: " + settings.ResolvedSessionsRoot;
            }

            sessionsRootPathField.RegisterValueChangedCallback(changeEvent =>
            {
                settings.SessionsRootPath = changeEvent.newValue;
                settings.SaveSettings();
                RefreshResolvedSessionsRootLabel();
            });

            browseSessionsRootButton.clicked += () =>
            {
                string chosenFolderPath = EditorUtility.OpenFolderPanel(
                    "Select Playtest Sessions Folder",
                    settings.ResolvedSessionsRoot,
                    string.Empty);

                if (string.IsNullOrEmpty(chosenFolderPath))
                {
                    return;
                }

                sessionsRootPathField.value = chosenFolderPath;
            };

            RefreshResolvedSessionsRootLabel();

            VisualElement sessionsRootRowContainer = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            sessionsRootPathField.style.flexGrow = 1;
            sessionsRootRowContainer.Add(sessionsRootPathField);
            sessionsRootRowContainer.Add(browseSessionsRootButton);

            // Sessions are written outside Assets/, so the Project window never shows them. Without
            // a way to open the folder from here, the printed path is the only clue a user gets.
            Button openSessionsFolderButton = new Button { text = "Open Sessions Folder" };
            openSessionsFolderButton.clicked += () =>
            {
                System.IO.Directory.CreateDirectory(settings.ResolvedSessionsRoot);
                PlaytestCopilotMenu.RevealFolder(settings.ResolvedSessionsRoot);
            };

            sectionContainer.Add(sessionsRootRowContainer);
            sectionContainer.Add(resolvedSessionsRootLabel);
            sectionContainer.Add(openSessionsFolderButton);
            return sectionContainer;
        }
    }
}
