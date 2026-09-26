#!/usr/bin/env bash
# 打出 dist/itamiben-<版本>_<arch>.deb。macOS 那边的对应物是 ./pack-macos.sh，
# Windows 那边是 pack-windows.ps1。
#
# 跟那两个包一样，这个包也是**依赖框架**的：deb 的 Depends 里写了 dotnet-runtime-10.0，
# apt 会从 Ubuntu 官方源自动装上——用户那边不用装 SDK、不用加第三方源。
#
# ⚠️ **RID 必须写死给出**。不给 -r 的话，Skia/HarfBuzz **每个平台**的 native 库和调试
#    符号会全被拉进来——v3 在 Windows 侧量过，27MB 变 560MB，光三个平台的
#    libSkiaSharp.pdb 就 244MB。.pdb 由 csproj 的 StripPdbFromPublish 在 Publish 之后删掉。
#
# 用法：  ./pack-ubuntu.sh [amd64|arm64]     # 不给参数就用本机架构
set -euo pipefail
cd "$(dirname "$0")"

# 版本号**只有一个出处**：Directory.Build.props，跟程序自己在界面上显示的是同一个值。
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)
if [ -z "$VERSION" ]; then
  echo "Directory.Build.props 里找不到 <Version>" >&2
  exit 1
fi

if [ $# -ge 1 ]; then
  ARCH="$1"
else
  ARCH="$(dpkg --print-architecture 2>/dev/null || uname -m | sed 's/^x86_64$/amd64/;s/^aarch64$/arm64/')"
fi
case "$ARCH" in
  amd64) RID="linux-x64" ;;
  arm64) RID="linux-arm64" ;;
  *) echo "不支持的架构: $ARCH（只要 amd64 / arm64）" >&2; exit 1 ;;
esac

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

echo "==> publish ($RID，依赖框架)"
dotnet publish src/ItamiBen.App -c Release -r "$RID" --self-contained false \
    -o "$STAGE/publish" --nologo -v quiet
echo "    $(du -sh "$STAGE/publish" | cut -f1)（.pdb 已由 csproj 的 StripPdbFromPublish 删掉）"

echo "==> 装配 deb"
PKG="$STAGE/itamiben_${VERSION}_${ARCH}"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/itamiben" "$PKG/usr/bin" \
         "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps"
cp -a "$STAGE/publish/." "$PKG/opt/itamiben/"
chmod 755 "$PKG/opt/itamiben/ItamiBen"

cat > "$PKG/usr/bin/itamiben" <<'EOF'
#!/bin/sh
exec /opt/itamiben/ItamiBen "$@"
EOF
chmod 755 "$PKG/usr/bin/itamiben" "$PKG/DEBIAN"

# 图标：跟 Windows 安装包用的是同一个源——tomato.ico 里藏着 PNG，直接抠出来。
# 抠不出来就当场失败：上游图标形状变了，打包的人应该知道。
python3 - "$PKG/usr/share/icons/hicolor/256x256/apps/itamiben.png" <<'PYEOF'
import struct, sys
data = open('src/ItamiBen.App/tomato.ico', 'rb').read()
_, _, count = struct.unpack('<HHH', data[:6])
best = None
for i in range(count):
    off = 6 + i * 16
    _, _, _, _, _, _, size, doff = struct.unpack('<BBBBHHII', data[off:off + 16])
    chunk = data[doff:doff + size]
    if chunk[:4] == b'\x89PNG' and (best is None or size > best[0]):
        best = (size, chunk)
if best is None:
    sys.exit('tomato.ico 里找不到 PNG 图标')
open(sys.argv[1], 'wb').write(best[1])
print('    图标 %d 字节' % best[0])
PYEOF

cat > "$PKG/usr/share/applications/itamiben.desktop" <<'EOF'
[Desktop Entry]
Version=1.0
Name=ItamiBen
Comment=A pomodoro clock with teeth
Exec=itamiben
Icon=itamiben
Terminal=false
Type=Application
Categories=Utility;
StartupWMClass=ItamiBen
EOF

# ⚠️ framework-dependent：运行时不跟着走，靠下面这行 Depends 让 apt 从 Ubuntu
#    官方源装 dotnet-runtime-10.0（24.04 的 noble-updates 里有，不用加微软源）。
#    libxss1 是 XScreenSaver 空闲检测要的（InputIdle 的 Linux 分支）。
cat > "$PKG/DEBIAN/control" <<'EOF'
Package: itamiben
Version: __VERSION__
Section: utils
Priority: optional
Architecture: __ARCH__
Maintainer: Achilles.Newman <https://github.com/Achillesy>
Description: A pomodoro clock with teeth
 A focus timer that watches your foreground window: only seconds spent
 in the applications you declared count as work. Everything else --
 opened the wrong app, wandered off for water -- is deducted from the
 same budget until you finish what you committed to.
 .
 Framework-dependent build: requires the .NET 10 runtime, installed
 automatically as a dependency -- no SDK needed.
Depends: dotnet-runtime-10.0, libx11-6, libice6, libsm6, libfontconfig1, libfreetype6, libxkbcommon0, libxss1
Homepage: https://github.com/Achillesy/CSharp_ItamiBen
EOF
sed -i "s/__VERSION__/$VERSION/; s/__ARCH__/$ARCH/" "$PKG/DEBIAN/control"
chmod 644 "$PKG/DEBIAN/control" "$PKG/usr/share/applications/itamiben.desktop" \
          "$PKG/usr/share/icons/hicolor/256x256/apps/itamiben.png"

mkdir -p dist
DEB="dist/itamiben_${VERSION}_${ARCH}.deb"
rm -f "$DEB"
dpkg-deb --build "$PKG" "$DEB" >/dev/null

echo
echo "发布包：$DEB  ($(du -h "$DEB" | cut -f1))"
