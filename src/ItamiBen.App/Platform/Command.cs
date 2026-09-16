using System.Diagnostics;
using ItamiBen.Core;

namespace ItamiBen.App.Platform;

/// <summary>
/// 闹钟到点要跑的那条命令（`rules.json` 的 `executeCommand`）。从 v3 搬过来，跟 AW 无关。
///
/// **永远只执行第 0 条**（v3 的 E9）：那是个常用命令的收藏夹，不是配置格式——
/// 想换命令就去文件里重排顺序，**不做界面去选**。
///
/// ⚠️ **起完就返回，绝不 await**：那条命令多半是关机/重启/休眠，等它「跑完」没有意义，
/// 而且命令挂死也卡不到分钟节拍。输出交给后台任务收进日志。
/// </summary>
public static class Command
{
    /// <summary>
    /// **到点时现读 `rules.json`**，不用启动时那份快照（v3 的 L4）。
    ///
    /// 这不违反「一个文件一条读取路径」：读的仍然是 <see cref="GoalRules.Parse"/>，
    /// 同一个解析器，只是时机从「启动一次」变成「每次到点」。一年也读不了几次。
    ///
    /// 读失败就退回传进来的快照并记一条 Error——**到点该关机却一声不响，比用一份
    /// 稍旧的命令更糟**，前提是日志里写清楚用了旧的。
    /// </summary>
    public static void LaunchDetached(GoalRules snapshot)
    {
        var rules = snapshot;
        try
        {
            rules = GoalRules.Parse(File.ReadAllText(AppData.RulesPath()));
        }
        catch (Exception e)
        {
            Log.Error("Cannot re-read rules.json for executeCommand; using the startup snapshot", e);
        }

        if (rules.CommandForThisOs() is not { Length: > 0 } cmd)
        {
            Log.Warn("Alarm fired with the command armed, but there is no executeCommand for this OS");
            return;
        }

        Log.Line($"running executeCommand: {cmd}");
        try
        {
            var proc = Process.Start(BuildStartInfo(cmd));
            if (proc is null) { Log.Warn($"executeCommand did not start: {cmd}"); return; }
            _ = DrainAsync(proc, cmd);
        }
        catch (Exception e)
        {
            Log.Error($"executeCommand failed to start: {cmd}", e);
        }
    }

    private static ProcessStartInfo BuildStartInfo(string cmd)
    {
        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // ⚠️ 必须设 true：这是个 GUI 进程、自己没有控制台，`UseShellExecute=false`
            //    起 cmd.exe 时 Windows 会**给子进程新建一个控制台窗口**——那个黑窗
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            // `cmd.exe /c` 有它自己一套引号规则，跟 ArgumentList 用的 C 运行库转义规则
            // **对不上**，别顺手把这条也改成 ArgumentList——v3 这条路真机验证过
            psi.Arguments = $"/c {cmd}";
        }
        else
        {
            // ⚠️ **脚本必须作为单独一个 argv 元素交给 `sh -c`**，绝不能拼成
            //    `-c "{cmd}"` 那样一整条字符串（v3 的 L1，2026-08-08 修的真 bug）。
            //    拼字符串时 .NET 还要按 C 运行库规则把它切回 argv，而默认那几条 macOS
            //    命令**自己就带双引号**：
            //        osascript -e 'tell application "System Events" to restart'
            //    内层的 " 会把外层引号提前闭合，切出来是：
            //        [ "-c", "osascript -e 'tell application System", "Events to restart'" ]
            //    脚本被截断、单引号没闭合，剩下半截还成了 $0。
            //    ⚠️ **这个 bug 最阴的地方是闹钟那一侧全对**：到点了、也真调了，日志照常，
            //    只有命令自己安静地失败——不翻 stderr 根本看不出来。
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cmd);
        }
        return psi;
    }

    /// <summary>把子进程的输出收进日志。**不阻塞调用方**——那条命令多半是关机。</summary>
    private static async Task DrainAsync(Process proc, string cmd)
    {
        try
        {
            var stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);

            Log.Line($"executeCommand exited with {proc.ExitCode}: {cmd}");
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Line($"  out: {line.TrimEnd()}");
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Log.Line($"  err: {line.TrimEnd()}");
        }
        catch (Exception e)
        {
            // 进程可能已经把我们连同系统一起带走了，收不到输出很正常
            Log.Error($"Could not collect the output of: {cmd}", e);
        }
    }
}
