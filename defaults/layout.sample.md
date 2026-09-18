# ItamiBen — window

Two settings with no user interface: how wide the window is, and how see-through it is.
Everything else about the window (theme, pin, position, sounds) has a control somewhere and is
remembered for you.

## How to change this

**Hand this whole file to an AI** and say what you want:

> Make the window small and about 60% opaque.

Then replace this file with what it gives you back, **and restart ItamiBen** — see below.

---

## Instructions for the AI

**Rewrite only the configuration block at the bottom. Keep everything above it as it is.** If
the prose is already damaged, restore it from `layout.sample.md`, which sits next to this file
and is refreshed every time ItamiBen starts.

| key | values | effect |
|---|---|---|
| `Layout` | `"standard"` or `"compact"` | window width 380 px or 292 px. Anything else is read as `standard`, with no error. |
| `OpacityPercent` | `10`–`100` | applies to the dial, the dominoes and the card backdrop. Buttons and text stay solid so they stay readable at low values. |

⚠️ **Both keys are read once, at startup.** Editing this file while ItamiBen is running has no
effect whatsoever, and the user will report that your change did not work. **Tell them they
have to quit and reopen ItamiBen** — this is the one place where waiting a minute does not
help.

⚠️ **An `OpacityPercent` outside 10–100 is not clamped, it is replaced by 90.** Writing `5`
gives you 90, not 10. That is deliberate: clamping would make "I wrote 5" and "I wrote 10"
look identical on screen, and the user would believe a value took effect that never did.

⚠️ **This is the one file that is a per-machine preference.** `rules.md` and `commands.md` are
meant to travel between a user's machines unchanged; a width and an opacity chosen for a
laptop may be wrong on a desktop. When you migrate a configuration, ask before carrying this
one over.

---

## The configuration

```json itamiben
{
  "Layout": "standard",
  "OpacityPercent": 90
}
```
