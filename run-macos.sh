#!/usr/bin/env bash
# 开发用：编译 → 装配成 .app → 启动。**发布用 ./pack-macos.sh。**
#
# ⚠️ **为什么非要包成 .app**：macOS 的辅助功能授权（TCC）是按「应用」记账的。
# 直接跑 bin/Debug 里那个裸二进制，授权列表里会出现一个没图标、名字怪异的条目，
# 而且换个路径就不认了。包成 bundle 之后它就是一个正常的 ItamiBen。
#
# ⚠️ **授权和重新编译曾经是冲突的**：ad-hoc 签名的授权绑在 cdhash 上，一重编就作废
#    （2026-09-15 被坑了好几轮）。现在用自签名证书，DR 绑的是证书不是 cdhash，
#    重编多少次都稳。证书没了就重新生成一张（DESIGN §2.2），代价是重新授权一次。
#    真想「只再跑一次、别动二进制」：  ./run-macos.sh --run-only
set -euo pipefail
cd "$(dirname "$0")"

OUT=dist/ItamiBen.app

if [ "${1:-}" = "--run-only" ]; then
  echo "==> 只启动，不编译不重签"
  pkill -f "ItamiBen.app/Contents/MacOS/ItamiBen" 2>/dev/null || true
  sleep 1
  open "$OUT"
  echo "已启动。日志：~/Library/Application Support/ItamiBen/itamiben.log"
  exit 0
fi

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)

echo "==> 编译"
dotnet build src/ItamiBen.App -c Release -v q --nologo

echo "==> 装配 bundle ($VERSION)"
# ⚠️ 装配规矩**只写在 bundle-macos.sh 里一份**，这里和 pack-macos.sh 都调它
./bundle-macos.sh src/ItamiBen.App/bin/Release/net10.0 "$OUT" "$VERSION"

echo "==> 启动"
# ⚠️ 必须先杀掉在跑的那个：`open` 对已运行的 app **只是切到前台**，不会用新二进制重启，
#    于是你以为在测新代码、其实还是旧进程（2026-09-15 被这个骗过一次）。
pkill -f "ItamiBen.app/Contents/MacOS/ItamiBen" 2>/dev/null || true
sleep 1
open "$OUT"
echo "已启动。没有授权的话窗口里会告诉你怎么开。"
