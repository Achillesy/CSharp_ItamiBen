#!/usr/bin/env bash
#
# 打出 dist/ItamiBen-<版本>-macOS-<arch>.dmg。Windows 那边的对应物是 pack-windows.ps1。
#
# ⚠️ **打包不是锦上添花，它是正确性的一部分。**
#    macOS 的辅助功能授权（TCC）按「bundle id + 代码签名」记账，而**读别的程序的窗口
#    标题必须有这个授权**——没有 bundle、没签名，这个程序的判定引擎就只能看见 app 名、
#    看不见标题，一半的规则直接失效，而且**不报错**。
#
# ⚠️ **这个脚本只产出发布镜像，别的什么都不干。**
#    不装到 ~/Applications、不启动。本机要跑用 ./run-macos.sh——装一份在别处只会制造
#    「我现在跑的到底是哪一个」。bundle 在临时目录里装配，跟着临时目录一起扔掉。
#
# 用法：  ./pack-macos.sh
set -euo pipefail
cd "$(dirname "$0")"

# 版本号**只有一个出处**：Directory.Build.props，跟程序自己在界面上显示的是同一个值。
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)

# ⚠️ **RID 必须写死给出**。不给 -r 的话，Skia/HarfBuzz **每个平台**的 native 库和调试
#    符号会全被拉进来——v3 在 Windows 侧量过，27MB 变 560MB，光三个平台的
#    libSkiaSharp.pdb 就 244MB。
ARCH=$(uname -m | sed 's/^x86_64$/x64/')
RID="osx-$ARCH"

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

echo "==> publish ($RID，依赖框架)"
dotnet publish src/ItamiBen.App -c Release -r "$RID" --self-contained false \
    -o "$STAGE/publish" --nologo -v quiet
echo "    $(du -sh "$STAGE/publish" | cut -f1)（.pdb 已由 csproj 的 StripPdbFromPublish 删掉）"

echo "==> 装配 bundle"
# ⚠️ 装配规矩**只写在 bundle-macos.sh 里一份**，跟 run-macos.sh 共用，
#    免得两份脚本各写一个 Info.plist、哪天 bundle id 或签名方式对不上（授权当场失效）
./bundle-macos.sh "$STAGE/publish" "$STAGE/dmg/ItamiBen.app" "$VERSION"

echo "==> 装 .dmg"
ln -s /Applications "$STAGE/dmg/Applications"
# ⚠️ **没有示例配置文件了**（2026-09-16 起配置住在库里，DECISIONS I15）。
#    改放 AGENT.md：装之前就能读到「怎么让智能体改配置」。
cp AGENT.md "$STAGE/dmg/"

# ⚠️ 这份 Read Me 是**面向用户的文档之一**（另外两份是 README.md 和 installer/README.txt）。
#    用户可见的行为变了，三份都要跟着改——v3 漏过一次。
cat > "$STAGE/dmg/Read Me.txt" <<NOTE
ItamiBen $VERSION for macOS ($ARCH)
一袋米要我洗嘞 — Too dumb to make excuses for you


Install
=======

Drag ItamiBen.app onto the Applications folder in this window.


Requires the .NET 10 Runtime (not the SDK)
==========================================

    https://dotnet.microsoft.com/download/dotnet/10.0


First launch: Gatekeeper
========================

This build is not notarized by Apple. The first time you open it, macOS will
say it is from an unidentified developer -- right-click (or Control-click)
ItamiBen.app and choose "Open", then confirm once.


Then: grant Accessibility  (this one is not optional)
=====================================================

ItamiBen reads the foreground window's TITLE, and macOS requires the
Accessibility permission for that. Without it the program still runs, but it
can only see application NAMES -- every rule written against a window title
silently never matches, and the ring turns red while you are working.

    System Settings -> Privacy & Security -> Accessibility -> enable ItamiBen

The app says the same thing in its own window, with a button that opens that
pane for you. No restart needed: the next sample picks it up.


Configuring it: ask an AI
=========================

There are no configuration files. Goals, matching rules, the command list and
the schedule are rows in one database, and they are meant to be written by an
AI, not by hand:

    ~/Library/Application Support/ItamiBen/ItamiBen.sqlite3

Next to it sits AGENT.md -- written for the AI, not for you. A copy is in this
disk image if you want to look first.

If you have a coding assistant with access to your files, point it at that
folder and say what you want:

    Read AGENT.md and set ItamiBen up so only VS Code counts as work.

If you do not, open Settings (the gear) and press the red "Configure online"
button. It shows you your current configuration, lets you write what you want
in plain words, and copies the whole lot to your clipboard. Paste that into any
web AI, bring the answer back, and press Apply.


When something looks wrong
==========================

Hand itamiben.log to an AI and say what you expected. It records every
configuration change ever applied -- what was asked for, what ran, whether it
worked. It is plain text.

The window itself never explains anything. That is deliberate.
NOTE

mkdir -p dist
DMG="dist/ItamiBen-$VERSION-macOS-$ARCH.dmg"
rm -f "$DMG"
hdiutil create -volname "ItamiBen $VERSION" -srcfolder "$STAGE/dmg" -ov -format UDZO -quiet "$DMG"

echo
echo "发布镜像：$DMG  ($(du -h "$DMG" | cut -f1))"
