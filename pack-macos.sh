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
mkdir -p "$STAGE/dmg/Examples"
cp alarms.cron.example layout.json.example rules.json "$STAGE/dmg/Examples/"

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

If .NET is missing, double-clicking the app says
"You must install .NET to run this application".


First launch: Gatekeeper
========================

This build is not notarized by Apple. The first time you open it, macOS will
say it is from an unidentified developer -- right-click (or Control-click)
ItamiBen.app and choose "Open", then confirm once. After that it opens
normally, including by double-click.


Then: grant Accessibility  (this one is not optional)
=====================================================

ItamiBen reads the foreground window's TITLE, and macOS requires the
Accessibility permission for that. Without it the program still runs, but it
can only see application NAMES -- every rule written against a window title
silently never matches, and the ring turns red while you are working.

    System Settings -> Privacy & Security -> Accessibility -> enable ItamiBen

The app tells you the same thing in its own window, with a button that opens
that pane for you. No restart needed: the next sample picks it up.


Your files
==========

    ~/Library/Application Support/ItamiBen/

    rules.json     you write this; the program only ever reads it.
                   A default one ships inside the app -- copy it here to edit.
    alarms.cron    optional reminder list, standard crontab format, read once
                   a minute. See Examples/alarms.cron.example.
    layout.json    optional: window size tier and opacity.
                   See Examples/layout.json.example.
    settings.json  the program rewrites this whole file. Edit it only while
                   ItamiBen is not running.
    during.json    accumulated hours per goal.
    samples.db     one row per second, kept across rounds.
    itamiben.log   this run's log; the previous run is itamiben.log.old.

Comments and trailing commas are allowed in the three files you write.


The window never explains itself
================================

That is deliberate. Nothing pops up, nothing nags -- there is only the red on
the ring, and the rest block being pushed further away. When something looks
wrong, itamiben.log is where you look.
NOTE

mkdir -p dist
DMG="dist/ItamiBen-$VERSION-macOS-$ARCH.dmg"
rm -f "$DMG"
hdiutil create -volname "ItamiBen $VERSION" -srcfolder "$STAGE/dmg" -ov -format UDZO -quiet "$DMG"

echo
echo "发布镜像：$DMG  ($(du -h "$DMG" | cut -f1))"
