# AGENT.md — configuring ItamiBen

**This file is for an AI agent, not for a person.** The user will say what they want
("only count VS Code", "remind me to stand up hourly", "sleep the machine at 23:00") and
you make it so by writing rows into ItamiBen's database.

There are no configuration files. There is one SQLite database.

```
macOS    ~/Library/Application Support/ItamiBen/ItamiBen.sqlite3
Windows  %APPDATA%\ItamiBen\ItamiBen.sqlite3
```

It is created on first run. You can edit it with `sqlite3` while ItamiBen is running —
SQLite is in WAL mode, readers and writers do not block each other.

---

## The one rule that matters most

**After any change, bump the config version:**

```sql
UPDATE config SET version = version + 1, changed_at = unixepoch(), note = 'what you did';
```

ItamiBen checks that number once a minute and reloads. **Without this the user sees no
effect until they restart**, decides your change did not work, and asks you to do it again.

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
recovered. `sample` is one row per second of observation. **Read them if it helps you
answer a question; never UPDATE or DELETE.**

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
- Anything that touches the ledger is refused and rolled back automatically, so do not try
  to "clean up" `sample`, `total` or `round` even if it seems helpful.

## Check your work

```
ItamiBen --query config      # goals, rules, commands, schedule, as the program reads them
ItamiBen --query events      # what fired, what failed
ItamiBen --query samples     # one row per second, to see what names really appear
```

On macOS the binary is inside the app bundle:
`/Applications/ItamiBen.app/Contents/MacOS/ItamiBen`.

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

- **Not bumping `config.version`.** The user concludes nothing happened.
- **Writing a regex for one platform only.** No error, no warning, just red.
- **Deleting a goal instead of disabling it.** Its rules cascade away and its hours orphan.
- **Putting command text in `schedule`.** There is no column for it; that is deliberate.
- **Touching `total` or `sample`.** That is the user's history, not configuration.
- **Assuming a scheduled command will run while ItamiBen is closed.** It will not.
