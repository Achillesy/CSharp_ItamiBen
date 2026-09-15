# ItamiBen — 一袋米要我洗嘞

> **Too dumb to make excuses for you.**

A pomodoro clock with teeth. Pick what you're allowed to work on, commit to a stretch of
focus, and it watches the foreground window for the whole run. Wander off and those minutes
simply don't count — the deadline arc slides further away and you watch it go.

No popups, no nagging sounds. Just a clock that is too dumb to negotiate.

**Status: early. Nothing works yet — this is the skeleton.**

## What makes it different from the other tomatoes

Most pomodoro timers are honour-system stopwatches: you press start, and 25 minutes later
they congratulate you regardless of what you actually did. ItamiBen judges. A minute only
counts if the window you were actually looking at matches a rule you set beforehand.

The rules are yours to write, but you write them **before** the run, not during it.

## Requirements

- .NET 10 runtime (SDK to build)
- Windows or macOS

⚠️ **macOS needs permission to read window titles.** Which permission — and what happens
when you decline — is still being decided; see the design notes.

There is **no ActivityWatch dependency**. ItamiBen reads the foreground window itself.
(Its predecessor, ItamiTimer, was built on ActivityWatch; that turned out to be the wrong
foundation for this job, and this is the rebuild.)

## Build & run

```bash
dotnet build ItamiBen.slnx
dotnet test ItamiBen.slnx
dotnet run --project src/ItamiBen.App
```

## Layout

```
src/ItamiBen.Core/   判定与计时的纯逻辑，零 UI 引用，可单测
src/ItamiBen.App/    Avalonia 界面；平台调用只允许出现在这一层
tests/               xUnit
```

## Name

「一袋米要我洗嘞」is a Chinese mondegreen — a deliberate mishearing — and so is 大笨钟,
the everyday Chinese nickname for Big Ben (literally "big **dumb** clock"). Big Ben is
punctual, loud, and impossible to negotiate with. That is the whole product pitch, so the
name is the pitch.

## License

Not decided yet.
