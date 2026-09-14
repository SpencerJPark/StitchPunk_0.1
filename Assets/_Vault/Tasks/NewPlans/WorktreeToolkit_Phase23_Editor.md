# Worktree Toolkit — Phases 2–3 Editor build spec (node window + gate broker)

> **Status:** built 2026-09-14. 13 Sonnet workers in two waves; a verifier passed the broker and
> guard on 9 checks. Compile clean; `WorktreeTreeLayoutTests` 2/2. First broker gate: compile-errors
> for a deliberate CS0103, stage restored. ⏸ C1 owner visual checkpoint open.
> **Orchestrator fixes after the waves:**
> - `CreateGUI` is not virtual (dropped `override`);
> - `RunAsync` resolves `EditorPrefs` and paths on the main thread;
> - the window polls its `Task`s instead of adding to `delayCall` from a thread-pool continuation;
> - session fields are `[System.NonSerialized]`, because EditorWindow private fields survive reloads.
>
> **Decision D9b (orchestrator, 2026-09-14):** stage moves refuse only on modified paths that the
> target also changes. git carries other edits and refuses real overwrites itself, and the owner's
> stage is never clean.
>
> Parent: [`WorktreeToolkit_Roadmap.md`](WorktreeToolkit_Roadmap.md). CLI surfaces: [`WorktreeToolkit_Phase1_CLI.md`](WorktreeToolkit_Phase1_CLI.md).
> Root: `Packages/com.worktreetoolkit/`. Executor: one Editor-connected orchestrator gates each wave
> through Unity MCP; `worker` subagents never touch MCP and cannot compile — they code against the
> signatures pinned here, and the orchestrator fixes compile fallout.

## 1. Rules for every C# file

- Namespace `WorktreeToolkit.Editor`. **No `var`, no single-letter or abbreviated names**, explicit types,
  names read like docs. Comments only for a *why*, one or two lines. No `#if UNITY_EDITOR` (the asmdef
  is Editor-only).
- UI Toolkit only: no IMGUI (`OnGUI`, `GUILayout`), no `Handles`, no GraphView. Edges draw with
  `generateVisualContent` + `Painter2D`.
- **No git in C#.** Every git fact or mutation goes through `WorktreeCliClient` (roadmap D2). If a
  task needs a git fact the CLI does not expose, stop and report it.
- Never call `AssetDatabase.SaveAssets` or save scenes on the owner's behalf.
- Package independence: never reference `StitchPunk`, `DotsAnimationToolkit`, or any `Assets/` type.
- Unity 6000.5: `GetInstanceID` is removed (CS0619) — use `GetEntityId()`.

## 2. Probe results these phases depend on (2026-09-14)

- **P3** Unity silently reloads a file git changed under it and **discards unsaved in-memory edits**
  (same instance, dirty flag cleared, no prompt, nothing written back). → `StageSwapGuard` refuses a
  stage move when a dirty asset's path is in the move's changed-path list, or any open scene is dirty.
- **P4** `RequestScriptCompilation()` with no script changes fires `compilationStarted` →
  `compilationFinished` (27 ms) and **no** `assemblyCompilationFinished`, then **reloads the domain
  anyway**. → detect "compile done" by `compilationFinished`; after reload read
  `EditorUtility.scriptCompilationFailed`; persist the broker phase before requesting.
- **P7** main → branch with one Editor script: `Refresh` 1.7 s, compile start +1.1 s,
  `Assembly-CSharp-Editor` 32.7 s, reload done ≈ 60 s after compile start (≈ 63 s swap-to-idle).
  → gates are minutes, not seconds; the window must show a busy banner, and the broker must not
  return to trunk between two queued gates for different commits only to leave again (v1 still
  returns after each gate; batching is a later optimisation).
- `EditorUtility.IsDirty` is noisy (11 shaders dirty on an idle Editor) — only intersect with changed paths.
- Test framework is a **built-in package** (`Editor/Data/Resources/PackageManager/BuiltInPackages/
  com.unity.test-framework`): `TestRunnerApi.Execute(ExecutionSettings) -> string`,
  `RegisterCallbacks<T>(T, int priority = 0) where T : ICallbacks`, `Filter { testMode, testNames,
  groupNames, categoryNames, assemblyNames }`, `ICallbacks { RunStarted, RunFinished, TestStarted,
  TestFinished }`, `IErrorCallbacks.OnError(string)`, `ExecutionSettings.runSynchronously`.

## 3. Files, owners and pinned surfaces

### 3.0 Orchestrator first (before wave A)
- `Editor/WorktreeToolkit.Editor.asmdef` — `name: WorktreeToolkit.Editor`, `rootNamespace:
  WorktreeToolkit.Editor`, `includePlatforms: ["Editor"]`, `references: ["UnityEditor.TestRunner",
  "UnityEngine.TestRunner"]`, `autoReferenced: true`.
- `Tests/Editor/WorktreeToolkit.Tests.Editor.asmdef` — mirror
  `Packages/com.dotsmovementtoolkit/Tests/EditMode/DotsMovementToolkit.Tests.EditMode.asmdef`
  (`overrideReferences: true`, `precompiledReferences: ["nunit.framework.dll"]`, `autoReferenced: false`,
  `defineConstraints: ["UNITY_INCLUDE_TESTS"]`, Editor only), referencing `WorktreeToolkit.Editor`,
  `UnityEngine.TestRunner`, `UnityEditor.TestRunner`.
- No `testables` entry: the manifest has none and the embedded movement toolkit's tests already run.
- Python (wave A worker 3.11): CLI `where` and `changed-paths`.

### 3.1 `Editor/Cli/PythonLocator.cs` + `Editor/Cli/WorktreeCliClient.cs` (one worker)
```csharp
public readonly struct PythonCommand { public readonly string executablePath; public readonly string[] prefixArguments; public PythonCommand(string executablePath, string[] prefixArguments); }
public static class PythonLocator
{
    public const string PythonPathEditorPrefsKey = "WorktreeToolkit.PythonPath";
    public static bool TryResolvePythonCommand(out PythonCommand pythonCommand, out string failureReason); // EditorPrefs override → "python3" → "python" → "py" with "-3"; probe with --version, 3 s timeout; cache success in a static field
}
public sealed class WorktreeCliResult { public int exitCode; public string standardOutput; public string standardError; public bool Succeeded => exitCode == 0; }
public static class WorktreeCliClient
{
    public static string ProjectRootPath { get; }   // Directory.GetParent(Application.dataPath).FullName
    public static string CliScriptPath { get; }      // Path.GetFullPath("Packages/com.worktreetoolkit/Tools~/worktree.py")
    public static WorktreeCliResult Run(params string[] arguments);                 // blocking, 120 s timeout, appends "--cwd", ProjectRootPath, "--json"
    public static System.Threading.Tasks.Task<WorktreeCliResult> RunAsync(params string[] arguments); // Task.Run(() => Run(arguments))
}
```
Process: `UseShellExecute=false`, redirect both streams (read asynchronously to avoid pipe deadlock),
`CreateNoWindow=true`, UTF-8 encodings, environment `PYTHONIOENCODING=utf-8`. Build the argument string
with a private `QuoteArgument` (Windows rules: wrap in quotes when it has spaces/quotes; escape `"` and
trailing backslashes) — do **not** rely on `ProcessStartInfo.ArgumentList`. Python missing → exit code
`-1` with the locator's reason in `standardError`. Timeout → kill the process, exit code `-2`.

### 3.2 `Editor/Cli/WorktreeCliDtos.cs` (one worker)
`[Serializable]` classes with **public lowerCamelCase fields named exactly like the CLI JSON** (JsonUtility):
`WorktreeGraphDto { int protocol; string trunk; StageDto stage; WorktreeNodeDto[] worktrees; }`,
`StageDto { string path; string branch; string detachedSha; int dirtyTracked; StageLockDto busyWith; string reviewWorktreeId; string[] stashes; }`,
`StageLockDto { string holder; string commitSha; string since; }` (JsonUtility turns `null` into an empty
instance → `public bool IsHeld => !string.IsNullOrEmpty(holder);`),
`WorktreeNodeDto { string id; bool registered; string branch; string path; string headSha; string parentBranch; string forkPointSha; int ahead; int behind; int dirty; bool parked; string mode; int sizeMegabytes; bool stale; bool locked; bool missing; LeadDto lead; }`,
`LeadDto { string spec; string leadModel; string workerModel; string status; public bool IsBound => !string.IsNullOrEmpty(status); }`,
`CliErrorDto { string error; int exitCode; }`,
`WherePathsDto { string stagePath; string commonGitDirectory; string stateDirectory; int protocol; }`,
`ChangedPathsDto { string[] paths; }`,
plus `public static class WorktreeCliJson { public static bool TryParse<T>(WorktreeCliResult result, out T value, out string errorMessage); }`
(non-zero exit → parse `CliErrorDto` for the message, falling back to stderr).

### 3.3 `Editor/Window/WorktreeTreeLayout.cs` + `Tests/Editor/WorktreeTreeLayoutTests.cs` (one worker)
```csharp
public sealed class WorktreeLayoutNode { public string nodeId; public string parentNodeId; public int column; public int row; public bool isTrunk; }
public static class WorktreeTreeLayout
{
    public const string TrunkNodeId = "__trunk__";
    public static List<WorktreeLayoutNode> Compute(WorktreeGraphDto graph);
}
```
Trunk: column 0, row 0. A worktree whose `parentBranch` equals another worktree's `branch` is that node's
child; otherwise (trunk, unknown, empty) its parent is the trunk. Column = parent column + 1. Rows:
depth-first from the trunk, children ordered by `id` (ordinal), every node gets the next row (trunk 0).
Cycles (a→b→a) break by treating the later-visited node's parent as the trunk. Pure — no Unity API.
**Tests (2):** a branch of a branch sits one column right of its parent with unique rows for all nodes;
an unknown parentBranch falls back to the trunk at column 1.

### 3.4 `Editor/Window/WorktreeNodeElement.cs` (one worker)
```csharp
public enum WorktreeNodeAction { PutOnStage, ReturnStage, Merge, Remove, Reveal }
public sealed class WorktreeNodeElement : VisualElement
{
    public const float CardWidth = 240f; public const float CardHeight = 84f;
    public string NodeId { get; }
    public event System.Action<string, WorktreeNodeAction> ActionRequested;
    public WorktreeNodeElement(string nodeId);
    public void BindWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy);
    public void BindTrunk(StageDto stage, string trunkBranch, bool isOnStage);
}
```
USS classes (styled in 3.5's sheet): root `worktree-node`, `worktree-node--on-stage`,
`worktree-node--inactive` (not on stage), `worktree-node--trunk`, `worktree-node--stale`,
`worktree-node--missing`; children `worktree-node__title` (branch), `worktree-node__models`
("opus ▸ sonnet", hidden when no lead), `worktree-node__status` (lead status or "stale"/"missing"/"locked"),
`worktree-node__counts` ("▲3 ▼0 ✎2", plus " 68 MB" when size > 0), `worktree-node__actions` holding
one icon button (`▶` PutOnStage when not on stage, `⏏` ReturnStage when on stage; disabled while
`stageIsBusy`) and a `⋯` button opening a `GenericMenu`-free `ContextualMenuManipulator` menu
(Merge, Remove, Reveal). Tooltips carry the words. Absolute positioning is the window's job.

### 3.5 `Editor/Window/WorktreeEdgeLayer.cs` + `Editor/Window/WorktreeToolkit.uss` (one worker)
```csharp
public readonly struct WorktreeEdge { public readonly Vector2 start; public readonly Vector2 end; public readonly bool isActive; public WorktreeEdge(Vector2 start, Vector2 end, bool isActive); }
public sealed class WorktreeEdgeLayer : VisualElement
{
    public WorktreeEdgeLayer();                          // pickingMode Ignore, absolute, fills parent
    public void SetEdges(IReadOnlyList<WorktreeEdge> edges); // stores, MarkDirtyRepaint()
}
```
Draw each edge as a horizontal-tangent cubic Bézier (`MoveTo(start)`, `BezierCurveTo(start + (dx/2,0),
end − (dx/2,0), end)`), 2 px, active colour vs 35 %-alpha inactive. USS: card box (6 px radius, 1 px
border, padding 6), on-stage accent border, inactive `opacity: 0.45`, trunk card tint, stale/missing
badge colours, a `worktree-window__busy-banner`, `worktree-window__error` help-box style, and the queue
panel classes from 3.10 (`gate-queue`, `gate-queue__row`, `gate-queue__phase`). Colours as USS variables
at the top of the sheet; works in both Editor skins (`--unity-colors-*` variables where possible).

### 3.6 `Editor/Broker/GateProtocol.cs` + `Editor/Broker/GateRequestStore.cs` (one worker)
```csharp
[Serializable] public sealed class GateRequestDto { public int protocol; public string requestId; public string worktreeId; public string commitSha; public string[] editModeFixtures; public string[] playModeFixtures; public float timeoutSeconds; public string createdUtc; public string phase; }
[Serializable] public sealed class CompilerErrorDto { public string file; public int line; public string code; public string message; }
[Serializable] public sealed class TestFailureDto { public string name; public string message; }
[Serializable] public sealed class TestSummaryDto { public int passed; public int failed; public TestFailureDto[] failures; }
[Serializable] public sealed class GateResultDto { public string requestId; public string verdict; public CompilerErrorDto[] compilerErrors; public string[] burstErrors; public TestSummaryDto tests; public string refusedReason; }
public static class GatePhases { public const string Queued = "queued", Staging = "staging", Compiling = "compiling", Testing = "testing", Returning = "returning", Done = "done"; }
public static class GateVerdicts { public const string Pass = "pass", CompileErrors = "compile-errors", BurstErrors = "burst-errors", TestFailures = "test-failures", Refused = "refused", Timeout = "timeout"; }
public const int GateProtocolVersion = 1; // on a static class GateProtocol
public sealed class GateRequestStore
{
    public GateRequestStore(string stateDirectory);
    public string QueueDirectory { get; } public string ResultsDirectory { get; } public string HeartbeatFilePath { get; }
    public List<GateRequestDto> ListQueuedRequests();       // queue/*.json, oldest createdUtc first; unreadable files skipped
    public void WriteRequest(GateRequestDto request);       // atomic: write <file>.tmp then File.Replace / Move
    public void WriteResult(GateResultDto result);          // results/<requestId>.json, atomic
    public void DeleteRequest(string requestId);
    public void WriteHeartbeat();                           // broker-heartbeat.json {"pid":..,"utc":".."}; mtime is what the CLI reads
    public string SideFilePath(string requestId, string suffix); // queue/<requestId>.<suffix>.jsonl
}
```
File names and JSON field names must match the Python `gate_client` exactly (heartbeat
`broker-heartbeat.json`, `queue/`, `results/`).

### 3.7 `Editor/Broker/CompileCapture.cs` (one worker)
```csharp
public static class CompileCapture
{
    public static void Begin(string compileSideFilePath, string burstSideFilePath); // subscribe; truncate both files
    public static void End();                                                        // unsubscribe
    public static bool HasCompilationFinishedMarker(string compileSideFilePath);
    public static List<CompilerErrorDto> ReadCompilerErrors(string compileSideFilePath);
    public static List<string> ReadBurstErrors(string burstSideFilePath);
}
```
`assemblyCompilationFinished`: append every `CompilerMessageType.Error` as one JSON line **immediately**
(the callback runs in the domain that is about to unload). `compilationFinished`: append the line
`{"marker":"compilationFinished"}`. `Application.logMessageReceivedThreaded`: append Error/Exception
messages containing `error BC` or starting with `Burst error` to the burst file (lock around writes —
threaded). Parse `CS####`/`BC####` codes from the message text into `code`.

### 3.8 `Editor/Broker/TestCapture.cs` (one worker)
```csharp
public static class TestCapture
{
    public static void RunEditModeFixtures(string[] fixtureNames, string resultSideFilePath); // TestRunnerApi, Filter{testMode=EditMode, groupNames = "^" + Regex.Escape(name) + "(\\.|$)"}
    public static bool TryReadSummary(string resultSideFilePath, out TestSummaryDto summary);   // false until RunFinished wrote it
}
```
An `ICallbacks` implementation (ScriptableObject-free; register with `TestRunnerApi.RegisterCallbacks`)
writes the summary JSON atomically in `RunFinished` (passed/failed counts from leaf results, failures
with full name + message). PlayMode fixtures are **out of scope for v1**: the broker reports them as
`refusedReason` "play-mode fixtures are not supported by the broker yet".

### 3.9 `Editor/Broker/StageSwapGuard.cs` (one worker)
```csharp
public sealed class StageMoveOutcome { public bool moved; public List<string> blockers; public WorktreeCliResult cliResult; }
public static class StageSwapGuard
{
    public static List<string> FindBlockers(string targetReference); // play mode / changing, compiling, updating; dirty open scenes; dirty assets ∩ `changed-paths <target>`
    public static StageMoveOutcome MoveStage(string targetReference, params string[] cliArguments); // blockers → no move; else DisallowAutoRefresh → CLI → finally AllowAutoRefresh + Refresh
}
```
Dirty assets: `Resources.FindObjectsOfTypeAll<UnityEngine.Object>()` where `EditorUtility.IsPersistent`
and `IsDirty`, mapped with `AssetDatabase.GetAssetPath`, intersected with the CLI `changed-paths` list
(P3). Blocker texts name the scene/asset. `AllowAutoRefresh` must be in a `finally`.

### 3.10 wave B — `Editor/Window/WorktreeGraphWindow.cs`, `Editor/Window/GateQueuePanel.cs`, `Editor/Broker/GateBroker.cs`
- **Window** (`[MenuItem("Window/Worktree Toolkit")]`): toolbar (⟳ refresh, ＋ create → text prompt →
  `create <id>`, stage label, broker toggle, queue count); busy banner when `stage.busyWith.IsHeld` or
  compiling; error help box; a `ScrollView` canvas holding the `WorktreeEdgeLayer` behind absolutely
  positioned `WorktreeNodeElement`s at `left = 16 + column × 272`, `top = 16 + row × 100`; edges from the
  parent card's right-middle to the child's left-middle, active when the child is on stage. Polls
  `list` via `RunAsync` every 3 s while the window has focus and once on focus gain; results marshal back
  with `EditorApplication.delayCall`. Actions: PutOnStage → `DisplayDialog` naming the branch and saying
  unsaved assets that git changes will be refused → `StageSwapGuard.MoveStage(branch, "review", id)`;
  ReturnStage → `MoveStage(trunk, "return")`; Merge → confirm → `merge <id>`; Remove → confirm (says an
  unmerged branch is kept) → `remove <id>`; Reveal → `EditorUtility.RevealInFinder(path)`.
- **GateQueuePanel**: foldout under the toolbar listing queued requests (worktree, phase, age) from
  `GateRequestStore`, refreshed with the graph.
- **GateBroker** `[InitializeOnLoad]` static: enabled via `EditorPrefs` `WorktreeToolkit.BrokerEnabled`
  (default true) and a `Tools/Worktree Toolkit/Gate Broker Enabled` menu toggle. Resolves the state
  directory once per domain with CLI `where`. `EditorApplication.update` tick every 2 s: heartbeat; if a
  request is mid-phase continue it, else take the oldest `queued`. Phases (persist `phase` with
  `WriteRequest` **before** each step):
  1. **staging** — protocol mismatch → refused; past `createdUtc + timeoutSeconds` → timeout; stage
     review active → wait; `MoveStage(commitSha, "stage-commit", sha, "--holder", "gate <id>")`;
     blockers → verdict refused with the blockers.
  2. **compiling** — `CompileCapture.Begin`, `RequestScriptCompilation()`. On the next domain (or tick)
     with the marker present and not compiling: errors → compile-errors; Burst errors → burst-errors;
     else if `EditorUtility.scriptCompilationFailed` → compile-errors with "compilation failed (see
     console)".
  3. **testing** — EditMode fixtures via `TestCapture`; wait for the summary; failures → test-failures.
  4. **returning** — `MoveStage(trunk, "restore-trunk")`; write the result; delete the request.
  Never run while Play mode is on; never call SaveAssets.

### 3.11 Python additions (one worker) — `Tools~/worktree_toolkit/cli.py`
- `where` → `{"stagePath", "commonGitDirectory", "stateDirectory", "protocol"}` (works anywhere).
- `changed-paths <reference>` → `{"paths": [...]}` from `git diff --name-only HEAD <reference>` run in the
  stage (forward slashes). No fixture (pass-through).

## 4. Waves and gates

- **Wave A (parallel):** 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.11 — ten workers, disjoint files.
  Orchestrator: 3.0 first, then one compile gate for the wave (`refresh_unity` → poll → `read_console`),
  fixes, `run_tests` on `WorktreeTreeLayoutTests` only.
- **Wave B (parallel):** window, queue panel, broker — three workers. Gate again.
- **Drive (orchestrator):** open the window against real worktrees; ⏸ **C1 owner checkpoint** (look,
  greyed style, card contents, left→right). Broker drive with a scratch worktree: a commit with a
  deliberate `CS0103` → `gate` → verdict compile-errors → fix commit → `gate` → pass; stage back on
  `main` afterwards; scratch worktree removed.
