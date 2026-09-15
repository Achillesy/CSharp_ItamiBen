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
set -euo pipefail
cd "$(dirname "$0")"

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)
OUT=dist/ItamiBen.app
CONTENTS=$OUT/Contents

echo "==> 编译"
dotnet build src/ItamiBen.App -c Release -v q --nologo

echo "==> 组装 bundle ($VERSION)"
rm -rf "$OUT"
mkdir -p "$CONTENTS/MacOS"
cp -R src/ItamiBen.App/bin/Release/net10.0/. "$CONTENTS/MacOS/"

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

echo "==> ad-hoc 签名"
codesign --force --deep --sign - --identifier com.achillesy.itamiben "$OUT" 2>/dev/null

echo "==> 启动"
open "$OUT"
echo "已启动。没有授权的话窗口里会告诉你怎么开。"
