# Isolated desktop launch and input corrections

Date: 2026-10-21

Status: Design prepared for user review. Product implementation has not started.

## Intent and scope

Port the behavior in the supplied Xapper request into this checkout of
`Conosuke/Xapper`, inspected at commit
`4969d411ac6d7fabdcab1f08b5f4d32166125a96`. Follow the request's principles rather
than copying names or assuming equivalence with `vway-dev/xapper`.

The outcome is an optional `isolated` argument to `xapper_launch`, corrected
click and keyboard injection, and the applicable defects from section C of the
request. Ordinary launch and attachment retain their current behavior unless a
specified defect requires a correction. Existing supported target runtimes
remain supported, including .NET Framework 4.x.

Isolation refers to a Win32 desktop on the interactive window station. It is
neither a security sandbox nor a Win+Tab virtual desktop. Files, registry,
network, and clipboard remain shared. It must never switch the person's input
desktop.

Tool descriptions, server instructions, errors, and skill prose follow the
repository's English convention. The skill's required question is kept exactly
as supplied: "분리된 환경에서 테스트할까요, 직접 보이도록 테스트할까요?"

## Existing code and required adaptations

| Area | Current location | Observation and planned change |
| --- | --- | --- |
| Launch and detach | `src/Xapper.McpServer/Tools/ProcessTools.cs` | Add isolation, retain existing hook/runtime planning and build checking, and replace the launch pipe probe. |
| Session ownership | `src/Xapper.McpServer/SessionManager.cs` | Currently owns pipe clients only; additionally own isolated applications by PID. |
| Runtime-dependent injection | `src/Xapper.Injector/WpfProcessInjector.cs` | Framework launch currently requires `MainWindowHandle`; introduce a narrowly scoped launch path based on a verified HWND on the owned desktop. |
| Native injection | `src/Xapper.Injector/NativeInjector.cs` | Injection itself uses a process handle and remote execution, not a main HWND. Reuse this implementation. |
| Inspector dispatch | `src/Xapper.Inspector/IpcServer.cs` | Owns synthetic gesture attempts, real-input fallback, capture, and native-window commands. Apply policy before real side effects. |
| Click action | `src/Xapper.Inspector/Actions/ClickAction.cs` | Currently ends with four direct routed mouse events; replace this path and add button focus handling. |
| Keyboard injection | `src/Xapper.Inspector/Actions/KeyboardInput.cs` | Add system keys, text composition, handled-key suppression, current-focus targeting, and layout-aware character mapping. |
| Input state hooks | `src/Xapper.Inspector/Actions/InputSpoof.cs` | Extend modifier spoofing with the currently injected virtual key and reliable cleanup. |
| Native windows | `src/Xapper.Inspector/NativeWindows/ProcessWindows.cs`, `WindowCommands.cs` | Prefer WPF's real main HWND; refuse foreground requests in isolated mode. |
| Recording and notices | `RecordTools.cs`, `NoticeTools.cs`, `Scripting/ScriptSession.cs` | Recording cannot capture isolated windows. Explicit script notices also need the policy applied. |
| Agent guidance | `Program.cs`, `Tools/ProcessTools.cs`, `skills/xapper-mcp/SKILL.md` | This checkout uses `xapper-mcp`, not `skills/xapper`. Update both arrival and click guidance. |

This checkout deliberately keeps an ordinary launched `Process` alive to drain
redirected stdout and stderr after the launch tool returns. Disposing it at
method exit closes those readers while the application still runs. Preserve
this behavior. An isolated application's `Process` has a different owner and
is disposed by that owner after termination.

## A. Isolated desktop launch

### Shared contract and native owner

Add `IsolatedDesktopEnvironment.Variable = "XAPPER_ISOLATED"` to Protocol, usable
on all existing Protocol targets. Add
`Infrastructure/IsolatedDesktopApp.cs` to the server. It owns the desktop, the
kill-on-close job, and the retained `Process`. It exposes desktop identity and
PID/Process access without transferring their ownership.

The desktop name is `xapper-<server pid>-<monotonic sequence>`. Create it using
`CreateDesktopW`, and keep its handle open throughout the session. Configure a
job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` before starting the application.

Build a case-insensitive, name-sorted Unicode environment block from the
server's environment and explicit overrides. Add `XAPPER_ISOLATED=1` and the
existing startup-hook override when the runtime supports it. Preserve current
hook planning rather than inventing a new runtime selection rule. The block
ends in two NUL characters; tests must not log inherited environment values.

Launch with `CreateProcessW`, an explicit application path, quoted executable
in the mutable command line, `WinSta0\\<desktop name>`, `CREATE_SUSPENDED`, and
`CREATE_UNICODE_ENVIRONMENT`. Do not inherit server MCP standard handles or
allow application output to enter MCP stdout. Pass through supplied arguments
and use the executable's directory as the working directory.

Assign the suspended process to the job before resuming it. Capture and open
the managed `Process.Handle` before resume so early exit codes remain readable.
Check `ResumeThread` for failure. Native errors include the original Win32
failure and identify the operation that failed.

Every failed creation step releases resources already acquired. If a process
exists but was not assigned to the job, terminate it explicitly; after job
assignment, close the job to terminate it and its descendants. Always close
temporary native process/thread handles. Dispose is idempotent, waits up to
five seconds for termination, and releases the Process and desktop even if an
intermediate cleanup step fails.

### Framework launch adaptation

For .NET 6+ use the current startup hook and pipe readiness path.

For .NET Framework, enumerate windows on the owned desktop using
`EnumDesktopWindows`, filtering by the launched process ID. Do not wait for
the server's `Process.MainWindowHandle`, which remains zero across desktops.
Once a window exists, use the current native injection and
`InspectorStartArguments.WaitForApplication` behavior.

The injector's launch entry point validates that the supplied HWND belongs to
the target PID before sharing the existing injection core. Ordinary attach
keeps its existing visible-window requirement; the change must not turn a
general attachment check into an unrestricted bypass.

### Ownership transfer and failure behavior

`ProcessTools.Launch` gains `isolated = false`, placed without breaking known
internal call sites. A created isolated app remains locally owned until its
Inspector connection succeeds. Immediately after connection, transfer it to
`SessionManager.OwnIsolated`, before checking the Inspector build.

`SessionManager` exposes `IsIsolated(pid)` and `IsolatedDesktopOf(pid)` and owns
the application through detach/disposal. Reject ambiguous or duplicate
ownership. Detach removes session state and closes an owned isolated app even
when pipe disposal fails. Disposing the manager attempts cleanup of all owned
apps rather than abandoning the remainder after one error.

Before transfer, timeout, cancellation, early exit, failed injection, and
connection errors all dispose the locally owned isolated app. Explain that
the app was closed because it cannot be reached later with attach. Ordinary
launch keeps its existing failure wording and application-lifetime behavior.
After transfer, build refusal and failed startup validation must detach and
clean up the isolated session. Job ownership also terminates the app and its
descendants when the server process crashes.

Detach reports that an isolated app was closed. A subsequent launch is needed
after detach or server termination; no later attach is promised.

### Inspector policy

Add `IsolatedMode` initialized once from the exact environment value `"1"`,
with the requested internal setter for tests. Its three refusal helpers return
null outside isolation. In isolation they explain:

1. Real mouse input cannot reach this app, the synthetic path was unavailable,
   and no real input was sent. Include the gesture name and alternatives such
   as choosing an uncovered point or using select, toggle, type, key, or scroll.
2. Screen capture reads the person's desktop; use `mode="render"` and use
   find/snapshot for separate popup and menu windows.
3. Bringing the app to the person's foreground is inapplicable; in-process
   interaction does not require it.

Each refusal contains `isolated desktop`. Guard click/double-click only after
synthetic delivery is unavailable, point gestures before foregrounding or
pressing real modifiers, and the real drag branch before its first side effect.
Preserve successful synthetic paths, stalled-handler handling, and thumb
dragging. Direct callers of coordinate `ClickAction` must also be unable to
send real input in isolation.

Refuse screen screenshots before reading desktop pixels. Adapt render warnings
so they do not recommend an unavailable screen mode. Refuse foreground window
commands before restoring/foregrounding the window, while allowing maximize,
minimize, and restore.

### Server-only policies and documentation

Reject `xapper_record_start` while the active session is isolated: this
recorder captures the person's desktop, so a warning followed by successful
recording would misleadingly imply that the app was recorded. Keep stop
available so an existing recording can still be finalized.

`xapper_notice_show` returns an informational "not needed" result for an
isolated session. Ensure the script notice path observes the same policy.
Notice hiding remains harmless. Real-input refusals must not be mistaken for
successful real-mouse actions by automatic notice detection.

Update launch/argument/detach descriptions, server instructions, README, tool
specification where applicable, and the skill's Arrive section. Before the
first launch in a task, agents ask the specified question once and reuse the
answer until the user changes it. Explain visibility, lock-screen behavior,
render-only capture, no later attach, refused real input, and process lifetime.

## B. Click and keyboard corrections

### Click paths

Expose an event-path predicate shared by `ClickAction` and the IPC click
handler. Recognized paths are Invoke, Toggle, selection of an unselected
selection item, and `ButtonBase.ClickEvent`. The predicate must agree with the
path actually taken, including selection state.

When neither coordinate is supplied and the target lacks an event path, fill
both coordinates with 0.5 and use the existing synthetic mouse path. Remove
the four direct left-button events as the terminal fallback. Report the actual
path and coordinates, including any refusal. Direct `ClickAction` use must
apply the same predicate and use the available coordinate path consistently.

For coordinate-free button activation, focus a focusable button before
activation, except for `ClickMode.Hover`. This allows lost-focus editing and
validation to commit before the click executes. Preserve existing
hit-testing warnings for event activation and modifier semantics.

### Keyboard pipeline

Mark key events as system keys using cached reflection for WPF's nonpublic
`KeyEventArgs.MarkSystem`: Alt combinations without Control, and F10 without
Control. Keep Ctrl combinations ordinary. Apply the system marking to the
appropriate down/up events and verify `Key.System`/`SystemKey` behavior.

Inject text through `TextCompositionManager.StartComposition` so
`PreviewTextInput` filters run. If the key-down route was handled, do not add
the corresponding text. Resolve the focused input element again when adding
text because the key may have opened an editor and moved focus. Avoid sending
the remainder of a string to an obsolete initial focus target.

Use `VkKeyScanW` for the current keyboard layout. A character maps to a key
only when it exists and requires no additional modifiers; otherwise inject
text without a misleading base-key event. This covers `_` and Korean text.

Extend `InputSpoof` to hold the injected virtual key down before key-down
processing and clear it before key-up. Always clear it in finally, including
when application handlers throw. Limit the spoof to the intended injection
scope and preserve rooted native detours. Without a hook, plain key/text
injection still runs without pressed-state spoofing. Preserve the existing
explicit failure for modifier combinations that cannot be represented safely.

## C. Confirmed applicability

1. Launch currently probes with `File.Exists` while attach enumerates pipe
   names. Use the enumeration probe in launch. Move
   `WaitForConnectionAsync` inside connection-local error handling; cancellation
   exits cleanly, while a broken connection leaves the listener available.
2. `ProcessWindows.Main` currently starts with `Process.MainWindowHandle`.
   Prefer the HWND of `Application.Current.MainWindow` using its owning
   dispatcher; retain a validated native fallback for a genuinely missing WPF
   main window. Explicit HWND requests retain their current semantics.
3. `EventWatcher` does not filter `CanExecute`. Add it to the noise rule without
   suppressing selection and click events.
4. `ScrollAction` calls `SetScrollPercent` on unsupported axes. Return a useful
   error before the provider throws, including scrollable height/width or the
   unsupported requested axis. Preserve -1 as "leave this axis unchanged".
5. `FindTools` and `CaptureTools` call `GetActive` outside a readable error
   response path. Return the session error using their existing text/content
   result shapes and the shared failure-prefix convention. Do not blanket-catch
   unrelated programming errors.
6. README select and scroll arguments are outdated. Align them with
   `itemIndex`/`itemText` and `verticalPercent`/`horizontalPercent`. The inspected
   README assert entry already uses `property`/`expected`; keep it correct.

## Validation and acceptance

Use the existing xUnit suite and its STA/desktop collections. Add regression
tests that observe behavior rather than merely repeating implementation
details. Tests mutating `IsolatedMode` restore it in finally and cannot run in
parallel with consumers of that shared state.

Required Windows checks:

- Exact environment parsing, all refusal messages, and ordinary-mode nulls.
- Unique desktop names, environment sorting/overrides/double-NUL termination,
  nonexistent-executable failure, and no remaining desktop after failure.
- Launch the built TestApp without an Inspector hook on an isolated desktop:
  a visible HWND exists there, external MainWindowHandle is zero, dispose ends
  the app within five seconds, and OpenDesktopW fails after handles close.
- Session ownership/detach and build-refusal cleanup; ordinary detach continues
  leaving ordinary launched apps alive. Verify job termination includes a
  child process and server termination does not leave an invisible app behind.
- Pipe probe/disconnect recovery, successful reconnect, and clean cancellation.
- Center mouse click changes parent/list selection; ordinary button activation
  commits an edited value; the removed direct-event fallback is not expected.
- Preview text filters, handled-key suppression, focus changes during key-down,
  system-key combinations, F10 with/without Control, pressed-state cleanup,
  layout-aware punctuation, and text-only Korean input.
- Main-window targeting with a misleading top-level window, event-noise
  filtering, zero-extent scrolling, and readable no-session tool responses.
- The full existing test suite with passed, failed, skipped, and unrun outcomes
  reported separately.

Actual Windows app acceptance additionally verifies synthetic coordinate
click/double-click/right-click/wheel, combo and menu popups, WPF modal and
native dialogs, render capture, refused screen/foreground/record actions,
unneeded notices, zero app windows on the person's desktop, and process and
desktop removal after detach. Repeat the interaction checks while locked when
the Windows test environment allows it. The supplied DevExpress observations
are reference behavior, not validation results for this checkout.

Current environment limitations: this cloud machine is Linux. It can build
the full solution with:

```sh
source /workspace/.xapper/activate.sh
cd /workspace/Xapper
dotnet build Xapper.slnx -p:EnableWindowsTargeting=true -m:2 --nologo
```

Run this after implementation across every existing target framework and
report warning/error counts. Do not claim Windows runtime, xUnit, lock-screen,
or real-app acceptance from a Linux build. Existing xUnit execution requires
Microsoft.WindowsDesktop.App on Windows; no substitute passing suite should
hide that limitation. No new package dependency is planned.

## Handoff

After written-spec approval, prepare and review the implementation plan using
the writing-plans workflow. Keep changes in the existing isolated checkout;
do not create a worktree unless requested. Product implementation follows that
review. Completion reporting maps A/B/C to changed files, explains adaptations,
and separates build evidence, unit-test evidence, and actual-app evidence.
