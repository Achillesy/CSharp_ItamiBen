# CLAUDE.md

本文件为在此仓库中工作的 Claude Code 提供指引。

⚠️ **这份文件不进 git**（跟 DESIGN.md / DECISIONS.md / signing/ 一样，用户 2026-09-15 定：
仓库里只放代码、README.md 和 LICENSE）。换机器要自己拷过去。

## 项目是什么

ItamiBen（痛みを知らせる）：带强制约束的番茄钟，Windows + macOS，Avalonia 12 / .NET 10。
副标题 **「痛みを知らせる」**——把痛告诉你。

勾选允许的小目标 → 提交任务 → 程序**每秒自己读前台窗口和标题**，只有命中规则的秒
才计入。偷懒不弹窗不出声，只有环上的红格，和那块**越推越远的淡蓝色休息区**。

Itami（痛み）是痛，Ben 是 Big Ben。**名字、核心视觉、惩罚机制是同一件事**——见 DESIGN §5。

⚠️ **窗口标题（`AppData.WindowTitle` = `痛みを知らせる`）是文案，不是标识符。**
2026-09-16 之前 Windows 的单实例靠 `FindWindow` 按这串精确匹配找窗口，等于「改文案 =
静默改坏功能」。那条依赖已经拆了（改按进程名找，DECISIONS I25），**别再接回去**。

**跟 ActivityWatch 一刀两断**，这是 ItamiTimer（v3）的重做而不是续集。原因见 DESIGN §1.2
（一句话：AW 是**订阅通知**的，实测它整整 13 分钟把全屏 mame 认成 Terminal）。

## 当前状态（2.5.5，2026-09-18）

| | |
|---|---|
| ✅ 骨架 | slnx + Core / App / Core.Tests，`dotnet build` / `dotnet test` 绿 |
| ✅ 前台窗口读取 | `App/Platform/ForegroundWindow.cs`，macOS 和 Windows 11 **都已实测**（后者 2026-09-18） |
| ✅ 采样 | `Sampler`：100ms 一拍，app 走 UI 线程、标题走后台线程（DECISIONS B1/B2/B6） |
| ✅ 签名 / 授权 | 自签名证书，重编不再失效 |
| ✅ 设计定稿 | DESIGN §4（判定与计时）/ §5（钟面视觉），护栏 DECISIONS C 组 / D 组 |
| ✅ 判定引擎 | Core 全套：判定 / 格子 / 累计 / 两小时环 / 三段读数投影。79 个测试 |
| ✅ 钟面 | `Drawing/DialControl.cs`：木框 + 两圈色环 + 三段色块，明暗两套色 |
| ✅ 产品界面 | 勾目标 → 选 10/25/50 → Start / Give up；累计落 `total` 表 |
| ✅ 闹钟 / 声音 | 从 v3 搬完：滚轮拨针 + 黄针 + 一次性 + 加速；只用系统音，响 4 遍（DESIGN §8） |
| ✅ 实机验收 | 2026-09-16 端到端跑通，结果见 DESIGN §7.1 和 §8.1 |
| ✅ 计划表 | `schedule.md`，标准 crontab + 行尾 `!命令名` + 钟面小红圈 + 提示条（DESIGN §10） |
| ✅ 窗口外观 | 无边框 + 透明底 + 置顶；右上角图钉 / 主题两个矢量图标；`layout.md`（**只在启动时读一次**，DESIGN §11） |
| ✅ 骨牌 / 界面布局 | 七个骨牌 = 星期几；绿红按钮、无数字滑块、目标单选行、版权行（DECISIONS C14） |
| ✅ 滴答声 | 运行时合成，不打包音频；喇叭图标 + 右键菜单联动（DECISIONS E7） |
| ✅ 到点跑命令 | `commands.md` 存正文、`Alarm` 分系统指名字；**开关每次启动都是关的**（DECISIONS E8） |
| ✅ 设置窗口 | 齿轮图标打开；只放音色和音量，开关都在右键菜单（DECISIONS E11） |
| ✅ 应用图标 | 矢量青番茄（带条纹），打包时现导 .icns / .ico（DECISIONS G10 / G12） |
| ✅ 单实例 | 运行时目录里的独占文件锁。⚠️ **v3 那个命名 Mutex 在 macOS 上根本不生效**（DECISIONS I1） |
| ✅ 记录 | 观测在库里；事件和错误在 `event.log` / `error.log` 两份纯文本里（2026-09-18） |
| ✅ 系统通知 | 计划表到点，提示条 + 系统通知**并存**，一条事件一个（I4） |
| ✅ 打包发布 | `pack-macos.sh` → .dmg、`pack-windows.ps1` + Inno Setup **两条都实测跑通**（含静默升级） |
| ✅ 开发工具 | `--dial-specimens` 钟面样张、跑偏归因日志（I6 / I7） |
| ✅ 文档 | README.md（英） + README_ZH.md（中）+ 安装包和 .dmg 各一份 Read Me（**四份，改一份要过一遍另外三份**，I5） |

**Windows 那一半 2026-09-18 在 Windows 11 上端到端跑通了**（提交 `e0e0af4`）。
⚠️ **别再往仓库里写「纸面代码 / 没在真机跑过」**——那些话现在是假的，
留着只会让人不敢信已经验过的代码。

验过的：`ForegroundWindow.Win`、`InputIdle.WindowsElapsed`、winmm 提示音、
到点的提醒和它带的命令、单实例的锁本身、几条 `--query`、
`pack-windows.ps1` + `installer/ItamiBen.iss`（编译到静默升级）。

**仍未验证的两处**（别当成已验）：
- 安装包里 .NET 运行时检测**只走到「已装」那一支**（测试机上有 10.0.10），
  下载 + `ShellExec('runas')` 那一支没跑过，记在 `.iss` 里；
- `SetForegroundWindow` 那「提到前台」的一下受 **Windows 前台锁**约束：
  双击图标那条路能成（启动者是资源管理器），从不在前台的终端里起第二个实例
  会被系统拒绝、只闪一下任务栏。**这是预期行为，别为它加重试。**

**下一步**：还欠 `screenshots/`（要真机截图）。

⚠️ **别顺手把 v3 的「三声通知」也搬过来**——那跟「偷懒不弹窗不出声」直接冲突，
DECISIONS E4 记着理由。闹钟出声不矛盾：它是墙上时钟的功能，跟专注判定无关。

数据流一共就这么几步（DESIGN §7 有图）：`GoalRules.Of`（从库里的行装配）→
`new Round(now, focusMinutes, goals, rules)` → `Sampler` 每拍
`round.Observe(now, app, title)` → 画 `round.Cells` / `round.Project()`
→ 受控退出时 `round.End(now, reason)` + `totals.Add(round.FocusedSecondsByGoal)`。

## 三份文档，各司其职

| 文件 | 内容 | 改代码前 |
|---|---|---|
| `DESIGN.md` | 当前设计 + **所有实测数据**（§2 那几张表是量出来的，不是推断） | **必读相关章节** |
| `DECISIONS.md` | 护栏（已到 I32）：被推翻的方案、知情代价、「不要做成」 | **动手前先查** |
| `README.md` / `README_ZH.md` | 进 git 的说明文档，中英各一份 | 用户可见行为变了要同步 |

⚠️ **面向用户的文档一共四份，改一份就要过一遍另外三份**（v3 漏过一次）：
`README.md`、`README_ZH.md`、`installer/README.txt`（Windows 安装包里那份）、
`pack-macos.sh` 里那段 `Read Me.txt`（.dmg 里那份）。
后两份**不在仓库的显眼处**，最容易被忘掉。

⚠️ **这个项目已经推翻过自己一次**（DECISIONS B5：「AX 会被全屏应用卡死」那条），
所以 DECISIONS 里**带「未实证」标签的推理，验证之前不许用来支撑架构决策**。

## 硬性约束（违反即事故）

- **`AssemblyName=ItamiBen` 和 `App.axaml` 的 `Name="ItamiBen"` 永不改**——macOS 的
  `localizedName` 报的就是它，**库里存的、`rule` 表里匹配的都是这个字符串**；
  2026-09-16 起 Windows 的单实例也按它找已有窗口（I25）。
  改了**不报错**，只会让历史采样和用户规则对不上号。
  ⚠️ **它不再是「自身豁免」的依据**——那个硬编码特例 2026-09-16 已由用户拍板删除
  （DECISIONS C9）。前台是 ItamiBen 自己就是普通的跑偏。
- **bundle id `com.achillesy.itamiben` 永不改**——macOS 授权绑在它 + 签名上。
- Core 保持 `net10.0`、零 UI 引用。⚠️ net10.0 **挡不住 P/Invoke**，「Core 不碰平台调用」
  是纪律不是编译器强制。平台调用一律收口在 `App/Platform/` 的单个文件里。
- **UI 是 Avalonia，不是 WPF / WinForms / Win32。** API 名字像、语义未必一样，而且这类错
  **不报错**只是安静失效。v3 栽过：滚轮 `Delta.Y` 在 Avalonia 里一格就是 1.0，不是 Win32
  的 120，照搬那个除法两天没生效过。
- **没有 CLI**（DECISIONS A3）。
- **主动轮询，绝不订阅系统通知**（DECISIONS B4，实测结论）。
- 源码注释、提交信息、开发文档用中文；日志、异常、界面文字用英文。

## 构建 / 运行 / 调试

```bash
dotnet build ItamiBen.slnx
dotnet test  ItamiBen.slnx
./run-macos.sh              # 编译 → 打 .app → 用证书签名 → 重启
./run-macos.sh --run-only   # 只启动，不编译不重签（保住授权）

./pack-macos.sh             # 发布：publish → .app → dist/ItamiBen-<版本>-macOS-<arch>.dmg
pwsh pack-windows.ps1       # 发布：publish → Inno Setup → dist/ItamiBen-<版本>-win-x64.exe
```

⚠️ **.app 的装配规矩只写在 `bundle-macos.sh` 里一份**，`run-macos.sh` 和 `pack-macos.sh`
都调它。别在任何一边另写一个 Info.plist——bundle id 和签名方式一旦两边对不上，
症状是「开发时好好的，装了发布版就读不到窗口标题」，而且**不报错**。

⚠️ `run-macos.sh` 用 `pkill`（SIGTERM）停进程。**SIGTERM 已经挂了处理器**，会走完
受控退出（落盘 + 存闹钟）；`kill -9` 仍然救不了，但那条路有观测库兜着——本轮状态在
库里，重开就接回来。

⚠️ **屏幕锁着的时候 app 根本起不来**：Avalonia 拿不到 RenderTimer
（`Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661`），
当场 SIGABRT，而 `open` 还是返回成功——症状是「启动了但进程不在、日志还是上一次的」。
**这不是 bug，解锁再跑。** 2026-09-16 04:23 为此查了一轮（先怀疑是自己刚改的代码）。

⚠️ **别用 `dotnet run` 或直接跑 `bin/Debug` 里的裸二进制**：macOS 的授权按「应用」记账，
裸二进制拿不到辅助功能授权，标题会读不到。必须走 `run-macos.sh` 打出来的 `.app`。

**查问题**（我自己能查，不用让用户描述窗口）：

```bash
B=dist/ItamiBen.app/Contents/MacOS/ItamiBen
$B --query apps                         # 见过的每一个程序名——写 App 规则照着抄
$B --query rounds  "2026-09-16 00:00"   # 开过哪些轮、怎么结束的
$B --query minutes "2026-09-16 12:10"   # 逐分钟构成，红的给出是哪扇窗口
$B --query samples "2026-09-16 12:10"   # 一秒一行的原始观测
```

事件和错误是**纯文本**，直接看：

```bash
D=~/Library/"Application Support"/ItamiBen
cat "$D/event.log"    # 完整时间线：启动 / 退出 / 闹钟 / 提醒 / 命令 / 配置重装 / 出错
cat "$D/error.log"    # 只有 warn 和 error，**正常情况下这个文件不存在**
```

⚠️ **`--query` 只留给二进制的库**（DECISIONS I24）：纯文本文件不配有出口，
为它做一条等于把 `cat` 包装一遍。`minutes` 更是非它不可——它要重放判定引擎。

⚠️ **教训**：2026-09-15 调试时因为没有眼睛，来回问了用户好几轮窗口上写了什么。
**给程序装眼睛，比省那几行代码重要得多**——只是眼睛现在长在数据库上，不在文本文件里。

## macOS 授权与签名

需要**辅助功能**授权才能读别的 app 的标题。几条实测结论：

- `kAXErrorAPIDisabled` 的含义是「**本进程未被信任**」，不是「那个 app 不给」。症状是
  **能读自己的标题、读不到别人的**——读自己进程的 AX 树本来就不需要授权。别往错方向查。
- 拨开关之后**不需要重启进程**，下一拍就生效。
- 用自签名证书签名之后，**重编不会作废授权**（DR 绑证书不绑 cdhash）。证书信息和
  重建方法见 `signing/README.md` 和 DESIGN §2.2。
- 真要重新授权：`tccutil reset Accessibility com.achillesy.itamiben`

## 布局与惯例

```
src/ItamiBen.Core/           判定与计时的纯逻辑（now 一律是参数，所以可测）
src/ItamiBen.App/            Avalonia 界面
src/ItamiBen.App/Platform/   平台调用，每处收口在单个文件
tests/ItamiBen.Core.Tests/   判定与计时的测试，测试名是中文句子，直接陈述行为
tests/ItamiBen.App.Tests/    **只测 App 层里的纯逻辑**（音频文件头这类），Avalonia 从不初始化
signing/                     证书材料，不进 git

运行时目录（macOS `~/Library/Application Support/ItamiBen/`）：
  rules.md / commands.md / schedule.md / layout.md   **配置**，人和 AI 写，程序只读
  *.sample.md       随程序发的参考件，**每次启动覆盖刷新**，程序自己从不读
  ItamiBen.sqlite3  程序自己记的：app / title / sample / round / setting / total 六张表
  event.log         完整时间线
  error.log         只有 warn 和 error；**正常情况下它不存在**

⚠️ **分界线是「谁写」，不是「配置 vs 账本」**（2026-09-18）：人和 AI 写的进文件，
程序自己写的留在库里。切口干净之后，**智能体连库的写权限都不需要**。
⚠️ 每份配置是三段式 Markdown（给人的说明 → 给 AI 的规矩 → 标记好的配置块），
所以**把单个文件扔给任何 AI 就够了**，不再有 `AGENT.md`。
⚠️ 配置块靠围栏信息串里的 `itamiben` 认（```json itamiben），全文件有且只有一个
——**不能用「第一个/最后一个代码块」**，说明部分本身就带示例代码块。
⚠️ 改完一分钟内生效（程序比四个文件的 mtime）。`layout.md` 例外，只在启动时读一次。
```

- git：默认分支 **`master`**，还没有远端（用户 2026-09-15：「先不用 github」）。
- 运行时数据在 `~/Library/Application Support/ItamiBen/`（macOS）——
  **跟 v3 完全隔离，不共用任何文件**（DECISIONS A6）。

## 姊妹项目 ItamiTimer（v3）

`../ItamiTimer`，**已定版、不再改**。它的 `DECISIONS.md` 里有大量仍然适用的结论
——钟面渲染、闹钟 cron、声音、主题、布局，**那些跟 AW 无关，可以直接搬**（清单见
DESIGN §6）。跟 AW 有关的（O 组、H2~H4a、C3）全部作废，别参考。
