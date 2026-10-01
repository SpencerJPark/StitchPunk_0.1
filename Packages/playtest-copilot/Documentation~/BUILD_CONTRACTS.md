# Playtest Copilot: build contracts

Every file in the package is built against the surfaces on this page. They are already written
and compiling — do not change them, and do not add members to them. If your file needs something
this page does not offer, write it inside your own file and say so in your report.

Source of truth for behaviour is `Assets/_Vault/playtest-copilot/docs/SPEC.md` in the main
checkout. This page is only the *shape* everything plugs into.

## House rules

- Never `var`. Never single-letter names. Explicit types everywhere.
- Names are the documentation. One or two lines of *why* in a comment, never a `<remarks>` essay,
  never a summary that restates the signature.
- Runtime code lives in namespace `PlaytestCopilot`, Editor code in `PlaytestCopilot.Editor`.
- The Editor assembly may use the Runtime one. The Runtime assembly may **not** use the Editor
  one, and may not use `UnityEditor` except inside `#if UNITY_EDITOR`.
- The package depends on no other packages. No Input System, no Entities, no Newtonsoft. JSON goes
  through `JsonUtility`, which is why every serialised type is a `[Serializable]` class of fields
  and there are no dictionaries anywhere in the contracts.

## Compile gate

There is no Unity Editor open. After editing, run:

```
bash Packages/playtest-copilot/Tools~/compile-gate.sh
```

It must print `GATE PASS`. It compiles Runtime, Editor and Tests with the Roslyn inside the Unity
install. It catches CS errors only — it cannot catch import, bake or runtime problems.

## Already built: `Runtime/Capture/PlaytestCaptureContracts.cs`

```csharp
namespace PlaytestCopilot

enum PlaytestNoteIntent   { Unclassified, Bug, Tuning, Feature, Content, Noise }
enum PlaytestVoiceMode    { PushToTalk, AlwaysOn }
enum PlaytestAnnotationTool { Circle, Pen, Arrow, Eraser }
enum PlaytestReferenceSource { CircledRegion, PointerUnderCursor, EditorSelection,
                               TranscriptName, NearCameraCenter, RecentInteraction }

static class PlaytestMarkerId { static string For(int markerIndex); }   // "note_014"

[Serializable] struct PlaytestTimestamp { double SecondsSinceSessionStart; int FrameIndex; }
[Serializable] struct PlaytestScreenRegion { Rect ScreenRect; PlaytestAnnotationTool Tool; }

[Serializable] sealed class PlaytestStateField { string Name; string Value;
                                                 PlaytestStateField(string, string); }

[Serializable] sealed class PlaytestObjectReference {
    string ObjectName; string HierarchyPath; string PrefabAssetPath;
    List<string> ComponentTypeNames; float Confidence; string ResolvedBy; }

[Serializable] sealed class PlaytestMarker {
    string Id; int Index; PlaytestTimestamp Start; PlaytestTimestamp End;
    string TranscriptText; string TypedNote; string Intent; bool HasAnnotation;
    List<PlaytestObjectReference> References; List<PlaytestStateField> State;
    List<PlaytestScreenRegion> CircledRegions; }

[Serializable] sealed class PlaytestSessionDescriptor {
    string SessionId; string StartedUtc; string EndedUtc; string UnityVersion;
    string ScenePath; string GitCommit; string VoiceMode;
    int CaptureWidth; int CaptureHeight; int MicrophoneSampleRate;
    List<string> StateBackends; string AbsoluteFolderPath; }

[Serializable] sealed class PlaytestStateSample {
    PlaytestTimestamp Time; string Kind; List<PlaytestStateField> Fields; }

// Not [Serializable]: the PNG bytes ride the bus and never go through JsonUtility.
sealed class PlaytestAnnotationCapture {
    string MarkerId; PlaytestTimestamp Time;
    byte[] FramePng; byte[] AnnotationPng; byte[] CombinedPng;
    List<PlaytestScreenRegion> CircledRegions; }
```

`PlaytestStateSample.Kind` is one of `"snapshot"`, `"pointer"`, `"event"`, `"console"`.

Screen regions are in **game-view pixels with the origin at the bottom left**, matching
`Camera.ScreenPointToRay`. IMGUI's origin is top left, so anything drawn in `OnGUI` must be
flipped before it becomes a `PlaytestScreenRegion`.

## Already built: `Runtime/Capture/PlaytestSessionClock.cs`

```csharp
static class PlaytestSessionClock {
    bool IsRunning { get; }
    void StartSession();  void StopSession();
    double SecondsSinceSessionStart { get; }
    PlaytestTimestamp Now { get; }
    PlaytestTimestamp AtSecondsAgo(double secondsAgo);
}
```

It is built on `Time.realtimeSinceStartupAsDouble`, so pause-and-draw setting `Time.timeScale`
to zero does not stop it. Never stamp anything with `Time.time`.

## Already built: `Runtime/Capture/PlaytestCaptureBus.cs`

```csharp
static class PlaytestCaptureBus {
    event Action<PlaytestSessionDescriptor> SessionStarted, SessionEnded;
    event Action<PlaytestMarker>            MarkerCompleted;
    event Action<PlaytestAnnotationCapture> AnnotationCaptured;
    event Action<PlaytestStateSample>       StateSampleRecorded;
    event Action<string>                    AudioFileWritten;   // absolute path

    void RaiseSessionStarted(PlaytestSessionDescriptor);
    void RaiseSessionEnded(PlaytestSessionDescriptor);
    void RaiseMarkerCompleted(PlaytestMarker);
    void RaiseAnnotationCaptured(PlaytestAnnotationCapture);
    void RaiseStateSampleRecorded(PlaytestStateSample);
    void RaiseAudioFileWritten(string absolutePath);
    void RemoveAllSubscribers();
}
```

Raises are individually try/caught: one throwing subscriber must never end a recording.

## Already built: `Runtime/PlaytestTrackAttribute.cs`

```csharp
[AttributeUsage(Field | Property)] sealed class PlaytestTrackAttribute : Attribute {
    string DisplayName { get; set; }
    PlaytestTrackAttribute();  PlaytestTrackAttribute(string displayName);
}
```

## Already built: `Editor/Session/PlaytestSessionPaths.cs`

```csharp
namespace PlaytestCopilot.Editor
static class PlaytestSessionPaths {
    const string DefaultRootFolderName = "PlaytestSessions";
    const string SessionDescriptorFileName = "session.json";   // AudioFileName "audio.wav"
    // TranscriptFileName "transcript.md", StateLogFileName "state.jsonl", IndexFileName "index.md"
    // NotesFolderName "notes", SpecsFolderName "specs", TasksFolderName "tasks"
    // NoteDescriptorFileName "note.json", NoteClipFileName "clip.mp4",
    // NoteFrameFileName "frame.png", NoteAnnotationFileName "annotation.png",
    // NoteCombinedFileName "annotated.png", NoteReferencesFileName "references.md"

    string BuildSessionId(DateTime startedLocal, string sceneName);   // "2026-09-28_1808_Level1"
    string BuildMarkerId(int markerIndex);
    string SanitiseForFolderName(string rawName);

    string SessionFolder(string sessionsRootAbsolutePath, string sessionId);
    string SessionDescriptorFile(string sessionFolder);
    string AudioFile(string sessionFolder);
    string TranscriptFile(string sessionFolder);
    string StateLogFile(string sessionFolder);
    string IndexFile(string sessionFolder);
    string NotesFolder(string sessionFolder);
    string SpecsFolder(string sessionFolder);
    string TasksFolder(string sessionFolder);
    string NoteFolder(string sessionFolder, string markerId);
    string NoteDescriptorFile(string sessionFolder, string markerId);
    string NoteFrameFile(string sessionFolder, string markerId);
    string NoteAnnotationFile(string sessionFolder, string markerId);
    string NoteCombinedFile(string sessionFolder, string markerId);
    string NoteClipFile(string sessionFolder, string markerId);
    string NoteReferencesFile(string sessionFolder, string markerId);

    string RelativeFromSessionFolder(string sessionFolder, string absolutePath);
    string Normalise(string path);      // backslashes to forward slashes, no trailing slash
}
```

Every path this returns is already forward-slashed. Do not re-normalise, and do not build session
paths by hand anywhere else.

## Surfaces your file must expose

These are the names the wave-2 composition root (`Runtime/PlaytestRecorderHost.cs`) and the Editor
writers will call. Match them exactly, including the property/method split. Add whatever private
members you need; just do not rename or drop what is listed.

### `Runtime/Capture/PlaytestMicrophoneRecorder.cs` — `sealed class : MonoBehaviour`

```csharp
const int SampleRate = 16000;               // 16 kHz mono, as in the spec
bool IsCapturing { get; }
string DeviceName { get; set; }             // null or empty means the default device
float CurrentInputLevel { get; }            // 0..1 RMS over the most recent window
double SecondsCaptured { get; }
void BeginCapture();
void EndCaptureAndWriteWav(string absoluteWavPath);   // then PlaytestCaptureBus.RaiseAudioFileWritten
void TickCapture();                          // drain Unity's mic ring buffer into ours
```

### `Runtime/Capture/PlaytestWavWriter.cs` — `static class`

```csharp
void Write(string absolutePath, float[] monoSamples, int sampleCount, int sampleRate);
```

16-bit PCM mono RIFF. Nothing else in the package writes WAV headers.

### `Runtime/Capture/PlaytestVoiceCaptureController.cs` — `sealed class : MonoBehaviour`

```csharp
PlaytestVoiceMode Mode { get; set; }
KeyCode RecordKey { get; set; }              // default KeyCode.BackQuote
float VoiceActivityThreshold { get; set; }   // default 0.02f
float SilenceHangSeconds { get; set; }       // default 0.8f
PlaytestMicrophoneRecorder Microphone { get; set; }
bool IsMarkerOpen { get; }
int CompletedMarkerCount { get; }
void BeginMarkerManually();
void EndMarkerManually();
void TickCapture(bool recordKeyHeld);        // host passes the key state; keeps this testable
```

Closing a marker mints its id with `PlaytestMarkerId.For(index)` and raises `MarkerCompleted`.
The marker's `State` and `References` are filled by the host, not here — leave them empty.

### `Runtime/Capture/PlaytestGameObjectStateBackend.cs` — `sealed class : MonoBehaviour`

```csharp
float SnapshotIntervalSeconds { get; set; }  // 0.25f
float NearbyRadius { get; set; }             // 20f
int MaxNearbyObjects { get; set; }           // 24
Transform PlayerTransform { get; set; }
void TickCapture(float unscaledDeltaTime);   // raises StateSampleRecorded, Kind "snapshot"
List<PlaytestStateField> CaptureSnapshotFields();
static List<PlaytestStateField> ReadTrackedMembers(Component component);
```

`ReadTrackedMembers` reflects for `[PlaytestTrack]` and **must cache the member list per type** in a
static dictionary. Uncached reflection at 4 Hz over two dozen objects is the difference between a
usable tool and one that ruins the feel it is supposed to measure.

### `Runtime/Capture/PlaytestPointerTracker.cs` — `sealed class : MonoBehaviour`

```csharp
Camera CaptureCamera { get; set; }
GameObject ObjectUnderCursor { get; }
GameObject ObjectAtCameraCenter { get; }
GameObject LastInteractedObject { get; }
void TickCapture();                          // raises StateSampleRecorded (Kind "pointer") on change only
void NotifyInteraction(GameObject interacted);
```

### `Runtime/Capture/PlaytestReferenceResolver.cs` — `static class`

```csharp
sealed class ResolveRequest {
    List<PlaytestScreenRegion> CircledRegions;
    Camera CaptureCamera;
    GameObject ObjectUnderCursor;
    GameObject EditorSelection;
    GameObject ObjectAtCameraCenter;
    GameObject LastInteractedObject;
    string TranscriptText;
    Transform PlayerTransform;
    float NearbyRadius;      // default 20f
    int MaxResults;          // default 5
}

List<PlaytestObjectReference> Resolve(ResolveRequest request);
PlaytestObjectReference Describe(GameObject gameObject, PlaytestReferenceSource source, float confidence);
string BuildHierarchyPath(GameObject gameObject);      // "Level1/Player" — no leading slash
float ConfidenceFor(PlaytestReferenceSource source);
```

Base confidence per source, and the ranking order:
`CircledRegion 0.92`, `PointerUnderCursor 0.80`, `EditorSelection 0.70`, `TranscriptName 0.60`,
`NearCameraCenter 0.45`, `RecentInteraction 0.40`. When more than one signal names the same object,
keep the highest base and add `0.05` per extra agreeing signal, capped at `0.98`. Results come back
sorted by confidence, highest first, at most `MaxResults`.

`ResolvedBy` is the winning source's `ToString()`, with extra agreeing sources appended after `+`,
for example `"CircledRegion+PointerUnderCursor"`.

`Resolve` must be safe with a null camera and an empty request — it returns an empty list rather
than throwing, because a note taken before anything is on screen is normal.

### `Runtime/Annotation/PlaytestStroke.cs` + `PlaytestAnnotationStrokes.cs`

```csharp
sealed class PlaytestStroke {
    PlaytestAnnotationTool Tool;
    List<Vector2> ScreenPoints;              // bottom-left origin, game-view pixels
    Color Color;
    int ThicknessPixels;
}

static class PlaytestAnnotationStrokes {
    Rect BoundsOf(PlaytestStroke stroke);
    List<PlaytestScreenRegion> ToRegions(List<PlaytestStroke> strokes);   // skips Eraser strokes
    Texture2D RenderToTexture(List<PlaytestStroke> strokes, int width, int height);  // transparent background
    Texture2D Combine(Texture2D frame, Texture2D annotation);             // annotation over frame
}
```

`RenderToTexture` writes pixels directly (`SetPixels32`/`Apply`); do not use `GL` or a
`RenderTexture`, because this runs while the game is paused and the render loop is not reliable.

### `Runtime/Annotation/PlaytestAnnotationOverlay.cs` — `sealed class : MonoBehaviour`

```csharp
bool IsOpen { get; }
PlaytestAnnotationTool ActiveTool { get; set; }
string TypedNote { get; }
void Open(string markerId);      // freezes time, grabs the frame
void CloseAndCapture();          // raises AnnotationCaptured, restores time
void Cancel();                   // restores time, raises nothing
```

Draws with IMGUI in `OnGUI`. IMGUI is the only way to put an interactive overlay over the game view
with no asset dependencies, and this is a developer tool, so it is allowed here. Remember the
y-flip when turning GUI points into `PlaytestScreenRegion`.

Freeze by saving and zeroing `Time.timeScale`, and restore the saved value — never assume it was 1.

### `Runtime/UI/PlaytestCaptureHud.cs` — `sealed class : MonoBehaviour`

```csharp
bool IsVisible { get; set; }
PlaytestVoiceCaptureController VoiceController { get; set; }
PlaytestMicrophoneRecorder Microphone { get; set; }
PlaytestAnnotationOverlay AnnotationOverlay { get; set; }
KeyCode RecordKey { get; set; }
KeyCode AnnotateKey { get; set; }
```

IMGUI. A red banner across the top while a note is recording, and a bottom-centre bar with the
hold-to-record button, the live microphone level and both key hints. Everything is sized from
`Screen.height / 900`, clamped to 3x: a fixed-pixel panel is unreadably small on a high-DPI game
view, which is what the first version shipped and the owner immediately hit.

It hides itself while the annotation overlay is open — two IMGUI layers competing for the same
clicks turns a circle into a button press. It is also the alternative for developers whose record
key clashes with a game binding, so it must work with no keyboard at all.

### `Editor/Settings/PlaytestCopilotSettings.cs` — `sealed class : ScriptableSingleton<PlaytestCopilotSettings>`

```csharp
[FilePath("ProjectSettings/PlaytestCopilot.asset", FilePathAttribute.Location.ProjectFolder)]

PlaytestVoiceMode VoiceMode;         // serialised fields, not properties
KeyCode RecordKey;                   // KeyCode.BackQuote
KeyCode AnnotateKey;                 // KeyCode.F2
int CaptureWidth;                    // 1280
int CaptureHeight;                   // 720
int MicrophoneSampleRate;            // 16000
bool CaptureGameObjectState;         // true
bool CaptureEntitiesState;           // false — the Entities backend is Milestone 1b
bool ShowOnScreenRecordButton;       // true
float VoiceActivityThreshold;        // 0.02f
string SessionsRootPath;             // empty means <project root>/PlaytestSessions

string ResolvedSessionsRoot { get; } // absolute, honours SessionsRootPath when set
void SaveSettings();                 // Save(true)
static PlaytestCopilotSettings Current { get; }   // just `instance`, named so call sites read
```

### `Editor/Settings/PlaytestRecordKeyClashDetector.cs` — `static class`

```csharp
sealed class PlaytestKeyClash { string AssetPath; string ActionMapName; string ActionName; string BindingPath; }

List<PlaytestKeyClash> FindClashes(KeyCode key);
string InputSystemPathFor(KeyCode key);   // KeyCode.BackQuote -> "<Keyboard>/backquote"
```

The package does not reference the Input System, so find clashes by loading every `.inputactions`
asset in the project **as text** (`AssetDatabase.FindAssets("t:TextAsset")` misses them — use
`AssetDatabase.FindAssets` with the `.inputactions` extension filtered from
`AssetDatabase.GetAllAssetPaths()`) and matching the binding path string in the JSON. Return an
empty list when there are no `.inputactions` assets; that is the common case, not an error.

### `Editor/Settings/PlaytestCopilotSettingsProvider.cs` — `static class`

A `SettingsProvider` at `Project/Playtest Copilot`, built with UI Toolkit (`activateHandler`
populating `rootElement`), editing every field above. The record-key field shows the clashes from
`PlaytestRecordKeyClashDetector` as a `HelpBox` under it, live as the key changes.

### `Editor/PlaytestIconLoader.cs` — `static class`

```csharp
Texture2D LoadAIPlayIcon();      // null-safe: returns null rather than throwing if the file moved
```

Picks `d_PlayAI` when `EditorGUIUtility.isProSkin`, else `PlayAI`, and the `@2x` file when
`EditorGUIUtility.pixelsPerPoint > 1f`. The files are at
`Packages/playtest-copilot/Editor/Icons/`; load them with `AssetDatabase.LoadAssetAtPath<Texture2D>`
using that package path, which works for an embedded package.

### `Editor/Toolbar/PlaytestAIPlayToolbarButton.cs` — `static class`

```csharp
bool IsCaptureSessionActive { get; }
void ToggleAIPlay();
```

The toolbar API is verified present in this Editor. Use exactly:

```csharp
[MainToolbarElement("PlaytestCopilot/AIPlay", defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 100)]
public static VisualElement CreateAIPlayToolbarElement()
```

on a **static** method returning `VisualElement`, in `using UnityEditor.Toolbars;`. Those are
*property* initialisers (`=`), not named constructor arguments (`:`) — the constructor takes the
path and nothing else. Build an `EditorToolbarButton`; give it the icon from `PlaytestIconLoader`.
Because the minimum Editor is 6.4, there is no reflection fallback to write.

Pressing it sets the capture flag and calls `EditorApplication.EnterPlaymode()`. Pressing normal
Play must leave the flag false, so nothing records. Survive the domain reload with a
`SessionState.SetBool` key, not a static field. Highlight the button while a session is active.

### `Editor/Session/PlaytestGitMetadata.cs` — `static class`

```csharp
string TryReadHeadCommit(string repositoryRoot);   // "" when there is no repo
```

Read `.git/HEAD` and follow the ref file yourself. Do not launch a `git` process: this runs when
play mode stops and must not block the Editor. Handle the worktree case, where `.git` is a *file*
containing `gitdir: <path>`.

### `Editor/Session/PlaytestSessionFolderWriter.cs` — `static class`, `[InitializeOnLoad]`

```csharp
string CurrentSessionFolder { get; }     // "" when no session is open
List<PlaytestMarker> CompletedMarkers { get; }
void BeginSession(PlaytestSessionDescriptor descriptor);
void EndSession();
```

Subscribes to the bus in its static constructor and writes, as they arrive: `session.json`,
each `notes/<id>/note.json` and `references.md`, the three PNGs from an annotation capture, and
one line of `state.jsonl` per state sample. Keep the `state.jsonl` writer as a single open
`StreamWriter` for the session — opening and closing a file 4 times a second is the naive version
of this and it stalls the Editor.

`EndSession` rewrites `session.json` with `EndedUtc`, then calls
`PlaytestSessionIndexWriter.Write` and `PlaytestAgentMarkdownExport.WriteToFile`.

### `Editor/Session/PlaytestSessionIndexWriter.cs` — `static class`

```csharp
void Write(string sessionFolder, PlaytestSessionDescriptor descriptor, List<PlaytestMarker> markers);
```

Writes `index.md`: a header with the session facts, then one section per note with its timestamp,
what was said or typed, an image link to `annotated.png` (or `frame.png` when there is no drawing),
and the resolved references with their confidences. Links are relative and forward-slashed via
`PlaytestSessionPaths.RelativeFromSessionFolder`, so the file reads correctly on any machine.

### `Editor/Session/PlaytestAgentMarkdownExport.cs` — `static class`

```csharp
string Build(PlaytestSessionDescriptor descriptor, List<PlaytestMarker> markers, string sessionFolder);
void WriteToFile(string absolutePath, PlaytestSessionDescriptor descriptor,
                 List<PlaytestMarker> markers, string sessionFolder);
```

This is the Phase 1 deliverable that makes the tool useful before any LLM exists: one markdown file
a developer can paste into any coding agent. It states the project, scene and commit, then each
note as: what was said, when, which objects with paths and prefab asset paths, the tracked state
values at that moment, and the image paths. Write it to `session-for-agent.md` in the session
folder. Nothing in it may depend on the tool being installed to make sense.
