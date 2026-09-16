using System.Diagnostics;
using ItamiBen.Core;

namespace ItamiBen.App.Platform;

/// <summary>
/// 闹钟到点要跑的那条命令：`setting.alarmCommand` 记的是名字，正文在库的 `command` 表里。
/// 从 v3 搬过来，跟 AW 无关。
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
    /// 闹钟到点：按名字从命令清单里取出这台机器该跑的那条，跑掉。
    ///
    /// ⚠️ **只按名字取，不接受命令原文**（DECISIONS I15）：可执行的文本只住在
    /// `command` 表里，这样「这台机器上有哪些命令能被自动跑」永远只要看一个地方。
    ///
    /// ⚠️ 配置**现在就在库里**，运行中改了下一分钟就重装，所以这里不用再去现读文件
    /// （原来那条「到点现读 rules.json」的路连同它的容错一起删掉了）。
    /// </summary>
    public static void LaunchDetached(GoalRules rules, string? name)
    {
        if (rules.CommandNamed(name) is not { Length: > 0 } cmd)
        {
            Events.Warn("command", $"Alarm fired with the command armed, but there is no command named "
                                 + $"'{name ?? "(none)"}' for this OS");
            return;
        }

        Events.Info("command", $"running '{name}': {cmd}");
        RunDetached(cmd);
    }

    /// <summary>
    /// 把**这一条**命令原样交给 shell 跑，起完就返回。
    ///
    /// ⚠️ 从 <see cref="LaunchDetached"/> 里拆出来，**只为了能测**：
    /// `LaunchDetached` 取的是这台机器上真实配置里的命令，而那条命令就是重启
    /// （`command` 表里 `alarm` 那行）——测试要是调它，当场把机器重启了。
    /// 拆开之后测试只喂一条自己写的无害命令，走的却是同一条起进程的代码，
    /// 引号那条路径一个字节都没绕开。
    /// </summary>
    public static void RunDetached(string cmd)
    {
        try
        {
            var proc = Process.Start(BuildStartInfo(cmd));
            if (proc is null) { Events.Warn("command", $"did not start: {cmd}"); return; }
            _ = DrainAsync(proc, cmd);
        }
        catch (Exception e)
        {
            Events.Error("command", $"failed to start: {cmd}", e);
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

            // ⚠️ **退出码 0 就一个字不记**：那条命令跑成了，`running:` 那行已经说明它跑过。
            //    只有出问题时才值得留痕——而那时 stderr 才是唯一能说明原因的东西。
            if (proc.ExitCode != 0)
                Events.Warn("command", $"exited {proc.ExitCode}: {cmd}"
                                     + (stderr.Trim() is { Length: > 0 } err ? $"  err: {err}" : ""));
        }
        catch (Exception e)
        {
            // 进程可能已经把我们连同系统一起带走了，收不到输出很正常
            Events.Error("command", $"Could not collect the output of: {cmd}", e);
        }
    }
}
