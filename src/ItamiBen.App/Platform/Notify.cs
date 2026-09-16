using System.Diagnostics;
using System.Text;

namespace ItamiBen.App.Platform;

/// <summary>
/// `alarms.cron` 到点时弹的**系统通知**（2026-09-16 从 v3 搬过来，DESIGN §10）。
///
/// ⚠️ **跟骨牌上那条自绘提示条并存，不是二选一。** 两者各补对方的短板：
/// 提示条保证**屏幕上一定看得见**（系统通知可能被「请勿打扰」吞掉、可能压根没权限），
/// 系统通知则保证**关掉程序也能事后翻看**，而且**一条事件一个通知、永远不合并**
/// ——提示条那边受硬高度上限约束，多出来的只剩一个 `+N`，
/// **通知中心这一份才是不丢内容的那份**（v3 的用户 2026-09-03 点名要的）。
///
/// ⚠️ **这不是闹钟的「到点跑命令」**（<see cref="Command"/>）：那边跑的是用户写在
/// rules.json 里的命令、而且有开关；这里永远只是「弹一条带这段文字的通知」，
/// **无条件执行，不受任何开关控制**——`alarms.cron` 的那个开关只管响不响铃（v3 的 J6）。
///
/// 两个平台都靠**起一个短命的子进程**做到，不加任何包依赖、不碰 TFM
/// （CLAUDE.md：App 保持 `net10.0`，`-windows` 别加回去）——真正的 WinRT/UWP toast
/// 绑定需要 `-windows` 系带 TFM 或者签名打包的应用身份，这个项目两样都没有。
///
/// ⚠️ **Windows 那半没在真机上跑过**，跟 <c>ForegroundWindow.Win</c> 一样是纸面代码。
/// 不过 v3 在真机上把它趟通了，连同下面那个 AppId 的坑——照搬没改。
/// </summary>
public static class Notify
{
    private const string Title = "ItamiBen";

    /// <summary>
    /// PowerShell 控制台宿主的**真实** AppId。
    ///
    /// ⚠️ **不是随便一个字符串都能让 Windows 认账**，这是 v3 一波三折趟出来的：
    /// 它最初用 <c>CreateToastNotifier("Windows PowerShell")</c> 这个裸字符串，真机上
    /// 根本弹不出来、Windows 通知设置里也找不到这个应用，一度整个放弃改走自绘提示条。
    /// 后来翻通知中心才发现——**当天早些时候用另一个 AppId 测的那条其实是送达的**，
    /// 只是「请勿打扰」开着、横幅被吞了，通知本身安静地躺在通知中心里。
    /// 真正的问题从来不是「这条路走不通」，而是**那个裸字符串不是一个注册过的 AppId**。
    /// 下面这串是 PowerShell 装机时在系统里登记的固定值（AppUserModelID），不是本项目分配的。
    ///
    /// ⚠️ 顺带记下那次误诊的形状：**「没看见」不等于「没送达」**。
    /// 会不会弹横幅终究要看 Windows 自己的通知设置和「请勿打扰」状态，
    /// 这个不确定性本来就在，不是 bug——也正是提示条必须并存的理由。
    /// </summary>
    private const string PowerShellAppId = @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

    /// <summary>
    /// 弹一条通知。失败一律安静收场、只写日志——**提示本身绝不能把程序搞挂**
    /// （跟 <see cref="Sound"/> 同一条原则）。
    /// </summary>
    public static void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            if (OperatingSystem.IsWindows()) ShowWindows(text);
            else if (OperatingSystem.IsMacOS()) ShowMac(text);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to show notification: {text}", e);
        }
    }

    // ---------------------------------------------------------------- Windows

    /// <summary>
    /// 走 WinRT 的 <c>ToastNotificationManager</c>，从纯 PowerShell 里直接调，
    /// 用 <see cref="PowerShellAppId"/> 而不是裸字符串——这是这条路真正能弹出来的关键。
    ///
    /// ⚠️ 脚本整体走 <c>-EncodedCommand</c>（Base64 的 UTF-16LE）传进去，不拼命令行参数
    /// ——这样完全绕开 cmd / PowerShell 的引号转义地狱，脚本里唯一要手工转义的只剩嵌进去
    /// 的文字本身：先 XML 转义（进的是 toast 的 XML），再 PowerShell 单引号转义
    /// （外层拿单引号包住那段 XML 字符串）。**两层，顺序不能反。**
    /// </summary>
    private static void ShowWindows(string text)
    {
        var t = PsSingleQuoted(XmlEscape(Title));
        var m = PsSingleQuoted(XmlEscape(text));
        var script =
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null\n" +
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null\n" +
            "$doc = New-Object Windows.Data.Xml.Dom.XmlDocument\n" +
            $"$doc.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>{t}</text><text>{m}</text></binding></visual></toast>')\n" +
            "$toast = New-Object Windows.UI.Notifications.ToastNotification $doc\n" +
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{PowerShellAppId}').Show($toast)\n";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded]);
    }

    private static string XmlEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string PsSingleQuoted(string s) => s.Replace("'", "''");

    // ---------------------------------------------------------------- macOS

    /// <summary>
    /// <c>osascript -e 'display notification ...'</c>——系统自带，**不需要额外的权限授予**
    /// （不同于从签名 .app 里调 <c>UNUserNotificationCenter</c> 那条路，那要 entitlement）。
    /// </summary>
    private static void ShowMac(string text)
    {
        var t = AppleScriptQuoted(Title);
        var m = AppleScriptQuoted(text);
        Run("osascript", ["-e", $"display notification \"{m}\" with title \"{t}\""]);
    }

    private static string AppleScriptQuoted(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ---------------------------------------------------------------- 共用

    /// <summary>
    /// ⚠️ 用 <c>ArgumentList</c> 而不是拼一整条 <c>Arguments</c> 字符串——每个元素原样交给
    /// 子进程的 argv，不用再操心一层 shell 转义。（跟 <see cref="Command"/> 不一样：
    /// 那边命令本身就是用户写在 rules.json 里的一整条 shell 命令，没法回避 shell。）
    /// </summary>
    private static void Run(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (p is null) { Log.Warn($"Notify: {exe} did not start"); return; }

        // 后台收退出码和 stderr，**不阻塞**——通知弹没弹好不该拖住 UI 线程，
        // 出错了日志里查得到就够了
        _ = Task.Run(async () =>
        {
            try
            {
                var stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0) Log.Warn($"Notify: {exe} exited with {p.ExitCode}: {stderr.Trim()}");
            }
            catch (Exception e) { Log.Error("Notify: failed to collect process output", e); }
        });
    }
}
