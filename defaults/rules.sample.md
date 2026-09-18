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

**Do not guess. Ask the machine.** It keeps every name it has ever seen:

```
ItamiBen --query apps
#   seconds  last seen          name
#      4821  2026-09-18 12:30  Code
#      1902  2026-09-18 11:58  Google Chrome
```

That is ground truth for this platform: copy the name exactly. On macOS the binary is at
`/Applications/ItamiBen.app/Contents/MacOS/ItamiBen`; on Windows it is `ItamiBen.exe` in the
install folder. It prints and exits, and works while ItamiBen is running.

For `Title` rules there is a second one, deliberately separate:

```
ItamiBen --query titles            # today; pass a start and end to widen
```

⚠️ They are separate because they leak different things: an application list says **what you
have installed**, a title list says **what you were doing**. Ask the user before putting
titles anywhere.

**If the application is not in the list**, it has never been in front of them during a round
— ItamiBen only records while a round is running and in its focus phase. Ask them to start a
round and use it for a minute, then run the command again. Do not conclude from an empty
result that the name does not exist.

**The other platform is never in that list.** For the half you cannot see, either have the
user run the same command on that machine, or tell them plainly that it is a guess.

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
