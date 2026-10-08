---
name: xapper-e2e-make
description: Use when a flow in a WPF app driven through Xapper should be kept as an end-to-end test that replays without an agent - writing a new E2E case, changing an existing one, or setting up the e2e folder of a project for the first time. Also "이 흐름 E2E 테스트로 남겨줘", "E2E 시나리오 만들어줘", "E2E 케이스 추가해줘", "C02 고쳐줘".
---

# Making an E2E case

An E2E case is the tool calls you made while driving the app, minus the trial and error, written as
a C# file a CI machine replays on every build. No tokens are spent when it runs. Each case has an id
(C01, C02 ...) and an entry in `e2e/cases.yaml` that says what it proves, so a person - or an agent
asked to "test the search feature" - can pick cases without reading code.

## 1. Look for a case that already covers it

Read `e2e/cases.yaml` before writing anything. If a case has the same `feature` and its `title` or
`checks` overlap what you were asked to cover, stop and tell the person: its id, feature, title and
checks. Then ask which they want:

- **Change that case** - same id, same file, list entry updated.
- **Add a new case** - it proves something the existing one does not.
- **Leave it** - the flow is already covered.

If nothing overlaps but the feature has other cases, list them anyway, so the person sees what is
already tested. When the person names a case ("fix C02"), skip this step and change that case.

## 2. Settle the flow by driving the app

Work the flow out live with Xapper first, following the xapper-mcp skill: find the controls, act,
and read the results until every step is known and the outcome is what it should be. Note which
selectors worked; a case cannot use refs, because refs are renumbered on every run.

## 3. Set up the e2e folder, once per project

If the project has no `e2e/` folder yet, copy this skill's `templates/` into it - `cases.yaml`,
`run-all.cs` and `common/`. Do not rewrite the shared code from memory; the templates are what has
been built and run.

Then make sure the project's `.gitignore` keeps out what a run produces: `e2e/results/` (recordings
and screenshots) and `e2e/common/bin/` and `e2e/common/obj/` (the shared project is built in place
the first time a case runs). Many .NET repositories already ignore every `bin/` and `obj/`; add only
what is missing.

- `common/` holds what every case shares: checking the environment, connecting to Xapper, the
  output folder, recording, the failure screenshot, closing the app and the exit code. It is the
  one real project in the folder and is committed.
- `run-all.cs` and the case files have no project file. `dotnet run` builds each of them in a
  temporary project outside the repository, from the `#:` lines at its top, and rebuilds it only
  when the file changes - so the first run of a case takes a few seconds longer.
- `run-all.cs` runs the listed cases and refuses to run anything when the list and the files disagree.

## 4. Register the case and write its file

Take the next id after the highest one ever listed; an id is never reused, even after its case is
deleted. Add the entry to `cases.yaml`:

```yaml
  - id: C02
    feature: search
    title: Typing draft into the filter shows only the three drafts
    file: C02-filter-shows-drafts.cs
    checks:
      - "screen: the result count becomes 3"
      - "data: the app's own server lists three drafts"
```

Then write the case file, named after its id:

```csharp
#:project common/E2E.Common.csproj
#:property PublishAot=false
using E2E.Common;

return await Scenario.RunAsync("C02", async x =>
{
    await x.Call("xapper_launch", new { exePath = x.App });
    await x.Call("xapper_type", new { target = "name=FilterBox", text = "draft" });
    await x.Call("xapper_key", new { key = "Enter" });
    await x.Call("xapper_run", new { script = "xapper.assert('name=ResultCount', 'Text', '3', { timeout: 5000 })" });

    // The screen shows what the app drew, not what it stored. Read the result through the app's own
    // means as well - here an MCP server it ships, started from APP_MCP; use its real tool and arguments.
    var app = await x.Connect("APP_MCP");
    var stored = await app.Call("list_items", new { filter = "draft" });
    if (!stored.Contains("draft"))
        throw new InvalidOperationException($"the app's own record does not match the screen: {stored}");
});
```

The rules that keep a case honest and repeatable:

- **Check the outcome by more than the screen.** If the app or its environment offers another way
  to read the result - an MCP server of its own, a command-line tool, the files, database or log it
  writes - use it in the case and say so under `checks`. A case that only reads the screen passes
  on a UI that looks right over data that is wrong. `x.Connect("VARIABLE")` starts any other MCP
  server from the path in that environment variable; a check that cannot run must fail the case,
  never be skipped.
- Target by selector, not by ref.
- `x.Call` ends the case at the first result Xapper marks as an error - a refused action, a failed
  assert, a script that failed, a batch that stopped early - so never read the text to decide.
- When a step has to wait for the app, wait inside `xapper_run` with `waitUntil` or `assert`'s
  timeout. The `xapper_assert` tool itself does not wait. A fixed sleep is too short on a slow
  machine and wasted on a fast one.
- Separate calls are fine here. Batching saves conversation turns, and a case has none.
- Leave launching, recording and clean-up to `Scenario.RunAsync`. Do not close the app or stop the
  recording inside the steps.

## 5. Run it once

Run the new case through the runner and make sure it passes before you hand it over:

```bash
dotnet run e2e/run-all.cs -- C02
```

A case you have not seen pass is not finished. If it fails, read the failing line and the
screenshot under `e2e/results/C02/`, fix the case, and run it again. The run records the whole
desktop and the failure screenshot is taken in screen mode, so on a machine a person is using, tell
them before you run it - their other windows end up in the video.

If the run stops at `xapper_record_start` saying ffmpeg was not found, ffmpeg has to be installed on
the machine that runs the cases - `winget install Gyan.FFmpeg`, then `ffmpeg -version` in a new shell
to confirm. Installing software changes the person's machine, so give them the command or ask
before running it. A Xapper server that was already running does not see the new `PATH`; restart
whatever starts it - for an agent session, a new session - or every run keeps failing the same way.

## 6. CI

To run the cases on every build, add a CI step that runs `dotnet run e2e/run-all.cs` and fails the
build on a non-zero exit, and keep `e2e/results` as an artifact so the recordings and screenshots
of a failed build can be opened from the CI page. The machine needs:

- a logged-in desktop session that stays unlocked with no screen saver - a WPF window draws nothing
  without one, and a locked desktop stops both the recording and the input, so a CI agent running
  as a Windows service fails;
- the .NET 10 SDK, ffmpeg on `PATH`, and the built app;
- `APP_EXE` (the app), `XAPPER_MCP` (the Xapper server) and every variable the cases connect to.

Run one instance of the app at a time. A check that does not tell instances apart may read the
wrong one, and clean-up closes every instance by name.
