# ItamiBen — rules

This file decides **what counts as work**. Every second, ItamiBen reads the application and
window title in front of you; a second is counted only if it matches one of the rules below.

Goals are what you pick before pressing Start. Each goal has rules; a second counts if **any**
of them matches.

## How to change this

**Hand this whole file to an AI** — a coding agent or a web chat, it does not matter — and say
what you want:

> Only count VS Code and Chrome when the title mentions GitHub.

Then replace this file with what it gives you back.

---

## Instructions for the AI

**Rewrite only the configuration block at the bottom. Keep everything above it as it is** —
the next person to open this file needs it. If the prose is already damaged, restore it from
`rules.sample.md`, which sits next to this file and is refreshed every time ItamiBen starts.

**One file, both operating systems.** There is no platform field: every `App` regex must match
the name on macOS *and* on Windows, so the file can be copied between machines unchanged.

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

### `Title` — the window title

A plain regex against the window title. Titles are usually the same text on both systems, so
they need no special handling. A rule may set `App`, `Title`, or both; when both are present
**both** must match.

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

### Rules that apply to the block itself

- **At least one goal must be enabled**, or the user cannot press Start at all.
- **A goal with an empty `Rules` list is refused.** An empty goal would match everything,
  which switches off the only thing this program does.
- Retire a goal with `"Disabled": true` instead of deleting it — the hours it has accumulated
  are keyed by its name.
- Strict JSON: no comments, no trailing commas.

---

## The configuration

```json itamiben
{
  "Groups": {
    "Pomodoro": {
      "Rules": [
        { "App": "^(ItamiBen|ItamiBen\\.exe)$" }
      ]
    }
  }
}
```
