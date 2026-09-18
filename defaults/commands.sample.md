# ItamiBen — commands

This file is **everything this machine may be made to run automatically**, and which one the
alarm runs. Nothing outside this file can be executed: the schedule and the alarm refer to
commands **by name**, never by text.

Commands are listed **least dangerous first**. Keep that order when you add one — this file is
read top to bottom by a person deciding what to trust.

## How to change this

**Hand this whole file to an AI** and say what you want:

> Make the alarm put my Mac to sleep instead of opening a folder.

Then replace this file with what it gives you back.

⚠️ The switch that lets the alarm run anything is **off every time ItamiBen starts**, on
purpose. Nothing here runs until you turn it on in the right-click menu.

---

## Instructions for the AI

**Rewrite only the configuration block at the bottom. Keep everything above it as it is.** If
the prose is already damaged, restore it from `commands.sample.md`, which sits next to this
file and is refreshed every time ItamiBen starts.

### The name is the identifier

| rule | why |
|---|---|
| `^[a-z0-9-]+$` — lowercase, digits, hyphen | it is quoted as a bare word from `Alarm` and from `schedule.md` |
| never reuse a name | ⚠️ **JSON does not error on a duplicate key. The second one silently wins and the first command is gone.** Verified: `{"a":"A","a":"B"}` parses to one entry, value `B`. Count your keys. |
| name the **action**, not the occasion | `shutdown`, `restart`, `sleep`, `lock` — never `alarm`, never `nightly` |

⚠️ That last rule is load-bearing. A name that says *when* instead of *what* lets the two
systems drift apart under one name, and this project shipped exactly that: `alarm` was
**restart** on macOS and **shutdown** on Windows, while the settings window only ever shows
the half you are standing on. **Both systems of one name must do the same thing.** When you
want the two machines to behave differently, that is what `Alarm` is for — it is bound per
system precisely so the command names can stay honest.

### Both systems live in one file

ItamiBen picks the entry for the system it is running on. A missing one means "this command
does not exist here" — nothing is substituted, and the attempt is recorded.

⚠️ **You cannot test the other system's half, and there is no way to discover it.** For
application names the user can show you (see `rules.md`). **There is no equivalent for
commands.** Write the half you can reason about, and where you are not certain, say so to the
user instead of writing it silently.

### Paths: the two systems expand them by opposite rules

|                  | outside quotes | inside quotes       |
|------------------|----------------|---------------------|
| `~` in `/bin/sh` | expands        | **does not expand** |
| `%VAR%` in `cmd` | expands        | expands             |

Quotes are needed either way, because both paths can contain a space (`Application Support`;
a Windows user name like `John Smith`). So the two are written differently:

```
macOS     open ~/"Library/Application Support/ItamiBen"     ← tilde OUTSIDE the quotes
Windows   explorer "%LOCALAPPDATA%\ItamiBen"                ← whole path inside
```

Tested: with the tilde inside the quotes the directory is not found.

⚠️ **Never write an absolute path containing the user's name.** Use `~` and `%LOCALAPPDATA%`
so the file stays portable between machines and safe to paste into a web chat.

⚠️ **Windows backslashes must be doubled in JSON**: `"%LOCALAPPDATA%\\ItamiBen"`. A single
backslash is a JSON escape character.

### `Note` is status, not documentation

`Note` names **what has not been verified yet**:

| value | meaning |
|---|---|
| `"untested"` | neither system's command has been run |
| `"Windows untested"` | the macOS half has been run on a real machine; the Windows half has not |
| key absent | both have been run |

**Remove or narrow it as soon as the user actually runs one.** Do not put prose in `Note`;
anything a human needs to know belongs in this file, above the block.

### How a shutdown-class command gets tested

There is no safe rehearsal: testing `shutdown` shuts the machine down. Tell the user the
honest version.

- **A wrong command fails harmlessly.** Nothing happens, and the failure is recorded.
- **The danger is a command that runs but does the wrong thing** — naming by action prevents
  that.
- So the test is not a separate step: **the first time they actually want to shut down, they
  arm the switch and let it run.** Until then the switch stays off.

### Caveats in the commands below

| command | caveat |
|---|---|
| `lock` on macOS | `pmset displaysleepnow` sleeps the display. It locks **only** if the user's security settings require a password immediately after sleep. |
| `sleep` on Windows | `SetSuspendState 0,1,0` may hibernate instead of sleeping when hibernation is enabled. |

---

## The configuration

```json itamiben
{
  "Alarm": {
    "MacOS": "open-config",
    "Windows": "open-config"
  },
  "Commands": {
    "open-config": {
      "MacOS": "open ~/\"Library/Application Support/ItamiBen\"",
      "Windows": "explorer \"%LOCALAPPDATA%\\ItamiBen\"",
      "Note": "Windows untested"
    },
    "sleep": {
      "MacOS": "pmset sleepnow",
      "Windows": "rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
      "Note": "untested"
    },
    "lock": {
      "MacOS": "pmset displaysleepnow",
      "Windows": "rundll32.exe user32.dll,LockWorkStation",
      "Note": "untested"
    },
    "restart": {
      "MacOS": "osascript -e 'tell application \"System Events\" to restart'",
      "Windows": "shutdown /r /t 0",
      "Note": "untested"
    },
    "shutdown": {
      "MacOS": "osascript -e 'tell application \"System Events\" to shut down'",
      "Windows": "shutdown /s /t 0",
      "Note": "untested"
    }
  }
}
```
