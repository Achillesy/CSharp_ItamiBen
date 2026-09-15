#!/usr/bin/env bash
# 编译 → 包成 .app → 启动。
#
# ⚠️ **为什么非要包成 .app**：macOS 的辅助功能授权（TCC）是按「应用」记账的。
# 直接跑 bin/Debug 里那个裸二进制，授权列表里会出现一个没图标、名字怪异的条目，
# 而且换个路径就不认了。包成 bundle 之后它就是一个正常的 ItamiBen。
#
# ⚠️ **已知痛点**：这里是 ad-hoc 签名（没有开发者证书），每次重新编译 cdhash 都会变，
# macOS 很可能因此**把授权作废、要求重新勾选**。真做成产品要解决签名。
#    症状：改完代码重跑，标题又读不到了 → 去「系统设置 → 隐私与安全性 → 辅助功能」
#    把 ItamiBen 关掉再打开（或者删掉条目重新授权）。
#
# ⚠️ **授权和重新编译是冲突的**：ad-hoc 签名的授权绑在 cdhash 上，源码一改、重编、
#    重签，cdhash 就变，刚给的辅助功能授权当场作废（2026-09-15 被这个坑了好几轮）。
#    所以「只想再跑一次、别动二进制」时用：  ./run-macos.sh --run-only
#    真正的解法是弄一张自签名证书，那样授权绑的是证书不是 cdhash，重编也不失效。
set -euo pipefail
cd "$(dirname "$0")"

if [ "${1:-}" = "--run-only" ]; then
  echo "==> 只启动，不编译不重签（保住已有授权）"
  pkill -f "ItamiBen.app/Contents/MacOS/ItamiBen" 2>/dev/null || true
  sleep 1
  open dist/ItamiBen.app
  echo "已启动。日志：~/Library/Application Support/ItamiBen/itamiben.log"
  exit 0
fi

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)
OUT=dist/ItamiBen.app
CONTENTS=$OUT/Contents

echo "==> 编译"
dotnet build src/ItamiBen.App -c Release -v q --nologo

echo "==> 组装 bundle ($VERSION)"
rm -rf "$OUT"
mkdir -p "$CONTENTS/MacOS"
cp -R src/ItamiBen.App/bin/Release/net10.0/. "$CONTENTS/MacOS/"

# ⚠️ Microsoft.Data.Sqlite 会带进来一个 runtimes/browser-wasm 目录，codesign --deep 认不出
#    它的格式，整个签名当场失败（"bundle format unrecognized, invalid, or unsuitable"）。
#    macOS 上永远用不到它，直接删掉。顺手把另外两个平台的 native 也删了，bundle 小一半。
rm -rf "$CONTENTS/MacOS/runtimes/browser-wasm"
rm -rf "$CONTENTS/MacOS/runtimes"/win-* "$CONTENTS/MacOS/runtimes"/linux-*

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>ItamiBen</string>
  <key>CFBundleDisplayName</key>       <string>ItamiBen</string>
  <key>CFBundleExecutable</key>        <string>ItamiBen</string>
  <!-- ⚠️ 这个 id 定了就别改：辅助功能授权绑在它 + 代码签名上 -->
  <key>CFBundleIdentifier</key>        <string>com.achillesy.itamiben</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key>           <string>$VERSION</string>
  <key>LSMinimumSystemVersion</key>    <string>12.0</string>
  <key>NSHighResolutionCapable</key>   <true/>
</dict>
</plist>
PLIST

# ⚠️ **必须用这张自签名证书签，不能用 ad-hoc**（2026-09-15 定，DESIGN §2.2）。
#    ad-hoc 的 Designated Requirement 只有一行 `cdhash H"..."`——连 bundle id 都没有，
#    于是**每次重编 cdhash 一变，辅助功能授权当场失配**，而系统设置里那条记录看着还在。
#    用证书签之后 DR 变成 `identifier + certificate leaf`，重编多少次都稳定。
#    证书没了就重新生成一张（见 DESIGN §2.2），代价只是重新授权一次。
SIGN_ID="ItamiBen Development"
if ! security find-identity -v -p codesigning | grep -q "$SIGN_ID"; then
  echo "✗ 找不到代码签名证书「$SIGN_ID」，退回 ad-hoc（⚠️ 每次重编都要重新授权）"
  codesign --force --deep --sign - --identifier com.achillesy.itamiben "$OUT" 2>/dev/null
else
  echo "==> 用「$SIGN_ID」签名"
  codesign --force --deep --sign "$SIGN_ID" --identifier com.achillesy.itamiben "$OUT"
fi

echo "==> 启动"
# ⚠️ 必须先杀掉在跑的那个：`open` 对已运行的 app **只是切到前台**，不会用新二进制重启，
#    于是你以为在测新代码、其实还是旧进程（2026-09-15 被这个骗过一次）。
pkill -f "ItamiBen.app/Contents/MacOS/ItamiBen" 2>/dev/null || true
sleep 1
open "$OUT"
echo "已启动。没有授权的话窗口里会告诉你怎么开。"
