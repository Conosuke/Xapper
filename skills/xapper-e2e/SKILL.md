---
name: xapper-e2e
description: Use when existing end-to-end test cases for a WPF app (an e2e folder with cases.yaml, made with Xapper) should be run - all of them, the ones for one feature, or specific ids - and their results read. Also "E2E 돌려줘", "E2E 테스트 실행해줘", "검색 기능 테스트해줘", "C02 돌려봐".
---

# Running E2E cases

The project's E2E cases live in `e2e/`: one C# file per case, and `e2e/cases.yaml` listing each
case's id, feature, title and what it checks. `e2e/run-all.cs` runs them without an agent. Your job
is to pick the right cases, run them, and report what happened - not to make them pass.

## 1. Pick the cases

Read `e2e/cases.yaml`. Match the request against `feature` first, then `title` and `checks`:
"test the search feature" means every case whose feature is search. Before running, tell the person
which ids you chose and why. If nothing matches, say so and offer to make a case with the
xapper-e2e-make skill instead of running something unrelated.

## 2. Check what the run needs

- `APP_EXE` (the app under test) and `XAPPER_MCP` (the Xapper server executable) are set, and so is
  every variable the chosen cases pass to `x.Connect(...)` - look in their files.
- The .NET 10 SDK is installed (`dotnet --version`).
- ffmpeg is on `PATH` (`ffmpeg -version`); every case starts by recording. If it is missing, the
  cases fail at `xapper_record_start` with an install hint: give the person the command
  (`winget install Gyan.FFmpeg`) or ask before running it, and know that a Xapper server that was
  already running keeps its old `PATH` - restart whatever starts it, for an agent session a new
  session.
- No other instance of the app is running; clean-up closes every instance by name.
- The desktop stays unlocked for the whole run.

The run records the whole desktop, and a failure screenshot is taken in screen mode. On a machine a
person is using, tell them before you start: their other windows end up in the video.

## 3. Run

```bash
dotnet run e2e/run-all.cs              # every case
dotnet run e2e/run-all.cs -- C02 C05   # only these
```

The runner first checks that `cases.yaml` and the case files agree - duplicate ids, missing files,
files missing from the list, unknown ids - and runs nothing if they do not. That is a problem with
the e2e folder, not with the app; report it.

## 4. Read the result

- The runner ends with a table - id, PASS or FAIL, time, feature and title - and exits 0 only if
  every chosen case passed.
- Every call a case makes prints one line, `[C02] xapper xapper_click: ...`; a failed case prints
  `[C02] FAIL` with the call that failed and its full text.
- `e2e/results/<id>/` holds `run.mp4` (the recording) and, for a failed case, `failure.png`.

Report each failed case with its id and title, the call that failed and its text, and the paths of
its recording and screenshot.

## 5. When a case fails

Never change a case's expected values, or loosen its checks, to make it pass. A failure means one of
three things; say which you think it is and why:

- **The app is wrong** - the feature broke. That is the finding; report it.
- **The app changed on purpose** - a renamed control or a new step. The case needs updating:
  offer to do it with the xapper-e2e-make skill, naming the case.
- **The run environment** - ffmpeg missing, a variable unset, a locked desktop, another instance
  running. Fix it with the person and run again.
