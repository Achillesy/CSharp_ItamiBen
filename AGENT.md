# AGENT.md — configuring ItamiBen

**This file is for an AI agent, not for a person.** The user will say what they want
("only count VS Code", "remind me to stand up hourly", "sleep the machine at 23:00") and
you make it so by writing rows into ItamiBen's database.

There are no configuration files. There is one SQLite database.

```
macOS    ~/Library/Application Support/ItamiBen/ItamiBen.sqlite3
Windows  %LOCALAPPDATA%\ItamiBen\ItamiBen.sqlite3
```

It is created on first run. You can edit it with `sqlite3` while ItamiBen is running —
SQLite is in WAL mode, readers and writers do not block each other.

---

## The two rules that matter most

**1. After any change, bump the config version:**

```sql
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'what you did';
```

ItamiBen checks that number once a minute and reloads. **Without this the user sees no
effect until they restart**, decides your change did not work, and asks you to do it again.

Then **confirm the running program actually saw it.** Within a minute it writes one line:

```sql
SELECT at, text FROM event WHERE kind = 'config' ORDER BY at DESC LIMIT 1;
-- expect: reloaded at version <the number you just wrote>
```

⚠️ **If that line never appears, you edited a different file than the one ItamiBen reads.**
Everything else will have looked like success: the UPDATE reported 1 row, a `SELECT` reads
your change straight back, `--query config` prints it. All of that is true of your copy and
says nothing about the user's. This is not hypothetical — it cost a whole session on
2026-09-18: the agent's shell was inside a sandbox that silently redirected
`%LOCALAPPDATA%` elsewhere, so hours of configuration went into a shadow database while the
user's ItamiBen kept seeding itself empty defaults. The `event` table is the only check
that crosses that boundary, because **the program writes it, not you**.

If the line does not appear, stop writing to the database. Hand the user SQL and have them
run it through **Settings → Configure online → Apply** instead: that path can only ever
touch the database ItamiBen itself has open.

**2. Append what you did to `itamiben.log`**, in the same folder as the database:

```
────────────────────────────────────────────────────────
2026-09-16 18:09:31  applied  OK  1 row(s)
asked: move tonight's reminder an hour earlier

UPDATE schedule SET cron = '0 20 * * 3' WHERE cron = '0 21 * * 3';
```

This is the only record of why the configuration looks the way it does. The results of your
change are in the database; **the change itself is not** — nothing can reconstruct it later.
Three weeks from now "why is this goal here?" has no other answer, and the user did not
write it, you did.

⚠️ **Append, never rewrite.** And keep the user's own words next to your statements: with
only the SQL, nobody can tell whether you understood what they asked for.

⚠️ It is a plain text file on purpose — no SQL can reach it, not even by mistake, and it is
still readable when the database itself is the thing that is broken.

---

## Two halves of this database. Only one is yours.

### Yours — configuration

| table | what it holds |
|---|---|
| `goal` | the things the user is allowed to work on |
| `rule` | how a goal is recognised on screen |
| `command` | every shell command this machine is allowed to run automatically |
| `schedule` | recurring reminders, and optionally a command to run |
| `config` | one row: the version number above |
| `setting` | mostly the program's own state — **you may change only `layout`, `opacityPercent` and `alarmCommand`** |

### Not yours — the ledger. Never write to these.

`sample` · `app` · `title` · `round` · `event` · `total`

`total` is the user's lifetime hours per goal — tens or hundreds of hours that cannot be
recovered. `sample` is one row per second of observation. **Read them if it helps you answer
a question; never UPDATE or DELETE.**

⚠️ **Nothing stops you.** There is no check that catches a statement touching these — a
guard that only covered one of the two ways in would have been worse than none, because it
would read as a promise. What exists instead is a record: whatever you run is written to
`itamiben.log` with the user's own words next to it, and the **Configure online** window
takes a copy of the database before it applies anything. So a mistake here is visible and
recoverable — but it is a mistake you have to not make.

---

## Tables

### `goal` — what the user may work on

```sql
CREATE TABLE goal (
  name     TEXT PRIMARY KEY,   -- shown in the window, and used by `total`
  enabled  INTEGER NOT NULL DEFAULT 1,
  position INTEGER NOT NULL DEFAULT 0,   -- display order
  note     TEXT
);
```

- **Retire a goal with `enabled = 0`, do not DELETE it.** Deleting cascades its rules away,
  and the user's accumulated hours in `total` are keyed by this name.
- Renaming a goal orphans its hours. If the user wants a rename, insert the new name and
  carry the hours over deliberately, or leave the old one disabled.
- **At least one goal must be enabled.** With none, the goal list is empty and the user
  cannot press Start at all.

### `rule` — how a goal is recognised

```sql
CREATE TABLE rule (
  id    INTEGER PRIMARY KEY,
  goal  TEXT NOT NULL REFERENCES goal(name) ON DELETE CASCADE,
  app   TEXT,    -- regex against the foreground application name, NULL = no constraint
  title TEXT,    -- regex against the foreground window title, NULL = no constraint
  note  TEXT,
  CHECK (app IS NOT NULL OR title IS NOT NULL)
);
```

A second counts toward a goal if **any** of its rules match. Within one rule, both columns
that are non-NULL must match.

**⚠️ The regexes are case sensitive, and the two platforms report different names.**
macOS reports `Claude`, `Code`, `Google Chrome`. Windows reports `claude.exe`, `Code.exe`.
A rule written for one platform matches **nothing** on the other, **and does not error** —
the user sees a whole session of red, which looks exactly like a day they wasted. Write
both forms:

```sql
INSERT INTO rule (goal, app) VALUES ('Coding', '^(Code|Code\.exe)$');
```

**⚠️ Before writing an `app` rule, check the name is real.** The database already knows
every application the user has actually been seen using:

```sql
SELECT name FROM app ORDER BY name;                        -- everything ever seen
SELECT text FROM title ORDER BY text LIMIT 50;             -- window titles
```

This single check would have caught the one real bug this project shipped: rules carried
from Windows to macOS that never matched anything for months.

### `command` — everything that may be run automatically

```sql
CREATE TABLE command (
  name    TEXT PRIMARY KEY,
  macos   TEXT,
  windows TEXT,
  note    TEXT,
  CHECK (macos IS NOT NULL OR windows IS NOT NULL)
);
```

**Executable text lives only in this table.** The schedule and the alarm refer to commands
**by name**, never by text, so "what can this machine be made to run" is answerable by
looking in exactly one place.

Write the command for both platforms when you can. A missing one means "this command does
not exist here" — ItamiBen will not substitute anything.

### `schedule` — recurring reminders, and optionally commands

```sql
CREATE TABLE schedule (
  id      INTEGER PRIMARY KEY,
  cron    TEXT NOT NULL,   -- the five crontab fields, or @daily / @hourly / ...
  text    TEXT,            -- reminder text; NULL = run silently
  run     TEXT REFERENCES command(name),   -- NULL = just remind
  enabled INTEGER NOT NULL DEFAULT 1,
  note    TEXT,
  CHECK (text IS NOT NULL OR run IS NOT NULL)
);
```

Standard Vixie crontab semantics, including the day-of-month / day-of-week OR rule: when
both are restricted, a day matches if **either** matches.

- A reminder appears as a banner for one minute **and** as a system notification, one per
  entry, never merged.
- **⚠️ Entries with `run` are never replayed.** If the machine was asleep when the minute
  passed, the command does not fire late. Reminders *are* replayed (up to 12 hours), because
  seeing a missed reminder is useful and running a missed shutdown is not.
- **⚠️ ItamiBen only runs commands while it is open.** This is not a substitute for `launchd`
  or Task Scheduler. Do not schedule anything here that must happen.

### `setting` — three keys are yours

| key | values |
|---|---|
| `layout` | `"standard"` (380 px wide) or `"compact"` (292 px) |
| `opacityPercent` | `10`–`100`; applies to the dial and the card backdrop only |
| `alarmCommand` | the `name` of a row in `command` — what the one-shot alarm runs |

Values are stored as JSON fragments, so strings keep their quotes: `'"compact"'`, not
`'compact'`.

Everything else in `setting` is the program's own state (sounds, window position, alarm
time). It rewrites those itself; your edits there will be overwritten.

---

## If the user has no agent of their own

They may be talking to you in a web chat, having pasted everything in by hand from
ItamiBen's **Configure online** window (Settings → the red button). In that case:

- **Reply with SQL and nothing else.** No explanation, no markdown fences. It gets pasted
  straight into a box and run.
- **Do not touch `config.version`.** That window bumps it for you.
- The text they pasted already contains their current configuration and the list of
  application names on their machine — **use those names**, do not guess.
- They cannot run `--query` to check anything for you, and they cannot iterate cheaply.
  **Prefer one correct statement over a clever one.**
- **Never write to `sample`, `total` or `round`.** Nothing stops you; those are the user's
  hours and history, and they cannot be rebuilt.
- Everything you send is written to `itamiben.log` alongside what the user asked for,
  whether it worked or not. That is for them, not against you — but write accordingly.
- The window reports how many rows **your** statement changed. **`0 rows` means nothing
  matched** — the SQL was valid but the value did not exist. It is not a success.

## Check your work

```
ItamiBen --query config      # goals, rules, commands, schedule, as the program reads them
ItamiBen --query events      # what fired, what failed
ItamiBen --query rounds      # which sessions ran and how they ended
ItamiBen --query minutes     # each session minute by minute, with the red explained
ItamiBen --query samples     # one row per second, to see what names really appear
```

These exist because the database is binary and `sqlite3` is not installed everywhere —
notably not on Windows. `--query minutes` is the one you cannot reproduce with SQL at all:
it replays the judgment engine over the stored observations.

There is deliberately no `--query log`: `itamiben.log` is plain text. Read the file.

**Neither installer puts `ItamiBen` on the PATH — use the full path:**

```
macOS    /Applications/ItamiBen.app/Contents/MacOS/ItamiBen --query config
Windows  cmd /c ""%LOCALAPPDATA%\Programs\ItamiBen\ItamiBen.exe" --query config"
```

On Windows that is where a per-user install lands (the default). If the user chose a
machine-wide install instead, it is `%ProgramFiles%\ItamiBen\ItamiBen.exe`.

⚠️ **On Windows you must go through `cmd /c`, and the doubled quotes are not a typo.**
`ItamiBen.exe` is a GUI-subsystem binary — it has to be, or every launch would flash up a
console window. PowerShell does not wait for one of those and does not wire up its stdout,
so running it directly there prints **nothing at all**, and `> file` writes an **empty
file**. No error, no exit code, no clue — the same silent shape as a rule written for the
wrong platform. `cmd.exe` does hand over the handles, so through `cmd /c` the output
arrives in full and can be captured or redirected normally. (Git Bash / WSL also work, but
they are not on a stock Windows machine.) `cmd`'s own rule for the outer pair of quotes is
why the command needs `""...path..." --query config"` — drop one and it fails to find the
executable.

`--query config` prints what **the program** sees, not what you think you wrote. Run it
after every change.

---

## Worked examples

**"Only count VS Code and Claude."**

```sql
INSERT INTO goal (name, position) VALUES ('Coding', 0);
INSERT INTO rule (goal, app) VALUES
  ('Coding', '^(Code|Code\.exe)$'),
  ('Coding', '^(Claude|claude\.exe)$');
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'add Coding';
```

**"Remind me to stand up every hour on weekdays."**

```sql
INSERT INTO schedule (cron, text) VALUES ('0 9-18 * * 1-5', 'Stand up.');
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'hourly stand-up';
```

**"Put the machine to sleep at 23:00."**

```sql
INSERT INTO command (name, macos, windows) VALUES
  ('sleep', 'pmset sleepnow', 'rundll32.exe powrprof.dll,SetSuspendState 0,1,0');
INSERT INTO schedule (cron, text, run) VALUES ('0 23 * * *', 'Sleeping.', 'sleep');
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'sleep at 23:00';
```

**"Make the window smaller and see-through."**

```sql
INSERT INTO setting (key, value) VALUES ('layout', '"compact"')
  ON CONFLICT(key) DO UPDATE SET value = excluded.value;
INSERT INTO setting (key, value) VALUES ('opacityPercent', '60')
  ON CONFLICT(key) DO UPDATE SET value = excluded.value;
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'compact 60%';
```

⚠️ `layout` and `opacityPercent` are read **once, at startup** — bumping the version is not
enough for these two; the user has to restart ItamiBen.

---

## What the defaults are, and why

A fresh database contains exactly one row in each config table. They are **templates as
much as defaults** — change them, disable them, or delete them freely.

| | |
|---|---|
| goal `Pomodoro`, matching ItamiBen itself | Without at least one goal the program cannot start a session at all. |
| command `show-files` | Opens the folder holding this database. Useful, and an example of the command table. |
| schedule `0 * * * *` "Nice work. Keep it up." | **`enabled = 0`.** It exists to show the shape of a schedule row. An example should not have side effects. |

---

## Things that will bite you

- **Padding a name with spaces.** `WHERE name = ' Pomodoro '` matches nothing, reports no
  error, and looks exactly like success. Copy names **byte for byte** from the configuration
  you were given — never retype them, and never add the spaces some styles put around CJK
  text. This has already happened once: `UPDATE goal SET enabled = 0 WHERE name = ' 番茄钟 '`
  changed zero rows while the window said it had applied.
- **Not bumping `config.version`.** The user concludes nothing happened.
- **Editing a database the program does not read.** Every local signal says success — rows
  changed, `SELECT` reads it back, `--query config` prints it — and the user sees nothing
  at all. Confirm the `reloaded at version N` event (rule 1); it is the only check that
  crosses out of your own view of the filesystem.
- **Writing a regex for one platform only.** No error, no warning, just red.
- **Deleting a goal instead of disabling it.** Its rules cascade away and its hours orphan.
- **Putting command text in `schedule`.** There is no column for it; that is deliberate.
- **Touching `total` or `sample`.** That is the user's history, not configuration.
- **Assuming a scheduled command will run while ItamiBen is closed.** It will not.
