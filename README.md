# ItamiBen — 一袋米要我洗嘞

> **Too dumb to make excuses for you.**

A pomodoro clock with teeth. Pick what you're allowed to work on, commit to a stretch of
focus, and it watches the foreground window for the whole run. Wander off and those minutes
simply don't count — the deadline arc slides further away and you watch it go.

No popups, no nagging sounds. Just a clock that is too dumb to negotiate. (There *is* an
alarm — scroll on the dial to move the yellow hand — but that is a wall clock's job, and it
has nothing to do with judging your focus.)

**Status: early, but it runs.** Pick a goal, pick 10 / 25 / 50 minutes, press Start, and
the clock watches the foreground window for the rest of the run. Scroll on the dial to set
the alarm. Verified end to end on macOS; **the Windows half has never actually been run.**

## What makes it different from the other tomatoes

Most pomodoro timers are honour-system stopwatches: you press start, and 25 minutes later
they congratulate you regardless of what you actually did. ItamiBen judges. A minute only
counts if the window you were actually looking at matches a rule you set beforehand.

The rules are yours to write, but you write them **before** the run, not during it.

## Reminders

Drop an `alarms.cron` next to your `rules.json` and ItamiBen will nag you on a schedule.
It is a **standard crontab** — Vixie semantics, no dialect of its own — and the sixth
column is text, never a command. See `alarms.cron.example`. The next one within twelve
hours shows up as a small red ring on the dial.

## Looks

The window has no frame and floats on top, so it sits on your desktop like a wall clock
rather than a program. Drag it by the dial; right-click the dial to unpin or close it.
Drop a `layout.json` next to your rules to make it smaller or see-through —
see `layout.json.example`.

## Requirements

- .NET 10 runtime (SDK to build)
- Windows or macOS

⚠️ **macOS needs Accessibility permission to read window titles.** Without it you still
get the app name, but title rules never match — the app says so in its status bar and
offers to open the right settings page. Windows needs no permission.

There is **no ActivityWatch dependency**. ItamiBen reads the foreground window itself.
(Its predecessor, ItamiTimer, was built on ActivityWatch; that turned out to be the wrong
foundation for this job, and this is the rebuild.)

## Build & run

```bash
dotnet build ItamiBen.slnx
dotnet test  ItamiBen.slnx
./run-macos.sh              # 编译 → 打成 .app → 签名 → 启动
./run-macos.sh --run-only   # 只启动，不编译不重签
```

⚠️ **macOS 上别用 `dotnet run`，也别直接跑 `bin/` 里的裸二进制。** 辅助功能授权是按
「应用」记账的，裸二进制拿不到，症状是**只能读到应用名、读不到窗口标题**——一半的
规则从此静默失效，而且不报错。必须走 `run-macos.sh` 打出来的 `.app`。

## Releases

```bash
./pack-macos.sh             # → dist/ItamiBen-<版本>-macOS-<arch>.dmg
pwsh pack-windows.ps1       # → dist\ItamiBen-<版本>-win-x64.exe（需要 Inno Setup 6）
```

两边都是**依赖框架**的，需要目标机器上已经装好 .NET 10 Runtime（不是 SDK）。
Windows 的安装包会自己检测并提出替用户下载；macOS 的 `.dmg` 在 Read Me 里说明。

版本号只有一个出处：`Directory.Build.props` 的 `<Version>`。

⚠️ **Windows 那一半到现在一次都没在真机上跑过**，`pack-windows.ps1` 和
`installer/ItamiBen.iss` 都是纸面代码。

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

[PolyForm Noncommercial License 1.0.0](./LICENSE) — free for noncommercial use.

Copyright (c) 2026 Achilles.Newman (https://github.com/Achillesy)
