---
name: xapper-mcp
description: Use when something has to be checked in a running WPF desktop application rather than in code or tests - launching the app, clicking and typing in it, reading what a screen holds, answering its message boxes and file dialogs, or confirming that a change behaves as intended. Also required after building a new WPF screen or changing a screen's XAML, to check it has no binding errors. Also "방금 작성한 코드 잘 돌아가는지 실제로 확인해줘", "앱 띄워서 확인해줘", "화면에서 직접 눌러봐", "UI 동작 확인해줘", "실제로 되는지 테스트해줘", "바인딩 에러 없는지 확인해줘".
---

# Driving a WPF app with Xapper

Xapper injects into the target process and walks its real visual tree, so it sees controls that
accessibility-based tooling cannot. That power is wasted if you drive it like a screenshot robot.
Almost every wasted token in a Xapper session comes from one of two habits: taking a picture of
something the tree already knows, and re-learning a screen you have already understood.

Work in this loop, once per screen:

**arrive → recall → act → verify → record**

## 1. Arrive

Start the app yourself with `xapper_launch` whenever you can. It loads the inspector before the
app's entry point, so there is no injection step and screens that appear before the main window -
splash, login - are reachable too. Fall back to `xapper_list_processes` and `xapper_attach` only
when the app is already running.

A .NET Framework 4.x app is still launched the same way, but it has no startup hook: the inspector
goes in the moment its first window appears, and the launch response says so. Anything that happened
before that window - including its binding errors - was not heard.

Before the first launch in each task, ask once:
**"분리된 환경에서 테스트할까요, 직접 보이도록 테스트할까요?"**
Reuse the answer throughout that task; change it only when the user asks to.

- **Isolated testing:** pass `isolated: true`. Windows, drop-downs, menus, and dialogs stay off the
  person's screen, including while the workstation is locked. This is a separate Win32 desktop,
  not a Win+Tab virtual desktop or a security sandbox; files, registry, network, and clipboard remain
  shared. Nobody can see the windows directly, so report what you observed. Screenshots must use
  `mode="render"`; find/snapshot show separate popups and menus. Later attach is unavailable.
  Foregrounding and screen recording are refused; notices are not needed. If a gesture needs real
  mouse fallback it is refused without sending input. **Detach or server exit closes the app and
  its children**, so launch again for another session.
- **Visible testing:** omit `isolated` (its default is false) and use the existing workflow.

## 2. Recall before you look

Call `xapper_screen_recall` **every time the screen changes**, not once at the start of the
session. Opening a document, switching a tab, moving to another step of a wizard, closing a dialog:
each is a new screen with its own record.

A known screen comes back with the selectors to act on, and you can work without looking at all.
An unknown screen is the signal to look once, and only once.

Read what comes back rather than skimming it. Every line is something you would otherwise have to
work out:

- **A line that starts with a selector** - `name=OrderList  ListBox` - goes straight into `target`.
- **A line reading `in X at a,b`** has no selector of its own. Act on it with `target=X` and
  `x=a`, `y=b`. Those are fractions of X, not pixels, so they keep working when the window is
  resized or the display scales. The type at the end of such a line is the element you are aiming
  at, not X itself.
- **The notes are standing facts**, worked out by earlier visits so that this one does not have to.
  Act on them instead of confirming them: a note saying a dialog can appear is the reason you do
  not need a picture to find out. If one turns out to be wrong, rewrite it with `replace` - a note
  left wrong costs every later visit.
- **The learned date and seen count** say how much may have changed since, not whether the record
  is right. An old record is not a suspect one; a record that contradicts what the app does is.

When a selector from the record no longer resolves, the screen itself has changed. Find the control
again, then learn the screen anew so the record stops lying.

A known screen is also the moment to stop moving one step at a time. You already hold every
selector the screen offers, so plan the whole sequence up front - click this, type that, press
Enter - and send it in a single call instead of looking between each step. Build those steps from
the record's selectors rather than from refs: a selector still resolves on a later visit and in a
later session, while a ref does not survive the next snapshot. That is what a learned screen buys
you, and it is worth more than the picture you saved.

## 3. Act, and read the response

Every action reports which path it took and what happened. Read that instead of taking a picture to
find out.

**Read the click path and any refusal.** A coordinate-free click uses Invoke, Toggle, selection of an
unselected item, or a button click event. Buttons receive keyboard focus before activation (except
Hover mode), so the previous editor commits its value before Save runs. Without an event path,
click uses the same synthetic mouse path at the center `(0.5, 0.5)` as a coordinate click. This
reaches parent mouse handlers; the four direct routed-mouse-event fallback is gone. Pass `x`/`y`
when the intended point is elsewhere. A success response describes delivery; verify the resulting
selection or value when that outcome matters.

**Five actions can refuse outright.** `type`, `toggle`, `expand`, `select` and `scroll` each try an
accessibility pattern and one stock WPF type, and say the element does not support the action when
neither fits. A custom control that looks like a text box may well not be one; when `type` refuses,
click into it with `x`/`y` and send `xapper_key` instead.

**The cursor usually stays the person's.** Coordinate clicks and most drags run inside the target
process: real window messages with the OS cursor reads briefly spoofed, so WPF accepts them as
genuine while the physical pointer never moves. The person can keep working. Only when that path
cannot be set up does Xapper fall back to real mouse input, which does move the pointer and take
focus - and the response says which one ran. On an isolated desktop that fallback is refused,
with no real input sent; choose an uncovered point or use select/toggle/type/key/scroll instead.

For visible testing, when you expect the real one, call `xapper_notice_show` first so a banner warns the person off. If
you raise that banner, you own it: call `xapper_notice_hide` the moment that stretch of work ends,
and again before you finish the task. A banner you raised and walked away from sits over the
person's screen until they close it by hand. The one the server raises for you when an action
drives the real mouse takes itself down once the input stops, but yours does not.

When an action refuses, the message names the single condition that blocked it:

| What it says | What it means | What to do |
|---|---|---|
| `stayed disabled` | the app itself refuses the action | a longer timeout will not help; find what enables the control, or accept that the action is unavailable here |
| `stayed hidden` | it is not on screen | open the tab, pane or dialog holding it first |
| `was still loading` | the view has not settled | raise the timeout, or act later |
| `cannot be resolved` | the ref is stale, or its element left the tree as virtualized rows and closed dialogs do | take fresh refs |
| `matches N elements` | the selector is ambiguous | add a second clause, or pass a ref |

**A drag over something that supports drag-and-drop cannot be finished cursor-free.** Some controls
start a drag-and-drop loop the moment the button is held and the mouse moves - tree and graph
nodes that can be re-parented, list rows that can be dropped elsewhere. That loop takes the drop
position from the real cursor and only notices input arriving through the system queue, so a
synthetic drag can open it but never complete it. Xapper notices, cancels it with Escape so the
app does not freeze, and tells you nothing was dropped. When you see that, the drop has to be done
by hand; note on the screen that dragging there starts drag-and-drop, so no later visit repeats it.

**A ref outlives its meaning, which is worse than failing.** `xapper_snapshot` does not merely
invalidate earlier refs - it restarts numbering from 1, so a ref taken before a snapshot may now
resolve to a *different* element and act on it without complaint. Use refs from the most recent
snapshot, and prefer target selectors when a sequence spans one. `xapper_find` leaves existing refs
alone and only hands out new numbers.

**When you have the app's source, read it.** Working on the app you are testing means its XAML is
right there, and it answers in seconds what a picture answers slowly. The `x:Name` in a XAML file
is the same name `target` takes, so grepping for a caption you can see on screen tells you which
control carries it and what it is called - often the fastest way out of a `find` that keeps coming
back empty. A `Command` binding tells you what a button does without clicking it, and the file the
control lives in tells you which screen it belongs to. None of this applies when you are driving
somebody else's program, where there is no source to read.

**The moment you can name the next two steps, send them together.** Not three, not "a long
sequence" - two. Typing into a box and pressing Enter is two. Selecting a row and pressing Delete
is two. One call per keystroke is the single most common way a session doubles in length, and it
buys nothing: check the result once at the end rather than between steps.

```json
[{"tool": "click",  "target": "name=FilterBox"},
 {"tool": "type",   "target": "name=FilterBox", "text": "draft"},
 {"tool": "key",    "key": "Enter"},
 {"tool": "assert", "target": "name=ResultCount", "property": "Text", "expected": "3"}]
```

Two tools send a sequence:

- **`xapper_batch`** for a straight line of steps. They run in order, the batch stops at the first
  failure, and you get one numbered result per step in the format the single tools return. Steps
  take target selectors, so a whole flow runs without a snapshot in between. A step that opens a
  modal dialog ends the batch, and the remaining steps do not run.
- **`xapper_run`** when there is a loop, a condition or a wait. The script runs entirely inside the
  app and returns a summary. Prefer `waitUntil` over sleeping, so the same script can be re-run.

### Dialogs outside the visual tree

**Message boxes and open/save dialogs are answered, not looked at.** They are Win32 windows, so a
snapshot does not show them, and while one is up the app handles nothing else. A click that raises
one comes back the moment it appears and names it; answer it with `xapper_message_box` (ok, yes,
no, cancel …) or `xapper_file_dialog` (an absolute path). Both press buttons by their standard ID,
so they work whatever language Windows is in, and both fail rather than claim success if the
dialog stays open. A save dialog that asks to overwrite fails quoting that question and leaves it
up for `xapper_message_box`. In a script the same click does not fail, so the next line can answer:

```js
xapper.click('name=SaveButton', {x: 0.5, y: 0.5});
xapper.messageBox('yes');
```

`xapper_list_windows` reads which of the app's windows are open and which appeared since a time,
each with when it showed and whether it was modal - pass the `now` of a call made before an action
as `since` to tell whether that action raised a dialog. `xapper_window` maximizes, restores,
minimizes or brings to the front one of the app's windows. None of these touch another program's
windows.

## 4. Verify without pixels

Choose by what you actually need to know:

- **Did the action work?** The action's own response already said so. When it does not go far enough -
  a click that should have selected a row, a key that should have committed an edit - `xapper_events`
  reads what the app itself raised. That reaches where the tree does not: a grid painting its own
  cells has no element to inspect, yet its selection still raises an event. Ask right after acting,
  while what you are looking for is near the end of the list.
- **What is this one value now?** `xapper_get_property` or `xapper_assert`.
- **Is a value blank because its binding is broken?** `xapper_binding_errors` - after a XAML change this
  check is required; see "Check the bindings whenever XAML is new or changed" below.
- **What does this panel hold?** `xapper_snapshot` with `rootRef` set to it. Same content, as text.
- **How is it drawn?** Only then a picture: layout, rendering, a chart, a control that paints itself.

When you do take one, narrow it with `ref`. That works in both modes, so choosing `mode="screen"`
does not force a full-desktop shot. Only `annotate` is render-only. You almost always have a ref to
narrow with: the `xapper_find` that located the thing you are about to look at
handed you one, and on a learned screen the record's selectors point at every panel worth framing.
Reach for the whole window only when what you need to see is the whole window.

Some controls genuinely cannot be read as text. High-performance grids - DevExpress `GridControl`
and `TreeList` among them - paint their cells without creating a visual element per cell, so their
contents appear in neither a snapshot nor `xapper_find`. Pixels are the only way in. Note that fact
on the screen record the first time you meet it, so the next session does not rediscover it.

## 5. Record what you learned

This is the step that gets skipped, and it is the one that pays.

**`xapper_screen_learn` once per screen.** Do it immediately after you have looked at an unknown
screen and understood it. You supply only a one-line name and any gotcha; Xapper works out the
regions itself from the live visual tree. From then on, `xapper_screen_recall` hands you those
selectors for a fraction of what a picture costs, in this session and in every later one - and
those selectors are what let you batch a whole flow instead of stepping through it.

**`xapper_screen_note` whenever you find something out.** Most of what is worth remembering is not
visible on arrival - it surfaces when you act.

Write a standing fact about the screen, not a report of your session. The test is simple: could the
next agent act on this line without checking it first?

| Write this | Not this |
|---|---|
| Ctrl+Z undoes the last edit here, Ctrl+Y redoes it | On 2026-09-23 I verified that undo works |
| The Add button does nothing unless the search box has text | Clicked Add and nothing happened |
| Opening a project from here can raise a backup-recovery dialog the visual tree does not show | A dialog appeared so I took a screenshot |
| The X icon in the row's toolbar deletes, and asks to confirm | Deleted a row to test undo |
| This grid paints its cells, so its contents need a picture | Could not find the cell text |

A dated verification log ages into noise. A fact about the screen stays true until the screen
changes - and when it does, call the tool again with `replace` and write the corrected set.

**Write the note the moment you find the thing out.** It lands on the screen showing at that
moment, so a fact noted later can land on the wrong record: if a button opens a dialog, the fact
that it does belongs to the screen with the button, not to the dialog. Collecting findings and
writing them all at the end also loses every one of them if the session ends first.

One fact per line. Lines are appended unless you pass `replace`, and the regions are left untouched
either way.

**When a screenshot response starts counting, stop and write.** From the second picture of a screen
with nothing recorded since, the response says which number this is. That count is the signal: what
the earlier pictures told you exists only in this conversation and goes when it does. Write the
note before the next call, not at the end of the task.

**Every screenshot response tells you where you stand.** It says either that this screen is already
learned - in which case recall its selectors rather than looking again - or that it is not in the
record yet, which means the moment you finish looking is the moment to learn it. Act on that line;
it is why it is there.

Two properties make the record durable, and both are worth knowing:

- The key is the screen's **structure**, with text and data deliberately excluded. Typing in a box,
  selecting a row or scrolling a virtualized list does not change it, so a record keeps matching as
  data changes.
- A rebuilt app produces new structures and therefore new keys, so stale records stop matching
  rather than answering wrongly. Use `xapper_screen_forget` after a redesign you want re-learned.

## Check the bindings whenever XAML is new or changed

A binding whose path names a property the data does not have shows nothing: the value is blank, no
exception is thrown, no element carries an error. WPF only writes it to its debug trace, which
`xapper_binding_errors` reads. Nothing else in a session will notice it, so this check is required,
not a fallback for when something looks wrong. Call it:

- **after building a new screen** - a window, page, dialog, user control or data template;
- **after changing the XAML of an existing screen** - bindings, templates, styles, converters or the
  view model it binds to;
- **whenever the person asks** whether bindings are broken.

Do it in this order:

1. Start the app with `xapper_launch`, not `xapper_attach`: launch hears errors from the very first
   screen, attach only from the moment it attaches. On a .NET Framework app launch hears them only
   from its first window on, so when the first window itself is the screen to check, reopen it (or
   navigate away and back) after launch before trusting "No binding errors".
2. Call `xapper_binding_errors` with `clear=true` just before opening the screen, so what comes back
   afterwards belongs to that screen.
3. Open the screen and make it draw what it binds: switch its tabs, expand its rows, select an item,
   open its pop-ups. A binding inside a template is only evaluated when that template is shown.
4. Call `xapper_binding_errors` again. "No binding errors" counts only for the parts you opened.

When errors come back, report each line as it is: it already names the binding's path, the type of
the data it looked in, and the element and property it feeds. If the screen is your change, fix it
and check again; a screen with binding errors is not finished.

## What the measurements show

These are the habits that actually cost sessions, each seen in a real transcript:

- 46 screenshots taken while `xapper_screen_learn` was never called once, so the next session
  started from nothing again.
- Recall called a single time, right after launch, while the app still showed its splash screen -
  and never again, so a good record built on the previous run was never opened.
- 35 of 46 pictures taken purely to check whether an action had worked.
- Nine consecutive full-desktop captures at maximum width, when a `ref` would have framed the one
  panel in question.
- A disabled button clicked three times, each costing the full timeout, each followed by a property
  read asking why.
- Nine arrow keys and an Enter sent as ten separate calls, then again as seven, when each run was a
  straight line known in advance and would have been one `xapper_batch`.
- The same confirmation dialog met on three separate runs and screenshotted every time, because no
  note ever said it could appear.
