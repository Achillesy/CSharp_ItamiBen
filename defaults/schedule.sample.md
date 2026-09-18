# ItamiBen — schedule

Recurring reminders, in **standard crontab format**. Each one shows a banner over the dominoes
for a minute and raises a system notification. A line may also end with a command name, and
then that command runs too.

## How to change this

**Hand this whole file to an AI** and say what you want:

> Remind me to stand up every hour between 9 and 6 on weekdays.

Then replace this file with what it gives you back.

⚠️ **ItamiBen only runs while it is open.** This is not a replacement for `launchd` or Task
Scheduler — do not schedule anything here that must happen.

---

## Instructions for the AI

**Rewrite only the configuration block at the bottom. Keep everything above it as it is.** If
the prose is already damaged, restore it from `schedule.sample.md`, which sits next to this
file and is refreshed every time ItamiBen starts.

### The line

```
<minute> <hour> <day-of-month> <month> <day-of-week>  <reminder text>  [!command]
```

Standard Vixie semantics, including the day-of-month / day-of-week OR rule: when both are
restricted, a day matches if **either** matches. `@daily`, `@hourly` and the other aliases work
in place of the five fields.

Strip the five timing fields; the rest is the reminder — **except that if its last
whitespace-separated word begins with `!`, that word names a command in `commands.md`**, and
everything before it is the reminder text.

The command goes last because that is where a crontab reader looks for "what runs". The `!` is
there to say out loud that the real command text lives in `commands.md`, not here.

⚠️ **Reminder text is mandatory.** A line carrying only a command is not valid and is skipped.
This is deliberate and structural: it makes "the machine did something and never said why"
impossible to write down.

⚠️ A reminder must therefore not **end** with a word beginning with `!`. (`快去做作业!` is
fine — that `!` is not at the start of a word.)

### Text and command do not gate each other

The banner and the notification come from the text; the command comes from the `!` word. They
run as two independent passes, so **a mistyped command name never swallows the reminder** —
the banner still appears and the failure is recorded.

⚠️ **The program cannot catch a wrong name.** `!sleap` parses perfectly; it fails when the
minute arrives. Check the name against `commands.md` yourself.

### ⚠️ An entry with a command is never replayed

If the machine was asleep when the minute passed, the command does **not** fire late: close
the lid for two hours and a 22:00 shutdown will not run when you open it. Reminders without a
command **are** replayed, up to 12 hours — seeing a missed reminder is useful, running a
missed shutdown is not.

### Comments, and how to disable an entry

`#` at the **start of a line** is a comment, exactly as in crontab. A `#` anywhere else is
ordinary reminder text.

**Disable an entry by commenting it out.** That is the crontab idiom and it keeps the line for
later; there is no `enabled` flag.

### Lines that cannot be read

A bad cron expression, too few fields, or a command with no text: that line is skipped and the
reason is recorded. The rest of the file is unaffected — every line is independent.

---

## The configuration

```cron itamiben
# Example: an hourly nudge. Off by default — delete the leading # to turn it on.
# 0 * * * *   Nice work. Keep it up.

# Example: put the machine to sleep at 23:00, and say why.
# The name after ! refers to commands.md.
# 0 23 * * *   Time to sleep !sleep
```
