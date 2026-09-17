# 打出 dist\ItamiBen-<版本>-win-x64.exe（Inno Setup 安装包）。
# macOS 那边的对应物是 ./pack-macos.sh。
#
# ✅ 2026-09-18 第一次在真机上跑通：Windows 11 + Inno Setup 6.7.3，一次就过，
#    产物 11MB。同一天 ForegroundWindow.Win / InputIdle.WindowsElapsed / Sound 的 winmm
#    那几条也一并在真机上验了。
#
# 跟 macOS 的 .dmg（只在 Read Me 里叫用户自己装 .NET 运行时）不同，这个安装包会
# 主动检测 .NET Desktop Runtime 在不在，不在就提出替用户下载并运行官方安装器
# ——见 installer\ItamiBen.iss 的 [Code] 段。
#
# 需要 Inno Setup 6 的 ISCC.exe（只是打包工具，不随程序发给用户）：
#     winget install --id JRSoftware.InnoSetup -e
#
# 用法：  .\pack-windows.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# 版本号**只有一个出处**：Directory.Build.props，跟 pack-macos.sh 和程序自己显示的
# 是同一个值。只在那一处改。
$propsContent = Get-Content "Directory.Build.props" -Raw
if ($propsContent -notmatch "<Version>([^<]+)</Version>") {
    throw "Directory.Build.props 里找不到 <Version>"
}
$Version = $Matches[1]

$StageDir = Join-Path $env:TEMP "ItamiBen-pack-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $StageDir | Out-Null
try {
    # ⚠️ **RID 必须写死给出**。不给 -r 的话，Skia/HarfBuzz 每个平台的 native 库和调试
    #    符号会全被拉进来——v3 在这边量过，27MB 变 560MB，光三个平台的
    #    libSkiaSharp.pdb 就 244MB。.pdb 由 csproj 的 StripPdbFromPublish 在 Publish
    #    之后删掉。
    Write-Host "==> publish (win-x64, 依赖框架)"
    dotnet publish src\ItamiBen.App -c Release -r win-x64 --self-contained false -o $StageDir --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

    # ⚠️ 窗口从不解释自己，所以这份说明**必须跟着装进去**——它是三份面向用户的文档
    #    之一（另外两份：README.md、pack-macos.sh 里那段 Read Me）。用户可见的行为
    #    变了，三份都要跟着改。
    Copy-Item "installer\README.txt" -Destination $StageDir -Force

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw "找不到 ISCC.exe。先装 Inno Setup 6：winget install --id JRSoftware.InnoSetup -e"
    }

    New-Item -ItemType Directory -Force -Path "dist" | Out-Null

    Write-Host "==> 编译安装包 (版本 $Version)"
    & $iscc "/DMyAppVersion=$Version" "/DStageDir=$StageDir" "installer\ItamiBen.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败" }
}
finally {
    Remove-Item $StageDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "安装包：dist\ItamiBen-$Version-win-x64.exe"
