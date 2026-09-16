#!/usr/bin/env bash
#
# 把一份构建产物装配成 ItamiBen.app。**run-macos.sh（开发）和 pack-macos.sh（发布）
# 共用这一个脚本**——bundle 的规矩只写一遍。
#
# ⚠️ **不共用的后果是实打实的**：Info.plist 里有 CFBundleIdentifier，而 macOS 的辅助
#    功能授权绑在「bundle id + 签名」上。两份脚本各写一份 plist，迟早有一天两边的 id
#    或者签名方式对不上——症状是「开发时好好的，装了发布版就读不到窗口标题了」，
#    而且**不报错**。
#
# 用法：  ./bundle-macos.sh <构建产物目录> <输出的 .app 路径> <版本号>
set -euo pipefail

SRC="$1"          # dotnet build 或 dotnet publish 的输出目录
APP="$2"          # 例如 dist/ItamiBen.app
VERSION="$3"
CONTENTS="$APP/Contents"

rm -rf "$APP"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
cp -R "$SRC/." "$CONTENTS/MacOS/"

# ⚠️ Microsoft.Data.Sqlite 会带进来一个 runtimes/browser-wasm 目录，codesign --deep 认不出
#    它的格式，整个签名当场失败（"bundle format unrecognized, invalid, or unsuitable"）。
#    macOS 上永远用不到它，直接删掉。顺手把另外两个平台的 native 也删了，bundle 小一半。
rm -rf "$CONTENTS/MacOS/runtimes/browser-wasm"
rm -rf "$CONTENTS/MacOS/runtimes"/win-* "$CONTENTS/MacOS/runtimes"/linux-* 2>/dev/null || true

# 图标：**代码画的，仓库里没有位图**。导出 → iconutil 压成 .icns → 放进 Resources。
# ⚠️ 这一步用的是 headless 渲染（见 Program.cs 的 HeadlessBuilder），**不依赖图形会话**
#    ——屏幕锁着、SSH 里打包都能跑。用 UsePlatformDetect 的话这里会当场 -6661 崩掉。
ICONSET="$(dirname "$APP")/.ItamiBen-$$.iconset"
"$CONTENTS/MacOS/ItamiBen" --export-iconset "$ICONSET" >/dev/null
iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/ItamiBen.icns"
rm -rf "$ICONSET"

# ⚠️ **依赖框架的 apphost 得找得到 .NET 运行时，而双击启动的 GUI 程序拿不到 shell 的
#    环境变量**：.NET 装在 /usr/local/share/dotnet 以外的地方时（比如免密码装进 ~/.dotnet），
#    从 Finder 双击只会说「You must install .NET to run this application」，
#    而同一个二进制在终端里跑得好好的。这个差别骗人骗得够狠，值得单写一条。
#    解法是 LSEnvironment，由 LaunchServices 在启动时注入。⚠️ 路径是**打包那一刻定死的**，
#    .NET 换了地方就要重新打一次包。
if [ -n "${DOTNET_ROOT:-}" ]; then
  DOTNET_DIR="$DOTNET_ROOT"
else
  DOTNET_BIN="$(command -v dotnet)"
  DOTNET_DIR="$(dirname "$(readlink "$DOTNET_BIN" 2>/dev/null || echo "$DOTNET_BIN")")"
fi
LS_ENV=""
if [ "$DOTNET_DIR" != "/usr/local/share/dotnet" ] && [ -d "$DOTNET_DIR" ]; then
  echo "    .NET 不在默认位置（$DOTNET_DIR），写进 LSEnvironment"
  LS_ENV="  <key>LSEnvironment</key>
  <dict>
    <key>DOTNET_ROOT</key><string>$DOTNET_DIR</string>
  </dict>"
fi

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
$LS_ENV
  <!-- ⚠️ CFBundleName 钉死在 ItamiBen：macOS 的 localizedName 报的就是它，
       **写进 samples.db、被 rules.json 匹配的都是这个字符串**（DECISIONS A4）。 -->
  <key>CFBundleName</key>              <string>ItamiBen</string>
  <key>CFBundleDisplayName</key>       <string>ItamiBen</string>
  <key>CFBundleExecutable</key>        <string>ItamiBen</string>
  <key>CFBundleIconFile</key>          <string>ItamiBen.icns</string>
  <!-- ⚠️ 这个 id 定了就别改：辅助功能授权绑在它 + 代码签名上 -->
  <key>CFBundleIdentifier</key>        <string>com.achillesy.itamiben</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key>           <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>    <string>12.0</string>
  <!-- 钟面是矢量画的，必须按物理像素渲染，否则 Retina 上会糊 -->
  <key>NSHighResolutionCapable</key>   <true/>
</dict>
</plist>
PLIST

# ⚠️ **必须用这张自签名证书签，不能用 ad-hoc**（2026-09-15 定，DESIGN §2.2）。
#    ad-hoc 的 Designated Requirement 只有一行 `cdhash H"..."`——连 bundle id 都没有，
#    于是**每次重编 cdhash 一变，辅助功能授权当场失配**，而系统设置里那条记录看着还在。
#    用证书签之后 DR 变成 `identifier + certificate leaf`，重编多少次都稳定，
#    **而且装到 /Applications 换了路径也照样认**——发布版和开发版共用同一份授权。
SIGN_ID="ItamiBen Development"
if security find-identity -v -p codesigning 2>/dev/null | grep -q "$SIGN_ID"; then
  echo "    用「$SIGN_ID」签名"
  codesign --force --deep --sign "$SIGN_ID" --identifier com.achillesy.itamiben "$APP"
else
  echo "    ✗ 找不到证书「$SIGN_ID」，退回 ad-hoc（⚠️ 每次重编都要重新授权）"
  codesign --force --deep --sign - --identifier com.achillesy.itamiben "$APP" 2>/dev/null || true
fi
