# ItamiBen — 痛みを知らせる

> **痛みを知らせる** — it lets you know it hurts.

A pomodoro clock with teeth. Pick what you're allowed to work on, commit to a stretch of
focus, and it watches the foreground window for the whole run. Wander off and those minutes
simply don't count — the rest block slides further away and you watch it go.

No popups, no nagging, no congratulations. Just a clock that is too dumb to negotiate.

**Status: early, but it runs.** Verified end to end on macOS, and now on Windows 11 too
(2026-09-18): foreground app and title, idle detection, the winmm sounds, the scheduled
reminder and its command, single-instance, the tray of debug queries, and the Inno Setup
installer from build to silent upgrade.

[中文说明](./README_ZH.md)

## What makes it different from the other tomatoes

Most pomodoro timers are honour-system stopwatches: you press start, and 25 minutes later
they congratulate you regardless of what you actually did. ItamiBen judges. A second only
counts if the window you were actually looking at matches a rule you wrote **before** the
run, not during it.

There is no ActivityWatch dependency. ItamiBen reads the foreground window itself, ten
times a second, and writes one row per second to its own database. (Its predecessor,
ItamiTimer, was built on ActivityWatch; that turned out to be the wrong foundation for this
job, and this is the rebuild.)

ItamiBen gets no special treatment either: looking at its own dial counts as off-task,
exactly like looking at anything else.

## How a round works

1. Pick one goal (single choice, and one of them is already selected).
2. Drag the slider — 10 to 50 minutes, in steps of 5. It has no numbers and no label; the
   whole point is that you guess.
3. Press **Start**. From that moment the clock reads the foreground window once a second.
4. When the focus target is reached, a short rest block follows: `ceil(focus / 5)` minutes.

Everything happens inside a **two-hour ring**. That is the hard part: the ring is wall
clock time, and it does not stretch. Every second that isn't focused — off-task, away from
the keyboard, screen locked, or spent looking at ItamiBen itself — eats into the same
budget:

    budget = (120 − rest) × 60 − target
    slack  = budget − (elapsed − focused)

When the slack runs out, the round ends. There is no way to earn it back.

### What the ring shows you

Each minute is one stave on the ring:

| what happened that minute | how it looks |
|---|---|
| mostly focused | a tall green stave |
| partly focused | shorter, and the colour walks green → yellow → red |
| off-task | a short red stave |
| away (no keyboard or mouse for 3 minutes) | a hollow dashed frame — time passed, but it isn't held against you |
| nothing sampled | nothing drawn |

The colour and the height encode the same number on purpose: one for normal vision, one for
everyone else — red and green are the most commonly confused pair.

Ahead of the hand you see the rest block, drawn as an outline. That outline is the whole
punishment: every wasted minute pushes it further away, and you watch it go.

### Drifting

While you are off-task the dial flips half-inverted once a second, and the tick sound comes
on. Nothing else happens. The speaker icon in the top-right silences the drift ticking.

## The window

Frameless and always on top, so it sits on your desktop like a wall clock rather than a
program. Drag it **by the dial** (the draggable area is the circle, not the square).

- **Scroll on the dial** moves the yellow alarm hand. Keep scrolling and it accelerates.
- **Right-click the dial** for: Keep on top · Force ticking · Run command at alarm · Close.
- **Click the small red ring** to peek at the next scheduled reminder within twelve hours.
  It shows for three seconds and changes nothing.
- The **seven dominoes** under the dial are the days of the week; the fallen ones are the
  days already gone.
- The **bottom bar** shows the foreground application and window title the clock is
  currently reading — this is what your rules are being matched against.
- The gear opens Settings; the pin toggles always-on-top; the third icon switches
  light / dark.

### A smaller window, and seeing through it

Two keys in the `setting` table — ask the agent, or set them yourself:

```sql
UPDATE setting SET value = '"compact"' WHERE key = 'layout';
UPDATE setting SET value = '60'        WHERE key = 'opacityPercent';
```

`layout` is `standard` (380 px wide) or `compact` (292 px). `opacity` is 0–100 and applies
to the **dial and the card's backdrop only** — buttons and text stay solid, because
dimming those just makes them unreadable.

Read once, at startup.

## Configuring it: hand a file to an AI

Configuration lives in four Markdown files. Each one explains itself — a short note for you,
detailed instructions for an AI, and the actual settings in a marked block at the bottom.

```
macOS    ~/Library/Application Support/ItamiBen/
Windows  %LOCALAPPDATA%\ItamiBen\
```

| file | decides |
|---|---|
| `rules.md` | what counts as work |
| `commands.md` | everything this machine may be made to run, and what the alarm runs |
| `schedule.md` | recurring reminders, in standard crontab format |
| `layout.md` | how wide and how see-through the window is |

**Give one whole file to any AI** — a coding agent with access to the folder, or a web chat
you paste it into — and say what you want:

> Only count VS Code and Chrome when the title mentions GitHub.

> Remind me to stand up every hour between 9 and 6 on weekdays.

Replace the file with what comes back. There is nothing else to install, nothing to
configure, and no second document the AI needs: **the file carries its own instructions.**

Next to each one sits a `*.sample.md` — the pristine version, rewritten every time ItamiBen
starts. If an AI mangles your file, that is what you compare against.

Changes take effect within a minute. The one exception is `layout.md`, which is read once at
startup; the file says so.

## Your files

All under `~/Library/Application Support/ItamiBen/` (macOS) or `%LOCALAPPDATA%\ItamiBen\`
(Windows).

| file | what it is |
|---|---|
| `rules.md` `commands.md` `schedule.md` `layout.md` | your configuration — yours to edit, never overwritten |
| `*.sample.md` | the shipped reference copies, refreshed at every launch |
| `ItamiBen.sqlite3` | what the program itself records: observations, rounds, settings, and the hours ledger |

## Recurring reminders

Lines in `schedule.md`, with **standard crontab** timing — Vixie semantics, including the
day-of-month / day-of-week OR rule. A line carries reminder text and, optionally, the name of
a command from `commands.md`. The file explains the rest.

When one comes due you get two things at once, on purpose:

- the banner over the dominoes, which is guaranteed to be on screen for a minute, and
- a **system notification**, one per entry, never merged — the banner has a hard height
  limit and collapses extras into `+N`, so the notification centre copy is the one that
  doesn't lose anything, and it is still there after you quit the program.

The Alarms switch in Settings controls **only the sound**. The reminder itself always fires.

## The alarm, and what happens when it fires

The yellow hand is a plain wall-clock alarm and has nothing to do with judging your focus.
Scroll on the dial to set it; it is one-shot and clears itself after firing. It survives a
restart.

When it fires:

It does **one of two things, never both** — that switch picks which:

1. **Off (the default).** The chosen system sound plays **four times** (the three
   focus-related notifications ring twice — they each leave something on screen you can look
   at afterwards; the alarm leaves nothing).
2. **On** — **Run command at alarm** in the right-click menu, or the Command card in
   Settings, where the switch reads `Ring` / `Run`. The command named by the `alarmCommand`
   setting runs, and **nothing rings**. Ringing means "go do it yourself"; running the
   command means "do it for me". Doing both would be neither.

That switch is **off every time ItamiBen starts** and is never persisted — the command is
usually a shutdown.

Commands are rows in one table, one column per platform, and the alarm refers to one **by
name**:

```sql
INSERT INTO command (name, macos, windows) VALUES
  ('sleep', 'pmset sleepnow', 'rundll32.exe powrprof.dll,SetSuspendState 0,1,0');
INSERT INTO setting (key, value) VALUES ('alarmCommand', '"sleep"')
  ON CONFLICT(key) DO UPDATE SET value = excluded.value;
```

⚠️ **Executable text lives only in `command`.** The alarm and the schedule both refer to
commands by name, never by text, so "what can this machine be made to run automatically" is
answerable by looking in exactly one place.

⚠️ **The switch is off every time the program starts.** Persisting it is an accident
waiting to happen — you restart, and last session's shutdown command kills the machine
while you have no idea it was still armed.

Settings shows you the exact command text before you flip the switch. It is usually a
shutdown command; you have a right to know what you are arming.

## Requirements

- .NET 10 Runtime to run, SDK to build
- Windows or macOS

⚠️ **macOS needs Accessibility permission to read window titles.** Without it you still get
the application name, but every title rule silently never matches. The app says so in its
status bar and offers a button that opens the right settings page. No restart needed — the
next sample picks it up. **Windows needs no permission.**

## Build & run

```bash
dotnet build ItamiBen.slnx
dotnet test  ItamiBen.slnx
./run-macos.sh              # build → .app → sign → launch
./run-macos.sh --run-only   # launch only, no rebuild, no re-sign
```

⚠️ **On macOS, don't use `dotnet run` and don't run the bare binary out of `bin/`.** The
Accessibility grant is recorded per *application*; a bare binary never gets it, so you can
read app names but not window titles — half your rules silently stop matching. Go through
the `.app` that `run-macos.sh` builds.

## Releases

```bash
./pack-macos.sh             # → dist/ItamiBen-<version>-macOS-<arch>.dmg
pwsh pack-windows.ps1       # → dist\ItamiBen-<version>-win-x64.exe   (needs Inno Setup 6)
```

Both are framework-dependent and need the .NET 10 Runtime on the target machine. The
Windows installer detects it and offers to download it; the macOS `.dmg` says so in its
Read Me. The version number has exactly one source: `<Version>` in
`Directory.Build.props`.

✅ Both halves have now been run on a real machine. `pack-windows.ps1` and
`installer/ItamiBen.iss` were first exercised on Windows 11 + Inno Setup 6.7.3 on
2026-09-18: an 11 MB per-user installer that needs no UAC, installs to
`%LOCALAPPDATA%\Programs\ItamiBen`, and upgrades cleanly over itself.

## When something looks wrong

Hand the relevant configuration file to an AI and say what you expected. Each one carries its
own instructions, so the AI can see both what you asked for and what the program will make of
it.

If the answer is "my rules never matched anything", `rules.md` tells the AI how to find out
what an application is really called on your machine — it will ask you to run a round and use
that application for a minute, then read the name out of the red.

There is nothing for you to run and nothing to memorise.

## Project layout

```
src/ItamiBen.Core/    judgment and timing, pure logic, zero UI references, unit tested
src/ItamiBen.App/     Avalonia UI; platform calls are allowed only in this layer
src/ItamiBen.App/Platform/   one file per platform concern
tests/                xUnit
installer/            Inno Setup script for the Windows installer
```

## Name

**Itami** (痛み) is pain. **Ben** is Big Ben — punctual, loud, impossible to negotiate with.

痛みを知らせる: *to make the pain known*. Not to inflict it — the hours were already gone.
Just to stop letting them disappear quietly. That is the whole product pitch, so the name
is the pitch.

## License

[PolyForm Noncommercial License 1.0.0](./LICENSE) — free for noncommercial use.

Copyright (c) 2026 Achilles.Newman (https://github.com/Achillesy)
