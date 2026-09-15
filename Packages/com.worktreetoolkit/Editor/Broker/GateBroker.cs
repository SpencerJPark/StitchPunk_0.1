using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    // Compiles and tests worktree commits on request, surviving the domain reloads that a real
    // compile causes (P4). State that must outlive a reload lives in SessionState; everything else
    // is cheap to recompute per tick.
    [InitializeOnLoad]
    public static class GateBroker
    {
        public const string BrokerEnabledEditorPrefsKey = "WorktreeToolkit.BrokerEnabled";

        private const string GateBrokerMenuPath = "Tools/Worktree Toolkit/Gate Broker Enabled";
        private const double TickIntervalSeconds = 2d;
        private const double CompilingPhaseFallbackSeconds = 180d;
        private const int ReturningMaxSilentFailedAttempts = 10;

        private const string ActiveRequestIdSessionKey = "WorktreeToolkit.ActiveRequestId";
        private const string ActiveRequestPhaseSessionKey = "WorktreeToolkit.ActiveRequestPhase";
        private const string PhaseStartedSessionKey = "WorktreeToolkit.PhaseStartedUnixSeconds";
        private const string PendingResultSessionKey = "WorktreeToolkit.PendingResult";
        private const string StateDirectorySessionKey = "WorktreeToolkit.StateDirectory";
        private const string TrunkBranchSessionKey = "WorktreeToolkit.TrunkBranch";
        private const string ResolutionFailedSessionKey = "WorktreeToolkit.ResolutionFailed";
        private const string StageWasMovedSessionKey = "WorktreeToolkit.StageWasMoved";
        private const string ReturningRetryCountSessionKey = "WorktreeToolkit.ReturningRetryCount";
        private const string TestsStartedSessionKeyPrefix = "WorktreeToolkit.TestsStarted.";

        private static double lastTickUnityTime = -1d;
        private static GateRequestStore cachedRequestStore;
        private static string cachedTrunkBranch;

        [Serializable]
        private sealed class ConfigDto
        {
            public string trunkBranch;
        }

        static GateBroker()
        {
            // No CLI call here — a subscribe-only constructor keeps every domain reload cheap (P4).
            EditorApplication.update += Tick;
        }

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(BrokerEnabledEditorPrefsKey, true);
            set => EditorPrefs.SetBool(BrokerEnabledEditorPrefsKey, value);
        }

        public static string CurrentRequestId
        {
            get
            {
                string activeRequestId = SessionState.GetString(ActiveRequestIdSessionKey, string.Empty);
                return string.IsNullOrEmpty(activeRequestId) ? null : activeRequestId;
            }
        }

        public static string CurrentPhase
        {
            get
            {
                if (string.IsNullOrEmpty(SessionState.GetString(ActiveRequestIdSessionKey, string.Empty)))
                {
                    return null;
                }

                string phase = SessionState.GetString(ActiveRequestPhaseSessionKey, string.Empty);
                return string.IsNullOrEmpty(phase) ? null : phase;
            }
        }

        [MenuItem(GateBrokerMenuPath)]
        private static void ToggleBrokerEnabledMenuItem()
        {
            IsEnabled = !IsEnabled;
        }

        [MenuItem(GateBrokerMenuPath, true)]
        private static bool ValidateToggleBrokerEnabledMenuItem()
        {
            Menu.SetChecked(GateBrokerMenuPath, IsEnabled);
            return true;
        }

        private static void Tick()
        {
            if (!IsEnabled)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastTickUnityTime < TickIntervalSeconds)
            {
                return;
            }

            lastTickUnityTime = now;

            GateRequestStore requestStore = ResolveRequestStore();
            if (requestStore == null)
            {
                return;
            }

            requestStore.WriteHeartbeat();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            GateRequestDto activeRequest = SelectActiveRequest(requestStore);
            if (activeRequest == null)
            {
                return;
            }

            ProcessRequest(requestStore, activeRequest);
        }

        // Resolved once per session (state directory + trunk branch never change mid-session) and
        // cached in SessionState so a domain reload does not repeat two CLI round trips every 2 s.
        private static GateRequestStore ResolveRequestStore()
        {
            if (cachedRequestStore != null)
            {
                return cachedRequestStore;
            }

            string cachedStateDirectory = SessionState.GetString(StateDirectorySessionKey, string.Empty);
            string cachedTrunk = SessionState.GetString(TrunkBranchSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(cachedStateDirectory) && !string.IsNullOrEmpty(cachedTrunk))
            {
                cachedTrunkBranch = cachedTrunk;
                cachedRequestStore = new GateRequestStore(cachedStateDirectory);
                return cachedRequestStore;
            }

            if (SessionState.GetBool(ResolutionFailedSessionKey, false))
            {
                return null;
            }

            WorktreeCliResult whereResult = WorktreeCliClient.Run("where");
            if (!WorktreeCliJson.TryParse(whereResult, out WherePathsDto wherePaths, out string whereErrorMessage))
            {
                Debug.LogWarning("[Worktree Toolkit] gate broker: failed to resolve the state directory (" + whereErrorMessage + "); disabled for this session.");
                SessionState.SetBool(ResolutionFailedSessionKey, true);
                return null;
            }

            WorktreeCliResult configResult = WorktreeCliClient.Run("config");
            if (!WorktreeCliJson.TryParse(configResult, out ConfigDto config, out string configErrorMessage))
            {
                Debug.LogWarning("[Worktree Toolkit] gate broker: failed to resolve the trunk branch (" + configErrorMessage + "); disabled for this session.");
                SessionState.SetBool(ResolutionFailedSessionKey, true);
                return null;
            }

            SessionState.SetString(StateDirectorySessionKey, wherePaths.stateDirectory);
            SessionState.SetString(TrunkBranchSessionKey, config.trunkBranch);
            cachedTrunkBranch = config.trunkBranch;
            cachedRequestStore = new GateRequestStore(wherePaths.stateDirectory);
            return cachedRequestStore;
        }

        // The oldest in-flight request resumes first (survives a reload mid-gate); otherwise the
        // oldest still-queued request starts. ListQueuedRequests is already oldest-createdUtc-first.
        private static GateRequestDto SelectActiveRequest(GateRequestStore requestStore)
        {
            List<GateRequestDto> queuedRequests = requestStore.ListQueuedRequests();
            if (queuedRequests.Count == 0)
            {
                ClearActiveRequestState(null);
                return null;
            }

            string activeRequestId = SessionState.GetString(ActiveRequestIdSessionKey, string.Empty);
            if (!string.IsNullOrEmpty(activeRequestId))
            {
                GateRequestDto resumedRequest = queuedRequests.Find(request => request.requestId == activeRequestId);
                if (resumedRequest != null)
                {
                    return resumedRequest;
                }

                ClearActiveRequestState(activeRequestId);
            }

            GateRequestDto inFlightRequest = queuedRequests.Find(request => !IsQueuedPhase(request.phase));
            GateRequestDto selectedRequest = inFlightRequest ?? queuedRequests.Find(request => IsQueuedPhase(request.phase));
            if (selectedRequest == null)
            {
                return null;
            }

            SessionState.SetString(ActiveRequestIdSessionKey, selectedRequest.requestId);
            SessionState.SetString(ActiveRequestPhaseSessionKey, selectedRequest.phase ?? GatePhases.Queued);
            SessionState.SetString(PhaseStartedSessionKey, CurrentUnixSecondsText());
            return selectedRequest;
        }

        private static bool IsQueuedPhase(string phase)
        {
            return string.IsNullOrEmpty(phase) || phase == GatePhases.Queued;
        }

        private static void ProcessRequest(GateRequestStore requestStore, GateRequestDto request)
        {
            if (request.phase == GatePhases.Returning)
            {
                AttemptReturnAndComplete(requestStore, request);
                return;
            }

            if (request.protocol != GateProtocol.GateProtocolVersion)
            {
                // Refused without ever staging this request's commit, so returning has nothing to undo.
                string reason = "protocol " + request.protocol.ToString(CultureInfo.InvariantCulture) + ", broker expects " + GateProtocol.GateProtocolVersion.ToString(CultureInfo.InvariantCulture);
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Refused, refusedReason = reason });
                return;
            }

            if (IsRequestTimedOut(request))
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Timeout });
                return;
            }

            switch (request.phase)
            {
                case GatePhases.Staging:
                    // Resumed mid blocking-CLI-call (Editor closed during the move) — retry from scratch.
                case GatePhases.Queued:
                    ProcessQueuedPhase(requestStore, request);
                    break;
                case GatePhases.Compiling:
                    ProcessCompilingPhase(requestStore, request);
                    break;
                case GatePhases.Testing:
                    ProcessTestingPhase(requestStore, request);
                    break;
                default:
                    Debug.LogWarning("[Worktree Toolkit] gate broker: unexpected phase \"" + request.phase + "\" for request " + request.requestId);
                    break;
            }
        }

        private static bool IsRequestTimedOut(GateRequestDto request)
        {
            if (!DateTime.TryParse(request.createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime createdUtc))
            {
                return false;
            }

            return createdUtc.AddSeconds(request.timeoutSeconds) < DateTime.UtcNow;
        }

        private static void ProcessQueuedPhase(GateRequestStore requestStore, GateRequestDto request)
        {
            if (request.playModeFixtures != null && request.playModeFixtures.Length > 0)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Refused, refusedReason = "play-mode fixtures are not supported by the broker yet" });
                return;
            }

            TransitionPhase(requestStore, request, GatePhases.Staging);

            StageMoveOutcome outcome = StageSwapGuard.MoveStage(request.commitSha, "stage-commit", request.commitSha, "--holder", "gate " + request.worktreeId);
            if (!outcome.moved)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Refused, refusedReason = DescribeStageMoveFailure(outcome) });
                return;
            }

            SessionState.SetBool(StageWasMovedSessionKey, true);
            TransitionPhase(requestStore, request, GatePhases.Compiling);
            CompileCapture.Begin(requestStore.SideFilePath(request.requestId, "compile"), requestStore.SideFilePath(request.requestId, "burst"));
            CompilationPipeline.RequestScriptCompilation();
        }

        private static void ProcessCompilingPhase(GateRequestStore requestStore, GateRequestDto request)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            string compileSideFilePath = requestStore.SideFilePath(request.requestId, "compile");
            string burstSideFilePath = requestStore.SideFilePath(request.requestId, "burst");
            bool hasMarker = CompileCapture.HasCompilationFinishedMarker(compileSideFilePath);
            if (!hasMarker && !HasPhaseExceededSeconds(CompilingPhaseFallbackSeconds))
            {
                return;
            }

            List<CompilerErrorDto> compilerErrors = hasMarker ? CompileCapture.ReadCompilerErrors(compileSideFilePath) : new List<CompilerErrorDto>();
            List<string> burstErrors = hasMarker ? CompileCapture.ReadBurstErrors(burstSideFilePath) : new List<string>();
            CompileCapture.End();

            if (compilerErrors.Count > 0)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.CompileErrors, compilerErrors = compilerErrors.ToArray() });
                return;
            }

            if (burstErrors.Count > 0)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.BurstErrors, burstErrors = burstErrors.ToArray() });
                return;
            }

            // P4 fallback: a no-op RequestScriptCompilation still reloads the domain but fires no
            // assemblyCompilationFinished, so a missing marker after 180 s falls back to this flag.
            if (EditorUtility.scriptCompilationFailed)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.CompileErrors, compilerErrors = new[] { new CompilerErrorDto { message = "compilation failed (see console)" } } });
                return;
            }

            if (request.editModeFixtures == null || request.editModeFixtures.Length == 0)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Pass });
                return;
            }

            TransitionPhase(requestStore, request, GatePhases.Testing);
        }

        private static void ProcessTestingPhase(GateRequestStore requestStore, GateRequestDto request)
        {
            string testsSideFilePath = requestStore.SideFilePath(request.requestId, "tests");
            string testsStartedSessionKey = TestsStartedSessionKeyPrefix + request.requestId;

            if (!File.Exists(testsSideFilePath) && !SessionState.GetBool(testsStartedSessionKey, false))
            {
                SessionState.SetBool(testsStartedSessionKey, true);
                TestCapture.RunEditModeFixtures(request.editModeFixtures, testsSideFilePath);
                return;
            }

            if (!TestCapture.TryReadSummary(testsSideFilePath, out TestSummaryDto summary))
            {
                return;
            }

            SessionState.EraseBool(testsStartedSessionKey);
            // Fixtures were named but nothing ran: a misspelt or unmatched fixture must never read as a pass.
            if (request.editModeFixtures.Length > 0 && summary.passed + summary.failed == 0)
            {
                BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Refused, refusedReason = "no tests matched the requested fixtures: " + string.Join(", ", request.editModeFixtures) });
                return;
            }

            string verdict = summary.failed > 0 ? GateVerdicts.TestFailures : GateVerdicts.Pass;
            BeginReturning(requestStore, request, new GateResultDto { requestId = request.requestId, verdict = verdict, tests = summary });
        }

        private static void BeginReturning(GateRequestStore requestStore, GateRequestDto request, GateResultDto result)
        {
            SessionState.SetString(PendingResultSessionKey, JsonUtility.ToJson(result));
            TransitionPhase(requestStore, request, GatePhases.Returning);
            AttemptReturnAndComplete(requestStore, request);
        }

        private static void AttemptReturnAndComplete(GateRequestStore requestStore, GateRequestDto request)
        {
            if (SessionState.GetBool(StageWasMovedSessionKey, false))
            {
                StageMoveOutcome outcome = StageSwapGuard.MoveStage(cachedTrunkBranch, "restore-trunk");
                if (!outcome.moved)
                {
                    int failedAttempts = SessionState.GetInt(ReturningRetryCountSessionKey, 0) + 1;
                    SessionState.SetInt(ReturningRetryCountSessionKey, failedAttempts);
                    if (failedAttempts >= ReturningMaxSilentFailedAttempts && failedAttempts % ReturningMaxSilentFailedAttempts == 0)
                    {
                        Debug.LogError("[Worktree Toolkit] gate " + request.worktreeId + " " + ShortSha(request.commitSha) + ": failed to restore trunk after " + failedAttempts.ToString(CultureInfo.InvariantCulture) + " attempts (" + DescribeStageMoveFailure(outcome) + ")");
                    }

                    return;
                }
            }

            CompleteRequest(requestStore, request);
        }

        private static void CompleteRequest(GateRequestStore requestStore, GateRequestDto request)
        {
            string resultJson = SessionState.GetString(PendingResultSessionKey, string.Empty);
            GateResultDto result = string.IsNullOrEmpty(resultJson) ? new GateResultDto { requestId = request.requestId, verdict = GateVerdicts.Refused, refusedReason = "gate broker lost its pending result" } : JsonUtility.FromJson<GateResultDto>(resultJson);

            requestStore.WriteResult(result);
            requestStore.DeleteRequest(request.requestId);
            DeleteSideFileQuietly(requestStore.SideFilePath(request.requestId, "compile"));
            DeleteSideFileQuietly(requestStore.SideFilePath(request.requestId, "burst"));
            DeleteSideFileQuietly(requestStore.SideFilePath(request.requestId, "tests"));
            ClearActiveRequestState(request.requestId);
        }

        // Before every phase transition the request is persisted first — even a no-op compile
        // reloads the domain (P4), so the phase on disk must already reflect where we are going.
        private static void TransitionPhase(GateRequestStore requestStore, GateRequestDto request, string newPhase)
        {
            request.phase = newPhase;
            requestStore.WriteRequest(request);
            SessionState.SetString(ActiveRequestPhaseSessionKey, newPhase);
            SessionState.SetString(PhaseStartedSessionKey, CurrentUnixSecondsText());
            Debug.Log("[Worktree Toolkit] gate " + request.worktreeId + " " + ShortSha(request.commitSha) + ": " + newPhase);
        }

        private static bool HasPhaseExceededSeconds(double thresholdSeconds)
        {
            string phaseStartedText = SessionState.GetString(PhaseStartedSessionKey, string.Empty);
            if (!long.TryParse(phaseStartedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long phaseStartedUnixSeconds))
            {
                return false;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - phaseStartedUnixSeconds >= thresholdSeconds;
        }

        private static string DescribeStageMoveFailure(StageMoveOutcome outcome)
        {
            if (outcome.blockers != null && outcome.blockers.Count > 0)
            {
                return string.Join("; ", outcome.blockers);
            }

            WorktreeCliJson.TryParse(outcome.cliResult, out CliErrorDto _, out string errorMessage);
            return errorMessage;
        }

        private static void ClearActiveRequestState(string requestId)
        {
            SessionState.EraseString(ActiveRequestIdSessionKey);
            SessionState.EraseString(ActiveRequestPhaseSessionKey);
            SessionState.EraseString(PhaseStartedSessionKey);
            SessionState.EraseString(PendingResultSessionKey);
            SessionState.EraseBool(StageWasMovedSessionKey);
            SessionState.EraseInt(ReturningRetryCountSessionKey);
            if (!string.IsNullOrEmpty(requestId))
            {
                SessionState.EraseBool(TestsStartedSessionKeyPrefix + requestId);
            }
        }

        private static void DeleteSideFileQuietly(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string CurrentUnixSecondsText()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        private static string ShortSha(string commitSha)
        {
            if (string.IsNullOrEmpty(commitSha))
            {
                return string.Empty;
            }

            return commitSha.Length <= 8 ? commitSha : commitSha.Substring(0, 8);
        }
    }
}
