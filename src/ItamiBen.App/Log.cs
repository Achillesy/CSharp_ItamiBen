namespace ItamiBen.App;

/// <summary>
/// 两份纯文本日志，跟配置文件放在同一个文件夹里。
///
/// <list type="bullet">
///   <item><c>event.log</c> —— **完整时间线**：启动、退出、闹钟、提醒到点、命令跑了、
///   配置重装、以及每一次出错。</item>
///   <item><c>error.log</c> —— **只有 warn 和 error**，而且这些行在 `event.log` 里
///   也有一份。</item>
/// </list>
///
/// ⚠️ **重复是有意的，而且不会漂**：两行是同一次调用里写的，append-only，没有第二个
/// 真相来源。`error.log` 的价值在于**它通常是空的**——看一眼文件大小就知道出没出事，
/// 而 `event.log` 是一条不缺的时间线（少了错误那几行，时间线上就会有洞：
/// 「23:00 跑了命令、23:05 关机」，中间那次失败却不在线上）。
///
/// ⚠️ **从库里的 `event` 表搬出来的**（2026-09-18 用户定）。原来事件住在库里，理由是
/// 「v4 自己就是记录者，再单开一份文本就是第二个真相来源」。那条理由对**观测数据**
/// 仍然成立，对事件不成立：用户的诊断路径是「把文件交给 AI」，而库是二进制的、
/// 得教人敲 `--query`（DECISIONS I24 明令不教）。
///
/// ⚠️ 搬出来顺带删掉了一整类 bug：事件住在库里时，`Events` 需要 `Bind`/`Unbind`
/// 和「库关了就回退到文本」的机制，因为**库会被关掉而文件不会**。2026-09-18 当天修的
/// 两个 bug（I26 的 stop 事件、`Settings.Detach`）都长在那块土壤上——都是
/// 「退出时谁先谁后」。文件没有这个问题，`Unbind` 这个概念不存在了。
///
/// ⚠️ **不滚存**。实测这个程序一天产生个位数的事件（3 天 9 条，不算我反复重编的
/// start/stop），一年十几 KB。最坏情况是某件事每分钟失败一次，被去重节流压到
/// 1 分钟 1 行 ≈ 90KB/天——那时候**文件变大本身就是信号**，滚掉它反而是帮倒忙。
///
/// 写失败一律吞掉——**记不上话绝不能把程序搞崩**。
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// <see cref="Arm"/> 调过没有。**没调过就一个字都不写。**
    ///
    /// ⚠️ 这不是开关，是护栏：`ItamiBen.App.Tests` 引的是 App 工程本身，被测到的代码
    /// （比如 <see cref="Platform.Sound"/> 读不到文件时）照样会报错，于是
    /// **单元测试会往用户真实的日志里写东西**（2026-09-16 实测撞上过，
    /// 看见日志里冒出 `itamiben-no-such-file.wav` 才发现）。
    ///
    /// ⚠️ 挂在 <c>Program.Main</c> 的最前面，**不是挂在窗口里**：被单实例挡回去的那个
    /// 进程根本走不到窗口，而它恰恰是最需要留句话的那一个。
    /// </summary>
    private static bool _armed;

    public static void Arm() => _armed = true;

    /// <summary>
    /// 记一行。**warn / error 同时进 `error.log`。**
    /// </summary>
    public static void Write(string level, string kind, string text)
        => WriteAt(DateTimeOffset.Now, level, kind, text);

    /// <summary>
    /// 带指定时刻记一行。**只有从老 `event` 表往 `event.log` 搬历史时才用**——
    /// 那些行必须带着当时的时间戳，否则时间线就成了「搬运的那一刻」。
    /// </summary>
    public static void WriteAt(DateTimeOffset at, string level, string kind, string text)
    {
        if (!_armed) return;

        var line = $"{at:yyyy-MM-dd HH:mm:ss}  {level,-5}  {kind,-9} {text}\n";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                File.AppendAllText(Path.Combine(AppData.Dir, "event.log"), line);
                if (level != "info")
                    File.AppendAllText(Path.Combine(AppData.Dir, "error.log"), line);
            }
            catch { }
        }
    }
}
