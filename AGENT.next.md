# AGENT.next.md — 配置回到文件之后，AGENT.md 的对应段落（草案）

⚠️ **这份文件不随程序发布，现在也不生效。** 眼下生效的是 `AGENT.md`，它描述的是
「配置住在 SQLite 里」，那仍然是代码的真实行为。

⚠️ **代码落地之前，不要把这里的内容并进 `AGENT.md`。** 一份说「去改 JSON」的说明，
会让智能体编辑程序根本不读的文件，而改完**每一个本地信号都显示成功**——那正是
DECISIONS I28 那个影子数据库事故的形状。

下面两节已经定稿（2026-09-18 讨论）。`schedule` 和外观设置还没讨论，等定了再补。

---

## `rules.json` — what counts as work

```
macOS    ~/Library/Application Support/ItamiBen/rules.json
Windows  %LOCALAPPDATA%\ItamiBen\rules.json
```

**One file, used unchanged on both operating systems.** There is no platform field: every
`App` regex matches the name on either system, so the file can be copied from one machine to
the other as it is.

```json
{
  "Groups": {
    "Coding": {
      "Rules": [
        { "App": "^(Code|Code\\.exe)$" },
        { "App": "^(Google Chrome|chrome\\.exe)$", "Title": "GitHub" }
      ]
    },
    "Last year's goal": {
      "Disabled": true,
      "Rules": [ { "App": "^Xcode$" } ]
    }
  }
}
```

A second counts toward a goal if **any** of its rules match. Within one rule, `App` and
`Title` must **both** match when both are present. `Disabled: true` retires a goal without
losing it; leave the key out to keep it selectable.

### `App` — the foreground application's name

The two systems report different strings, and **you cannot derive one from the other**:

| app           | macOS           | Windows                        |
|---------------|-----------------|--------------------------------|
| VS Code       | `Code`          | `Code.exe`                     |
| Claude        | `Claude`        | `claude.exe`  ← different case |
| Google Chrome | `Google Chrome` | `chrome.exe`  ← different name |

Write both real names:

```json
{ "App": "^(Google Chrome|chrome\\.exe)$" }
```

`^Code(\.exe)?$` is a shorthand that is correct **only** when the two names differ by exactly
`.exe`. Chrome is why that is not a general rule.

⚠️ The regexes are **case sensitive**. A rule that matches nothing produces no error and no
warning — the user just sees a session of red, which looks exactly like a day they wasted.

### When you do not know what an application is called

Do not guess. Have the user show you.

1. Ask them to **start a round**, then use that application for a minute.
   ⚠️ ItamiBen records only while a round is running and in its focus phase. With no round in
   progress nothing is written and you will find nothing — do not conclude from an empty
   result that the application has no name.
2. It counts as off-task, so it turns red. That red is the evidence.
3. Ask for the time and read the name the system actually reported:

```
ItamiBen --query minutes "2026-09-17 01:50"
01:52  focus=46  off=14  ...  ← 5s [Doubao] FNT格式与在线解题 - 豆包
                                   ^^^^^^^^ this is the name for the rule
```

4. That name is ground truth for **this** platform only. For the other one, either have the
   user repeat this on that machine, or tell them plainly that the other half is a guess.

---

## `commands.json` — everything this machine may be made to run

```
macOS    ~/Library/Application Support/ItamiBen/commands.json
Windows  %LOCALAPPDATA%\ItamiBen\commands.json
```

**Executable text lives only in this file.** The alarm and the schedule refer to commands
**by name**, never by text, so "what can this machine be made to run automatically" is
answerable by looking in exactly one place.

```json
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
    }
  }
}
```

`Alarm` names the command the one-shot alarm runs when the user arms **Run command at
alarm** — **per system**, because which command belongs on which machine is a preference.
The shipped default is `open-config` on both: arming the switch opens a folder and costs
nothing. `""` means nothing is armed.

⚠️ Commands are listed **least dangerous first**. Keep that order when you add one; the file
is read top to bottom by a human deciding what to trust.

### The name is the identifier

| rule | why |
|---|---|
| `^[a-z0-9-]+$` — lowercase, digits, hyphen | it is quoted as a bare word from `Alarm` and from the schedule |
| never reuse a name | ⚠️ **JSON does not error on a duplicate key. The second one silently wins and the first command is gone.** Verified: `{"a":"A","a":"B"}` parses to one entry, value `B`. Count your keys, and ask the user to search the file. |
| name the **action**, not the occasion | `shutdown`, `restart`, `sleep`, `lock` — never `alarm`, never `nightly` |

⚠️ That last rule is load-bearing. A name that says *when* instead of *what* lets the two
systems drift apart under one name, and this project shipped exactly that: `alarm` was
**restart** on macOS and **shutdown** on Windows, while the settings window only ever shows
the half you are standing on. **Both systems of one name must do the same thing.** When you
want them to differ, that is what the per-system `Alarm` binding is for.

### Both systems live in one file

ItamiBen picks the entry for the system it is running on. A missing one means "this command
does not exist here" — nothing is substituted, and the attempt is written to `itamiben.log`.

⚠️ **You cannot test the other system's half, and there is no way to discover it.** For
application names the user can show you (start a round, use the app, read the red minute).
**There is no equivalent for commands.** Write the half you can reason about, and where you
are not certain, say so to the user instead of writing it silently.

### Paths: the two systems expand them by opposite rules

|                    | outside quotes | inside quotes        |
|--------------------|----------------|----------------------|
| `~` in `/bin/sh`   | expands        | **does not expand**  |
| `%VAR%` in `cmd`   | expands        | expands              |

Quotes are required either way, because both paths can contain a space (`Application
Support`; a Windows user name like `John Smith`). So the two are written differently:

```
macOS     open ~/"Library/Application Support/ItamiBen"     ← tilde OUTSIDE the quotes
Windows   explorer "%LOCALAPPDATA%\ItamiBen"                ← whole path inside
```

Tested: `"~/Library/Application Support/ItamiBen"` with the tilde inside the quotes does not
find the directory.

⚠️ **Never write an absolute path containing the user's name.** Use `~` and `%LOCALAPPDATA%`
so the file stays portable between machines and safe to paste into a web chat.

⚠️ **Windows backslashes must be doubled in JSON**: `"%LOCALAPPDATA%\\ItamiBen"`. A single
backslash is a JSON escape character — it will either change the string or fail to parse.

### `Note` is status, not documentation

`Note` names **what has not been verified yet**:

| value | meaning |
|---|---|
| `"untested"` | neither system's command has been run |
| `"Windows untested"` | the macOS half has been run on a real machine; the Windows half has not |
| key absent | both have been run |

**Remove or narrow it as soon as the user actually runs one.**

⚠️ Do not put prose in `Note`. Anything a human needs to know belongs in AGENT.md — data
drifts, AGENT.md is reviewed.

### How a shutdown-class command gets tested

There is no safe rehearsal: testing `shutdown` shuts the machine down. Tell the user the
honest version.

- **A wrong command fails harmlessly.** Nothing happens, and the failure lands in
  `itamiben.log`. It cannot damage anything.
- **The danger is a command that runs but does the wrong thing** — and naming by action is
  what prevents that.
- So the test is not a separate step: **the first time they actually want to shut down, they
  arm the switch and let it run.** Until then the switch stays off.

The switch is off at every start, by design. Never ask the user to leave it armed.

### Caveats in the shipped commands

| command | caveat |
|---|---|
| `lock` on macOS | `pmset displaysleepnow` sleeps the display. It locks **only** if the user's security settings require a password immediately after sleep. |
| `sleep` on Windows | `SetSuspendState 0,1,0` may hibernate instead of sleeping when hibernation is enabled. |

### The file is strict JSON

No comments, no trailing commas, UTF-8 without a BOM, LF line endings. It must open cleanly
in any plain text editor and parse in any standard JSON tool. If something needs explaining,
explain it to the user in conversation — not in the file.
