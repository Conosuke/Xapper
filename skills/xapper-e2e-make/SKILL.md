---
name: xapper-e2e-make
description: Use when a flow in a WPF app driven through Xapper should be kept as an end-to-end test that replays without an agent - writing a new E2E case, changing an existing one, or setting up the e2e folder of a project for the first time. Also "이 흐름 E2E 테스트로 남겨줘", "E2E 시나리오 만들어줘", "E2E 케이스 추가해줘", "C02 고쳐줘".
---

# Making an E2E case

An E2E case is the tool calls you made while driving the app, minus the trial and error, written as
a C# file a CI machine replays on every build. No tokens are spent when it runs. Each case has an id
(C01, C02 ...) and an entry in `e2e/cases.json` that says what it proves, so a person - or an agent
asked to "test the search feature" - can pick cases without reading code.

## 1. Look for a case that already covers it

Read `e2e/cases.json` before writing anything. If a case has the same `feature` and its `title` or
`checks` overlap what you were asked to cover, stop and tell the person: its id, feature, title and
checks. Then ask which they want:

- **Change that case** - same id, same file, list entry updated.
- **Add a new case** - it proves something the existing one does not.
- **Leave it** - the flow is already covered.

If nothing overlaps but the feature has other cases, list them anyway, so the person sees what is
already tested. When the person names a case ("fix C02"), skip this step and change that case.

## 2. Settle the flow

A case is only as good as the steps it replays, so every step has to be one that has actually
worked. Where those steps come from depends on what this conversation already holds.

**If the flow was already driven in this conversation**, do not decide on your own. Gather the calls
that belong to it - in order, only the ones that succeeded, leaving out trial and error and calls
made just to look - and ask the person:

> I already did this in our conversation: (the steps, one line each, and what showed it worked).
> Shall I write the case from that, or drive the flow again from the start and write it from the
> new run?

Say what bears on the choice: how many of those steps used refs and must be turned into selectors,
and anything that may have changed since - the app rebuilt, data edited, a step that only worked
on a second try.

- **From the conversation:** turn every ref into a selector, using the `name` or `id` the find or
  snapshot response showed for it, or the selector a screen record lists for it. A step whose place
  came from an anchor (`in X at a,b`) has no selector of its own: write it as `target = "X"` with
  `x = a` and `y = b` as separate arguments, never as one string. A step you cannot pin down either
  way is driven again on its own; do not guess. Then show the person the final steps - which of
  them belong to a setup and which to the case (section 4) - and what the case will assert, on
  screen and beyond it, and once they agree, write it (sections 3 and 4).
- **From the start:** drive the flow as below and write the case from the new run.

If only part of the flow was driven - it stopped halfway, or its outcome was never confirmed - say
so when you ask, and offer a third choice: keep the steps that worked and drive only the rest live.

**Otherwise**, work the flow out live with Xapper using a subagent to avoid accumulating Xapper
tool results in this agent's context. Launch a subagent task with:

```
Agent({
  description: "Drive WPF app flow with Xapper and return steps",
  prompt: "Use the xapper-mcp skill to drive this flow: [the flow description]. 
           Record every successful step as a call with selector (no refs). 
           When the flow is complete and verified, return ONLY:
           1. The ordered list of steps, each with: tool name, target selector, arguments
           2. Outcome verification (what proves it worked)
           3. Any screen notes worth keeping (gotchas, dialogs that may appear, etc.)
           Do not return screenshots, full events lists, or intermediate tool results."
})
```

Once the subagent returns, extract the step list and outcome. Use those steps to write the case.
Note which selectors worked; a case cannot use refs, because refs are renumbered on every run.

Either way, a case is not finished until it passes on its own run (section 5) - a flow that worked
in the conversation is not proof that the written case does.

## 3. Set up the e2e folder, once per project

If the project has no `e2e/` folder yet, copy this skill's `templates/` into it as `e2e/` whole -
do not rewrite the shared code from memory; the templates are what has been built and run.

- `E2E.csproj` is the one project in the folder: a console app that builds every case in `Cases/`,
  every setup in `Setups/` and the shared code in `Common/`. Set its `TargetFramework` to the
  app's own, without `-windows` (`net8.0-windows` -> `net8.0`). If the app targets below .NET 8, or targets .NET Framework
  (`net4x`), leave it at `net8.0` - this project never references the app, it only talks to it
  through MCP, so it does not need to match exactly.
- Keep `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` as they
  are. The first two are empty on purpose: MSBuild uses the nearest one of each and stops looking,
  so the app repository's own build settings - target framework, WPF, analyzers - stay out of the
  E2E project. `Directory.Packages.props` is not empty: it sets `ManagePackageVersionsCentrally` to
  false, so central package management in the app repository cannot override the versions in
  `E2E.csproj`. Emptying or deleting any of them breaks the build in some repositories.
- Copy `e2e.env.example` to `e2e.env` and fill in this machine's values. `${E2E}` in a value stands
  for the e2e folder. A variable already set in the environment wins over the file.
- Make sure the project's `.gitignore` keeps out what a run produces and what a build leaves behind:
  `e2e/results/`, `e2e/e2e.env`, `e2e/bin/` and `e2e/obj/`. Many .NET repositories already ignore
  every `bin/` and `obj/`; add only what is missing.

The E2E project does not need to be added to the app's solution - it is run by path, not built as
part of the app.

## 4. Split the setup from the case, then register and write it

**Cases do not launch the app; setups do.** A setup is a static class in `Setups/` marked
`[Setup("name")]` whose `Run` launches the app and does what its cases need before their own steps.
Every case names its setup in `cases.json`. The runner runs a setup once and then every case that
names it, one after another in the same running app - so running the whole suite starts the app
once per setup, not once per case. The templates ship one setup, `app-started`, that only launches
the app.

Decide what belongs in a setup from the flows themselves, for this app:

- A step every case of a group would repeat before its own work - launching, and whatever must hold
  before any of them can start (signing in, opening what they work on, reaching their starting
  screen) - belongs in a setup, not in each case.
- Reuse a setup whose steps already match. Extend `app-started` when every case needs the same
  start; add another setup when only some cases do. Keep one step in one place.
- What the case checks belongs to the case, even if it begins with the same moves as another case.

Because cases share the running app, write each case to start from the state its setup leaves and
to leave that state behind when it passes: close what it opened, go back to where it started. A case
that cannot undo what it changes - it deletes or edits data the cases after it rely on, or changes a
setting - gets `"resetAfter": true` in `cases.json`: once it ends, the runner closes the app and runs
the setup again before the next case. Mark the case that makes the change, not the ones after it.
After a case fails, the runner does the same, since a failed case can leave the app anywhere. A
setup that fails is reported once, and its cases are marked not run.

A setup looks like this:

```csharp
using E2E.Common;

namespace E2E.Setups;

[Setup("app-started")]
public static class AppStarted
{
    public static async Task Run(Session x)
    {
        await x.Call("xapper_launch", new { exePath = x.App });
        // Then whatever every case naming this setup needs before its own steps.
    }
}
```

Take the next id after the highest one ever listed; an id is never reused, even after its case is
deleted. A deleted case leaves no trace in `cases.json`, so check its history too
(`git log -p -- e2e/cases.json`) before settling on the next id. Add the entry to `cases.json`:

```jsonc
{
  "id": "C02",
  "feature": "search",
  "title": "Typing draft into the filter shows only the three drafts",
  "file": "C02-filter-shows-drafts.cs",
  "setup": "app-started",
  "checks": [
    "screen: the result count becomes 3",
    "data: the app's own server lists three drafts"
  ]
}
```

Then write the case file, named `Cases/<id>-<slug>.cs` after its id:

```csharp
using E2E.Common;

namespace E2E.Cases;

[Case("C02")]
public static class C02FilterShowsDrafts
{
    public static async Task Run(Session x)
    {
        // Starts where the setup "app-started" left the app.
        await x.Call("xapper_type", new { target = "name=FilterBox", text = "draft" });
        await x.Call("xapper_key", new { key = "Enter" });
        await x.Call("xapper_run", new { script = "xapper.assert('name=ResultCount', 'Text', '3', { timeout: 5000 })" });

        // The screen shows what the app drew, not what it stored. Read the result through the app's own
        // means as well - here an MCP server it ships, started from APP_MCP; use its real tool and arguments.
        var app = await x.Connect("APP_MCP");
        var stored = await app.Call("list_items", new { filter = "draft" });
        if (!stored.Contains("draft"))
            throw new InvalidOperationException($"the app's own record does not match the screen: {stored}");

        // Leave the app as the setup left it, for the case after this one.
        await x.Call("xapper_type", new { target = "name=FilterBox", text = "" });
        await x.Call("xapper_key", new { key = "Enter" });
    }
}
```

The file is `e2e/Cases/C02-filter-shows-drafts.cs`. If the case connects to another server
(`x.Connect("APP_MCP")`), add that variable to `e2e.env.example` too.

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
- A case never launches or closes the app and never stops the recording; its setup and the runner
  do that.

## 5. Run it once

Run the new case through the runner and make sure it passes before you hand it over:

```bash
dotnet run --project e2e -- C02
```

The runner runs the case's setup first, so a single case runs exactly as it does in the full suite.
Run the whole suite as well when you added a setup or changed one, or when the new case shares a
setup with others: it must pass after the cases before it and leave the app fit for the ones after.

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

To run the cases on every build, add a CI step that runs `dotnet run --project e2e` and fails the
build on a non-zero exit, and keep `e2e/results` as an artifact so the recordings and screenshots
of a failed build can be opened from the CI page. The machine needs:

- a logged-in desktop session that stays unlocked with no screen saver - a WPF window draws nothing
  without one, and a locked desktop stops both the recording and the input, so a CI agent running
  as a Windows service fails;
- the .NET SDK that builds the app (8 or later), ffmpeg on `PATH`, and the built app;
- `APP_EXE` (the app), `XAPPER_MCP` (the Xapper server) and every variable the cases connect to -
  pass these as real environment variables and `e2e.env` does not need to exist.

Run one instance of the app at a time. A check that does not tell instances apart may read the
wrong one, and clean-up closes every instance by name.
