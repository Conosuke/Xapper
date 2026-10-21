# Isolated Launch and Input Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional isolated-desktop launch, correct click/keyboard injection, and fix the applicable section C defects without regressing ordinary desktop operation.

**Architecture:** A server-owned native application object retains the desktop, kill-on-close job, and Process; SessionManager takes ownership immediately after connection. Inspector policy blocks only operations that require the person's desktop, and input changes reuse the current synthetic path and WPF input pipeline. Preserve the ordinary launch output-draining lifetime and existing runtime selection.

**Tech Stack:** C# 12, .NET 9 server, net462 and net6/7/8/9-windows Inspector/Protocol, Win32 Desktop/Job APIs, WPF, existing xUnit and MCP SDK. No additional packages.

**Spec:** `docs/superpowers/specs/2026-10-21-isolated-launch-input-design.md`

## Global Constraints

- Existing supported target runtimes remain supported, including .NET Framework 4.x.
- It must never switch the person's input desktop.
- Files, registry, network, and clipboard remain shared.
- Tool descriptions, server instructions, errors, and skill prose follow the repository's English convention.
- The skill's required question is kept exactly as supplied: "분리된 환경에서 테스트할까요, 직접 보이도록 테스트할까요?"
- The desktop name is `xapper-<server pid>-<monotonic sequence>`.
- `IsolatedDesktopEnvironment.Variable = "XAPPER_ISOLATED"`; only the exact value `"1"` enables Inspector isolation.
- No new package dependency is planned.
- Use the existing checkout; do not create a worktree unless the user requests one.
- Respect `CLAUDE.md`: XML documentation and regions, no nullable-forgiving suppression, UI access through the owning dispatcher.
- This host is Linux: compilation can be verified here; Windows xUnit, real-app, and lock-screen acceptance remain unrun until executed on Windows.

## Review Focus

1. Exit or native failure between suspended creation and resume: close the process/job/desktop without leaving an invisible application. Pin in Tasks 1 and 2.
2. Paths with spaces, Unicode arguments, and differently cased inherited environment names: launch the intended executable and override only the intended entries. Pin in Task 1.
3. An exception during connection, build checking, or client disposal: cleanup still removes isolated ownership and does not kill an ordinary launched app. Pin in Task 2.
4. A key handler changes focus or throws: text follows the current focus and pressed-key spoofing never persists. Pin in Task 5.
5. Indirect desktop operations through scripts and automatic failure screenshots: never show a notice or capture the person's pixels for an isolated app. Pin in Task 3.

## Verification protocol

For each task, add the specified tests first, then implement and run the task's
Windows test filter. The expected red result must be an assertion or missing
implementation, not an unavailable runtime. This Linux host cannot execute
those Windows tests: record that limit, compile tests and all production TFMs,
and leave runtime results explicitly unrun. Do not manufacture passing results
with an alternate test runner or remove tests that cannot run here.

Windows commands run from the repository root after `dotnet build Xapper.slnx`.
The full solution build supplies the TestApp used by integration fixtures.
Tests that mutate isolation or operate on real desktop state use
`[Collection(DesktopWindowCollection.Name)]`, restoring state in finally.

Linux build command, used after each coherent group of changes:

```sh
source /workspace/.xapper/activate.sh
dotnet build Xapper.slnx -p:EnableWindowsTargeting=true -m:2 --nologo
```

Commit only files belonging to the completed task; never stage unrelated user
changes. A commit is not evidence that its Windows runtime tests passed.

---

### Task 1: Native isolated application owner and Inspector policy

**Files:**
- Create: `src/Xapper.Protocol/IsolatedDesktopEnvironment.cs`
- Create: `src/Xapper.McpServer/Infrastructure/IsolatedDesktopApp.cs`
- Create: `src/Xapper.Inspector/IsolatedMode.cs`
- Create: `tests/Xapper.Tests/IsolatedDesktopAppTests.cs`
- Create: `tests/Xapper.Tests/IsolatedModeTests.cs`

**Interfaces:**
- Produces: `public const string IsolatedDesktopEnvironment.Variable = "XAPPER_ISOLATED"`.
- Produces: `IsolatedDesktopApp.Start(string desktopName, string exePath, string? args, string workingDirectory, IReadOnlyDictionary<string, string> environment) -> IsolatedDesktopApp`.
- Produces: `NextDesktopName() -> string`, `DesktopName: string`, `Process: Process`, `FindVisibleWindow() -> IntPtr`, `Dispose() -> void`.
- Produces: `internal static char[] EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)`.
- Produces: `IsolatedMode.IsOn: bool` with internal setter, `EnabledBy(string? value) -> bool`, `RefuseRealInput(string gesture) -> string?`, `RefuseScreenCapture() -> string?`, `RefuseForeground() -> string?`.

- [ ] Add tests first. `EnabledBy_RequiresExactOne`: assert true for `"1"`, false for null, `""`, `"true"`, `"01"`, and `" 1 "`. `Refusals_OnlyApplyInIsolation`: outside isolation assert three nulls; inside assert `isolated desktop`, gesture `wheel`, and `mode="render"` in their corresponding messages; restore IsOn in finally.
- [ ] Add `EnvironmentBlock_OverridesIgnoringCaseAndEndsWithTwoNuls`: temporarily supply a uniquely named environment key, override it with different casing, assert a single effective entry and two final NULs; restore the key without printing other variables. Add a Unicode value assertion and sorted-name assertion.
- [ ] Add Windows integration tests: a real TestApp window exists on the owned desktop while external MainWindowHandle is zero; repeated Dispose ends the app within five seconds; OpenDesktopW fails after cleanup; nonexistent exe throws Win32Exception and leaves no desktop. Copy the full TestApp build output into a temporary directory containing spaces and Unicode to test executable-path handling. Separately launch Windows PowerShell with a temporary script that writes its received Unicode argument and current directory to a temporary result file, then waits; assert the file's values. Use a helper that locates the current configuration's built TestApp and fails clearly if absent.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~IsolatedModeTests|FullyQualifiedName~IsolatedDesktopAppTests"`. Expect new tests to fail before implementation; distinguish this from host capability failure.
- [ ] Implement the shared constant and policy with exact-string parsing and the three actionable English refusals. No Win32 calls are needed for the policy.
- [ ] Implement native lifetime: create desktop/job, construct the Unicode block, create suspended with no inherited MCP handles, assign job, open managed Process.Handle, check ResumeThread, and release temporary handles in finally. Cleanup must explicitly terminate an unassigned process and always release job/Process/desktop. `FindVisibleWindow` enumerates only this desktop and filters by this Process.Id. Never call SwitchDesktop or SetThreadDesktop.
- [ ] Add a Windows child-process fixture in this test file: use the system Windows PowerShell executable as the isolated parent; its temporary script starts system ping.exe with `-t 127.0.0.1`, writes the child's PID to a temporary file, then waits. Close ownership and assert both captured PIDs exit. Bound file/process waits and clean up only test-owned processes. Verify failed creation cannot return a live owner or leave its desktop open; do not add a general native mocking framework solely for this check. Server-exit behavior is tested in Task 2.
- [ ] Run the task filter on Windows and the full cross-build on Linux when Windows is unavailable. Inspect failure paths for job assignment and resume failure even when OS fault injection is unavailable, and report unexercised native error paths.
- [ ] Commit as `feat: add isolated desktop application ownership and policy`.

### Task 2: Launch, Framework injection, and session cleanup

**Files:**
- Modify: `src/Xapper.McpServer/SessionManager.cs`
- Modify: `src/Xapper.McpServer/Tools/ProcessTools.cs`
- Modify: `src/Xapper.Injector/WpfProcessInjector.cs`
- Create: `tests/Xapper.Tests/IsolatedSessionTests.cs`
- Create: `tests/Xapper.Tests/IsolatedLaunchTests.cs`
- Create: `tests/Xapper.Tests/IsolatedServerLifecycleTests.cs`
- Modify: `tests/Xapper.Tests/LaunchPlanTests.cs`
- Modify: `tests/Xapper.Tests/AttachToRunningInspectorTests.cs`

**Interfaces:**
- Consumes: Task 1's native owner and shared constant.
- Produces: `SessionManager.OwnIsolated(IsolatedDesktopApp app) -> void`, `IsIsolated(int pid) -> bool`, `IsolatedDesktopOf(int pid) -> string?`.
- Produces: `WpfProcessInjector.InjectLaunched(int processId, IntPtr windowHandle) -> void`, validating the HWND belongs to the target PID and reusing native injection with WaitForApplication.
- Changes: `ProcessTools.Launch(string exePath, string? args = null, int timeoutMs = 15000, bool isolated = false, CancellationToken ct = default) -> Task<string>`.

- [ ] Add fake-pipe-backed session tests: transfer an actual isolated TestApp owner after AttachAsync, detach it, assert its Process exits and IsIsolated becomes false; repeat manager disposal with multiple owned apps. Assert duplicate ownership and ownership without a connected PID are rejected without losing the original owner.
- [ ] Add launch tests for successful ownership transfer, timeout before pipe connection, cancellation, early process exit, and build mismatch. Use actual TestApp/payload extraction and a wrong InspectorBuildCheck for build refusal; assert the isolated PID exits, its desktop cannot be opened, and no active isolated session remains. Add an ordinary detach regression that does not end the ordinary app; terminate only the test's own ordinary app in finally.
- [ ] Add IsolatedServerLifecycleTests: start the built server with redirected stdio, complete MCP initialize/initialized, call xapper_launch with the TestApp and isolated=true, capture the returned PID, and abruptly kill only that server. Assert the launched PID exits within five seconds and its desktop cannot be reopened. Use the test's own server PID to identify `xapper-<server pid>-1`; always clean up the test processes in finally and fail with bounded protocol-read diagnostics if initialization or launch fails.
- [ ] Add injection validation tests for a foreign or zero HWND, preserving normal attachment's main-window check. Add a Windows integration case for the net48 TestApp proving that isolated launch uses a window enumerated on that desktop instead of MainWindowHandle.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~IsolatedSessionTests|FullyQualifiedName~IsolatedLaunchTests|FullyQualifiedName~IsolatedServerLifecycleTests|FullyQualifiedName~LaunchPlanTests|FullyQualifiedName~AttachToRunningInspectorTests"`; confirm the intended failures.
- [ ] Add ownership by PID and cleanup with finally protection. Dispose a client that fails ConnectAsync; detach and manager disposal must still clean up every owned isolated application. If cleanup itself fails, retain evidence of the failure rather than report a successful close.
- [ ] Split the injector's validated ordinary/launch entry points around the shared runtime selection and injection core. For Framework launch wait on `FindVisibleWindow`, then call InjectLaunched; for .NET 6+ retain startup hooks.
- [ ] Implement the isolated launch branch, keep ownership local before connection, transfer immediately after AttachAsync, and detach on any isolated post-connection startup-validation failure. Preserve normal output draining and existing ordinary failure wording. Record the isolated PID/name before disposal to avoid accessing a disposed Process in error messages.
- [ ] Replace launch's File.Exists pipe probe with `Directory.EnumerateFiles(@"\\.\pipe\", IpcPipeNames.ForProcess(pid)).Any()`. Add the isolated close suffix on all relevant early-return failure paths. Detach records isolation before cleanup and reports closure afterward.
- [ ] Run task tests on Windows, all-target build on Linux, inspect git diff for unintended ordinary-launch changes, and commit as `feat: launch isolated apps with session-owned cleanup`.

### Task 3: Enforce isolation across gestures, capture, notices, and recording

**Files:**
- Modify: `src/Xapper.Inspector/IpcServer.cs`
- Modify: `src/Xapper.Inspector/Actions/ClickAction.cs`
- Modify: `src/Xapper.Inspector/Actions/ClickOutcome.cs`
- Modify: `src/Xapper.Inspector/NativeWindows/WindowCommands.cs`
- Modify: `src/Xapper.McpServer/Tools/NoticeTools.cs`
- Modify: `src/Xapper.McpServer/Tools/RecordTools.cs`
- Modify: `src/Xapper.McpServer/Tools/RunTools.cs`
- Create: `tests/Xapper.Tests/IsolatedOperationTests.cs`
- Modify: `tests/Xapper.Tests/OperatorNoticeTests.cs`
- Modify: `tests/Xapper.Tests/ScriptWindowTests.cs`

**Interfaces:**
- Consumes: Tasks 1 and 2's policy and session queries.
- Changes: `ClickOutcome(string Path, string? Warning, string? Error = null)` so refusal is an explicit result and old two-argument construction remains valid.
- Changes: `RecordTools(ScreenRecorder recorder, SessionManager sessionManager)`; update any callers and rely on existing DI for production.
- Changes: `RunPointGesture`'s named result adds `string? Error`; right-click and wheel callers return IPC errors when set.

- [ ] Add refusal tests that assert no input/foreground/capture side effect occurs for an isolated fallback, while synthetic-delivered and synthetic-stalled outcomes retain existing handling. Test screen capture and foreground refusal, allowed window state commands, and the direct ClickAction real-input entry point. Restore isolation after each test.
- [ ] Add server-tool tests proving isolated recording returns Error before starting ffmpeg and stop remains callable; an isolated notice returns "not needed" without invoking IOperatorNotice.ShowAsync. Cover ordinary notices unchanged.
- [ ] Add a RunTools/ScriptRunner fake-send regression: isolated `notice.show` never invokes the notice, and automatic failure screenshot uses render. Ordinary automatic failure screenshot still uses screen. Reuse existing fake notices and script helpers.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~IsolatedOperationTests|FullyQualifiedName~OperatorNoticeTests|FullyQualifiedName~ScriptWindowTests"` and confirm the relevant pre-change failures.
- [ ] Guard only unavailable synthetic fallbacks before foregrounding, real modifiers, or real drag; retain delivered/stalled semantics and thumb dragging. Return policy failures through IPC rather than exceptions inside the injected app. Guard direct ClickAction and WindowCommands callers too.
- [ ] Refuse screen capture before reading pixels and correct render warnings to recommend find/snapshot in isolation. For isolated scripts pass no notice to the runner; use render in RunTools.AttachScreenshot. Add the recording/notice checks based on the active session's PID and preserve no-session recording behavior.
- [ ] Run Windows filters, all-target build, and diff review; commit as `feat: block unsupported desktop operations in isolated sessions`.

### Task 4: Correct coordinate-free click routing and button focus

**Files:**
- Modify: `src/Xapper.Inspector/Actions/ClickAction.cs`
- Modify: `src/Xapper.Inspector/IpcServer.cs`
- Modify: `tests/Xapper.Tests/ClickPathTests.cs`
- Modify: `tests/Xapper.Tests/SyntheticMouseGestureTests.cs`
- Create: `tests/Xapper.Tests/ButtonFocusTests.cs`

**Interfaces:**
- Consumes: Task 3's error-capable ClickOutcome.
- Produces: `ClickAction.HasEventPath(UIElement element) -> bool`; predicate and execution agree on Invoke, Toggle, unselected SelectionItem Select, and ButtonBase.ClickEvent.
- Retains: `ClickAction.Execute(UIElement element, double? relativeX = null, double? relativeY = null, bool doubleClick = false, ModifierKeys modifiers = ModifierKeys.None) -> ClickOutcome`.

- [ ] Replace the regression expecting four direct routed events with a real-window test: click a child without an event path and assert its parent/list selection handler runs through synthetic input at (0.5,0.5). Assert the response names synthetic input, not simulated routed events. Cover selected vs unselected selection-item behavior.
- [ ] Add an editor plus Save button test with a lost-focus commit and assert Save observes the updated value. Add ClickMode.Hover and non-focusable-button cases preserving existing behavior.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~ClickPathTests|FullyQualifiedName~SyntheticMouseGestureTests|FullyQualifiedName~ButtonFocusTests"`; confirm the tests expose current behavior.
- [ ] Implement the shared predicate and matching event activation; focus eligible buttons before coordinate-free activation. In HandleClick, fill X/Y with 0.5 when neither was supplied and no event path exists, then take the existing synthetic block. Apply the same decision in ClickAction for direct callers and retain isolated refusal before real input.
- [ ] Keep warnings and actual path reporting; do not report success for an unavailable element/coordinate path. Run Windows filters and all-target build; commit as `fix: route unsupported event clicks through synthetic center input`.

### Task 5: Correct keyboard pipeline and pressed-state spoofing

**Files:**
- Modify: `src/Xapper.Inspector/Actions/KeyboardInput.cs`
- Modify: `src/Xapper.Inspector/Actions/InputSpoof.cs`
- Modify: `tests/Xapper.Tests/KeyboardInputTests.cs`
- Modify: `tests/Xapper.Tests/KeyboardInputModifierTests.cs`
- Modify: `tests/Xapper.Tests/InputSpoofModifierTests.cs`
- Create: `tests/Xapper.Tests/KeyboardCompositionTests.cs`

**Interfaces:**
- Retains: KeyboardInput.Send and TypeText result shapes and current named-key parsing.
- Produces: `InputSpoof.BeginKey(int virtualKey) -> void`, `InputSpoof.EndKey() -> void`, scoped to the injecting thread with finally cleanup.
- Changes internal implementation: RaiseKey returns handled state; cached MarkSystem reflection marks down/up for Alt without Ctrl and F10 without Ctrl; text uses StartComposition.

- [ ] Add tests: Alt+Down reaches Key.System/SystemKey.Down, F10 without Control is system, Ctrl+F10 and Ctrl+Alt+Down are ordinary, PreviewTextInput can reject letters in numeric input, and a handled key-down produces no text.
- [ ] Add a key-down handler that focuses a second TextBox: assert its following text and subsequent strokes go there. Add a throwing handler and assert injected key state is cleared afterward. Assert IsDown is true in key-down and false in key-up when the hook is available; an unavailable hook must not block plain text injection.
- [ ] Add punctuation/Korean tests: text `"_"` and `"한"` produces the requested characters without a misleading base-key event. For the active layout, compare VkKeyScanW's modifier requirement and assert only characters requiring no modifiers receive base-key events. Do not assume every keyboard layout is US English.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~KeyboardInputTests|FullyQualifiedName~KeyboardInputModifierTests|FullyQualifiedName~InputSpoofModifierTests|FullyQualifiedName~KeyboardCompositionTests"`; confirm intended failures.
- [ ] Implement layout mapping and system-key marking, StartComposition, handled suppression, and current-focus resolution per stroke. Recompute presentation source after a focus move rather than retaining an obsolete source. Keep Ctrl/Alt text suppression and existing missing-modifier-hook failures.
- [ ] Extend GetKeyState/GetAsyncKeyState spoofing for the injected virtual key; initialize hooks opportunistically for plain keys, enable down before processing, clear before up, and clear again in finally. Preserve the existing mouse spoof lifetimes and rooted delegates.
- [ ] Run Windows filters plus existing TypeActionFallbackTests and InputSpoofHookLifetimeTests, then all-target build; commit as `fix: match WPF keyboard and text composition semantics`.

### Task 6: Fix applicable pipe, window, event, scroll, and session defects

**Files:**
- Modify: `src/Xapper.Inspector/IpcServer.cs`
- Modify: `src/Xapper.Inspector/NativeWindows/ProcessWindows.cs`
- Modify: `src/Xapper.Inspector/Events/EventWatcher.cs`
- Modify: `src/Xapper.Inspector/Actions/ScrollAction.cs`
- Modify: `src/Xapper.McpServer/Tools/FindTools.cs`
- Modify: `src/Xapper.McpServer/Tools/CaptureTools.cs`
- Modify: `tests/Xapper.Tests/IpcResilienceTests.cs`
- Modify: `tests/Xapper.Tests/NativeWindowsTests.cs`
- Modify: `tests/Xapper.Tests/EventLogTests.cs`
- Create: `tests/Xapper.Tests/ScrollActionTests.cs`
- Create: `tests/Xapper.Tests/MissingSessionTests.cs`

**Interfaces:**
- Retains: existing public action/tool request and response shapes.
- Changes: ProcessWindows.Main prefers the WPF MainWindow's HWND, resolved on its dispatcher, before a validated native fallback.

- [ ] Add pipe regressions: connect-and-immediately-disconnect, then healthy ping; repeated probes do not consume a connection; Stop while waiting ends cleanly. Assert listener completion is not faulted by a connection-local exception.
- [ ] Add a misleading top-level/native glow-like window beside a WPF main window and assert default window commands target the WPF main HWND; explicit HWND still targets the specified owned window.
- [ ] Add noise assertions: CanExecute and PreviewCanExecute are noise; SelectionChanged and Click are retained. Add zero-height/zero-width scrolling tests returning readable axis/extent failures, plus valid single-axis movement with the other axis -1.
- [ ] Add no-session Find, ElementAt, and Screenshot tests: response text starts with Error and contains the active-session reason, including the screenshot content-block shape. Assert ToolFailure recognizes those responses.
- [ ] Run on Windows: `dotnet test tests/Xapper.Tests --filter "FullyQualifiedName~IpcResilienceTests|FullyQualifiedName~NativeWindowsTests|FullyQualifiedName~EventLogTests|FullyQualifiedName~ScrollActionTests|FullyQualifiedName~MissingSessionTests"`; confirm intended failures.
- [ ] Move WaitForConnectionAsync into connection-local try/catch, exit cleanly on server cancellation, and recreate the pipe after a transient broken connection. Do not hide unexpected listener failures with a perpetual retry loop.
- [ ] Resolve the real WPF main HWND through its dispatcher and retain validated fallback. Add CanExecute noise. Validate scroll axes/extents before SetScrollPercent; leave -1 axes unchanged. Catch only GetActive's session InvalidOperationException in find/capture tool entry points, preserving normal content types.
- [ ] Run Windows filters and all-target build; commit as `fix: recover pipe startup and improve window and tool diagnostics`.

### Task 7: Documentation, integration review, and final verification

**Files:**
- Modify: `src/Xapper.McpServer/Program.cs`
- Modify: `src/Xapper.McpServer/Tools/ProcessTools.cs` descriptions
- Modify: relevant capture/record/notice tool descriptions
- Modify: `skills/xapper-mcp/SKILL.md`
- Modify: `README.md`
- Modify: `TOOLS_SPECIFICATION.md`
- Update: this plan's completed checkboxes and verified/unrun outcomes

**Interfaces:**
- Consumes: all preceding task interfaces. Adds no runtime interface.

- [ ] Explain the first-launch question, task-scoped preference, isolation boundaries, window invisibility, lock-screen operation, render-only screenshots, no later attach, rejected real input, and closure on detach/server exit in launch descriptions, server guidance, and the skill's Arrive section.
- [ ] Rewrite obsolete "click never refuses" and four-event fallback guidance to describe actual event paths, button focus, center synthetic fallback, and refusal. Add matching README/tool-spec behavior; correct select/scroll parameter names while preserving the already-correct assert names.
- [ ] Run full solution build on Linux with EnableWindowsTargeting and record warning/error counts. Attempt the existing xUnit command once to confirm/report the platform restriction; do not retry unchanged failures. On Windows, run `dotnet build Xapper.slnx` followed by `dotnet test tests/Xapper.Tests --no-build --logger "trx;LogFileName=isolated-input.trx"` and inspect current-run counts.
- [ ] On a Windows desktop, perform the spec's real TestApp checks, then DevExpress checks if that app is supplied: synthetic gestures, menus/dialogs, render capture, refusals, no visible app windows on the person's desktop, detach cleanup, server-kill cleanup, and locked-session operation. Report any unavailable app or machine explicitly.
- [ ] Inspect the full diff against the starting commit and run `git diff --check`. Review ownership races, every early return, direct and scripted operations, WPF dispatcher access, and all target frameworks. Request the workflow's final independent review only when implementation is ready; do not delegate product implementation unless the execution method authorizes it.
- [ ] Commit the documentation and report A/B/C file mapping, differences from the supplied reference, build counts, unit-test outcomes, and actual-app coverage. Distinguish a verified Linux build from unexecuted Windows acceptance.

## Plan self-review and execution handoff

### Execution record (2026-10-21)

The user approved native implementation. Tasks 1-6 have product implementations
and regression tests committed; the full Debug solution build across all
existing target frameworks passed with **0 warnings and 0 errors** after the
changes. Documentation and tool descriptions for Task 7 are updated.

Windows test execution is **not complete**. The existing xUnit command was
attempted here and aborted because Microsoft.WindowsDesktop.App 9.0 is
unavailable on Linux; no test cases executed. Unchecked runtime-test steps
above remain acceptance work, including tests that need OS failure injection,
real TestApp/DevExpress interaction, and a locked Windows session. Compilation
of regression tests does not establish their runtime outcome. No skipped
Windows operation is represented as a passing test.

The reference-skill retrieval baseline found missing launch-choice/isolation
guidance and obsolete click descriptions. A fresh retrieval check after the
update correctly recovered the required question, all isolated restrictions,
center-click behavior, and visible-mode notice ownership, finding no conflict.

Coverage: A maps to Tasks 1-3 and 7; B to Tasks 4-5 and 7; C to Tasks 2, 6,
and 7. Regression tests cover the normal lifetime, startup refusal, concurrent
session identity, script capture, input filtering, and associated error paths.
Deterministic assignment/resume failure, exit during the suspended startup
handoff, and throwing client-disposal fault injection are not covered. These
native/cleanup failure paths were inspected but remain unexercised.
Final review added atomic InspectorSession snapshots, AttachIsolatedAsync, and
client-specific build verification to remove active-session races. Direct
synthetic center clicks now deliver from a separate sender while pumping the
owning dispatcher, preserving the stall watchdog. Keyboard coupling tests
select an unmodified character in the active layout and state tests use F24.

Interfaces above are the names later tasks consume. Native failures that
cannot be induced reliably are explicitly reviewed and reported, not marked
as runtime-tested.

Execution method selected by the user: native implementation in this session,
followed by independent final review. That review identified three important
correctness issues and one layout-dependent test issue. The fix pass added
regression cases and the atomic session and separate-sender corrections above.
The complete solution still cross-compiles with zero warnings/errors. Windows
runtime execution remains pending; see `docs/validation/2026-10-21-isolated-launch-input.md`.
