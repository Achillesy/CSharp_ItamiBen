# ItamiBen — 痛みを知らせる

> **痛みを知らせる** —— 让你知道它有多疼。

一个会较真的番茄钟。先划定什么算工作，再承诺一段专注，整段时间里它盯着你的前台窗口。
打开别的东西、起身去喝口水——那些分钟都不算数，休息区眼睁睁往后挪。

不弹窗、不唠叨、也不恭喜你。就是一台笨到没法跟你讲条件的钟。

[English](./README.md)

## 截图

| 浅色 | 深色 |
| ---- | ---- |
| ![主界面，浅色主题](screenshots/main-light.png) | ![主界面，深色主题](screenshots/main-dark.png) |

| 透明 | 透明 |
| ---- | ---- |
| ![主界面，壁纸上透明显示](screenshots/main-transparent-dark.png) | ![主界面，壁纸上透明显示](screenshots/main-transparent-light.png) |

| 设置 | 设置，命令示例 |
| ---- | -------------- |
| ![设置窗口](screenshots/settings.png) | ![设置窗口，配置了一条命令](screenshots/settings-command.png) |

## 安装

**macOS** —— 从 [Releases](https://github.com/Achillesy/CSharp_ItamiBen/releases) 下载
`ItamiBen-<版本>-macOS-<架构>.dmg`，打开后拖进 Applications。

**Windows 11** —— 从 [Releases](https://github.com/Achillesy/CSharp_ItamiBen/releases) 下载
`ItamiBen-<版本>-win-x64.exe`，运行安装。

**Ubuntu 24.04** —— 下载 `ItamiBen_<版本>_Ubuntu_amd64.deb`（Intel/AMD）或
`ItamiBen_<版本>_Ubuntu_arm64.deb`（ARM——比如 M1 Mac 上的 Ubuntu 24.04 虚拟机），然后：

```bash
sudo apt install ./ItamiBen_2.7.1_Ubuntu_arm64.deb   # 或 _amd64，看你的机器是哪个架构
ItamiBen
```

这个 deb 也是依赖框架的，跟另外两个包一样：`apt` 会自动从 Ubuntu 官方源装上
`dotnet-runtime-10.0`——不用 SDK，不用加第三方源，不用手动折腾。

三个包都是依赖框架的，都需要装 .NET 10 Runtime。Windows 要的是 Desktop Runtime，
它的安装包会检测缺没缺、缺了就帮你装。（WSL 下需要 WSLg 才能显示窗口。）

## 一轮是怎么走的

1. 选一个目标，拖滑块（10–50 分钟），按 **Start**。
2. 只有当你真正看着的那扇窗口命中**开始前**写好的规则，那一秒才算数——事中改规则没用。
3. 跑偏、发呆、锁屏，吃的是同一份固定预算。预算见底，这一轮就结束，**没有任何办法赚回来**。

ItamiBen 自己也没有特例：盯着它自己的钟面就是跑偏，跟盯别的没两样。

## 怎么配置：把文件丢给 AI

装好之后，点 **Settings → Open config folder** 就行，不用自己去找路径。配置就是四份
Markdown 文件：

| 文件 | 管什么 |
|---|---|
| `rules.md` | 什么算工作 |
| `commands.md` | 这台机器可以被自动跑什么命令 |
| `schedule.md` | 周期提醒，crontab 格式 |
| `layout.md` | 窗口大小和透明度 |

每份文件都自带说明——给你看，也给 AI 看。**把整个文件丢给任意一个 AI，说出你的目标，
拿回来的内容覆盖回去就行。** 配置这件事到此结束：不用装任何东西，不用背任何东西。

（实在想手找路径：macOS 在 `~/Library/Application Support/ItamiBen/`，
Windows 在 `%LOCALAPPDATA%\ItamiBen\`。）

## 给开发者

```bash
dotnet build ItamiBen.slnx
dotnet test  ItamiBen.slnx
./run-macos.sh              # 编译 → 打成 .app → 签名 → 启动
```

`dotnet` 那两行在 macOS、Windows、Linux 上是一样的（装好 .NET 10 SDK 就行）。

## 发布

```bash
./pack-macos.sh                # → dist/ItamiBen-<版本>-macOS-<架构>.dmg
.\pack-windows.ps1             # → dist\ItamiBen-<版本>-win-x64.exe（需要 Inno Setup 6）
./pack-ubuntu.sh [amd64|arm64] # → dist/ItamiBen-<版本>_Ubuntu_<架构>.deb（不给参数就用本机架构）
```

三个包打的都是同样的依赖框架发布（`-r <rid> --self-contained false`，.pdb 由 csproj
删掉）——Ubuntu 那个只是外面套了个 deb，`Depends` 让 apt 从 Ubuntu 官方源装
`dotnet-runtime-10.0`。Ubuntu 的包你也可以自己打，装上之后怎么跑、有什么问题，
欢迎反馈。

## 平台状态

| 平台 | 状态 |
|---|---|
| macOS | ✅ 端到端验过 |
| Windows 11 | ✅ 端到端验过（2026-09-18） |
| Ubuntu 24.04 | ✅ 有可安装的 deb 包（amd64/arm64），在 24.04 上验过安装；X11 下专注判定可用（2026-09-26） |

## 名字

**Itami**（痛み）是痛，**Ben** 是 Big Ben——准时、响亮、没法跟它讲条件。

## 赞助

ItamiBen 对非商业用途永久免费。如果你觉得它治住了你的拖延症，欢迎请作者喝杯咖啡：

- ☕ [Ko-fi](https://ko-fi.com/achillesy)（海外，走 PayPal）
- 💸 [PayPal 直接打赏](https://paypal.me/achillesnewman)

赞助完全自愿，不影响任何功能。

## 许可证

[PolyForm Noncommercial License 1.0.0](./LICENSE) —— 非商业用途免费。

Copyright (c) 2026 Achilles.Newman (https://github.com/Achillesy)
