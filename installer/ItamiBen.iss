; ItamiBen 的 Windows 安装包（Inno Setup 6）。
;
; ⚠️ **由仓库根目录的 pack-windows.ps1 调用，别手工跑**：MyAppVersion 和 StageDir
;    要靠 /D 传进来。它是 macOS 那边 pack-macos.sh 的对应物。
;
; ✅ 2026-09-18 第一次在真机上跑通（Windows 11 + Inno Setup 6.7.3）：向导走完、
;    静默安装（/VERYSILENT）、覆盖升级、开始菜单三个快捷方式、桌面快捷方式、
;    HKCU 的卸载项，全都对。**全程没有弹过 UAC**——PrivilegesRequired=lowest 成立，
;    {autopf} 解析到 %LOCALAPPDATA%\Programs。
;
; ⚠️ 下面那段 .NET 运行时检测**只走到了「已装」这一支**：测试机上装着
;    Microsoft.WindowsDesktop.App 10.0.10，所以 IsDotNetDesktopRuntimeInstalled 返回
;    True、直接跳过。**下载 + ShellExec('runas') 那一支仍然没在真机上走过**
;    ——要验它得找一台没装 .NET 10 桌面运行时的机器。
;
; 跟 macOS 的 .dmg（只在 Read Me 里叫用户自己去装 .NET 运行时）不同，这个安装包会
; **主动检测** .NET Desktop Runtime 在不在，不在就提出替用户下载并运行官方安装器
; ——见下面的 [Code] 段。
;
; 程序本身**按用户装**（PrivilegesRequired=lowest，常见情况下完全不弹 UAC），
; {autopf} 因此解析到 %LOCALAPPDATA%\Programs。而 .NET 运行时那一步是真需要管理员
; 权限的，Inno 的 Exec()（CreateProcess）在调用方自己没提权时没法提升子进程，
; 所以**单独那一步**走 ShellExec 的 'runas' 动词，为它自己弹一次 UAC，
; 其余部分仍然全程按用户装。
;
; ⚠️ **Windows 这边不需要辅助功能授权**（那是 macOS 的事）：读窗口标题走的是
;    GetWindowText，不要权限。所以这份安装包不用引导用户去开任何开关。

#define MyAppName "ItamiBen"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef StageDir
  #define StageDir "..\installer-stage"
#endif
#define MyAppPublisher "Achilles.Newman"
#define MyAppURL "https://github.com/Achillesy/ItamiBen"
#define MyAppExeName "ItamiBen.exe"
; net10.0 -> Microsoft.WindowsDesktop.App's major version; keep this in sync if the TFM changes.
#define DotNetMajor "10"
#define DotNetRuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{DA35900E-96B8-4523-9BB2-49090D3D4058}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=ItamiBen-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\ItamiBen.App\tomato.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; 窗口本身**从不解释自己**（这是刻意的），所以唯一一份说明得让人找得到，
; 而不是躺在安装目录里没人看见。
Name: "{group}\{#MyAppName} (Read Me)"; Filename: "{app}\README.txt"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  RuntimeInstallerReady: Boolean;

// net10.0 needs Microsoft.WindowsDesktop.App 10.x: scan
// C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App for a subfolder
// starting with "10.". 64-bit install path only -- this project only ships win-x64.
// The runtime itself is always machine-wide regardless of whether ItamiBen is
// installed per-user or per-machine, so this check doesn't change either way.
function IsDotNetDesktopRuntimeInstalled(const MajorVersion: string): Boolean;
var
  FindRec: TFindRec;
  BaseDir: string;
begin
  Result := False;
  BaseDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BaseDir) then
    Exit;
  if FindFirst(BaseDir + '\' + MajorVersion + '.*', FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  RuntimeInstallerReady := False;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  if IsDotNetDesktopRuntimeInstalled('{#DotNetMajor}') then
    Exit;

  if SuppressibleMsgBox(
       'ItamiBen needs the .NET {#DotNetMajor} Desktop Runtime, which was not found on this computer.' + #13#10 + #13#10 +
       'Setup can download it now (about 60 MB) from Microsoft and launch its installer right after ItamiBen is installed. ' +
       'That step needs administrator approval (the runtime itself installs machine-wide); ItamiBen itself does not.' + #13#10 + #13#10 +
       'Continue?',
       mbConfirmation, MB_YESNO, IDYES) <> IDYES then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetRuntimeUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      RuntimeInstallerReady := True;
    except
      if DownloadPage.AbortedByUser then
        Log('User aborted .NET runtime download')
      else
        // A failed download doesn't block this install: the framework-dependent apphost
        // shows its own "you need to install .NET" prompt on first launch if the runtime
        // is still missing, pointing at the official download page. That's the fallback.
        SuppressibleMsgBox(
          'Could not download the .NET Desktop Runtime automatically (' + GetExceptionMessage + ').' + #13#10 + #13#10 +
          'ItamiBen will still be installed. If it fails to start, install the .NET ' + '{#DotNetMajor}' +
          ' Desktop Runtime (x64) manually from https://dotnet.microsoft.com/download and try again.',
          mbInformation, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep <> ssPostInstall) or not RuntimeInstallerReady then
    Exit;

  // ItamiBen's own install stays per-user (no elevation). The runtime installer
  // genuinely needs admin, and a plain Exec() can't elevate a nested process when
  // the caller isn't already elevated -- ShellExec's 'runas' verb pops its own UAC
  // prompt just for this one step.
  if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'),
       '/install /passive /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    SuppressibleMsgBox(
      'Could not launch the .NET Desktop Runtime installer (administrator approval was likely declined).' + #13#10 + #13#10 +
      'ItamiBen is installed. If it fails to start, install the .NET ' + '{#DotNetMajor}' +
      ' Desktop Runtime (x64) manually from https://dotnet.microsoft.com/download and try again.',
      mbInformation, MB_OK, IDOK);
end;
